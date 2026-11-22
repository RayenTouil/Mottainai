using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Mottainai.Net;
using UnityEngine;
using UnityEngine.Networking;
using Debug = UnityEngine.Debug;

namespace Mottainai.Client
{
    /// Network Lab: (a) edge RTT 50 HTTPS GETs (keep-alive), (b) room RTT 100 WS pings, (c) pack download, (d) 5 MB upload.
    public class NetworkLab
    {
        public bool Running { get; private set; }
        public string Phase = "";
        public float Progress;
        public LabResultDto Result;
        public string CsvPath => Path.Combine(Application.persistentDataPath, "network_lab.csv");

        class DiscardHandler : DownloadHandlerScript
        {
            public long bytes;
            public DiscardHandler() : base(new byte[64 * 1024]) { }
            protected override bool ReceiveData(byte[] data, int dataLength) { bytes += dataLength; return true; }
        }

        static void Stats(List<float> v, out float avg, out float p95, out float jitter)
        {
            avg = p95 = jitter = 0f;
            if (v.Count == 0) return;
            float sum = 0f;
            for (int i = 0; i < v.Count; i++) { sum += v[i]; if (i > 0) jitter += Mathf.Abs(v[i] - v[i - 1]); }
            avg = sum / v.Count;
            jitter = v.Count > 1 ? jitter / (v.Count - 1) : 0f;
            var s = new List<float>(v);
            s.Sort();
            p95 = s[Mathf.Clamp(Mathf.CeilToInt(s.Count * 0.95f) - 1, 0, s.Count - 1)];
        }

        static string Rnd() => UnityEngine.Random.Range(0, int.MaxValue).ToString();

        public IEnumerator Run(ContentService content, string netType, string note)
        {
            Running = true; Result = null; Progress = 0f;
            string baseUrl = content.BaseUrl;
            var r = new LabResultDto { ts = DateTime.UtcNow.ToString("O"), device = SystemInfo.deviceModel, netType = netType, note = note, edgeColo = content.EdgeColo };

            // (a) edge RTT
            Phase = "Edge RTT: 50 HTTPS requests (keep-alive)...";
            var edge = new List<float>(50);
            string path = "/cdn-cgi/trace";
            for (int i = 0; i < 51; i++)
            {
                using var req = UnityWebRequest.Get(baseUrl + path + (path == "/api/lab/ping" ? "?r=" + i : ""));
                req.timeout = 5;
                var sw = Stopwatch.StartNew();
                yield return req.SendWebRequest();
                float ms = (float)sw.Elapsed.TotalMilliseconds;
                if (req.result != UnityWebRequest.Result.Success)
                {
                    if (path != "/api/lab/ping") { path = "/api/lab/ping"; i = -1; edge.Clear(); }
                    continue;
                }
                if (i == 0)
                {
                    string t = req.downloadHandler.text;
                    int c = t.IndexOf("colo=", StringComparison.Ordinal);
                    if (c >= 0) { int e = t.IndexOf('\n', c); r.edgeColo = t.Substring(c + 5, (e > c ? e : t.Length) - c - 5).Trim(); }
                    continue; // first request pays the TLS handshake: dropped
                }
                edge.Add(ms);
                Progress = i / 51f * 0.25f;
            }
            Stats(edge, out r.edgeRttAvg, out r.edgeRttP95, out r.edgeJitter);
            r.edgeN = edge.Count;

            // (b) room RTT over WebSocket to a lab Durable Object
            Phase = "Room RTT: 100 WebSocket pings to the room server...";
            var room = new List<float>(100);
            var ws = new WsClient();
            var wr = new ByteWriter();
            var clock = Stopwatch.StartNew();
            ws.Connect(NetClient.WsUrl(baseUrl, "LAB" + UnityEngine.Random.Range(1000, 9999), "lab" + Rnd(), "lab", Role.None));
            float t0 = Time.realtimeSinceStartup;
            bool welcomed = false;
            int sent = 0;
            double lastSend = -1;
            while (Time.realtimeSinceStartup - t0 < 40f && room.Count < 100)
            {
                ws.Poll((m, recvMs) =>
                {
                    if (m.Length == 0) return;
                    var br = new ByteReader(m, 1, m.Length - 1);
                    if (m[0] == Msg.Welcome) { r.roomColo = Codec.ReadWelcome(ref br).roomColo; welcomed = true; }
                    else if (m[0] == Msg.Pong) { room.Add((float)(recvMs - br.F64())); lastSend = -1; }
                });
                double now = NetTime.Ms;
                if (welcomed && sent < 100 && (lastSend < 0 || now - lastSend > 1000))
                {
                    ws.Send(wr.Begin(Msg.Ping).F64(now).ToArray());
                    lastSend = now;
                    sent++;
                }
                Progress = 0.25f + room.Count / 100f * 0.25f;
                yield return null;
            }
            ws.Close();
            Stats(room, out r.roomRttAvg, out r.roomRttP95, out r.roomJitter);
            r.roomN = room.Count;

            // (c) download: content pack files, cache-busted
            Phase = "Download: content pack from the edge...";
            if (content.Manifest == null)
            {
                using var mreq = UnityWebRequest.Get(baseUrl + "/pack/manifest.json?r=" + Rnd());
                yield return mreq.SendWebRequest();
                if (mreq.result == UnityWebRequest.Result.Success) content.Manifest = JsonUtility.FromJson<ContentManifest>(mreq.downloadHandler.text);
            }
            long total = 0;
            float dlSecs = 0f;
            if (content.Manifest?.files != null)
            {
                long all = Math.Max(1, content.Manifest.totalBytes);
                foreach (var f in content.Manifest.files)
                {
                    var dh = new DiscardHandler();
                    using var req = new UnityWebRequest(baseUrl + "/pack/" + f.name + "?r=" + Rnd(), UnityWebRequest.kHttpVerbGET, dh, null);
                    req.timeout = 120;
                    var sw = Stopwatch.StartNew();
                    var op = req.SendWebRequest();
                    while (!op.isDone) { Progress = 0.5f + Mathf.Clamp01((total + dh.bytes) / (float)all) * 0.3f; yield return null; }
                    dlSecs += (float)sw.Elapsed.TotalSeconds;
                    total += dh.bytes;
                }
            }
            r.downMB = total / 1048576f;
            r.downMbps = dlSecs > 0 ? total * 8f / 1e6f / dlSecs : 0f;

            // (d) upload 5 MB
            Phase = "Upload: 5 MB to the edge...";
            var payload = new byte[5 * 1024 * 1024];
            new System.Random(1).NextBytes(payload);
            using (var up = new UnityWebRequest(baseUrl + "/api/lab/upload", UnityWebRequest.kHttpVerbPOST, new DownloadHandlerBuffer(), new UploadHandlerRaw(payload)))
            {
                up.SetRequestHeader("Content-Type", "application/octet-stream");
                up.timeout = 120;
                var sw = Stopwatch.StartNew();
                var op = up.SendWebRequest();
                while (!op.isDone) { Progress = 0.8f + up.uploadProgress * 0.2f; yield return null; }
                float secs = (float)sw.Elapsed.TotalSeconds;
                if (up.result == UnityWebRequest.Result.Success) { r.upMB = payload.Length / 1048576f; r.upMbps = secs > 0 ? payload.Length * 8f / 1e6f / secs : 0f; }
            }

            Progress = 1f;
            Result = r;
            SaveCsv(r);
            using (var post = UnityWebRequest.Post(baseUrl + "/api/telemetry", JsonUtility.ToJson(r), "application/json"))
            {
                post.timeout = 8;
                yield return post.SendWebRequest();
            }
            Phase = "Done";
            Running = false;
        }

        void SaveCsv(LabResultDto r)
        {
            try
            {
                bool header = !File.Exists(CsvPath);
                using var sw = new StreamWriter(CsvPath, true);
                var ci = System.Globalization.CultureInfo.InvariantCulture;
                if (header) sw.WriteLine("ts,device,netType,edgeColo,roomColo,edgeRttAvg,edgeRttP95,edgeJitter,roomRttAvg,roomRttP95,roomJitter,downMbps,upMbps,downMB,upMB");
                string F(float v) => v.ToString("F2", ci);
                sw.WriteLine(string.Join(",", r.ts, r.device?.Replace(",", ";"), r.netType, r.edgeColo, r.roomColo, F(r.edgeRttAvg), F(r.edgeRttP95), F(r.edgeJitter),
                    F(r.roomRttAvg), F(r.roomRttP95), F(r.roomJitter), F(r.downMbps), F(r.upMbps), F(r.downMB), F(r.upMB)));
            }
            catch (Exception e) { Debug.LogWarning("[Lab] csv: " + e.Message); }
        }
    }
}
