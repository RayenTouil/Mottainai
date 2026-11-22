using System;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Mottainai.Net
{
    /// Minimal RFC 6455 client on a raw Socket with TCP_NODELAY = true (+ SslStream for wss).
    /// Why: Mono/IL2CPP ClientWebSocket leaves Nagle ON and the socket cannot be reached under IL2CPP (reflection crash),
    /// so 50 msg/s voice frames would wait for ACKs (up to 1 RTT each). Binary frames only; ping -> pong; close.
    public sealed class RawWebSocket : IDisposable
    {
        Socket sock;
        Stream stream;
        readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        readonly Random rng = new Random();
        public bool NoDelay { get; private set; }
        public volatile bool Open;

        public async Task ConnectAsync(Uri uri, CancellationToken ct)
        {
            bool tls = uri.Scheme == "wss" || uri.Scheme == "https";
            int port = uri.IsDefaultPort ? (tls ? 443 : 80) : uri.Port;
            var addrs = await Dns.GetHostAddressesAsync(uri.DnsSafeHost).ConfigureAwait(false);
            Exception last = null;
            using (ct.Register(() => { try { sock?.Close(); } catch { } }))
            {
                foreach (var a in addrs)
                {
                    if (a.AddressFamily != AddressFamily.InterNetwork && a.AddressFamily != AddressFamily.InterNetworkV6) continue;
                    var s = new Socket(a.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
                    sock = s;
                    try { await s.ConnectAsync(a, port).ConfigureAwait(false); last = null; break; }
                    catch (Exception e) { last = e; try { s.Close(); } catch { } sock = null; }
                }
                if (sock == null) throw last ?? new IOException("no address for " + uri.Host);
                sock.NoDelay = true;
                NoDelay = sock.NoDelay;
                Stream st = new NetworkStream(sock, true);
                if (tls)
                {
                    var ssl = new SslStream(st, false);
                    await ssl.AuthenticateAsClientAsync(uri.DnsSafeHost).ConfigureAwait(false);
                    st = ssl;
                }
                stream = st;
                var keyBytes = new byte[16]; rng.NextBytes(keyBytes);
                string req = $"GET {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Host}\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
                             $"Sec-WebSocket-Key: {Convert.ToBase64String(keyBytes)}\r\nSec-WebSocket-Version: 13\r\n\r\n";
                var rb = Encoding.ASCII.GetBytes(req);
                await stream.WriteAsync(rb, 0, rb.Length, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                // response headers (byte by byte until CRLFCRLF; small, once per connection)
                var hb = new StringBuilder();
                var one = new byte[1];
                while (!hb.ToString().EndsWith("\r\n\r\n"))
                {
                    if (await stream.ReadAsync(one, 0, 1, ct).ConfigureAwait(false) <= 0) throw new IOException("closed during handshake");
                    hb.Append((char)one[0]);
                    if (hb.Length > 8192) throw new IOException("handshake too long");
                }
                string status = hb.ToString();
                if (!status.StartsWith("HTTP/1.1 101")) throw new IOException("upgrade refused: " + status.Split('\r')[0]);
            }
            Open = true;
        }

        public async Task SendBinaryAsync(byte[] data, CancellationToken ct) => await SendFrame(0x2, data, ct).ConfigureAwait(false);

        async Task SendFrame(int opcode, byte[] data, CancellationToken ct)
        {
            int n = data?.Length ?? 0;
            int hl = n < 126 ? 2 : n < 65536 ? 4 : 10;
            var f = new byte[hl + 4 + n];
            f[0] = (byte)(0x80 | opcode);
            if (n < 126) f[1] = (byte)(0x80 | n);
            else if (n < 65536) { f[1] = 0x80 | 126; f[2] = (byte)(n >> 8); f[3] = (byte)n; }
            else { f[1] = 0x80 | 127; for (int i = 0; i < 8; i++) f[2 + i] = (byte)((long)n >> (56 - 8 * i)); }
            var mask = new byte[4];
            lock (rng) rng.NextBytes(mask);
            Buffer.BlockCopy(mask, 0, f, hl, 4);
            for (int i = 0; i < n; i++) f[hl + 4 + i] = (byte)(data[i] ^ mask[i & 3]);
            await sendLock.WaitAsync(ct).ConfigureAwait(false);
            try { await stream.WriteAsync(f, 0, f.Length, ct).ConfigureAwait(false); await stream.FlushAsync(ct).ConfigureAwait(false); }
            finally { sendLock.Release(); }
        }

        readonly byte[] hdr = new byte[8];

        async Task ReadExactly(byte[] b, int off, int n, CancellationToken ct)
        {
            while (n > 0)
            {
                int r = await stream.ReadAsync(b, off, n, ct).ConfigureAwait(false);
                if (r <= 0) throw new IOException("connection closed");
                off += r; n -= r;
            }
        }

        /// Next complete binary message, or null when the server closed the connection.
        public async Task<byte[]> ReceiveAsync(CancellationToken ct)
        {
            MemoryStream frag = null;
            for (;;)
            {
                await ReadExactly(hdr, 0, 2, ct).ConfigureAwait(false);
                bool fin = (hdr[0] & 0x80) != 0;
                int op = hdr[0] & 0x0F;
                bool masked = (hdr[1] & 0x80) != 0;
                long len = hdr[1] & 0x7F;
                if (len == 126) { await ReadExactly(hdr, 0, 2, ct).ConfigureAwait(false); len = (hdr[0] << 8) | hdr[1]; }
                else if (len == 127) { await ReadExactly(hdr, 0, 8, ct).ConfigureAwait(false); len = 0; for (int i = 0; i < 8; i++) len = (len << 8) | hdr[i]; }
                byte[] mk = null;
                if (masked) { mk = new byte[4]; await ReadExactly(mk, 0, 4, ct).ConfigureAwait(false); }
                if (len > 16 * 1024 * 1024) throw new IOException("frame too large");
                var p = new byte[len];
                await ReadExactly(p, 0, (int)len, ct).ConfigureAwait(false);
                if (masked) for (int i = 0; i < p.Length; i++) p[i] ^= mk[i & 3];
                switch (op)
                {
                    case 0x8: Open = false; try { await SendFrame(0x8, p.Length >= 2 ? new[] { p[0], p[1] } : Array.Empty<byte>(), ct).ConfigureAwait(false); } catch { } return null;
                    case 0x9: await SendFrame(0xA, p, ct).ConfigureAwait(false); continue;
                    case 0xA: continue;
                    case 0x0: frag?.Write(p, 0, p.Length); if (fin && frag != null) { var m = frag.ToArray(); frag = null; return m; } continue;
                    default:
                        if (fin) return p;
                        frag = new MemoryStream(); frag.Write(p, 0, p.Length); continue;
                }
            }
        }

        public async Task CloseAsync()
        {
            if (!Open) return;
            Open = false;
            try { await SendFrame(0x8, new byte[] { 0x03, 0xE8 }, CancellationToken.None).ConfigureAwait(false); } catch { }
        }

        public void Abort() { Open = false; try { sock?.Close(); } catch { } }
        public void Dispose() { Abort(); try { stream?.Dispose(); } catch { } }
    }
}
