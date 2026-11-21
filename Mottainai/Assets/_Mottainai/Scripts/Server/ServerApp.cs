using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Mottainai.Net;
using Unity.Netcode;
using UnityEngine;

namespace Mottainai.Server
{
    public static class ServerEntry
    {
        [UnityEngine.Scripting.Preserve, RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register()
        {
            AppHooks.StartServer = args =>
            {
                if (AppHooks.ServerRunning) return true;
                var go = new GameObject("MottainaiServer");
                UnityEngine.Object.DontDestroyOnLoad(go);
                var app = go.AddComponent<ServerApp>();
                return app.Launch(args);
            };
        }
    }

    public class ServerApp : MonoBehaviour
    {
        LaunchArgs args;
        RoomManager rooms;
        HttpApi http;
        ContentPack content;
        SessionLog log;
        float acc, statusTimer, consoleTimer = 5f, avgRtt;
        int ticksThisSecond, measuredTickRate;
        float tickSecond;
        List<string> ips = new List<string>();
        string banner = "";
        long lastBytes;
        float kbpsOut;
        GUIStyle big, small;

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        static extern uint SetThreadExecutionState(uint esFlags);
#endif
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern bool AttachConsole(uint pid);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern IntPtr GetStdHandle(int which);
        [System.Runtime.InteropServices.DllImport("kernel32.dll")] static extern uint GetFileType(IntPtr h);
        [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sec, uint disposition, uint flags, IntPtr template);
#endif

        /// The Windows player is a GUI exe: without this, Console output never reaches the run_server.bat window.
        static void AttachParentConsole()
        {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
            try
            {
                var std = GetStdHandle(-11);
                if (std != IntPtr.Zero && std != new IntPtr(-1) && GetFileType(std) != 0) return; // stdout already usable/redirected
                if (!AttachConsole(0xFFFFFFFF)) return; // no parent console (double-click)
                var con = CreateFileW("CONOUT$", 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (con == new IntPtr(-1)) return;
                var fs = new FileStream(new Microsoft.Win32.SafeHandles.SafeFileHandle(con, true), FileAccess.Write, 1);
                Console.SetOut(new StreamWriter(fs) { AutoFlush = true });
            }
            catch (Exception e) { Debug.LogWarning("[Server] console attach: " + e.Message); }
#endif
        }

        /// Windows: no system sleep while the server runs (main thread; display may still turn off).
        static void KeepAwake(bool on)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            const uint ES_CONTINUOUS = 0x80000000, ES_SYSTEM_REQUIRED = 0x00000001;
            try
            {
                if (SetThreadExecutionState(on ? ES_CONTINUOUS | ES_SYSTEM_REQUIRED : ES_CONTINUOUS) == 0) Debug.LogWarning("[Server] SetThreadExecutionState failed");
                else if (on) Debug.Log("[Server] Windows sleep disabled while the server runs");
            }
            catch (Exception e) { Debug.LogWarning("[Server] SetThreadExecutionState: " + e.Message); }
#endif
        }

        public bool Launch(LaunchArgs a)
        {
            args = a;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ServerData"));
            Directory.CreateDirectory(root);
            log = new SessionLog(root);
            var tuningOverride = Path.Combine(root, "tuning.json");
            if (File.Exists(tuningOverride)) GameTuning.ApplyJson(File.ReadAllText(tuningOverride));
            else File.WriteAllText(tuningOverride, GameTuning.ToJson());

            rooms = new RoomManager(log);
            ips = NetUtil.LocalIPv4();

            var nm = NetBus.EnsureManager();
            nm.NetworkConfig.TickRate = (uint)Mathf.Clamp(a.tick, 10, 120);
            NetBus.Transport.SetConnectionData(true, "0.0.0.0", a.port, "0.0.0.0");
            NetBus.OnServer = OnMessage;
            nm.OnClientDisconnectCallback += id => { if (nm.IsServer && id != nm.LocalClientId) rooms.Disconnected(id); };

            bool ok = a.host ? nm.StartHost() : nm.StartServer();
            if (!ok) { Debug.LogError($"[Server] failed to start on UDP {a.port}"); Destroy(gameObject); return false; }
            AppHooks.ServerRunning = true;
            AppHooks.HostMode = a.host;
            AppHooks.ServerStatus = () => banner;

            content = new ContentPack(root);
            content.BuildAsync(GameTuning.ToJson(), new ServerJson());
            http = new HttpApi(rooms, content, log) { gamePort = a.port };
            bool httpOk = http.Start(a.http);
            KeepAwake(true);
            if (!a.host) AttachParentConsole();

            var sb = new StringBuilder();
            sb.AppendLine("==========================================================");
            sb.AppendLine("  M O T T A I N A I   S E R V E R   (what a waste!)");
            sb.AppendLine("==========================================================");
            sb.AppendLine($"  Game UDP 0.0.0.0:{a.port}   API HTTP {(httpOk ? http.Bound : $"FAILED on TCP {a.http} (port in use?)")}   Tick {nm.NetworkConfig.TickRate} Hz");
            sb.AppendLine($"  Waiting for playit tunnels:  Custom UDP -> 127.0.0.1:{a.port}   TCP -> 127.0.0.1:{a.http}");
            sb.AppendLine("  Phones: type the PUBLIC playit addresses (game host + port, API host + port). See PLAYIT.md");
            if (ips.Count > 0) sb.AppendLine("  LAN/dev: " + string.Join("   ", ips));
            sb.AppendLine($"  Logs: {log.dir}");
            sb.AppendLine("==========================================================");
            banner = sb.ToString();
            Debug.Log("\n" + banner);
            Console.WriteLine(banner);
            File.WriteAllText(Path.Combine(root, "server_status.txt"), banner);
            log.Event("server_start", "", $"udp={a.port} http={a.http} ips={string.Join("|", ips)}");

            if (a.headless) Application.targetFrameRate = 240;
            else if (!a.host) Application.targetFrameRate = 120;
            return true;
        }

        void OnMessage(ulong sender, Msg type, FastBufferReader r)
        {
            try
            {
                if (type == Msg.LabEcho) // Network Lab ping burst: echoed on the game socket, no session needed
                {
                    r.ReadValueSafe(out int seq);
                    r.ReadValueSafe(out double ct);
                    var w = NetBus.Begin(Msg.LabEchoReply, 16);
                    w.WriteValueSafe(seq);
                    w.WriteValueSafe(ct);
                    NetBus.ToClient(sender, w, NetworkDelivery.Unreliable);
                    w.Dispose();
                    return;
                }
                if (type == Msg.Hello)
                {
                    r.ReadValueSafe(out string token);
                    r.ReadValueSafe(out string name);
                    rooms.Hello(sender, token, name);
                    return;
                }
                var s = rooms.Get(sender);
                if (s == null) return;
                s.lastSeen = Time.realtimeSinceStartup;
                var room = s.room;
                switch (type)
                {
                    case Msg.Ping:
                    {
                        r.ReadValueSafe(out double ct);
                        var w = NetBus.Begin(Msg.Pong, 32);
                        w.WriteValueSafe(ct);
                        w.WriteValueSafe(room?.sim?.Tick ?? 0u);
                        NetBus.ToClient(sender, w, NetworkDelivery.Unreliable);
                        w.Dispose();
                        break;
                    }
                    case Msg.Input:
                    {
                        r.ReadValueSafe(out uint _);
                        r.ReadValueSafe(out ushort interp);
                        s.interpMs = interp;
                        break;
                    }
                    case Msg.Stroke:
                    {
                        r.ReadValueSafe(out float power);
                        r.ReadValueSafe(out bool back);
                        if (room?.sim != null) room.sim.ApplyStroke(room.sim.Teams[0], s.role, power, back);
                        break;
                    }
                    case Msg.Throw:
                    {
                        r.ReadValueSafe(out ushort target);
                        r.ReadValueSafe(out float dx);
                        r.ReadValueSafe(out float dz);
                        r.ReadValueSafe(out ushort interpMs);
                        if (room?.sim != null && s.role == Role.Collector)
                        {
                            float rtt = sender == NetBus.NM.LocalClientId && NetBus.IsHost ? 0f : NetBus.Transport.GetCurrentRtt(sender);
                            float rewindMs = Mathf.Min(rtt * 0.5f + interpMs, GameTuning.Current.lagCompCapMs);
                            room.sim.ApplyThrow(room.sim.Teams[0], target, dx, dz, rewindMs / 1000f);
                        }
                        break;
                    }
                    case Msg.CreateRoom: rooms.CreateRoom(s); break;
                    case Msg.JoinRoom: r.ReadValueSafe(out string code); rooms.JoinRoom(s, code); break;
                    case Msg.PickRole: r.ReadValueSafe(out byte role); rooms.PickRole(s, (Role)role); break;
                    case Msg.SetReady: r.ReadValueSafe(out bool ready); rooms.SetReady(s, ready); break;
                    case Msg.StartMatch: rooms.StartMatch(s); break;
                    case Msg.LeaveRoom: rooms.Leave(s); break;
                    case Msg.BackToLobby: rooms.BackToLobby(s); break;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Server] bad message {type} from {sender}: {e.Message}"); }
        }

        void Update()
        {
            if (rooms == null) return;
            float dt = 1f / Mathf.Max(10, args.tick);
            acc += Mathf.Min(Time.unscaledDeltaTime, 0.25f);
            while (acc >= dt)
            {
                acc -= dt;
                rooms.Tick(dt);
                ticksThisSecond++;
            }
            tickSecond += Time.unscaledDeltaTime;
            if (tickSecond >= 1f)
            {
                measuredTickRate = ticksThisSecond;
                ticksThisSecond = 0;
                kbpsOut = (NetBus.BytesSent - lastBytes) * 8f / 1000f / tickSecond;
                lastBytes = NetBus.BytesSent;
                tickSecond = 0f;
            }
            statusTimer -= Time.unscaledDeltaTime;
            if (statusTimer <= 0f)
            {
                statusTimer = 0.5f;
                http.roomsJson = rooms.RoomsJson();
                http.statusJson = $"{{\"tick\":{measuredTickRate},\"rooms\":{rooms.rooms.Count},\"clients\":{rooms.byClient.Count},\"kbpsOut\":{kbpsOut:F0},\"httpBytes\":{http.BytesServed},\"content\":{(content.ready ? "true" : "false")}}}";
            }
            consoleTimer -= Time.unscaledDeltaTime;
            if (consoleTimer <= 0f && !args.host)
            {
                consoleTimer = 5f;
                var nm = NetBus.NM;
                float sum = 0; int n = 0;
                foreach (var id in rooms.byClient.Keys)
                {
                    ulong rtt = nm.IsHost && id == nm.LocalClientId ? 0 : NetBus.Transport.GetCurrentRtt(id);
                    if (rtt > 0) { sum += rtt; n++; }
                }
                avgRtt = n > 0 ? sum / n : 0f;
                Console.WriteLine($"[STATUS] {DateTime.Now:HH:mm:ss}  players {rooms.byClient.Count}  rooms {rooms.rooms.Count}  tick {measuredTickRate} Hz  avg RTT {avgRtt:F0} ms  out {kbpsOut:F0} kbps");
            }
        }

        void OnGUI()
        {
            if (args == null || args.headless || args.host) return;
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 1f, 0.6f) } };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUILayout.BeginArea(new Rect(12, 12, Screen.width - 24, Screen.height - 24));
            GUILayout.Label("MOTTAINAI SERVER", big);
            GUILayout.Label($"  UDP {args.port} + TCP {args.http} on 0.0.0.0  -  expose with playit tunnels (PLAYIT.md)", big);
            if (ips.Count > 0) GUILayout.Label("  LAN/dev: " + string.Join("   ", ips), small);
            GUILayout.Space(8);
            GUILayout.Label($"Tick {measuredTickRate} Hz   Players {rooms.byClient.Count}   Rooms {rooms.rooms.Count}   avg RTT {avgRtt:F0} ms   Out {kbpsOut:F0} kbps   Content {(content.ready ? "ready" : "building...")}", small);
            foreach (var room in rooms.rooms.Values)
            {
                var sb = new StringBuilder($"  [{room.code}] {room.Phase}  ");
                foreach (var m in room.members) sb.Append($"{m.name}:{RoleInfo.Letter(m.role)}{(m.Connected ? "" : "(off)")}  ");
                if (room.sim != null) sb.Append($"t={room.sim.Time:F0}s score={room.sim.Teams[0].score.catchScore}");
                GUILayout.Label(sb.ToString(), small);
            }
            GUILayout.EndArea();
        }

        void OnApplicationQuit()
        {
            http?.Stop();
            KeepAwake(false);
            log?.Event("server_stop", "", "");
        }
    }
}
