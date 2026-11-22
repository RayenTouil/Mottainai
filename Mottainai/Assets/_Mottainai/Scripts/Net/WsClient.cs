using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mottainai.Net
{
    /// Monotonic, thread-safe millisecond clock shared by the socket threads and the main thread.
    public static class NetTime
    {
        static readonly double k = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        public static double Ms => System.Diagnostics.Stopwatch.GetTimestamp() * k;
    }

    sealed class RefEq : IEqualityComparer<object>
    {
        public static readonly RefEq I = new RefEq();
        public new bool Equals(object a, object b) => ReferenceEquals(a, b);
        public int GetHashCode(object o) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o);
    }

    public struct Inbound { public byte[] data; public double recvMs; }

    /// ClientWebSocket on background tasks: receive loop -> lock-free queue -> main thread (Poll).
    /// Auto-reconnects while Want is true. v4: heartbeat PING from the socket thread (gated on a live main thread),
    /// ForceReconnect (dead-link detection by the caller), Suspend/Resume (app in background), fast backoff in a match.
    public class WsClient
    {
        public enum State { Idle, Connecting, Open, Closed }
        public volatile State state = State.Idle;
        volatile string url;
        /// Read at every (re)connect attempt, so the caller can add parameters for the next reconnect (e.g. &match=).
        public string Url { get => url; set => url = value; }
        public bool Want { get; private set; }
        public string LastError = "";
        public long BytesRecv, BytesSent, MsgsRecv, MsgsSent;
        public int Reconnects;
        public event Action Opened;
        public event Action<string> ClosedEvt;

        /// In a match: reconnect at once, then 250, 500, 1000, 2000 ms (max) and a 5 s connect timeout.
        public volatile bool Fast;
        /// Built on the socket thread every HeartbeatMs while the main thread is alive (PING with a socket-thread timestamp).
        public Func<byte[]> HeartbeatFrame;
        public int HeartbeatMs = 500;
        /// No heartbeat when the main thread has not polled for this long (frozen app = the server should see it as gone).
        public double MainStallMs = 2000;
        static readonly int[] FastBackoff = { 0, 250, 500, 1000, 2000 };

        readonly ConcurrentQueue<Inbound> inbox = new ConcurrentQueue<Inbound>();
        public volatile bool NoDelay;
        readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim outSignal = new SemaphoreSlim(0);
        readonly SemaphoreSlim resumeSignal = new SemaphoreSlim(0);
        readonly ConcurrentQueue<Action> mainActions = new ConcurrentQueue<Action>();
        CancellationTokenSource cts;
        int generation;
        volatile Conn current;
        /// v4: own RFC 6455 client with TCP_NODELAY (default). Falls back to ClientWebSocket after 2 failed raw handshakes.
        public static bool UseRawSocket = true;
        int rawFailures;
        public volatile string Transport = "-";
        /// Voice frames (S2C 0x88/0x89) go here on the socket thread, never through the main-thread inbox (backlogMs).
        public Action<byte[], double> VoiceSink;

        abstract class Conn
        {
            public abstract bool IsOpen { get; }
            public abstract Task Connect(Uri u, CancellationToken ct);
            public abstract Task Send(byte[] b, CancellationToken ct);
            public abstract Task<byte[]> Receive(CancellationToken ct); // null = closed
            public abstract void Abort();
            public abstract Task Close();
            public bool NoDelay;
        }
        sealed class RawConn : Conn
        {
            readonly RawWebSocket ws = new RawWebSocket();
            public override bool IsOpen => ws.Open;
            public override async Task Connect(Uri u, CancellationToken ct) { await ws.ConnectAsync(u, ct).ConfigureAwait(false); NoDelay = ws.NoDelay; }
            public override Task Send(byte[] b, CancellationToken ct) => ws.SendBinaryAsync(b, ct);
            public override Task<byte[]> Receive(CancellationToken ct) => ws.ReceiveAsync(ct);
            public override void Abort() => ws.Abort();
            public override Task Close() => ws.CloseAsync();
        }
        sealed class CwsConn : Conn
        {
            readonly ClientWebSocket ws = new ClientWebSocket();
            byte[] buf = new byte[64 * 1024];
            public override bool IsOpen => ws.State == WebSocketState.Open;
            public override async Task Connect(Uri u, CancellationToken ct)
            {
                ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                await ws.ConnectAsync(u, ct).ConfigureAwait(false);
                NoDelay = DisableNagle(ws);
            }
            public override Task Send(byte[] b, CancellationToken ct) => ws.SendAsync(new ArraySegment<byte>(b), WebSocketMessageType.Binary, true, ct);
            public override async Task<byte[]> Receive(CancellationToken ct)
            {
                int len = 0;
                for (;;)
                {
                    var res = await ws.ReceiveAsync(new ArraySegment<byte>(buf, len, buf.Length - len), ct).ConfigureAwait(false);
                    if (res.MessageType == WebSocketMessageType.Close) return null;
                    len += res.Count;
                    if (!res.EndOfMessage) { if (len == buf.Length) Array.Resize(ref buf, buf.Length * 2); continue; }
                    var m = new byte[len]; Buffer.BlockCopy(buf, 0, m, 0, len); return m;
                }
            }
            public override void Abort() { try { ws.Abort(); } catch { } }
            public override async Task Close() { try { if (ws.State == WebSocketState.Open) await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch { } }
        }
        volatile bool suspended;
        volatile string forceCause;
        long lastRecvBits, mainAliveBits;

        public bool IsOpen => state == State.Open;
        public bool Suspended => suspended;
        /// Socket-thread time of the last received frame (NetTime.Ms).
        public double LastRecvMs => BitConverter.Int64BitsToDouble(Interlocked.Read(ref lastRecvBits));
        double MainAliveMs => BitConverter.Int64BitsToDouble(Interlocked.Read(ref mainAliveBits));
        static void Stamp(ref long bits) => Interlocked.Exchange(ref bits, BitConverter.DoubleToInt64Bits(NetTime.Ms));

        public void Connect(string url)
        {
            Close();
            Url = url;
            Want = true;
            suspended = false;
            cts = new CancellationTokenSource();
            int gen = ++generation;
            var token = cts.Token;
            Stamp(ref mainAliveBits);
            Task.Run(() => Loop(gen, token));
        }

        public void Close()
        {
            Want = false;
            generation++;
            try { cts?.Cancel(); } catch { }
            cts = null;
            try { current?.Abort(); } catch { }
            while (outbox.TryDequeue(out _)) { }
            while (inbox.TryDequeue(out _)) { }
            state = State.Idle;
        }

        /// Drop the current socket now (dead link detected by the caller) and reconnect with the fast backoff.
        public void ForceReconnect(string cause)
        {
            if (!Want) return;
            forceCause = cause;
            try { current?.Abort(); } catch { }
        }

        /// App goes to the background: close the socket (the server pauses the match at once) and do not reconnect.
        public void Suspend()
        {
            if (!Want || suspended) return;
            suspended = true;
            forceCause = "app paused";
            try { current?.Abort(); } catch { }
        }

        /// App back in the foreground: reconnect immediately.
        public void Resume()
        {
            if (!suspended) return;
            suspended = false;
            Stamp(ref mainAliveBits);
            resumeSignal.Release();
        }

        public void Send(byte[] data)
        {
            if (state != State.Open) return;
            outbox.Enqueue(data);
            outSignal.Release();
        }

        /// Call from the main thread each frame; invokes handler for every received frame (with its socket-thread receive time).
        public void Poll(Action<byte[], double> handler)
        {
            Stamp(ref mainAliveBits);
            while (mainActions.TryDequeue(out var a)) a();
            while (inbox.TryDequeue(out var m)) handler(m.data, m.recvMs);
        }

        public int Backlog => inbox.Count;
        /// Age of the oldest undelivered frame (main-thread starvation), ms.
        public double OldestAgeMs => inbox.TryPeek(out var m) ? NetTime.Ms - m.recvMs : 0.0;

        async Task Loop(int gen, CancellationToken ct)
        {
            int attempt = 0;
            while (!ct.IsCancellationRequested && gen == generation)
            {
                while (suspended && !ct.IsCancellationRequested && gen == generation)
                {
                    try { await resumeSignal.WaitAsync(250, ct).ConfigureAwait(false); } catch { break; }
                }
                if (ct.IsCancellationRequested || gen != generation) break;
                bool raw = UseRawSocket && rawFailures < 2;
                Conn ws = raw ? (Conn)new RawConn() : new CwsConn();
                using (var conn = CancellationTokenSource.CreateLinkedTokenSource(ct))
                {
                    current = ws;
                    forceCause = null;
                    state = State.Connecting;
                    bool opened = false;
                    try
                    {
                        using (var to = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            to.CancelAfter(Fast ? 5000 : 8000);
                            await ws.Connect(new Uri(Url), to.Token).ConfigureAwait(false);
                        }
                        if (suspended) throw new OperationCanceledException("app paused");
                        opened = true;
                        if (raw) rawFailures = 0;
                        NoDelay = ws.NoDelay;
                        Transport = raw ? "raw socket" : "ClientWebSocket";
                        Stamp(ref lastRecvBits);
                        state = State.Open;
                        attempt = 0;
                        mainActions.Enqueue(() => Opened?.Invoke());
                        var sendTask = SendLoop(ws, conn.Token);
                        var beatTask = Heartbeat(ws, conn.Token);
                        try { await RecvLoop(ws, conn.Token).ConfigureAwait(false); }
                        finally { conn.Cancel(); outSignal.Release(); }
                        try { await sendTask.ConfigureAwait(false); } catch { }
                        try { await beatTask.ConfigureAwait(false); } catch { }
                        LastError = forceCause ?? "closed";
                    }
                    catch (Exception e)
                    {
                        LastError = forceCause ?? e.InnerException?.Message ?? e.Message;
                        if (raw && !opened && forceCause == null && !suspended) { rawFailures++; Debug.LogWarning("[Ws] raw socket failed (" + rawFailures + "): " + LastError); }
                    }
                    try { await ws.Close().ConfigureAwait(false); } catch { }
                    ws.Abort();
                    current = null;
                }
                if (ct.IsCancellationRequested || gen != generation) break;
                state = State.Closed;
                string err = LastError;
                mainActions.Enqueue(() => ClosedEvt?.Invoke(err));
                while (outbox.TryDequeue(out _)) { }
                Reconnects++;
                int delay = Fast ? FastBackoff[Math.Min(attempt, FastBackoff.Length - 1)] : Math.Min(250 << Math.Min(attempt, 4), 4000);
                attempt++;
                if (delay > 0 && !suspended) { try { await Task.Delay(delay, ct).ConfigureAwait(false); } catch { break; } }
            }
        }

        async Task Heartbeat(Conn ws, CancellationToken ct)
        {
            while (ws.IsOpen && !ct.IsCancellationRequested)
            {
                var f = HeartbeatFrame;
                if (f != null && NetTime.Ms - MainAliveMs < MainStallMs) Send(f());
                await Task.Delay(HeartbeatMs, ct).ConfigureAwait(false);
            }
        }

        async Task RecvLoop(Conn ws, CancellationToken ct)
        {
            while (ws.IsOpen && !ct.IsCancellationRequested)
            {
                var m = await ws.Receive(ct).ConfigureAwait(false);
                if (m == null) break;
                if (m.Length == 0) continue;
                double t = NetTime.Ms;
                Interlocked.Exchange(ref lastRecvBits, BitConverter.DoubleToInt64Bits(t));
                Interlocked.Add(ref BytesRecv, m.Length);
                Interlocked.Increment(ref MsgsRecv);
                var vs = VoiceSink;
                if (vs != null && (m[0] == 0x88 || m[0] == 0x89)) { try { vs(m, t); } catch (Exception e) { Debug.LogWarning("[Voice] " + e.Message); } continue; }
                inbox.Enqueue(new Inbound { data = m, recvMs = t });
            }
        }

        /// Mono's ClientWebSocket leaves Nagle on (measured NoDelay=False): small event frames (strokes) could wait
        /// for the ACK of the previous frame (up to one RTT). Find the TCP socket under the stream chain and set TCP_NODELAY.
        static bool DisableNagle(ClientWebSocket ws)
        {
#if ENABLE_IL2CPP
            return false; // reflection walk segfaults under IL2CPP (stripped metadata)
#else
            try
            {
                var s = FindSocket(ws, 0, new HashSet<object>(RefEq.I));
                if (s == null) { Debug.Log("[Ws] TCP socket not found, Nagle unchanged"); return false; }
                s.NoDelay = true;
                Debug.Log("[Ws] TCP_NODELAY=" + s.NoDelay);
                return s.NoDelay;
            }
            catch (Exception e) { Debug.Log("[Ws] NoDelay failed: " + e.Message); return false; }
#endif
        }

        static Socket FindSocket(object o, int depth, HashSet<object> seen)
        {
            if (o == null || depth > 7 || o is string || !seen.Add(o)) return null;
            if (o is Socket s) return s;
            var t = o.GetType();
            if (t.IsPrimitive || t.IsArray || t.IsEnum || o is Delegate || o is Task || o is SemaphoreSlim || o is CancellationTokenSource) return null;
            string ns = t.Namespace ?? "";
            if (depth > 0 && !(ns.StartsWith("System.Net") || ns.StartsWith("Mono.") || ns.StartsWith("System.IO") || o is System.IO.Stream)) return null;
            for (var tt = t; tt != null && tt != typeof(object); tt = tt.BaseType)
                foreach (var f in tt.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
                {
                    var ft = f.FieldType;
                    if (ft.IsPrimitive || ft.IsEnum || ft == typeof(string) || ft.IsArray) continue;
                    object v;
                    try { v = f.GetValue(o); } catch { continue; }
                    var r = FindSocket(v, depth + 1, seen);
                    if (r != null) return r;
                }
            return null;
        }

        async Task SendLoop(Conn ws, CancellationToken ct)
        {
            while (ws.IsOpen && !ct.IsCancellationRequested)
            {
                await outSignal.WaitAsync(ct).ConfigureAwait(false);
                while (outbox.TryDequeue(out var m))
                {
                    await ws.Send(m, ct).ConfigureAwait(false);
                    Interlocked.Add(ref BytesSent, m.Length);
                    Interlocked.Increment(ref MsgsSent);
                }
            }
        }
    }
}
