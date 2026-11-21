using System;
using System.Collections.Generic;
using Mottainai.Gameplay;
using Mottainai.Net;
using Mottainai.Platform;
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
        float fpsAcc; int fpsFrames;
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

        public void OnSnapshot() => snapshots++;
        public void ResetAggregates() { sumRtt = 0; nRtt = 0; maxP95 = 0; sumLoss = 0; nLoss = 0; sumDown = 0; nDown = 0; }

        public void Update(float dt, WsClient ws)
        {
            window += dt;
            fpsAcc += dt; fpsFrames++;
            if (fpsAcc >= 0.5f) { Fps = fpsFrames / fpsAcc; fpsAcc = 0; fpsFrames = 0; }
            if (window < 1f) return;
            long recv = ws.BytesRecv, sent = ws.BytesSent, msgs = ws.MsgsRecv + ws.MsgsSent;
            DownKbps = Mathf.Max(0, recv - lastRecv) * 8f / 1000f / window;
            UpKbps = Mathf.Max(0, sent - lastSent) * 8f / 1000f / window;
            Pps = Mathf.Max(0, msgs - lastMsgs) / window;
            TotalDownMB = recv / 1048576f; TotalUpMB = sent / 1048576f;
            lastRecv = recv; lastSent = sent; lastMsgs = msgs;
            SnapshotRate = snapshots / window;
            sumDown += DownKbps; nDown++;
            if (RttP95 > maxP95) maxP95 = RttP95;
            snapshots = 0;
            window = 0;
        }
    }

    /// One WebSocket per room (Cloudflare RoomDO). Event-based uplink, full-snapshot downlink.
    public class NetClient
    {
        public readonly NetStats stats = new NetStats();
        public readonly WsClient ws = new WsClient();
        public event Action<WelcomeInfo> OnWelcome;
        public event Action<LobbyState> OnLobby;
        public event Action<string> OnError;
        public event Action<MatchStartInfo> OnMatchStart;
        public event Action<SnapshotFrame> OnSnapshot;
        public event Action<List<MatchEvent>> OnEvents;
        public event Action<ResultsData> OnResults;
        public event Action<string> OnDisconnected;

        public string token, playerName, roomCode = "", baseUrl;
        public string RoomColo = "?";
        public ulong ClientId { get; private set; } = ulong.MaxValue;
        public bool Welcomed { get; private set; }
        public bool WantConnected => ws.Want;
        public int TickRate = NetConst.TickRate;
        public int SnapshotHz = 60;
        public int MyTeam, Seed;
        public bool ProtocolMismatch;

        readonly SnapshotFrame frame = new SnapshotFrame();
        readonly List<MatchEvent> evBuffer = new List<MatchEvent>(64);
        readonly ByteWriter w = new ByteWriter();
        float pingTimer, statsTimer, backlogMaxMs;
        readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
        public double NowMs => clock.Elapsed.TotalMilliseconds;
        public double Now => clock.Elapsed.TotalSeconds;

        public NetClient()
        {
            ws.ClosedEvt += reason => { Welcomed = false; OnDisconnected?.Invoke(reason); };
        }

        public static string WsUrl(string httpBase, string code, string token, string name, Role role)
        {
            string b = httpBase.TrimEnd('/');
            b = b.StartsWith("https://") ? "wss://" + b.Substring(8) : b.StartsWith("http://") ? "ws://" + b.Substring(7) : b;
            return $"{b}/ws/room/{Uri.EscapeDataString(code)}?token={Uri.EscapeDataString(token ?? "")}&name={Uri.EscapeDataString(name ?? "Player")}&role={(int)role}";
        }

        public void Connect(string httpBase, string code, string tok, string name, Role wantRole)
        {
            baseUrl = httpBase; roomCode = code.ToUpperInvariant(); token = tok; playerName = name;
            Welcomed = false;
            ws.Connect(WsUrl(httpBase, roomCode, tok, name, wantRole));
        }

        public void Disconnect()
        {
            Welcomed = false;
            ws.Close();
        }

        public bool Ready => Welcomed && ws.IsOpen;

        void Dispatch(byte[] m)
        {
            var r = new ByteReader(m, 1, m.Length - 1);
            try
            {
                switch (m[0])
                {
                    case Msg.Welcome:
                    {
                        var wi = Codec.ReadWelcome(ref r);
                        ClientId = wi.playerId; RoomColo = wi.roomColo; roomCode = wi.roomCode; TickRate = wi.tickRate; SnapshotHz = wi.snapshotHz;
                        ProtocolMismatch = wi.protocolVersion != Msg.ProtocolVersion;
                        Welcomed = true;
                        pingTimer = 0f;
                        OnWelcome?.Invoke(wi);
                        break;
                    }
                    case Msg.Pong:
                    {
                        double sent = r.F64();
                        stats.ServerTick = r.U32();
                        stats.AddRtt((float)(NowMs - sent));
                        break;
                    }
                    case Msg.Lobby: OnLobby?.Invoke(Codec.ReadLobby(ref r, roomCode)); break;
                    case Msg.Notice: OnError?.Invoke(r.Str()); break;
                    case Msg.MatchStart:
                    {
                        var ms = Codec.ReadMatchStart(ref r, roomCode);
                        MyTeam = ms.team; Seed = ms.seed; TickRate = ms.tickRate;
                        OnMatchStart?.Invoke(ms);
                        break;
                    }
                    case Msg.Snapshot:
                        Codec.ReadSnapshot(ref r, frame, MyTeam, Seed, GameTuning.Current);
                        SnapshotHz = Mathf.Max(1, frame.snapshotHz);
                        stats.OnSnapshot();
                        stats.ServerTick = frame.tick;
                        OnSnapshot?.Invoke(frame);
                        break;
                    case Msg.Events:
                        evBuffer.Clear();
                        Codec.ReadEvents(ref r, evBuffer);
                        OnEvents?.Invoke(evBuffer);
                        break;
                    case Msg.Results: OnResults?.Invoke(Codec.ReadResults(ref r)); break;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Net] bad frame 0x{m[0]:X2}: {e.Message}"); }
        }

        public void Tick(float dt, bool inMatch)
        {
            backlogMaxMs = Mathf.Max(backlogMaxMs, ws.Backlog * 1000f / Mathf.Max(1, SnapshotHz));
            ws.Poll(Dispatch);
            stats.Update(dt, ws);
            if (!Ready) return;
            pingTimer -= dt;
            if (pingTimer <= 0f)
            {
                pingTimer = 0.5f;
                ws.Send(w.Begin(Msg.Ping).F64(NowMs).ToArray());
            }
            statsTimer -= dt;
            if (statsTimer <= 0f)
            {
                statsTimer = 2f;
                ws.Send(w.Begin(Msg.Stats).U8(Mathf.Clamp(Mathf.RoundToInt(stats.Fps), 0, 255)).U16(Mathf.Clamp(Mathf.RoundToInt(stats.RttAvg), 0, 65535))
                    .U8(NetworkInfo.TypeCode).U16(Mathf.Clamp(Mathf.RoundToInt(backlogMaxMs), 0, 65535)).ToArray());
                backlogMaxMs = 0f;
            }
        }

        float ClientTime => (float)Now;
        static int Power255(float p) => Mathf.Clamp(Mathf.RoundToInt(p * 255f), 0, 255);

        public void SendStroke(Role side, float power, bool back)
        {
            int s = side == Role.RightPaddler ? 1 : 0;
            ws.Send(w.Begin(back ? Msg.BackPaddle : Msg.Stroke).U8(s).U8(Power255(power)).F32(ClientTime).ToArray());
        }

        public void SendThrow(ushort target, Vector2 dir)
        {
            if (dir.sqrMagnitude > 1e-6f) dir.Normalize();
            ws.Send(w.Begin(Msg.Net).U16(target).I16(Mathf.RoundToInt(dir.x * 1000f)).I16(Mathf.RoundToInt(dir.y * 1000f)).F32(ClientTime).ToArray());
        }

        public void PickRole(Role r) => ws.Send(w.Begin(Msg.Role).U8((int)r).ToArray());
        public void SetReady(bool ready) => ws.Send(w.Begin(Msg.Ready).U8(ready ? 1 : 0).ToArray());
        public void StartMatch() => ws.Send(w.Begin(Msg.Start).ToArray());
    }
}
