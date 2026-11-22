using System.Collections;
using System.Collections.Generic;
using Mottainai.Gameplay;
using Mottainai.Net;
using Mottainai.Platform;
using UnityEngine;

namespace Mottainai.Client
{
    public enum UiScreen { Login, Download, Menu, Lobby, Game, Results, Lab }

    /// Client state machine: Title -> Content Pack -> Menu -> Lobby (room WebSocket) -> Match -> Results.
    public partial class ClientApp : MonoBehaviour
    {
        public static ClientApp Instance { get; private set; }

        ContentService content;
        NetClient net;
        WorldView world;
        GestureInput input;
        Sfx sfx;
        MusicPlayer musicPlayer;
        NetworkLab lab;
        UiScreen screen;

        string playerName, token;
        bool forceDownload, netHudOn = true;
        Role preferredRole;
        Role myRole;
        LobbyState lobby;
        MatchStartInfo match;
        ResultsData results;
        float hintUntil, localThrowReady, lastLocalStroke, lastLocalThrow;
        int hudScore; float hudTimeLeft; MatchPhase hudPhase; bool hudCombo; float hudShield;
        int syncStreak; float lastSyncTime;
        float magnetUntil, turboUntil;

        public static ClientApp Create()
        {
            var go = new GameObject("MottainaiClient");
            DontDestroyOnLoad(go);
            return go.AddComponent<ClientApp>();
        }

        void Awake()
        {
            Instance = this;
            content = new ContentService();
            net = new NetClient();
            lab = new NetworkLab();
            sfx = gameObject.AddComponent<Sfx>();
            musicPlayer = gameObject.AddComponent<MusicPlayer>();
            gameObject.AddComponent<PerfGovernor>();
            input = gameObject.AddComponent<GestureInput>();
            // match the display (120 Hz phones -> lower touch-to-photon latency); PerfGovernor protects low-end devices
            Application.targetFrameRate = Mathf.Clamp(Mathf.RoundToInt((float)Screen.currentResolution.refreshRateRatio.value), 60, 120);
            playerName = PlayerPrefs.GetString("name", "Player" + Random.Range(10, 99));
            token = PlayerPrefs.GetString("token", "");
            preferredRole = (Role)PlayerPrefs.GetInt("role", 0);
            NetworkInfo.Provider.Refresh();

            net.OnWelcome += w => { if (net.ProtocolMismatch) Toast("Update app: protocol mismatch"); else Toast($"Connected to room {w.roomCode} (room colo {w.roomColo})"); };
            net.OnLobby += OnLobby;
            net.OnError += e => { Toast(e); SetStatus(e); };
            net.OnMatchStart += OnMatchStart;
            net.OnSnapshot += OnSnapshot;
            net.OnEvents += OnEvents;
            net.OnResults += OnResults;
            net.OnDisconnected += OnDisconnected;

            input.OnStroke += LocalStroke;
            input.OnTap += LocalTap;
            input.OnSwipe += LocalSwipe;
            input.OnToggleHud += () => { netHudOn = !netHudOn; netHudPanel.SetActive(netHudOn); };
            input.OnDevStroke += side => { if (side == myRole) LocalStroke(0.85f, false); };
            input.OnDevThrow += () => { if (myRole == Role.Collector && world != null) ThrowDirection(world.CameraFlatForward); };

            var cli = System.Environment.GetCommandLineArgs();
            for (int i = 0; i < cli.Length; i++)
            {
                if (cli[i] == "-autoplay") autoplay = true;
                if (cli[i] == "-connect" && i + 1 < cli.Length) content.SetBaseUrl(cli[i + 1]);
                if (cli[i] == "-name" && i + 1 < cli.Length) playerName = cli[i + 1];
                if (cli[i] == "-shots" && i + 1 < cli.Length) shotsDir = cli[i + 1];
                if (cli[i] == "-quitafter" && i + 1 < cli.Length) float.TryParse(cli[i + 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out quitAfter);
                if (cli[i] == "-room" && i + 1 < cli.Length) autoRoom = cli[i + 1];
                if (cli[i] == "-role" && i + 1 < cli.Length && int.TryParse(cli[i + 1], out int ar)) preferredRole = (Role)ar;
            }

            world = WorldView.Create(null); // live river behind the menus (attract mode)
            BuildUi();
            Show(UiScreen.Login);
            if (autoplay) StartCoroutine(AutoPlay());
        }

        // ---------------- smoke test: -autoplay -connect <ip> ----------------
        bool autoplay;
        string shotsDir, autoRoom = "DEMO";
        float quitAfter;
        int shotIndex;

        void Shot(string label)
        {
            if (string.IsNullOrEmpty(shotsDir)) return;
            System.IO.Directory.CreateDirectory(shotsDir);
            var p = System.IO.Path.Combine(shotsDir, $"{shotIndex++:00}_{label}.png");
            ScreenCapture.CaptureScreenshot(p);
            Debug.Log("[AUTOPLAY] screenshot " + p);
        }

        IEnumerator AutoPlay()
        {
            yield return null;
            nameInput.text = playerName;
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-lab") >= 0)
            {
                Show(UiScreen.Lab);
                RunLab();
                yield return null;
                while (lab.Running) yield return null;
                var lr = lab.Result;
                Debug.Log(lr == null ? "[AUTOPLAY] LAB failed" : $"[AUTOPLAY] LAB edge {lr.edgeColo} {lr.edgeRttAvg:F1}/{lr.edgeRttP95:F1} ms, room {lr.roomColo} {lr.roomRttAvg:F1}/{lr.roomRttP95:F1} ms, down {lr.downMbps:F0} Mbit/s, up {lr.upMbps:F0} Mbit/s csv={lab.CsvPath}");
                Shot("lab");
            }
            Debug.Log($"[AUTOPLAY] login to {content.BaseUrl} as {playerName}");
            StartLogin();
            float t0 = Time.realtimeSinceStartup;
            while (screen != UiScreen.Menu && Time.realtimeSinceStartup - t0 < 90f) yield return null;
            if (screen != UiScreen.Menu) { Debug.LogError("[AUTOPLAY] FAIL: never reached menu. " + loginStatus.text); Application.Quit(2); yield break; }
            Debug.Log($"[AUTOPLAY] menu; content {content.BytesTotal / 1048576f:F1} MB at {content.Mbps:F1} MB/s cached={content.FromCache}");
            yield return new WaitForSecondsRealtime(1.5f);
            Shot("menu");
            JoinRoom(autoRoom);
            while (screen != UiScreen.Lobby || lobby == null) yield return null;
            yield return new WaitForSecondsRealtime(0.8f);
            Shot("lobby");
            Debug.Log($"[AUTOPLAY] room {lobby.code} role {myRole}, starting");
            net.StartMatch();
            float logT = 0, throwT = 0, gameT = 0;
            int nextShot = 0;
            float[] shotTimes = { 1.5f, 8f, 20f, 40f };
            while (results == null)
            {
                logT += Time.unscaledDeltaTime; throwT += Time.unscaledDeltaTime;
                if (screen == UiScreen.Game) gameT += Time.unscaledDeltaTime;
                if (nextShot < shotTimes.Length && gameT >= shotTimes[nextShot]) Shot($"game_{shotTimes[nextShot++]:0}s");
                if (quitAfter > 0 && gameT > quitAfter) { Debug.Log("[AUTOPLAY] quitafter reached"); Application.Quit(0); yield break; }
                if (screen == UiScreen.Game && throwT > 0.7f && world != null && myRole == Role.Collector)
                {
                    throwT = 0;
                    ThrowDirection(Quaternion.Euler(0, Random.Range(-35f, 35f), 0) * world.CameraFlatForward);
                }
                if (logT > 5f)
                {
                    logT = 0;
                    var s = net.stats;
                    Debug.Log($"[AUTOPLAY] screen={screen} tick={s.ServerTick} score={hudScore} left={hudTimeLeft:F0}s snaps={s.SnapshotRate:F0}/s down={s.DownKbps:F0}kbps up={s.UpKbps:F0}kbps msgs={s.Pps:F0}/s rtt={s.RttAvg:F1}/{s.RttP95:F1}ms jit={s.Jitter:F1}ms buf={s.InterpMs:F0}ms snapJit={s.SnapJitterMs:F0}ms late={s.LatePct:F0}% lead={s.PredLeadMs:F0}ms nodelay={s.NoDelay} fps={s.Fps:F0} raft={(world != null ? world.RaftPos.ToString() : "-")}");
                }
                yield return null;
            }
            var r = results;
            Debug.Log($"[AUTOPLAY] RESULT reason={r.reason} total={r.total} catch={r.catchScore} toxic=-{r.toxicPenalty} acc={r.Accuracy:P0} dist={r.distance:F0}m kg={r.kg:F1} dur={r.duration:F0}s counts={string.Join(",", r.counts)} rxMB={net.stats.TotalDownMB:F1}");
            yield return new WaitForSecondsRealtime(2.5f);
            Shot("results");
            yield return new WaitForSecondsRealtime(1f);
            Application.Quit(0);
        }

        // ---------------- flow ----------------
        void StartLogin()
        {
            playerName = string.IsNullOrWhiteSpace(nameInput.text) ? playerName : nameInput.text.Trim();
            PlayerPrefs.SetString("name", playerName);
            PlayerPrefs.Save();
            StartCoroutine(LoginFlow());
        }

        IEnumerator LoginFlow()
        {
            string error = null;
            Show(UiScreen.Download);
            downloadTitle.text = "Contacting server...";
            yield return content.CheckHealth(e => error = e);
            if (error != null) { Show(UiScreen.Login); SetStatus(error); yield break; }
            downloadTitle.text = "Logging in...";
            yield return content.DoLogin(playerName, e => error = e);
            if (error != null) { Show(UiScreen.Login); SetStatus(error); yield break; }
            if (string.IsNullOrEmpty(token)) token = content.Login.token; // stable token: a reconnect restores the role
            playerName = content.Login.name;
            PlayerPrefs.SetString("token", token);

            downloadTitle.text = "Downloading Content Pack";
            yield return content.DownloadPack(forceDownload, e => error = e);
            if (error != null) { Show(UiScreen.Login); SetStatus(error); yield break; }
            downloadTitle.text = content.Status;
            yield return new WaitForSecondsRealtime(content.FromCache ? 0.4f : 1.2f);
            world.ApplyContent(content); // texture upload hitch now, not at match start
            Show(UiScreen.Menu);
            RefreshMenuUi();
        }

        void JoinRoom(string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (code.Length < 3) { SetStatus("Enter a room code"); return; }
            lobby = null;
            net.Connect(content.BaseUrl, code, token, playerName, preferredRole);
            StartCoroutine(WaitJoin(code));
        }

        void CreateRoom() => StartCoroutine(content.CreateRoom(JoinRoom, e => SetStatus(e)));

        IEnumerator WaitJoin(string code)
        {
            SetStatus($"Joining {code}...");
            float t0 = Time.realtimeSinceStartup;
            while (!net.Ready && Time.realtimeSinceStartup - t0 < 10f) yield return null;
            if (!net.Ready) { string err = net.ws.LastError; net.Disconnect(); SetStatus($"Could not join {code}: {err}"); yield break; }
            SetStatus("");
            Show(UiScreen.Lobby);
            RefreshLobbyUi();
        }

        void LeaveRoom()
        {
            net.Disconnect();
            lobby = null;
            Show(UiScreen.Menu);
            RefreshMenuUi();
        }

        void OnDisconnected(string reason)
        {
            if (!net.WantConnected) return;
            Toast("Connection lost - reconnecting...");
        }

        void OnLobby(LobbyState s)
        {
            lobby = s;
            var me = s.players.Find(p => p.clientId == net.ClientId);
            if (me != null && screen != UiScreen.Game) myRole = me.role;
            if (screen == UiScreen.Lobby) RefreshLobbyUi();
        }

        void OnMatchStart(MatchStartInfo info)
        {
            match = info;
            myRole = info.role;
            if (world == null) world = WorldView.Create(content);
            world.ResetWorld();
            world.TickRate = info.tickRate; world.Seed = info.seed;
            net.TickRate = info.tickRate;
            world.raftView.SetLocalRole(myRole);
            world.Attract = false;
            magnetUntil = turboUntil = 0f;
            world.SetPowers(0f, 0f);
            world.AimAssist = myRole == Role.Collector;
            input.role = myRole;
            input.active = true;
            net.stats.ResetAggregates();
            hintUntil = Time.time + info.countdown + 3f;
            results = null;
            Show(UiScreen.Game);
            SetRoleBadge(myRole);
        }

        void OnSnapshot(SnapshotFrame f)
        {
            if (world == null || screen == UiScreen.Login) return;
            bool first = world.LatestTick == 0;
            world.OnSnapshot(f);
            if (f.hasRaft)
            {
                hudScore = f.score; hudTimeLeft = f.timeLeft; hudPhase = f.phase; hudCombo = f.comboArmed; hudShield = f.raft.shieldLeft;
                if (first) { world.SnapCamera(); }
            }
        }

        void OnResults(ResultsData r)
        {
            results = r;
            input.active = false;
            StartCoroutine(ShowResultsSoon());
        }

        IEnumerator ShowResultsSoon()
        {
            yield return new WaitForSecondsRealtime(1.6f);
            if (results != null) { FillResults(); Show(UiScreen.Results); }
        }

        // ---------------- events from server ----------------
        void OnEvents(List<MatchEvent> evs)
        {
            if (world == null) return;
            var raft = world.raftView;
            foreach (var e in evs)
            {
                switch (e.type)
                {
                    case EvType.Countdown: Big(e.value.ToString(), Color.white, 0.9f); sfx.Play(Sfx.Id.Beep); break;
                    case EvType.Go: Big("PADDLE!", new Color(0.5f, 1f, 0.6f), 1f); sfx.Play(Sfx.Id.Go); break;
                    case EvType.Stroke:
                        if (e.role == myRole) world.AckLocalStroke(); // prediction: this stroke is now in the server state
                        bool mine = e.role == myRole && Time.time - lastLocalStroke < 0.6f;
                        if (!mine)
                        {
                            raft.Stroke(e.role, e.extra == 1);
                            world.fx.Splash(raft.PaddleTip(e.role == Role.LeftPaddler), false);
                            sfx.Play(Sfx.Id.Splash, 0.35f);
                        }
                        break;
                    case EvType.Sync:
                        syncStreak = Time.time - lastSyncTime < 2f ? syncStreak + 1 : 1;
                        lastSyncTime = Time.time;
                        Big(syncStreak > 1 ? $"SYNC x{syncStreak}!" : "SYNC!", Color.Lerp(new Color(0.4f, 0.9f, 1f), new Color(1f, 0.85f, 0.3f), Mathf.Clamp01((syncStreak - 1) / 4f)), 0.7f);
                        world.SyncKick();
                        world.fx.Splash(raft.PaddleTip(true), true);
                        world.fx.Splash(raft.PaddleTip(false), true);
                        sfx.Play(Sfx.Id.Sync);
                        if (myRole != Role.Collector) NetworkInfo.Provider.Vibrate(25);
                        break;
                    case EvType.NetThrow:
                        if (myRole != Role.Collector || Time.time - lastLocalThrow > 0.5f)
                        {
                            raft.Throw();
                            world.fx.ThrowNet(raft.CollectorHand, new Vector3(e.x, 0.1f, e.z), e.value / 1000f);
                        }
                        break;
                    case EvType.Catch:
                    {
                        var type = (WasteType)(e.extra & 0x7F);
                        bool combo = (e.extra & 0x80) != 0;
                        var pos = world.EntityPosition(e.entity, new Vector3(e.x, 0.2f, e.z));
                        world.Collect(e.entity);
                        Color c = WasteCatalog.Colors[(int)type];
                        world.fx.Burst(pos, c, type == WasteType.Valuable ? 26 : 12, 2f);
                        if (type == WasteType.Toxic) { Popup(pos, $"TOXIC -{Mathf.Abs(GameTuning.Current.wasteScores[(int)WasteType.Toxic])}", c, 1.1f); sfx.Play(Sfx.Id.Toxic); }
                        else
                        {
                            Popup(pos, (combo ? "COMBO " : "") + "+" + e.value, c, combo ? 1.4f : 1f);
                            sfx.Play(type == WasteType.Valuable ? Sfx.Id.Valuable : Sfx.Id.Catch);
                        }
                        if (myRole == Role.Collector) NetworkInfo.Provider.Vibrate(15);
                        break;
                    }
                    case EvType.Combo: Toast($"COMBO READY: next catch x{e.value / 10f:0.0}"); sfx.Play(Sfx.Id.Combo, 0.6f); break;
                    case EvType.Miss: Popup(new Vector3(e.x, 0.3f, e.z), "miss", new Color(0.8f, 0.85f, 0.9f), 0.8f); world.fx.Splash(new Vector3(e.x, 0.1f, e.z), false); sfx.Play(Sfx.Id.Miss, 0.6f); break;
                    case EvType.Hit:
                        world.fx.Burst(new Vector3(e.x, 0.5f, e.z), new Color(0.55f, 0.4f, 0.3f), 30, 4f, 0.2f, 4f);
                        world.fx.Splash(world.RaftPos, true);
                        Big("CRASH!", new Color(1f, 0.4f, 0.3f), 1.5f);
                        world.HitShake();
                        sfx.Play(Sfx.Id.Hit);
                        NetworkInfo.Provider.Vibrate(120);
                        break;
                    case EvType.ShieldOn: world.Collect(e.entity); Big("SHIELD!", new Color(0.4f, 0.9f, 1f), 0.8f); sfx.Play(Sfx.Id.Shield); break;
                    case EvType.ShieldOff: Toast("Shield off"); break;
                    case EvType.PowerOn:
                    {
                        world.Collect(e.entity);
                        float dur = e.value / 10f;
                        if ((PowerUp)e.extra == PowerUp.Magnet) { magnetUntil = Time.time + dur; Big("MAGNET NET!", new Color(1f, 0.45f, 0.9f), 0.9f); }
                        else { turboUntil = Time.time + dur; Big("TURBO!", new Color(1f, 0.65f, 0.2f), 0.9f); world.SyncKick(); }
                        world.SetPowers(magnetUntil, turboUntil);
                        sfx.Play(Sfx.Id.Shield);
                        NetworkInfo.Provider.Vibrate(40);
                        break;
                    }
                    case EvType.PowerOff:
                        if ((PowerUp)e.extra == PowerUp.Magnet) magnetUntil = 0f; else turboUntil = 0f;
                        world.SetPowers(magnetUntil, turboUntil);
                        break;
                    case EvType.GameOver:
                        var reason = (EndReason)e.value;
                        if (reason == EndReason.TimeUp) raft.Cheer();
                        Big(reason == EndReason.TimeUp ? "TIME UP!" : reason == EndReason.Crashed ? "GAME OVER" : "MATCH ENDED", Color.white, 2f);
                        break;
                    case EvType.BotTakeover: Toast($"Bot took over {RoleInfo.Name(e.role)}"); break;
                }
            }
        }

        // ---------------- local gestures (immediate feedback, server-corrected) ----------------
        void LocalStroke(float power, bool back)
        {
            if (screen != UiScreen.Game || world == null) return;
            net.SendStroke(myRole, power, back);
            if (hudPhase == MatchPhase.Running) world.PredictStroke(myRole == Role.LeftPaddler, power, back);
            lastLocalStroke = Time.time;
            world.raftView.Stroke(myRole, back);
            world.fx.Splash(world.raftView.PaddleTip(myRole == Role.LeftPaddler), false);
            sfx.Play(Sfx.Id.Splash, 0.6f);
            NetworkInfo.Provider.Vibrate(12);
        }

        void LocalTap(Vector2 screenPos)
        {
            if (screen != UiScreen.Game || world == null || myRole != Role.Collector) return;
            if (world.PickWaste(screenPos, out var id, out var p)) ThrowAt(id, p);
            else if (world.ScreenToWater(screenPos, out var w)) ThrowDirection(w - world.RaftPos);
        }

        void LocalSwipe(Vector2 dir)
        {
            if (screen != UiScreen.Game || world == null || myRole != Role.Collector) return;
            ThrowDirection(world.CameraFlatRight * dir.x + world.CameraFlatForward * dir.y);
        }

        void ThrowAt(ushort id, Vector3 p)
        {
            if (Time.time < localThrowReady) return;
            var t = GameTuning.Current;
            var d = p - world.RaftPos; d.y = 0;
            if (d.magnitude > t.netRange * 1.1f) { ThrowDirection(d); return; }
            localThrowReady = Time.time + t.netCooldown;
            lastLocalThrow = Time.time;
            net.SendThrow(id, new Vector2(d.x, d.z));
            world.raftView.Throw();
            world.fx.ThrowNet(world.raftView.CollectorHand, p, t.netTravel);
            sfx.Play(Sfx.Id.Throw);
            NetworkInfo.Provider.Vibrate(10);
        }

        void ThrowDirection(Vector3 d)
        {
            if (Time.time < localThrowReady) return;
            var t = GameTuning.Current;
            d.y = 0;
            if (d.sqrMagnitude < 1e-4f) d = world.CameraFlatForward;
            d.Normalize();
            localThrowReady = Time.time + t.netCooldown;
            lastLocalThrow = Time.time;
            net.SendThrow(NetConst.NoEntity, new Vector2(d.x, d.z));
            world.raftView.Throw();
            world.fx.ThrowNet(world.raftView.CollectorHand, world.RaftPos + d * t.netRange * 0.75f, t.netTravel);
            sfx.Play(Sfx.Id.Throw);
        }

        // ---------------- per frame ----------------
        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            bool inMatch = screen == UiScreen.Game;
            if (world != null)
            {
                var st = net.stats;
                st.InterpMs = world.InterpMs; st.SnapJitterMs = world.JitterMs; st.PredLeadMs = world.PredictionLeadMs; st.LatePct = world.LatePct;
                world.RttMs = st.RttAvg; world.RttMinMs = st.RttMin; world.SnapshotHz = net.SnapshotHz;
            }
            net.Tick(dt, inMatch);
            NetworkInfo.Provider.Refresh();

            UpdateUi(dt);
        }

        void OnApplicationPause(bool paused)
        {
            if (!paused && net.WantConnected && !net.ws.IsOpen) net.Connect(content.BaseUrl, net.roomCode, token, playerName, myRole);
        }
    }
}
