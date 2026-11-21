using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

namespace Mottainai.Client
{
    /// HTTP side: login, Content Pack ("DLC") download with live MB/s + hash cache, telemetry.
    public class ContentService
    {
        public string BaseUrl { get; private set; }
        public string CacheDir => Path.Combine(Application.persistentDataPath, "content");
        public LoginResponse Login;
        public ContentManifest Manifest;

        // progress (read by UI)
        public long BytesDone, BytesTotal, BytesDownloadedThisRun;
        public float Mbps, Elapsed;
        public string CurrentFile = "", Status = "";
        public bool FromCache;

        public string WaterNormalA, WaterNormalB, RaftSkin;

        public void SetServer(string host, int httpPort)
        {
            host = (host ?? "").Trim();
            if (host.Contains("://")) BaseUrl = host.TrimEnd('/');
            else BaseUrl = $"http://{host}:{httpPort}";
        }

        public IEnumerator DoLogin(string name, string token, Action<string> onError)
        {
            var body = JsonUtility.ToJson(new LoginRequest { name = name, token = token, device = SystemInfo.deviceModel });
            using var req = UnityWebRequest.Post(BaseUrl + "/login", body, "application/json");
            req.timeout = 6;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Login failed: {req.error} ({BaseUrl})"); yield break; }
            Login = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
        }

        public void ClearCache()
        {
            try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        public IEnumerator DownloadPack(bool force, Action<string> onError)
        {
            if (force) ClearCache();
            Directory.CreateDirectory(CacheDir);
            Status = "Fetching manifest...";
            Manifest = null;
            for (int attempt = 0; attempt < 20 && Manifest == null; attempt++)
            {
                using var mreq = UnityWebRequest.Get(BaseUrl + "/content/manifest.json");
                mreq.timeout = 6;
                yield return mreq.SendWebRequest();
                if (mreq.responseCode == 503) { Status = "Server is building the pack..."; yield return new WaitForSecondsRealtime(0.5f); continue; }
                if (mreq.result != UnityWebRequest.Result.Success) { onError?.Invoke("Manifest failed: " + mreq.error); yield break; }
                Manifest = JsonUtility.FromJson<ContentManifest>(mreq.downloadHandler.text);
            }
            if (Manifest == null || Manifest.files == null) { onError?.Invoke("Manifest unavailable"); yield break; }

            BytesTotal = Manifest.totalBytes; BytesDone = 0; BytesDownloadedThisRun = 0; Elapsed = 0; Mbps = 0;
            FromCache = true;
            float start = Time.realtimeSinceStartup;
            foreach (var f in Manifest.files)
            {
                string final = Path.Combine(CacheDir, f.hash + "_" + f.name);
                CurrentFile = f.name;
                if (File.Exists(final) && new FileInfo(final).Length == f.size)
                {
                    BytesDone += f.size;
                    Register(f.name, final);
                    continue;
                }
                FromCache = false;
                string tmp = final + ".part";
                using var req = new UnityWebRequest(BaseUrl + "/content/" + f.name, UnityWebRequest.kHttpVerbGET) { downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true } };
                req.timeout = 120;
                var op = req.SendWebRequest();
                long baseDone = BytesDone;
                while (!op.isDone)
                {
                    long now = (long)req.downloadedBytes;
                    BytesDone = baseDone + now;
                    Elapsed = Time.realtimeSinceStartup - start;
                    Mbps = Elapsed > 0.05f ? (BytesDownloadedThisRun + now) / 1048576f / Elapsed : 0f;
                    Status = $"Downloading {f.name}";
                    yield return null;
                }
                if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Download {f.name} failed: {req.error}"); yield break; }
                BytesDownloadedThisRun += f.size;
                BytesDone = baseDone + f.size;
                if (File.Exists(final)) File.Delete(final);
                File.Move(tmp, final);
                Register(f.name, final);
            }
            Elapsed = Time.realtimeSinceStartup - start;
            if (BytesDownloadedThisRun > 0) Mbps = BytesDownloadedThisRun / 1048576f / Mathf.Max(0.01f, Elapsed);
            Status = FromCache ? "Content pack up to date (cached)" : $"Downloaded {BytesDownloadedThisRun / 1048576f:F1} MB at {Mbps:F1} MB/s";
        }

        void Register(string name, string path)
        {
            if (name == "tuning.json") GameTuning.ApplyJson(File.ReadAllText(path));
            else if (name.StartsWith("water_normal_a")) WaterNormalA = path;
            else if (name.StartsWith("water_normal_b")) WaterNormalB = path;
            else if (name.StartsWith("raft_skin")) RaftSkin = path;
        }

        public static Texture2D LoadRaw(string path, bool linear)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            try
            {
                var name = Path.GetFileNameWithoutExtension(path);
                int us = name.LastIndexOf('_');
                int size = int.Parse(name.Substring(us + 1));
                var bytes = File.ReadAllBytes(path);
                if (bytes.Length != size * size * 4) return null;
                var tex = new Texture2D(size, size, TextureFormat.RGBA32, true, linear) { wrapMode = TextureWrapMode.Repeat, anisoLevel = 2 };
                tex.LoadRawTextureData(bytes);
                tex.Apply(true, true);
                return tex;
            }
            catch (Exception e) { Debug.LogWarning("[Content] texture load failed: " + e.Message); return null; }
        }

        public void PostJson(MonoBehaviour host, string path, string json)
        {
            if (string.IsNullOrEmpty(BaseUrl)) return;
            host.StartCoroutine(Post(path, json));
        }

        IEnumerator Post(string path, string json)
        {
            using var req = UnityWebRequest.Post(BaseUrl + path, json, "application/json");
            req.timeout = 5;
            yield return req.SendWebRequest();
        }
    }
}
