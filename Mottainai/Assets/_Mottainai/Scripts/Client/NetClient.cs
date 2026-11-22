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
        public float InterpMs, SnapJitterMs, PredLeadMs, LatePct;
        public bool NoDelay;
        public int ReconnectCount; public float LastReconnectMs; public string LastReconnectCause = "-";
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
        public event Action<PauseInfo> OnPause;
        public event Action<int> OnResume;

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
        float statsTimer, backlogMaxMs;
        string baseWsUrl;
        double downSince, stallCooldown;
        string downCause;
        /// No snapshot or PONG for this long during a match = dead link: reconnect without waiting for TCP.
        public const double StallMs = 1000;
        Action<byte[], double> dispatch;
        public double NowMs => NetTime.Ms;
        public double Now => NetTime.Ms * 0.001;

        public NetClient()
        {
            ws.ClosedEvt += reason => { Welcomed = false; if (downSince == 0) { downSince = NetTime.Ms; downCause = reason; } OnDisconnected?.Invoke(reason); };
            // PING from the socket thread every 500 ms (gated on a live main thread); the server drops silent sockets after 1.5 s
            ws.HeartbeatFrame = () => new ByteWriter().Begin(Msg.Ping).F64(NetTime.Ms).ToArray();
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
            downSince = 0;
            baseWsUrl = WsUrl(httpBase, roomCode, tok, name, wantRole);
            ws.Connect(baseWsUrl);
        }

        public void Disconnect()
        {
            Welcomed = false;
            ws.Close();
        }

        public bool Ready => Welcomed && ws.IsOpen;

        void Dispatch(byte[] m, double recvMs)
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
                        if (downSince > 0)
                        {
                            stats.ReconnectCount++;
                            stats.LastReconnectMs = (float)(NetTime.Ms - downSince);
                            stats.LastReconnectCause = downCause ?? "?";
                            Debug.Log($"[NET] reconnected in {stats.LastReconnectMs:F0} ms (cause: {stats.LastReconnectCause})");
                            downSince = 0;
                        }
                        OnWelcome?.Invoke(wi);
                        break;
                    }
                    case Msg.Pong:
                    {
                        double sent = r.F64();
                        stats.ServerTick = r.U32();
                        stats.AddRtt((float)(recvMs - sent)); // socket-thread receive time: no frame quantization
                        break;
                    }
                    case Msg.Lobby: OnLobby?.Invoke(Codec.ReadLobby(ref r, roomCode)); break;
                    case Msg.Notice: OnError?.Invoke(r.Str()); break;
                    case Msg.MatchStart:
                    {
                        var ms = Codec.ReadMatchStart(ref r, roomCode);
                        MyTeam = ms.team; Seed = ms.seed; TickRate = ms.tickRate;
                        ws.Url = baseWsUrl + "&match=" + (uint)ms.seed; // a reconnect tells the room which match we think we are in
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
                    case Msg.Results: ws.Url = baseWsUrl; OnResults?.Invoke(Codec.ReadResults(ref r)); break;
                    case Msg.Pause: OnPause?.Invoke(Codec.ReadPause(ref r)); break;
                    case Msg.Resume: OnResume?.Invoke(r.U16()); break;
                }
            }
            catch (Exception e) { Debug.LogWarning($"[Net] bad frame 0x{m[0]:X2}: {e.Message}"); }
        }

        public void Tick(float dt, bool inMatch)
        {
            // backlog = age of the oldest undelivered frame; single hitches (loading) are not a sustained backlog
            if (dt < 0.05f) backlogMaxMs = Mathf.Max(backlogMaxMs, (float)ws.OldestAgeMs);
            ws.Poll(dispatch ??= Dispatch);
            stats.NoDelay = ws.NoDelay;
            stats.Update(dt, ws);
            ws.Fast = inMatch;
            if (!Ready) return;
            // dead link: snapshots (60 Hz) and PONGs (2 Hz) stopped -> reconnect now instead of waiting for TCP / keep-alive (15 s)
            double now = NetTime.Ms, silent = now - ws.LastRecvMs;
            if (inMatch && silent > StallMs && now > stallCooldown)
            {
                stallCooldown = now + 1500;
                downSince = ws.LastRecvMs; downCause = $"no data {silent / 1000:F1} s";
                Debug.Log("[NET] " + downCause + " -> reconnect");
                Welcomed = false;
                ws.ForceReconnect(downCause);
                return;
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
        /// v3: voluntary leave (bot takes the role immediately).
        public void SendLeave() => ws.Send(w.Begin(Msg.Leave).ToArray());
        public void StartMatch() => ws.Send(w.Begin(Msg.Start).ToArray());
        /// v3: host skips the reconnect wait (bots take the missing roles).
        public void ContinueWithBot() => ws.Send(w.Begin(Msg.ContinueBot).ToArray());

        /// App to background: close the socket so the room pauses at once; foreground: reconnect immediately.
        public void Suspend() { if (WantConnected) ws.Suspend(); }
        public void Resume()
        {
            if (!WantConnected) return;
            if (ws.Suspended) { downSince = NetTime.Ms; downCause = "app resumed"; ws.Resume(); }
            // only an OPEN but silent socket is dead; a socket that is (re)connecting must not be aborted (Android sends
            // resume + focus back to back: aborting the fresh connection made the room see a second drop)
            else if (ws.IsOpen && NetTime.Ms - ws.LastRecvMs > StallMs) { downSince = NetTime.Ms; downCause = "focus regained, link dead"; Welcomed = false; ws.ForceReconnect(downCause); }
        }
    }
}
