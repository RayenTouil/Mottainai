using System;
using System.Collections.Generic;
using Mottainai.Gameplay;
using Mottainai.Net;
using Unity.Netcode;
using UnityEngine;

namespace Mottainai.Client
{
    public class NetStats
    {
        readonly float[] rtt = new float[100];
        readonly float[] sorted = new float[100];
        int rttCount, rttHead;
        public float RttAvg, RttP95, RttMin, RttMax, Jitter;
        public float DownKbps, UpKbps, Pps, LossPct, SnapshotRate, Fps;
        public float TotalDownMB, TotalUpMB;
        public uint ServerTick;
        public float InterpMs;
        long lastRecv, lastSent, lastMsgs;
        float window;
        int snapshots;
        uint seqMin = uint.MaxValue, seqMax;
        int seqCount;
        float fpsAcc; int fpsFrames;
        // whole-match aggregates for the results card
        public double sumRtt; public int nRtt; public float maxP95, sumLoss; public int nLoss; public float sumDown; public int nDown;

        public void AddRtt(float ms)
        {
            rtt[rttHead] = ms;
            rttHead = (rttHead + 1) % rtt.Length;
            rttCount = Mathf.Min(rttCount + 1, rtt.Length);
            float sum = 0, jit = 0, mn = float.MaxValue, mx = 0;
            for (int i = 0; i < rttCount; i++)
            {
                float v = rtt[i];
                sum += v; mn = Mathf.Min(mn, v); mx = Mathf.Max(mx, v);
                sorted[i] = v;
                if (i > 0) jit += Mathf.Abs(rtt[i] - rtt[i - 1]);
            }
            RttAvg = sum / rttCount; RttMin = mn; RttMax = mx;
            Jitter = rttCount > 1 ? jit / (rttCount - 1) : 0;
            Array.Sort(sorted, 0, rttCount);
            RttP95 = sorted[Mathf.Clamp(Mathf.CeilToInt(rttCount * 0.95f) - 1, 0, rttCount - 1)];
            sumRtt += ms; nRtt++;
        }

        public void OnSnapshotChunk(uint seq, bool first)
        {
            if (first) snapshots++;
            seqMin = Math.Min(seqMin, seq);
            seqMax = Math.Max(seqMax, seq);
            seqCount++;
        }

        public void ResetAggregates() { sumRtt = 0; nRtt = 0; maxP95 = 0; sumLoss = 0; nLoss = 0; sumDown = 0; nDown = 0; }

        public void Update(float dt)
        {
            window += dt;
            fpsAcc += dt; fpsFrames++;
            if (fpsAcc >= 0.5f) { Fps = fpsFrames / fpsAcc; fpsAcc = 0; fpsFrames = 0; }
            if (window < 1f) return;
            long recv = NetBus.BytesRecv, sent = NetBus.BytesSent, msgs = NetBus.MsgsRecv + NetBus.MsgsSent;
            DownKbps = (recv - lastRecv) * 8f / 1000f / window;
            UpKbps = (sent - lastSent) * 8f / 1000f / window;
            Pps = (msgs - lastMsgs) / window;
            TotalDownMB = recv / 1048576f; TotalUpMB = sent / 1048576f;
            lastRecv = recv; lastSent = sent; lastMsgs = msgs;
            SnapshotRate = snapshots / window;
            if (seqCount > 0)
            {
                long expected = (long)seqMax - seqMin + 1;
                LossPct = expected > 0 ? Mathf.Clamp01(1f - (float)seqCount / expected) * 100f : 0f;
                sumLoss += LossPct; nLoss++;
                sumDown += DownKbps; nDown++;
            }
            if (RttP95 > maxP95) maxP95 = RttP95;
            snapshots = 0; seqCount = 0; seqMin = uint.MaxValue; seqMax = 0;
            window = 0;
        }
    }

    /// Owns the NGO client connection + message dispatch.
    public class NetClient
    {
        public readonly NetStats stats = new NetStats();
        public event Action<ulong, string> OnWelcome;
        public event Action<LobbyState> OnLobby;
        public event Action<string> OnError;
        public event Action<MatchStartInfo> OnMatchStart;
        public event Action<SnapshotFrame> OnSnapshot;
        public event Action<List<MatchEvent>> OnEvents;
        public event Action<ResultsData> OnResults;
        public event Action<string> OnDisconnected;
        public event Action OnConnected;

        public string token, playerName, address;
        public ushort port;
        public ulong ClientId { get; private set; } = ulong.MaxValue;
        public bool Welcomed { get; private set; }
        public bool WantConnected { get; private set; }

        readonly SnapshotFrame frame = new SnapshotFrame();
        readonly List<MatchEvent> evBuffer = new List<MatchEvent>(64);
        float pingTimer, inputTimer;
        uint inputSeq;
        public int TickRate = NetConst.TickRate;
        float TickDt => 1f / Mathf.Max(10, TickRate);
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public double Now => clock.Elapsed.TotalSeconds;

        public void Connect(string addr, ushort gamePort, string tok, string name)
        {
            address = addr; port = gamePort; token = tok; playerName = name;
            WantConnected = true;
            Welcomed = false;
            var nm = NetBus.EnsureManager();
            NetBus.OnClient = Dispatch;
            nm.OnClientConnectedCallback -= HandleConnected;
            nm.OnClientConnectedCallback += HandleConnected;
            nm.OnClientDisconnectCallback -= HandleDisconnected;
            nm.OnClientDisconnectCallback += HandleDisconnected;

            if (nm.IsHost)
            {
                SendHello();
                return;
            }
            if (nm.IsListening) nm.Shutdown();
            NetBus.Transport.SetConnectionData(true, ResolveIPv4(addr), gamePort);
            if (!nm.StartClient()) OnDisconnected?.Invoke("Could not start client");
        }

        static string ResolveIPv4(string host)
        {
            if (System.Net.IPAddress.TryParse(host, out _)) return host;
            try
            {
                foreach (var a in System.Net.Dns.GetHostAddresses(host))
                    if (a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork) return a.ToString();
            }
            catch { }
            return host == "localhost" ? "127.0.0.1" : host;
        }

        public void Disconnect()
        {
            WantConnected = false;
            Welcomed = false;
            var nm = NetBus.NM;
            if (nm != null && nm.IsClient && !nm.IsHost) nm.Shutdown();
        }

        void HandleConnected(ulong id)
        {
            var nm = NetBus.NM;
            if (nm.IsServer || id != nm.LocalClientId) return;
            OnConnected?.Invoke();
            SendHello();
        }

        void HandleDisconnected(ulong id)
        {
            var nm = NetBus.NM;
            if (nm.IsServer) return;
            Welcomed = false;
            string reason = string.IsNullOrEmpty(nm.DisconnectReason) ? "Connection lost" : nm.DisconnectReason;
            OnDisconnected?.Invoke(reason);
        }

        void SendHello()
        {
            var w = NetBus.Begin(Msg.Hello, 128);
            w.WriteValueSafe(token ?? "");
            w.WriteValueSafe(playerName ?? "Player");
            NetBus.ToServer(w, NetworkDelivery.ReliableSequenced);
        }

        public bool Ready => Welcomed && (NetBus.IsHost || NetBus.IsConnectedClient);

        void Dispatch(Msg type, FastBufferReader r)
        {
            switch (type)
            {
                case Msg.Welcome:
                    r.ReadValueSafe(out ulong id);
                    r.ReadValueSafe(out string name);
                    r.ReadValueSafe(out string tok);
                    ClientId = id; playerName = name; token = tok; Welcomed = true;
                    OnWelcome?.Invoke(id, name);
                    break;
                case Msg.Pong:
                    r.ReadValueSafe(out double sent);
                    r.ReadValueSafe(out uint _);
                    stats.AddRtt((float)((Now - sent) * 1000.0));
                    break;
                case Msg.Lobby: OnLobby?.Invoke(Codec.ReadLobby(r)); break;
                case Msg.Error: r.ReadValueSafe(out string err); OnError?.Invoke(err); break;
                case Msg.MatchStart: OnMatchStart?.Invoke(Codec.ReadMatchStart(r)); break;
                case Msg.Snapshot:
                    Codec.ReadSnapshot(r, frame, GameTuning.Current);
                    stats.OnSnapshotChunk(frame.seq, frame.chunk == 0);
                    if (frame.hasRaft) stats.ServerTick = frame.tick;
                    OnSnapshot?.Invoke(frame);
                    break;
                case Msg.Events:
                    r.ReadValueSafe(out uint _);
                    evBuffer.Clear();
                    Codec.ReadEvents(r, evBuffer);
                    OnEvents?.Invoke(evBuffer);
                    break;
                case Msg.Results: OnResults?.Invoke(Codec.ReadResults(r)); break;
            }
        }

        public void Tick(float dt, bool inMatch)
        {
            stats.Update(dt);
            if (!Ready) return;
            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = 0.1f;
                var w = NetBus.Begin(Msg.Ping, 16);
                w.WriteValueSafe(Now);
                NetBus.ToServer(w, NetworkDelivery.Unreliable);
            }
            if (inMatch)
            {
                inputTimer += dt;
                // input heartbeat every tick (unreliable sequenced)
                while (inputTimer >= TickDt)
                {
                    inputTimer -= TickDt;
                    var w = NetBus.Begin(Msg.Input, 16);
                    w.WriteValueSafe(inputSeq++);
                    w.WriteValueSafe((ushort)Mathf.Clamp(stats.InterpMs, 0, 1000));
                    NetBus.ToServer(w, NetworkDelivery.UnreliableSequenced);
                }
            }
        }

        public void SendStroke(float power, bool back)
        {
            var w = NetBus.Begin(Msg.Stroke, 16);
            w.WriteValueSafe(power);
            w.WriteValueSafe(back);
            NetBus.ToServer(w, NetworkDelivery.Reliable);
        }

        public void SendThrow(ushort target, Vector2 dir)
        {
            var w = NetBus.Begin(Msg.Throw, 24);
            w.WriteValueSafe(target);
            w.WriteValueSafe(dir.x);
            w.WriteValueSafe(dir.y);
            w.WriteValueSafe((ushort)Mathf.Clamp(stats.InterpMs, 0, 1000));
            NetBus.ToServer(w, NetworkDelivery.Reliable);
        }

        public void Simple(Msg m)
        {
            var w = NetBus.Begin(m, 8);
            NetBus.ToServer(w, NetworkDelivery.ReliableSequenced);
        }

        public void JoinRoom(string code)
        {
            var w = NetBus.Begin(Msg.JoinRoom, 32);
            w.WriteValueSafe(code ?? "");
            NetBus.ToServer(w, NetworkDelivery.ReliableSequenced);
        }

        public void PickRole(Role r)
        {
            var w = NetBus.Begin(Msg.PickRole, 8);
            w.WriteValueSafe((byte)r);
            NetBus.ToServer(w, NetworkDelivery.ReliableSequenced);
        }

        public void SetReady(bool ready)
        {
            var w = NetBus.Begin(Msg.SetReady, 8);
            w.WriteValueSafe(ready);
            NetBus.ToServer(w, NetworkDelivery.ReliableSequenced);
        }
    }
}
