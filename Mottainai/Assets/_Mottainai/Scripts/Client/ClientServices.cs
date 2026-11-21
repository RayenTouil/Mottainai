using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Debug = UnityEngine.Debug;

namespace Mottainai.Client
{
    /// Procedural SFX (fallback when no CC0 audio in Resources/Audio).
    public class Sfx : MonoBehaviour
    {
        public enum Id { Splash, Catch, Valuable, Toxic, Miss, Hit, Shield, Beep, Go, Sync, Throw, Combo }
        AudioClip[] clips;
        AudioSource src;
        const int Rate = 22050;

        void Awake()
        {
            src = gameObject.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            var names = Enum.GetNames(typeof(Id));
            clips = new AudioClip[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                var c = Resources.Load<AudioClip>("Audio/" + names[i].ToLowerInvariant());
                clips[i] = c != null ? c : Make((Id)i);
            }
        }

        public void Play(Id id, float vol = 1f) { if (clips != null) src.PlayOneShot(clips[(int)id], vol); }

        static AudioClip Make(Id id)
        {
            float len = id switch { Id.Splash => 0.3f, Id.Hit => 0.6f, Id.Shield => 0.6f, Id.Go => 0.4f, Id.Sync => 0.35f, Id.Valuable => 0.5f, Id.Combo => 0.4f, _ => 0.18f };
            int n = (int)(Rate * len);
            var data = new float[n];
            var rng = new System.Random((int)id + 7);
            float lp = 0f;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / Rate, k = (float)i / n;
                float env = Mathf.Exp(-k * 5f) * Mathf.Clamp01(t * 200f);
                float noise = (float)rng.NextDouble() * 2f - 1f;
                float v;
                switch (id)
                {
                    case Id.Splash: lp += (noise - lp) * 0.25f; v = lp * env * 1.6f; break;
                    case Id.Catch: v = Mathf.Sin(2 * Mathf.PI * t * (660 + 600 * k)) * env * 0.5f; break;
                    case Id.Valuable: v = (Mathf.Sin(2 * Mathf.PI * t * 988) + Mathf.Sin(2 * Mathf.PI * t * 1319 * (k > 0.3f ? 1.5f : 1f))) * env * 0.3f; break;
                    case Id.Toxic: v = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * t * (220 - 90 * k))) * env * 0.25f; break;
                    case Id.Miss: v = Mathf.Sin(2 * Mathf.PI * t * (420 - 200 * k)) * env * 0.35f; break;
                    case Id.Hit: lp += (noise - lp) * 0.08f; v = (lp * 1.5f + Mathf.Sin(2 * Mathf.PI * t * (90 - 40 * k))) * Mathf.Exp(-k * 4f) * 0.7f; break;
                    case Id.Shield: v = Mathf.Sin(2 * Mathf.PI * t * (520 + 520 * Mathf.Floor(k * 4f) / 4f)) * Mathf.Exp(-k * 2f) * 0.35f; break;
                    case Id.Beep: v = Mathf.Sin(2 * Mathf.PI * t * 880) * env * 0.4f; break;
                    case Id.Go: v = Mathf.Sin(2 * Mathf.PI * t * 1320) * Mathf.Exp(-k * 3f) * 0.45f; break;
                    case Id.Sync: v = (Mathf.Sin(2 * Mathf.PI * t * 523) + Mathf.Sin(2 * Mathf.PI * t * 659) + Mathf.Sin(2 * Mathf.PI * t * 784)) * env * 0.22f; break;
                    case Id.Throw: lp += (noise - lp) * 0.5f; v = lp * Mathf.Sin(Mathf.PI * k) * 0.4f; break;
                    case Id.Combo: v = Mathf.Sin(2 * Mathf.PI * t * (880 * (1 + Mathf.Floor(k * 3f) * 0.25f))) * Mathf.Exp(-k * 2f) * 0.35f; break;
                    default: v = 0; break;
                }
                data[i] = Mathf.Clamp(v, -1f, 1f);
            }
            var clip = AudioClip.Create(id.ToString(), n, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }

    /// Drops URP render scale to 0.8 if FPS < 45 for 3 s.
    public class PerfGovernor : MonoBehaviour
    {
        float low, fps = 60f;
        bool dropped;
        float originalScale = 1f;
        UniversalRenderPipelineAsset asset;

        void Start()
        {
            asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null) originalScale = asset.renderScale;
        }

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            if (dt <= 0f) return;
            fps = Mathf.Lerp(fps, 1f / dt, 0.05f);
            if (dropped || asset == null) return;
            low = fps < 45f ? low + dt : 0f;
            if (low > 3f)
            {
                dropped = true;
                asset.renderScale = 0.8f;
                Debug.Log("[Perf] FPS < 45 for 3 s -> render scale 0.8");
            }
        }

        void OnDestroy() { if (dropped && asset != null) asset.renderScale = originalScale; }
        void OnApplicationQuit() => OnDestroy();
        public bool Dropped => dropped;
    }

    /// Network Lab: 10 s UDP echo burst @100 Hz + 20 MB HTTP throughput test.
    public class NetworkLab
    {
        public bool Running { get; private set; }
        public string Phase = "";
        public float Progress;
        public LabResultDto Result;
        public string CsvPath => Path.Combine(Application.persistentDataPath, "network_lab.csv");

        class DiscardHandler : DownloadHandlerScript
        {
            public long bytes;
            public DiscardHandler() : base(new byte[64 * 1024]) { }
            protected override bool ReceiveData(byte[] data, int dataLength) { bytes += dataLength; return true; }
        }

        public IEnumerator Run(string host, string httpBase, string playerName, string net, string slice)
        {
            Running = true;
            Result = null;
            Progress = 0;
            Phase = "Ping burst (UDP echo, 100 Hz, 10 s)...";
            const int count = 1000;
            var rtts = new float[count];
            int received = 0, sent = 0;
            bool done = false;
            string ip = host;
            try { if (!IPAddress.TryParse(host, out _)) foreach (var a in Dns.GetHostAddresses(host)) if (a.AddressFamily == AddressFamily.InterNetwork) { ip = a.ToString(); break; } }
            catch { }

            var thread = new Thread(() =>
            {
                try
                {
                    using var udp = new UdpClient();
                    udp.Client.ReceiveTimeout = 50;
                    udp.Connect(ip, NetConst.EchoPort);
                    var sw = Stopwatch.StartNew();
                    var recvThread = new Thread(() =>
                    {
                        var ep = new IPEndPoint(IPAddress.Any, 0);
                        while (!done)
                        {
                            try
                            {
                                var d = udp.Receive(ref ep);
                                if (d.Length < 12) continue;
                                int seq = BitConverter.ToInt32(d, 0);
                                long t0 = BitConverter.ToInt64(d, 4);
                                if (seq >= 0 && seq < count && rtts[seq] == 0)
                                {
                                    rtts[seq] = (float)((sw.ElapsedTicks - t0) * 1000.0 / Stopwatch.Frequency);
                                    Interlocked.Increment(ref received);
                                }
                            }
                            catch (SocketException) { }
                            catch (ObjectDisposedException) { return; }
                        }
                    }) { IsBackground = true };
                    recvThread.Start();
                    var buf = new byte[32];
                    for (int i = 0; i < count; i++)
                    {
                        long due = (long)(i * 0.01 * Stopwatch.Frequency);
                        while (sw.ElapsedTicks < due) Thread.Sleep(1);
                        BitConverter.GetBytes(i).CopyTo(buf, 0);
                        BitConverter.GetBytes(sw.ElapsedTicks).CopyTo(buf, 4);
                        udp.Send(buf, buf.Length);
                        sent = i + 1;
                    }
                    Thread.Sleep(500);
                }
                catch (Exception e) { Debug.LogWarning("[Lab] ping failed: " + e.Message); }
                done = true;
            }) { IsBackground = true };
            thread.Start();
            while (!done) { Progress = sent / (float)count * 0.5f; yield return null; }

            int got = 0; float sum = 0, mn = float.MaxValue, mx = 0, jit = 0, prev = -1;
            var list = new System.Collections.Generic.List<float>(count);
            for (int i = 0; i < count; i++)
            {
                if (rtts[i] <= 0) continue;
                got++; sum += rtts[i]; mn = Mathf.Min(mn, rtts[i]); mx = Mathf.Max(mx, rtts[i]);
                if (prev >= 0) jit += Mathf.Abs(rtts[i] - prev);
                prev = rtts[i];
                list.Add(rtts[i]);
            }
            list.Sort();
            var r = new LabResultDto
            {
                name = playerName, device = SystemInfo.deviceModel, net = net, slice = slice, time = DateTime.Now.ToString("O"),
                sent = sent, received = got, rttAvg = got > 0 ? sum / got : 0, rttMin = got > 0 ? mn : 0, rttMax = mx,
                rttP95 = got > 0 ? list[Mathf.Clamp(Mathf.CeilToInt(got * 0.95f) - 1, 0, got - 1)] : 0,
                jitter = got > 1 ? jit / (got - 1) : 0, loss = sent > 0 ? (1f - got / (float)sent) * 100f : 100f
            };

            Phase = "Throughput (download 20 MB)...";
            var dh = new DiscardHandler();
            using (var req = new UnityWebRequest(httpBase + "/lab/download?mb=20", UnityWebRequest.kHttpVerbGET, dh, null))
            {
                req.timeout = 90;
                float t0 = Time.realtimeSinceStartup;
                var op = req.SendWebRequest();
                while (!op.isDone) { Progress = 0.5f + Mathf.Clamp01(dh.bytes / (20f * 1048576f)) * 0.5f; yield return null; }
                float secs = Time.realtimeSinceStartup - t0;
                r.downMB = dh.bytes / 1048576f;
                r.downSec = secs;
                r.downMbps = secs > 0 ? dh.bytes * 8f / 1e6f / secs : 0;
            }
            Progress = 1f;
            Result = r;
            SaveCsv(r);
            using (var post = UnityWebRequest.Post(httpBase + "/lab/result", JsonUtility.ToJson(r), "application/json"))
            {
                post.timeout = 5;
                yield return post.SendWebRequest();
            }
            Phase = "Done";
            Running = false;
        }

        void SaveCsv(LabResultDto r)
        {
            try
            {
                bool header = !File.Exists(CsvPath);
                using var sw = new StreamWriter(CsvPath, true);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (header) sw.WriteLine("time,device,net,slice,rtt_avg,rtt_min,rtt_p95,rtt_max,jitter,loss,sent,received,down_mbps,down_mb,down_sec");
                sw.WriteLine(string.Join(",", r.time, r.device?.Replace(",", ";"), r.net, r.slice, r.rttAvg.ToString("F2", ci), r.rttMin.ToString("F2", ci), r.rttP95.ToString("F2", ci),
                    r.rttMax.ToString("F2", ci), r.jitter.ToString("F2", ci), r.loss.ToString("F2", ci), r.sent, r.received, r.downMbps.ToString("F2", ci), r.downMB.ToString("F2", ci), r.downSec.ToString("F2", ci)));
            }
            catch (Exception e) { Debug.LogWarning("[Lab] csv: " + e.Message); }
        }
    }
}
