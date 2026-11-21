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
        EchoServer echo;
        ContentPack content;
        SessionLog log;
        float acc, statusTimer;
        int ticksThisSecond, measuredTickRate;
        float tickSecond;
        List<string> ips = new List<string>();
        string banner = "";
        long lastBytes;
        float kbpsOut;
        GUIStyle big, small;

        public bool Launch(LaunchArgs a)
        {
            args = a;
            string root = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "ServerData"));
            Directory.CreateDirectory(root);
            log = new SessionLog(root);
            // Optional hand-written override (only the fields you put in it). Effective values are dumped for reference.
            var tuningOverride = Path.Combine(root, "tuning_override.json");
            if (File.Exists(tuningOverride)) { GameTuning.ApplyJson(File.ReadAllText(tuningOverride)); Debug.Log("[Server] applied " + tuningOverride); }
            File.WriteAllText(Path.Combine(root, "tuning_current.json"), GameTuning.ToJson());

            Room.TickRate = Mathf.Clamp(a.tick, 10, 120);
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
            content.BuildAsync(GameTuning.ToJson(), new ServerJson { address = NetUtil.FirstIp(ips), gamePort = a.port, httpPort = a.http });
            http = new HttpApi(rooms, content, log) { gamePort = a.port };
            http.Start(a.http);
            echo = new EchoServer();
            echo.Start(NetConst.EchoPort);

            var sb = new StringBuilder();
            sb.AppendLine("==========================================================");
            sb.AppendLine("  M O T T A I N A I   S E R V E R   (what a waste!)");
            sb.AppendLine("==========================================================");
            sb.AppendLine($"  Game  UDP {a.port}   HTTP TCP {a.http}   Echo UDP {NetConst.EchoPort}   Tick {nm.NetworkConfig.TickRate} Hz");
            sb.AppendLine("  Type one of these addresses on the phones:");
            foreach (var ip in ips) sb.AppendLine("     >>>  " + ip);
            sb.AppendLine($"  Logs: {log.dir}");
            sb.AppendLine("==========================================================");
            banner = sb.ToString();
            Debug.Log("\n" + banner);
            Console.WriteLine(banner);
            File.WriteAllText(Path.Combine(root, "server_status.txt"), banner);
            log.Event("server_start", "", $"udp={a.port} http={a.http} ips={string.Join("|", ips)}");

            // high server frame rate: inputs wait <= ~4 ms before being processed, snapshots leave on time
            if (!a.host) Application.targetFrameRate = 240;
            return true;
        }

        void OnMessage(ulong sender, Msg type, FastBufferReader r)
        {
            try
            {
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
        }

        void OnGUI()
        {
            if (args == null || args.headless || args.host) return;
            big ??= new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = new Color(0.6f, 1f, 0.6f) } };
            small ??= new GUIStyle(GUI.skin.label) { fontSize = 15, normal = { textColor = Color.white } };
            GUILayout.BeginArea(new Rect(12, 12, Screen.width - 24, Screen.height - 24));
            GUILayout.Label("MOTTAINAI SERVER", big);
            foreach (var ip in ips) GUILayout.Label($"  {ip}   UDP {args.port} / HTTP {args.http}", big);
            GUILayout.Space(8);
            GUILayout.Label($"Tick {measuredTickRate} Hz   Clients {rooms.byClient.Count}   Rooms {rooms.rooms.Count}   Out {kbpsOut:F0} kbps   Content {(content.ready ? "ready" : "building...")}", small);
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
            echo?.Stop();
            log?.Event("server_stop", "", "");
        }
    }
}
