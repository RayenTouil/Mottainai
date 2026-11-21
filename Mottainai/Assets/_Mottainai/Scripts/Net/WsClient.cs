using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Mottainai.Net
{
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

        readonly ConcurrentQueue<byte[]> inbox = new ConcurrentQueue<byte[]>();
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

        /// Call from the main thread each frame; invokes handler for every received frame.
        public void Poll(Action<byte[]> handler)
        {
            while (mainActions.TryDequeue(out var a)) a();
            while (inbox.TryDequeue(out var m)) handler(m);
        }

        public int Backlog => inbox.Count;

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
                    inbox.Enqueue(m);
                    Interlocked.Add(ref BytesRecv, len);
                    Interlocked.Increment(ref MsgsRecv);
                }
                len = 0;
            }
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
