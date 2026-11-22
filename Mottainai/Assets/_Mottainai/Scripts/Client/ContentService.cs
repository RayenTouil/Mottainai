using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Networking;

namespace Mottainai.Client
{
    /// HTTPS side (Cloudflare Worker): health, login, rooms, Content Pack download (live MB/s, sha256 cache), telemetry.
    public class ContentService
    {
        public string BaseUrl { get; private set; }
        public string DefaultUrl { get; private set; }
        public string CacheDir => Path.Combine(Application.persistentDataPath, "content");
        public LoginResponse Login;
        public ContentManifest Manifest;
        public HealthResponse Health;
        public string EdgeColo => Health?.colo ?? "?";

        public long BytesDone, BytesTotal, BytesDownloadedThisRun;
        public float Mbps, Elapsed;
        public string CurrentFile = "", Status = "";
        public bool FromCache;

        public string WaterNormalA, WaterNormalB, RaftSkin;

        public ContentService()
        {
            var cfg = Resources.Load<TextAsset>("server_config");
            DefaultUrl = cfg != null ? JsonUtility.FromJson<ServerConfig>(cfg.text)?.baseUrl : null;
            if (string.IsNullOrEmpty(DefaultUrl)) DefaultUrl = "https://mottainai.mottainai-server.workers.dev";
            SetBaseUrl(PlayerPrefs.GetString("serverOverride", ""));
        }

        /// Empty = use server_config.json.
        public void SetBaseUrl(string overrideUrl)
        {
            overrideUrl = (overrideUrl ?? "").Trim();
            PlayerPrefs.SetString("serverOverride", overrideUrl);
            BaseUrl = (string.IsNullOrEmpty(overrideUrl) ? DefaultUrl : overrideUrl).TrimEnd('/');
        }

        public IEnumerator CheckHealth(Action<string> onError)
        {
            using var req = UnityWebRequest.Get(BaseUrl + "/api/health");
            req.timeout = 8;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Server unreachable: {req.error} ({BaseUrl})"); yield break; }
            Health = JsonUtility.FromJson<HealthResponse>(req.downloadHandler.text);
            if (Health == null || Health.protocolVersion != Net.Msg.ProtocolVersion)
                onError?.Invoke($"Update app: server protocol v{Health?.protocolVersion}, app v{Net.Msg.ProtocolVersion}");
        }

        public IEnumerator DoLogin(string name, Action<string> onError)
        {
            using var req = UnityWebRequest.Post(BaseUrl + "/api/login", JsonUtility.ToJson(new LoginRequest { name = name }), "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke($"Login failed: {req.error} ({BaseUrl})"); yield break; }
            Login = JsonUtility.FromJson<LoginResponse>(req.downloadHandler.text);
        }

        /// JSON GET (body null) / POST to the Worker API; done(httpStatus, responseText).
        public IEnumerator Api(string path, string body, Action<long, string> done)
        {
            using var req = body == null ? UnityWebRequest.Get(BaseUrl + path) : UnityWebRequest.Post(BaseUrl + path, body, "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            done?.Invoke(req.responseCode, req.downloadHandler?.text ?? "");
        }

        public IEnumerator CreateRoom(Action<string> onCode, Action<string> onError)
        {
            using var req = UnityWebRequest.Post(BaseUrl + "/api/rooms", "{}", "application/json");
            req.timeout = 8;
            yield return req.SendWebRequest();
            if (req.result != UnityWebRequest.Result.Success) { onError?.Invoke("Create room failed: " + req.error); yield break; }
            onCode?.Invoke(JsonUtility.FromJson<RoomCreated>(req.downloadHandler.text)?.code);
        }

        public void ClearCache()
        {
            try { if (Directory.Exists(CacheDir)) Directory.Delete(CacheDir, true); } catch (Exception e) { Debug.LogWarning(e.Message); }
        }

        public static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var fs = File.OpenRead(path);
            var h = sha.ComputeHash(fs);
            var sb = new System.Text.StringBuilder(64);
            foreach (var b in h) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public IEnumerator DownloadPack(bool force, Action<string> onError)
        {
            if (force) ClearCache();
            Directory.CreateDirectory(CacheDir);
            Status = "Fetching manifest...";
            using (var mreq = UnityWebRequest.Get(BaseUrl + "/pack/manifest.json?r=" + UnityEngine.Random.Range(0, int.MaxValue)))
            {
                mreq.timeout = 8;
                yield return mreq.SendWebRequest();
                if (mreq.result != UnityWebRequest.Result.Success) { onError?.Invoke("Manifest failed: " + mreq.error); yield break; }
                Manifest = JsonUtility.FromJson<ContentManifest>(mreq.downloadHandler.text);
            }
            if (Manifest == null || Manifest.files == null) { onError?.Invoke("Manifest unavailable"); yield break; }

            BytesTotal = Manifest.totalBytes; BytesDone = 0; BytesDownloadedThisRun = 0; Elapsed = 0; Mbps = 0;
            FromCache = true;
            float start = Time.realtimeSinceStartup;
            foreach (var f in Manifest.files)
            {
                string final = Path.Combine(CacheDir, f.hash.Substring(0, Math.Min(16, f.hash.Length)) + "_" + f.name);
                CurrentFile = f.name;
                if (File.Exists(final) && new FileInfo(final).Length == f.size)
                {
                    BytesDone += f.size;
                    Register(f.name, final);
                    continue;
                }
                FromCache = false;
                string tmp = final + ".part";
                using var req = new UnityWebRequest(BaseUrl + "/pack/" + f.name + (force ? "?r=" + UnityEngine.Random.Range(0, int.MaxValue) : ""), UnityWebRequest.kHttpVerbGET)
                    { downloadHandler = new DownloadHandlerFile(tmp) { removeFileOnAbort = true } };
                req.timeout = 180;
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
                Status = $"Verifying {f.name} (sha256)";
                yield return null;
                string got = Sha256File(tmp);
                if (!string.Equals(got, f.hash, StringComparison.OrdinalIgnoreCase)) { File.Delete(tmp); onError?.Invoke($"{f.name}: sha256 mismatch"); yield break; }
                BytesDownloadedThisRun += f.size;
                BytesDone = baseDone + f.size;
                if (File.Exists(final)) File.Delete(final);
                File.Move(tmp, final);
                Register(f.name, final);
            }
            Elapsed = Time.realtimeSinceStartup - start;
            if (BytesDownloadedThisRun > 0) Mbps = BytesDownloadedThisRun / 1048576f / Mathf.Max(0.01f, Elapsed);
            Status = FromCache ? "Content pack up to date (cached by sha256)" : $"Downloaded {BytesDownloadedThisRun / 1048576f:F1} MB at {Mbps:F1} MB/s ({Mbps * 8.39f:F0} Mbit/s)";
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
                tex.SetPixelData(bytes, 0); // pack holds mip 0 only (LoadRawTextureData expects the full mip chain)
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
            req.timeout = 8;
            yield return req.SendWebRequest();
        }
    }
}
