using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Mottainai.Server
{
    public class SessionLog
    {
        readonly object gate = new object();
        public readonly string dir, sessionPath, labPath;

        public SessionLog(string root)
        {
            dir = Path.Combine(root, "logs");
            Directory.CreateDirectory(dir);
            sessionPath = Path.Combine(dir, $"session_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            labPath = Path.Combine(dir, "lab_results.csv");
            File.WriteAllText(sessionPath, "time,kind,room,name,net,slice,rtt_avg,rtt_p95,jitter,fps,dl_kbps,ul_kbps,loss,interp_ms,pps,device,detail\n");
            if (!File.Exists(labPath))
                File.WriteAllText(labPath, "time,name,device,net,slice,rtt_avg,rtt_min,rtt_p95,rtt_max,jitter,loss,sent,received,down_mbps,down_mb,down_sec\n");
        }

        static string Esc(string s) => string.IsNullOrEmpty(s) ? "" : s.Replace(",", ";").Replace("\n", " ");
        static string F(float v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

        void Append(string path, string line)
        {
            lock (gate)
            {
                try { File.AppendAllText(path, line + "\n"); } catch (Exception e) { Debug.LogWarning("[Log] " + e.Message); }
            }
        }

        public void Event(string kind, string room, string detail) =>
            Append(sessionPath, $"{DateTime.Now:O},{kind},{Esc(room)},,,,,,,,,,,,,,{Esc(detail)}");

        public void Telemetry(TelemetryDto t) =>
            Append(sessionPath, $"{DateTime.Now:O},telemetry,{Esc(t.room)},{Esc(t.name)},{Esc(t.net)},{Esc(t.slice)},{F(t.rttAvg)},{F(t.rttP95)},{F(t.jitter)},{F(t.fps)},{F(t.dlKbps)},{F(t.ulKbps)},{F(t.loss)},{F(t.interpMs)},{F(t.pps)},{Esc(t.device)},");

        public void Lab(LabResultDto r)
        {
            Append(labPath, $"{DateTime.Now:O},{Esc(r.name)},{Esc(r.device)},{Esc(r.net)},{Esc(r.slice)},{F(r.rttAvg)},{F(r.rttMin)},{F(r.rttP95)},{F(r.rttMax)},{F(r.jitter)},{F(r.loss)},{r.sent},{r.received},{F(r.downMbps)},{F(r.downMB)},{F(r.downSec)}");
            Append(sessionPath, $"{DateTime.Now:O},lab,,{Esc(r.name)},{Esc(r.net)},{Esc(r.slice)},{F(r.rttAvg)},{F(r.rttP95)},{F(r.jitter)},,{F(r.downMbps * 1000f)},,{F(r.loss)},,,{Esc(r.device)},");
        }
    }

    /// UDP echo for the Network Lab ping burst.
    public class EchoServer
    {
        UdpClient udp;
        Thread thread;
        volatile bool running;
        public long Echoed;

        public void Start(int port)
        {
            try
            {
                udp = new UdpClient(new IPEndPoint(IPAddress.Any, port));
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "UdpEcho" };
                thread.Start();
            }
            catch (Exception e) { Debug.LogError($"[Echo] cannot bind UDP {port}: {e.Message}"); }
        }

        void Loop()
        {
            var any = new IPEndPoint(IPAddress.Any, 0);
            while (running)
            {
                try
                {
                    var data = udp.Receive(ref any);
                    udp.Send(data, data.Length, any);
                    Interlocked.Increment(ref Echoed);
                }
                catch (SocketException) { }
                catch (ObjectDisposedException) { return; }
            }
        }

        public void Stop() { running = false; try { udp?.Close(); } catch { } }
    }

    /// Generates the downloadable Content Pack ("DLC"): tuning, server info, high-res water normal maps, raft skin.
    public class ContentPack
    {
        public readonly string dir;
        public volatile string manifestJson;
        public volatile bool ready;

        public ContentPack(string root) { dir = Path.Combine(root, "content"); Directory.CreateDirectory(dir); }

        public void BuildAsync(string tuningJson, ServerJson server)
        {
            File.WriteAllText(Path.Combine(dir, "tuning.json"), tuningJson);
            File.WriteAllText(Path.Combine(dir, "server.json"), JsonUtility.ToJson(server, true));
            new Thread(() =>
            {
                try
                {
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    EnsureNormalMap("water_normal_a_2048.rgba", 2048, 1234, 1.0f);
                    EnsureNormalMap("water_normal_b_2048.rgba", 2048, 98765, 0.6f);
                    EnsureSkin("raft_skin_1024.rgba", 1024);
                    var files = new List<ContentFile>();
                    long total = 0;
                    foreach (var name in new[] { "tuning.json", "server.json", "water_normal_a_2048.rgba", "water_normal_b_2048.rgba", "raft_skin_1024.rgba" })
                    {
                        var p = Path.Combine(dir, name);
                        var info = new FileInfo(p);
                        files.Add(new ContentFile { name = name, size = info.Length, hash = Hash(p) });
                        total += info.Length;
                    }
                    var m = new ContentManifest { version = DateTime.Now.ToString("yyyyMMdd"), totalBytes = total, files = files.ToArray() };
                    manifestJson = JsonUtility.ToJson(m);
                    ready = true;
                    Debug.Log($"[Content] pack ready: {total / 1048576f:F1} MB in {sw.ElapsedMilliseconds} ms");
                }
                catch (Exception e) { Debug.LogError("[Content] build failed: " + e); }
            }) { IsBackground = true, Name = "ContentPack" }.Start();
        }

        static string Hash(string path)
        {
            using var md5 = MD5.Create();
            using var fs = File.OpenRead(path);
            var h = md5.ComputeHash(fs);
            var sb = new StringBuilder(32);
            foreach (var b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        void EnsureNormalMap(string name, int size, int seed, float strength)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path) && new FileInfo(path).Length == size * size * 4) return;
            var rng = new System.Random(seed);
            const int waves = 14;
            var kx = new int[waves]; var ky = new int[waves]; var amp = new float[waves]; var ph = new int[waves];
            for (int i = 0; i < waves; i++)
            {
                float f = 2f + i * 1.7f;
                double ang = rng.NextDouble() * Math.PI * 2;
                kx[i] = (int)Math.Round(Math.Cos(ang) * f);
                ky[i] = (int)Math.Round(Math.Sin(ang) * f);
                if (kx[i] == 0 && ky[i] == 0) kx[i] = 1;
                amp[i] = 1f / (1f + i * 0.6f);
                ph[i] = rng.Next(size);
            }
            var cosT = new float[size];
            for (int i = 0; i < size; i++) cosT[i] = (float)Math.Cos(2 * Math.PI * i / size);
            var data = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float du = 0, dv = 0;
                    for (int i = 0; i < waves; i++)
                    {
                        int idx = ((kx[i] * x + ky[i] * y + ph[i]) % size + size) % size;
                        float c = cosT[idx] * amp[i];
                        du += c * kx[i];
                        dv += c * ky[i];
                    }
                    uint hsh = (uint)(x * 73856093 ^ y * 19349663 ^ seed);
                    hsh ^= hsh >> 13; hsh *= 0x5bd1e995; hsh ^= hsh >> 15;
                    float noise = ((hsh & 0xFF) / 255f - 0.5f) * 0.08f;
                    float nx = -du * 0.045f * strength + noise, ny = -dv * 0.045f * strength - noise, nz = 1f;
                    float inv = 1f / (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                    int o = (y * size + x) * 4;
                    data[o] = (byte)((nx * inv * 0.5f + 0.5f) * 255);
                    data[o + 1] = (byte)((ny * inv * 0.5f + 0.5f) * 255);
                    data[o + 2] = (byte)((nz * inv * 0.5f + 0.5f) * 255);
                    data[o + 3] = 255;
                }
            File.WriteAllBytes(path, data);
        }

        void EnsureSkin(string name, int size)
        {
            var path = Path.Combine(dir, name);
            if (File.Exists(path) && new FileInfo(path).Length == size * size * 4) return;
            var data = new byte[size * size * 4];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool stripe = ((x + y / 4) / 96) % 2 == 0;
                    uint h = (uint)(x * 2654435761u ^ y * 40503u);
                    h ^= h >> 15;
                    int n = (int)(h & 15) - 8;
                    int o = (y * size + x) * 4;
                    data[o] = (byte)Mathf.Clamp((stripe ? 235 : 255) + n, 0, 255);
                    data[o + 1] = (byte)Mathf.Clamp((stripe ? 90 : 210) + n, 0, 255);
                    data[o + 2] = (byte)Mathf.Clamp((stripe ? 40 : 60) + n, 0, 255);
                    data[o + 3] = 255;
                }
            File.WriteAllBytes(path, data);
        }
    }

    public static class NetUtil
    {
        public static List<string> LocalIPv4()
        {
            var list = new List<string>();
            var virt = new List<string>();
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = ni.GetIPProperties();
                    bool hasGateway = false;
                    foreach (var g in props.GatewayAddresses)
                        if (g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any)) hasGateway = true;
                    string desc = (ni.Description + " " + ni.Name).ToLowerInvariant();
                    bool isVirtual = desc.Contains("virtualbox") || desc.Contains("vmware") || desc.Contains("hyper-v") || desc.Contains("vethernet")
                                     || desc.Contains("host-only") || desc.Contains("wsl") || desc.Contains("bluetooth") || desc.Contains("docker");
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        var ip = ua.Address.ToString();
                        if (ip.StartsWith("169.254.")) continue; // APIPA, unreachable
                        if (hasGateway && !isVirtual) list.Add($"{ip}  ({ni.Name}, LAN)");
                        else virt.Add($"{ip}  ({ni.Name}, {(isVirtual ? "virtual - not for phones" : "no gateway")})");
                    }
                }
            }
            catch (Exception e) { Debug.LogWarning("[Net] interface enumeration failed: " + e.Message); }
            if (list.Count == 0)
            {
                try
                {
                    foreach (var a in Dns.GetHostEntry(Dns.GetHostName()).AddressList)
                        if (a.AddressFamily == AddressFamily.InterNetwork) list.Add(a.ToString());
                }
                catch { }
            }
            list.AddRange(virt);
            return list;
        }

        public static string FirstIp(List<string> ips) => ips.Count > 0 ? ips[0].Split(' ')[0] : "127.0.0.1";
    }
}
