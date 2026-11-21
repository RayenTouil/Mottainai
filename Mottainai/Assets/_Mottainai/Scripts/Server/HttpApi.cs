using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Mottainai.Server
{
    /// Minimal HTTP/1.1 server on a raw TcpListener bound to 0.0.0.0 (no http.sys / URL ACL / admin needed).
    /// Endpoints: /ping /status /login /rooms /content/* /telemetry /lab/download /lab/result
    public class HttpApi
    {
        TcpListener listener;
        Thread thread;
        volatile bool running;
        readonly RoomManager rooms;
        readonly ContentPack content;
        readonly SessionLog log;
        public volatile string roomsJson = "{\"rooms\":[]}";
        public volatile string statusJson = "{}";
        public string serverName = "Mottainai Edge";
        public int gamePort;
        public long BytesServed;
        static readonly byte[] labBlock = new byte[1 << 20];

        class Req
        {
            public string method, path, query, remote;
            public byte[] body = Array.Empty<byte>();
            public string BodyText => Encoding.UTF8.GetString(body);
            public string Query(string key)
            {
                if (string.IsNullOrEmpty(query)) return null;
                foreach (var kv in query.Split('&'))
                {
                    int eq = kv.IndexOf('=');
                    if (eq > 0 && Uri.UnescapeDataString(kv.Substring(0, eq)) == key) return Uri.UnescapeDataString(kv.Substring(eq + 1));
                }
                return null;
            }
        }

        public HttpApi(RoomManager rooms, ContentPack content, SessionLog log)
        {
            this.rooms = rooms; this.content = content; this.log = log;
            new System.Random(42).NextBytes(labBlock);
        }

        public bool Start(int port)
        {
            try
            {
                listener = new TcpListener(IPAddress.Any, port);
                listener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                listener.Start(64);
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "HttpApi" };
                thread.Start();
                Debug.Log($"[HTTP] listening on 0.0.0.0:{port} (TcpListener)");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[HTTP] cannot bind TCP {port}: {e.Message}");
                return false;
            }
        }

        public void Stop() { running = false; try { listener?.Stop(); } catch { } }

        void Loop()
        {
            while (running)
            {
                TcpClient c;
                try { c = listener.AcceptTcpClient(); }
                catch { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Serve(c));
            }
        }

        void Serve(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    client.ReceiveTimeout = 10000;
                    client.SendTimeout = 60000;
                    var stream = client.GetStream();
                    var req = Read(stream);
                    if (req == null) return;
                    req.remote = client.Client.RemoteEndPoint?.ToString();
                    Handle(req, stream);
                }
                catch (Exception e) { Debug.LogWarning("[HTTP] " + e.Message); }
            }
        }

        static Req Read(NetworkStream s)
        {
            // read headers byte-wise until CRLFCRLF (max 16 KB)
            var head = new MemoryStream();
            int state = 0;
            while (head.Length < 16384)
            {
                int b = s.ReadByte();
                if (b < 0) return null;
                head.WriteByte((byte)b);
                state = (b == '\r' && (state == 0 || state == 2)) ? state + 1 : (b == '\n' && (state == 1 || state == 3)) ? state + 1 : 0;
                if (state == 4) break;
            }
            var lines = Encoding.ASCII.GetString(head.ToArray()).Split(new[] { "\r\n" }, StringSplitOptions.None);
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;
            var req = new Req { method = first[0].ToUpperInvariant() };
            string target = first[1];
            int q = target.IndexOf('?');
            req.path = Uri.UnescapeDataString(q >= 0 ? target.Substring(0, q) : target).TrimEnd('/');
            req.query = q >= 0 ? target.Substring(q + 1) : "";
            int len = 0;
            foreach (var l in lines)
                if (l.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) int.TryParse(l.Substring(15).Trim(), out len);
            foreach (var l in lines)
                if (l.StartsWith("Expect:", StringComparison.OrdinalIgnoreCase) && l.IndexOf("100-continue", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    var cont = Encoding.ASCII.GetBytes("HTTP/1.1 100 Continue\r\n\r\n");
                    s.Write(cont, 0, cont.Length);
                }
            if (len > 0 && len < 1 << 20)
            {
                req.body = new byte[len];
                int read = 0;
                while (read < len) { int r = s.Read(req.body, read, len - read); if (r <= 0) break; read += r; }
            }
            return req;
        }

        void Header(Stream s, int code, string type, long length)
        {
            string status = code == 200 ? "OK" : code == 404 ? "Not Found" : code == 503 ? "Service Unavailable" : code == 204 ? "No Content" : "Error";
            var h = $"HTTP/1.1 {code} {status}\r\nContent-Type: {type}\r\nContent-Length: {length}\r\nAccess-Control-Allow-Origin: *\r\nAccess-Control-Allow-Headers: *\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            var b = Encoding.ASCII.GetBytes(h);
            s.Write(b, 0, b.Length);
        }

        void Send(Stream s, string text, string type = "application/json", int code = 200)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            Header(s, code, type, bytes.Length);
            s.Write(bytes, 0, bytes.Length);
            Interlocked.Add(ref BytesServed, bytes.Length);
        }

        void Handle(Req req, Stream s)
        {
            string path = req.path;
            if (req.method == "OPTIONS") { Header(s, 204, "text/plain", 0); return; }
            if (path == "" || path == "/ping") Send(s, "{\"ok\":true,\"name\":\"" + serverName + "\"}");
            else if (path == "/status") Send(s, statusJson);
            else if (path == "/login")
            {
                var lr = (req.body.Length > 0 ? JsonUtility.FromJson<LoginRequest>(req.BodyText) : null) ?? new LoginRequest { name = req.Query("name") };
                var session = rooms.Login(lr.name, lr.token);
                var resp = new LoginResponse { token = session.token, name = session.name, serverName = serverName, gamePort = gamePort, rooms = JsonUtility.FromJson<RoomList>(roomsJson)?.rooms };
                log.Event("login", "", $"{session.name} device={lr.device} ip={req.remote}");
                Send(s, JsonUtility.ToJson(resp));
            }
            else if (path == "/rooms") Send(s, roomsJson);
            else if (path == "/content/manifest.json")
            {
                if (!content.ready) Send(s, "{\"error\":\"building\"}", code: 503);
                else Send(s, content.manifestJson);
            }
            else if (path.StartsWith("/content/"))
            {
                string file = Path.Combine(content.dir, Path.GetFileName(path.Substring("/content/".Length)));
                if (!File.Exists(file)) { Send(s, "{\"error\":\"not found\"}", code: 404); return; }
                using var fs = File.OpenRead(file);
                Header(s, 200, "application/octet-stream", fs.Length);
                var buf = new byte[64 * 1024];
                int n;
                while ((n = fs.Read(buf, 0, buf.Length)) > 0) { s.Write(buf, 0, n); Interlocked.Add(ref BytesServed, n); }
            }
            else if (path == "/telemetry")
            {
                var t = JsonUtility.FromJson<TelemetryDto>(req.BodyText);
                if (t != null) log.Telemetry(t);
                Send(s, "{\"ok\":true}");
            }
            else if (path == "/lab/download")
            {
                int.TryParse(req.Query("mb"), out int mb);
                mb = Mathf.Clamp(mb <= 0 ? 20 : mb, 1, 200);
                Header(s, 200, "application/octet-stream", (long)mb << 20);
                for (int i = 0; i < mb; i++) { s.Write(labBlock, 0, labBlock.Length); Interlocked.Add(ref BytesServed, labBlock.Length); }
            }
            else if (path == "/lab/result")
            {
                var r = JsonUtility.FromJson<LabResultDto>(req.BodyText);
                if (r != null) log.Lab(r);
                Send(s, "{\"ok\":true}");
            }
            else Send(s, "{\"error\":\"unknown endpoint\"}", code: 404);
        }
    }
}
