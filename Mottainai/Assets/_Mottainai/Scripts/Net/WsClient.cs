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
    /// Auto-reconnects with exponential backoff while Want is true.
    public class WsClient
    {
        public enum State { Idle, Connecting, Open, Closed }
        public volatile State state = State.Idle;
        public string Url { get; private set; }
        public bool Want { get; private set; }
        public string LastError = "";
        public long BytesRecv, BytesSent, MsgsRecv, MsgsSent;
        public int Reconnects;
        public event Action Opened;
        public event Action<string> ClosedEvt;

        readonly ConcurrentQueue<Inbound> inbox = new ConcurrentQueue<Inbound>();
        public volatile bool NoDelay;
        readonly ConcurrentQueue<byte[]> outbox = new ConcurrentQueue<byte[]>();
        readonly SemaphoreSlim outSignal = new SemaphoreSlim(0);
        readonly ConcurrentQueue<Action> mainActions = new ConcurrentQueue<Action>();
        CancellationTokenSource cts;
        int generation;

        public bool IsOpen => state == State.Open;

        public void Connect(string url)
        {
            Close();
            Url = url;
            Want = true;
            cts = new CancellationTokenSource();
            int gen = ++generation;
            var token = cts.Token;
            Task.Run(() => Loop(gen, token));
        }

        public void Close()
        {
            Want = false;
            generation++;
            try { cts?.Cancel(); } catch { }
            cts = null;
            while (outbox.TryDequeue(out _)) { }
            while (inbox.TryDequeue(out _)) { }
            state = State.Idle;
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
            while (mainActions.TryDequeue(out var a)) a();
            while (inbox.TryDequeue(out var m)) handler(m.data, m.recvMs);
        }

        public int Backlog => inbox.Count;
        /// Age of the oldest undelivered frame (main-thread starvation), ms.
        public double OldestAgeMs => inbox.TryPeek(out var m) ? NetTime.Ms - m.recvMs : 0.0;

        async Task Loop(int gen, CancellationToken ct)
        {
            int backoffMs = 250;
            while (!ct.IsCancellationRequested && gen == generation)
            {
                using (var ws = new ClientWebSocket())
                {
                    ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
                    state = State.Connecting;
                    try
                    {
                        using (var to = CancellationTokenSource.CreateLinkedTokenSource(ct))
                        {
                            to.CancelAfter(8000);
                            await ws.ConnectAsync(new Uri(Url), to.Token).ConfigureAwait(false);
                        }
                        NoDelay = DisableNagle(ws);
                        state = State.Open;
                        backoffMs = 250;
                        mainActions.Enqueue(() => Opened?.Invoke());
                        var sendTask = SendLoop(ws, ct);
                        await RecvLoop(ws, ct).ConfigureAwait(false);
                        outSignal.Release();
                        try { await sendTask.ConfigureAwait(false); } catch { }
                        LastError = ws.CloseStatusDescription ?? "closed";
                    }
                    catch (Exception e) { LastError = e.InnerException?.Message ?? e.Message; }
                    try { if (ws.State == WebSocketState.Open) await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false); } catch { }
                }
                if (ct.IsCancellationRequested || gen != generation) break;
                state = State.Closed;
                string err = LastError;
                mainActions.Enqueue(() => ClosedEvt?.Invoke(err));
                while (outbox.TryDequeue(out _)) { }
                Reconnects++;
                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); } catch { break; }
                backoffMs = Math.Min(backoffMs * 2, 4000);
            }
        }

        async Task RecvLoop(ClientWebSocket ws, CancellationToken ct)
        {
            var buf = new byte[64 * 1024];
            int len = 0;
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                var seg = new ArraySegment<byte>(buf, len, buf.Length - len);
                var res = await ws.ReceiveAsync(seg, ct).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close) break;
                len += res.Count;
                if (!res.EndOfMessage)
                {
                    if (len == buf.Length) Array.Resize(ref buf, buf.Length * 2);
                    continue;
                }
                if (res.MessageType == WebSocketMessageType.Binary && len > 0)
                {
                    var m = new byte[len];
                    Buffer.BlockCopy(buf, 0, m, 0, len);
                    inbox.Enqueue(new Inbound { data = m, recvMs = NetTime.Ms });
                    Interlocked.Add(ref BytesRecv, len);
                    Interlocked.Increment(ref MsgsRecv);
                }
                len = 0;
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

        async Task SendLoop(ClientWebSocket ws, CancellationToken ct)
        {
            while (ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                await outSignal.WaitAsync(ct).ConfigureAwait(false);
                while (outbox.TryDequeue(out var m))
                {
                    await ws.SendAsync(new ArraySegment<byte>(m), WebSocketMessageType.Binary, true, ct).ConfigureAwait(false);
                    Interlocked.Add(ref BytesSent, m.Length);
                    Interlocked.Increment(ref MsgsSent);
                }
            }
        }
    }
}
