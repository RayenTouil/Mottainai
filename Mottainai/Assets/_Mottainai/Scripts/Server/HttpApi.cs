using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using UnityEngine;

namespace Mottainai.Server
{
    /// Login / content pack / telemetry / lab endpoints (TCP 8080). Runs on background threads.
    public class HttpApi
    {
        HttpListener listener;
        Thread thread;
        volatile bool running;
        readonly RoomManager rooms;
        readonly ContentPack content;
        readonly SessionLog log;
        public volatile string roomsJson = "{\"rooms\":[]}";
        public volatile string statusJson = "{}";
        public string serverName = "Mottainai Server";
        public int gamePort;
        public long BytesServed;
        public string Bound = "none";
        static readonly byte[] labBlock = new byte[1 << 20];

        public HttpApi(RoomManager rooms, ContentPack content, SessionLog log)
        {
            this.rooms = rooms; this.content = content; this.log = log;
            new System.Random(42).NextBytes(labBlock);
        }

        /// "+"/"*" = 0.0.0.0, no admin needed with Mono, any Host header. Localhost fallback: Mono answers 400 to other Host names.
        public bool Start(int port)
        {
            foreach (var prefixes in new[] { new[] { $"http://+:{port}/" }, new[] { $"http://*:{port}/" }, new[] { $"http://localhost:{port}/", $"http://127.0.0.1:{port}/" } })
            {
                try
                {
                    listener = new HttpListener();
                    foreach (var p in prefixes) listener.Prefixes.Add(p);
                    listener.Start();
                    running = true;
                    thread = new Thread(Loop) { IsBackground = true, Name = "HttpApi" };
                    thread.Start();
                    Bound = string.Join(" ", prefixes);
                    Debug.Log($"[HTTP] listening on {Bound}");
                    if (prefixes.Length > 1)
                        Debug.LogWarning($"[HTTP] localhost-only fallback: requests via playit/LAN (other Host names) get HTTP 400. Free TCP {port} or start with -http <port> and retarget the playit TCP tunnel.");
                    return true;
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[HTTP] prefix {string.Join(" ", prefixes)} failed: {e.Message}");
                    try { listener?.Close(); } catch { }
                }
            }
            return false;
        }

        public void Stop() { running = false; try { listener?.Stop(); listener?.Close(); } catch { } }

        void Loop()
        {
            while (running)
            {
                HttpListenerContext ctx;
                try { ctx = listener.GetContext(); }
                catch { if (!running) return; continue; }
                ThreadPool.QueueUserWorkItem(_ => Handle(ctx));
            }
        }

        static string Body(HttpListenerRequest req)
        {
            using var sr = new StreamReader(req.InputStream, req.ContentEncoding ?? Encoding.UTF8);
            return sr.ReadToEnd();
        }

        void Send(HttpListenerResponse res, string text, string type = "application/json", int code = 200)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            res.StatusCode = code;
            res.ContentType = type;
            res.ContentLength64 = bytes.Length;
            res.OutputStream.Write(bytes, 0, bytes.Length);
            Interlocked.Add(ref BytesServed, bytes.Length);
        }

        void Handle(HttpListenerContext ctx)
        {
            var req = ctx.Request;
            var res = ctx.Response;
            res.AddHeader("Access-Control-Allow-Origin", "*");
            res.AddHeader("Cache-Control", "no-store");
            try
            {
                string path = req.Url.AbsolutePath.TrimEnd('/');
                if (path == "" || path == "/ping") { Send(res, "{\"ok\":true,\"name\":\"" + serverName + "\"}"); }
                else if (path == "/status") Send(res, statusJson);
                else if (path == "/login")
                {
                    var lr = JsonUtility.FromJson<LoginRequest>(Body(req)) ?? new LoginRequest();
                    var s = rooms.Login(lr.name, lr.token);
                    var resp = new LoginResponse { token = s.token, name = s.name, serverName = serverName, gamePort = gamePort, rooms = JsonUtility.FromJson<RoomList>(roomsJson)?.rooms };
                    log.Event("login", "", $"{s.name} device={lr.device} ip={req.RemoteEndPoint}");
                    Send(res, JsonUtility.ToJson(resp));
                }
                else if (path == "/rooms") Send(res, roomsJson);
                else if (path == "/content/manifest.json")
                {
                    if (!content.ready) Send(res, "{\"error\":\"building\"}", code: 503);
                    else Send(res, content.manifestJson);
                }
                else if (path.StartsWith("/content/"))
                {
                    string name = Path.GetFileName(path.Substring("/content/".Length));
                    string file = Path.Combine(content.dir, name);
                    if (!File.Exists(file)) { Send(res, "{\"error\":\"not found\"}", code: 404); return; }
                    res.ContentType = "application/octet-stream";
                    using var fs = File.OpenRead(file);
                    res.ContentLength64 = fs.Length;
                    var buf = new byte[64 * 1024];
                    int n;
                    while ((n = fs.Read(buf, 0, buf.Length)) > 0) { res.OutputStream.Write(buf, 0, n); Interlocked.Add(ref BytesServed, n); }
                }
                else if (path == "/telemetry")
                {
                    var t = JsonUtility.FromJson<TelemetryDto>(Body(req));
                    if (t != null) log.Telemetry(t);
                    Send(res, "{\"ok\":true}");
                }
                else if (path == "/lab/download")
                {
                    int mb = 20;
                    int.TryParse(req.QueryString["mb"], out mb);
                    mb = Mathf.Clamp(mb <= 0 ? 20 : mb, 1, 200);
                    res.ContentType = "application/octet-stream";
                    res.ContentLength64 = (long)mb << 20;
                    for (int i = 0; i < mb; i++) { res.OutputStream.Write(labBlock, 0, labBlock.Length); Interlocked.Add(ref BytesServed, labBlock.Length); }
                }
                else if (path == "/lab/result")
                {
                    var r = JsonUtility.FromJson<LabResultDto>(Body(req));
                    if (r != null) log.Lab(r);
                    Send(res, "{\"ok\":true}");
                }
                else Send(res, "{\"error\":\"unknown endpoint\"}", code: 404);
            }
            catch (Exception e)
            {
                try { Send(res, "{\"error\":\"" + e.Message.Replace("\"", "'") + "\"}", code: 500); } catch { }
            }
            finally { try { res.OutputStream.Close(); } catch { } }
        }
    }
}
