using System.Collections.Generic;
using System.Text;
using Mottainai.Gameplay;
using Mottainai.Net;
using Mottainai.Platform;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mottainai.Client
{
    public partial class ClientApp
    {
        Canvas mainCanvas, hudCanvas, netCanvas;
        readonly Dictionary<UiScreen, GameObject> panels = new Dictionary<UiScreen, GameObject>();

        // login
        TMP_InputField nameInput, serverInput;
        TextMeshProUGUI loginStatus, menuHeader, menuStatus;
        Button forceToggle;
        GameObject debugPanel;
        int titleTaps; float titleTapAt;
        // download
        TextMeshProUGUI downloadTitle, downloadInfo, downloadFile;
        RectTransform downloadFill;
        // lobby
        TextMeshProUGUI lobbyHeader, lobbyRoomTitle, lobbyPlayers, lobbyStatus;
        TMP_InputField codeInput;
        readonly Button[] roleButtons = new Button[3];
        Button readyButton, startButton;
        // hud
        RectTransform progressFill;
        TextMeshProUGUI distanceText;
        TextMeshProUGUI scoreText, comboText, timerText, shieldText, bigText, hintText, toastText, badgeLetter, badgeName;
        Image badgeCircle;
        GameObject hintPanel;
        float bigUntil, bigDur, toastUntil;
        class Pop { public TextMeshProUGUI t; public Vector3 world; public float age, life, scale; public bool on; }
        readonly List<Pop> pops = new List<Pop>();
        // net hud
        GameObject netHudPanel;
        TextMeshProUGUI netHudText;
        readonly Image[] qualityBars = new Image[4];
        float netHudTimer;
        // results / lab
        TextMeshProUGUI resultsText, labText, labPhase;
        TextMeshProUGUI resultsHead, resultsTotal, resultsRecord;
        readonly Image[] stars = new Image[3];
        float resultsShownAt;
        int resultsTarget, starsEarned, starsShown;
        RectTransform labFill;
        readonly StringBuilder sb = new StringBuilder(512);

        void BuildUi()
        {
            UIKit.EnsureEventSystem();
            // persistent background camera so menus never show "No cameras rendering"; the world camera draws over it
            var uiCam = new GameObject("UICamera").AddComponent<Camera>();
            DontDestroyOnLoad(uiCam.gameObject);
            uiCam.clearFlags = CameraClearFlags.SolidColor;
            uiCam.backgroundColor = new Color(0.07f, 0.22f, 0.27f);
            uiCam.cullingMask = 0;
            uiCam.depth = -100;
            mainCanvas = UIKit.Canvas("UI_Main", 10);
            hudCanvas = UIKit.Canvas("UI_Hud", 5);
            hudCanvas.GetComponent<GraphicRaycaster>().enabled = false;
            netCanvas = UIKit.Canvas("UI_NetHud", 30);
            netCanvas.GetComponent<GraphicRaycaster>().enabled = false;
            BuildLogin(); BuildDownload(); BuildMenu(); BuildLobby(); BuildResults(); BuildLab(); BuildHud(); BuildNetHud(); BuildV4();
        }

        GameObject Panel(UiScreen s, Color bg)
        {
            var rt = UIKit.Rect(s.ToString(), mainCanvas.transform);
            UIKit.Stretch(rt);
            if (bg.a > 0) { var img = rt.gameObject.AddComponent<Image>(); img.color = bg; }
            panels[s] = rt.gameObject;
            return rt.gameObject;
        }

        RectTransform Column(Transform parent, float w, float h, float y = 0)
        {
            var rt = UIKit.Rect("Column", parent);
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(w, h));
            UIKit.VLayout(rt, 18, 10, TextAnchor.MiddleCenter);
            return rt;
        }

        void BuildLogin()
        {
            var p = Panel(UiScreen.Login, new Color(0.04f, 0.16f, 0.2f, 0.45f));
            var col = Column(p.transform, 1000, 1000);
            var title = UIKit.Size(UIKit.Text(col, "MOTTAINAI", 130, new Color(0.55f, 0.95f, 0.6f)), 150);
            title.fontStyle = FontStyles.Bold;
            title.textWrappingMode = TextWrappingModes.NoWrap;
            title.overflowMode = TextOverflowModes.Overflow;
            title.characterSpacing = 12;
            title.raycastTarget = true;
            var tapper = title.gameObject.AddComponent<Button>();
            tapper.transition = Selectable.Transition.None;
            tapper.onClick.AddListener(TitleTap);
            UIKit.Size(UIKit.Text(col, "\"What a waste!\"  -  paddle together, clean the river", 38, UIKit.TextLight), 60);
            nameInput = UIKit.Size(UIKit.Input(col, "Your name", playerName), 92);
            UIKit.Size(UIKit.Button(col, "PLAY", UIKit.Accent, StartLogin, 56), 120);
            var row = UIKit.Rect("Row", col);
            UIKit.HLayout(row, 16, 0);
            UIKit.Size(row, 90);
            UIKit.Button(row, "NETWORK LAB", UIKit.Accent2, () => Show(UiScreen.Lab), 34);
            forceToggle = UIKit.Button(row, "", UIKit.Muted, () => { forceDownload = !forceDownload; RefreshToggles(); }, 30);
            // hidden debug panel (5 taps on the title): server URL override
            var dbg = UIKit.Rect("Debug", col);
            UIKit.HLayout(dbg, 16, 0);
            UIKit.Size(dbg, 90);
            serverInput = UIKit.Input(dbg, "Server URL (empty = default)", PlayerPrefs.GetString("serverOverride", ""), 30);
            UIKit.Button(dbg, "APPLY", UIKit.Warn, () => { content.SetBaseUrl(serverInput.text); SetStatus("Server: " + content.BaseUrl); }, 30);
            debugPanel = dbg.gameObject;
            debugPanel.SetActive(false);
            loginStatus = UIKit.Size(UIKit.Text(col, "Landscape, 3 players per raft. Bots fill empty seats.", 32, new Color(1f, 0.9f, 0.6f)), 90);
            RefreshToggles();
        }

        void TitleTap()
        {
            titleTaps = Time.unscaledTime - titleTapAt < 0.8f ? titleTaps + 1 : 1;
            titleTapAt = Time.unscaledTime;
            if (titleTaps < 5) return;
            titleTaps = 0;
            debugPanel.SetActive(!debugPanel.activeSelf);
            SetStatus("Server: " + content.BaseUrl);
        }

        void RefreshToggles()
        {
            UIKit.Label(forceToggle).text = forceDownload ? "FORCE RE-DOWNLOAD: ON" : "FORCE RE-DOWNLOAD: OFF";
            forceToggle.image.color = forceDownload ? UIKit.Warn : UIKit.Muted;
        }

        void BuildMenu()
        {
            var p = Panel(UiScreen.Menu, new Color(0.04f, 0.16f, 0.2f, 0.5f));
            menuHeader = UIKit.Text(p.transform, "", 34, UIKit.TextLight, TextAlignmentOptions.Left);
            UIKit.Place(menuHeader.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -20), new Vector2(1600, 60));
            var col = Column(p.transform, 1000, 860, -30);
            UIKit.Size(UIKit.Text(col, "Team up on one raft!", 64, new Color(0.55f, 0.95f, 0.6f)), 90);
            UIKit.Size(UIKit.Button(col, "QUICK PLAY", UIKit.Accent, () => JoinRoom("DEMO"), 56), 120);
            UIKit.Size(UIKit.Button(col, "CREATE ROOM", UIKit.Accent2, CreateRoom, 44), 100);
            var row = UIKit.Rect("JoinRow", col);
            UIKit.HLayout(row, 16, 0);
            UIKit.Size(row, 100);
            codeInput = UIKit.Input(row, "CODE", "", 48);
            codeInput.characterLimit = 4;
            codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            codeInput.onValueChanged.AddListener(v => { var up = v.ToUpperInvariant(); if (up != v) codeInput.text = up; });
            UIKit.Button(row, "JOIN CODE", UIKit.Accent2, () => JoinRoom(codeInput.text), 40);
            var row2 = UIKit.Rect("Row2", col);
            UIKit.HLayout(row2, 16, 0);
            UIKit.Size(row2, 90);
            UIKit.Button(row2, "NETWORK LAB", UIKit.Muted, () => Show(UiScreen.Lab), 34);
            UIKit.Button(row2, "RE-DOWNLOAD PACK", UIKit.Muted, () => StartCoroutine(Redownload()), 30);
            menuStatus = UIKit.Size(UIKit.Text(col, "", 32, new Color(1f, 0.9f, 0.6f)), 70);
        }

        void RefreshMenuUi()
        {
            var ni = NetworkInfo.Provider;
            menuHeader.text = $"Hi <b>{playerName}</b>  |  <color=#7CFC9A>{ni.NetworkType}</color>  slice: {ni.SliceStatus}  |  edge <b>{content.EdgeColo}</b>";
        }

        void BuildDownload()
        {
            var p = Panel(UiScreen.Download, new Color(0.04f, 0.14f, 0.2f, 0.6f));
            var col = Column(p.transform, 1200, 600);
            downloadTitle = UIKit.Size(UIKit.Text(col, "Downloading Content Pack", 60, UIKit.TextLight), 100);
            var bar = UIKit.Size(UIKit.Image(col, new Color(1, 1, 1, 0.15f)), 50);
            downloadFill = UIKit.Image(bar.transform, UIKit.Accent).rectTransform;
            downloadFill.anchorMin = Vector2.zero; downloadFill.anchorMax = new Vector2(0, 1); downloadFill.offsetMin = downloadFill.offsetMax = Vector2.zero;
            downloadInfo = UIKit.Size(UIKit.Text(col, "", 46, new Color(0.7f, 1f, 0.8f)), 80);
            downloadFile = UIKit.Size(UIKit.Text(col, "", 30, new Color(0.8f, 0.85f, 0.9f)), 60);
            UIKit.Size(UIKit.Text(col, "Tuning + high-res water normal maps + raft skin from the Cloudflare edge, cached by sha256", 28, UIKit.Muted * 2f), 50);
        }

        void BuildLobby()
        {
            var p = Panel(UiScreen.Lobby, new Color(0.04f, 0.16f, 0.2f, 0.55f));
            lobbyHeader = UIKit.Text(p.transform, "", 34, UIKit.TextLight, TextAlignmentOptions.Left);
            UIKit.Place(lobbyHeader.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -20), new Vector2(1500, 60));

            var rp = Column(p.transform, 1500, 800, -50);
            lobbyRoomTitle = UIKit.Size(UIKit.Text(rp, "", 80, new Color(1f, 0.9f, 0.5f)), 110);
            lobbyRoomTitle.fontStyle = FontStyles.Bold;
            var roles = UIKit.Rect("Roles", rp);
            UIKit.HLayout(roles, 20, 0);
            UIKit.Size(roles, 190);
            for (int i = 0; i < 3; i++)
            {
                var r = (Role)(i + 1);
                roleButtons[i] = UIKit.Button(roles, "", UIKit.Panel, () => { preferredRole = r; PlayerPrefs.SetInt("role", (int)r); net.PickRole(r); }, 34);
            }
            lobbyPlayers = UIKit.Size(UIKit.Text(rp, "", 34, UIKit.TextLight), 120);
            var actions = UIKit.Rect("Actions", rp);
            UIKit.HLayout(actions, 20, 0);
            UIKit.Size(actions, 120);
            readyButton = UIKit.Button(actions, "READY", UIKit.Muted, ToggleReady, 46);
            startButton = UIKit.Button(actions, "START", UIKit.Accent, () => net.StartMatch(), 52);
            UIKit.Button(actions, "LEAVE", UIKit.Warn, LeaveRoom, 40);

            lobbyStatus = UIKit.Text(p.transform, "", 32, new Color(1f, 0.9f, 0.6f));
            UIKit.Place(lobbyStatus.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1600, 60));
        }

        void ToggleReady()
        {
            var me = lobby?.players.Find(x => x.clientId == net.ClientId);
            net.SetReady(me == null || !me.ready);
        }

        System.Collections.IEnumerator Redownload()
        {
            var back = screen;
            Show(UiScreen.Download);
            downloadTitle.text = "Re-downloading Content Pack";
            string err = null;
            yield return content.DownloadPack(true, e => err = e);
            downloadTitle.text = err ?? content.Status;
            yield return new WaitForSecondsRealtime(1.5f);
            Show(back);
            RefreshMenuUi();
        }

        void RefreshLobbyUi()
        {
            var ni = NetworkInfo.Provider;
            lobbyHeader.text = $"Hi <b>{playerName}</b>  |  <color=#7CFC9A>{ni.NetworkType}</color>  slice: {ni.SliceStatus}  |  edge <b>{content.EdgeColo}</b>  room <b>{net.RoomColo}</b>";
            lobbyRoomTitle.text = net.roomCode == "DEMO" ? "QUICK PLAY  (room DEMO)" : $"ROOM  {net.roomCode}";
            if (lobby == null) { lobbyPlayers.text = "Waiting for room state..."; return; }
            for (int i = 0; i < 3; i++)
            {
                var r = (Role)(i + 1);
                var holder = lobby.IsBot(r) ? null : lobby.players.Find(x => x.role == r && x.connected);
                bool mine = holder != null && holder.clientId == net.ClientId;
                string who = holder == null ? "<color=#9AA>BOT</color>" : holder.name + (holder.ready ? "  <color=#7CFC9A>READY</color>" : "");
                UIKit.Label(roleButtons[i]).text = $"{RoleInfo.Name(r)}\n<size=28>{who}</size>";
                roleButtons[i].image.color = mine ? UIKit.Accent : holder == null ? UIKit.Panel : UIKit.Muted;
            }
            var me = lobby.players.Find(x => x.clientId == net.ClientId);
            readyButton.image.color = me != null && me.ready ? UIKit.Accent : UIKit.Muted;
            UIKit.Label(readyButton).text = me != null && me.ready ? "READY!" : "READY?";
            sb.Clear();
            sb.Append("Crew: ");
            foreach (var pl in lobby.players) if (pl.connected) sb.Append(pl.name).Append("   ");
            sb.Append("\n<size=26>Tap a role to take it. Empty roles are played by bots. Anyone can press START.</size>");
            lobbyPlayers.text = sb.ToString();
            UIKit.Label(startButton).text = lobby.phase == MatchPhase.Running || lobby.phase == MatchPhase.Countdown ? "IN MATCH..." : "START";
        }

        void BuildResults()
        {
            var p = Panel(UiScreen.Results, new Color(0.03f, 0.12f, 0.16f, 0.82f));
            var card = UIKit.Image(p.transform, UIKit.Panel);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1700, 860));
            resultsHead = UIKit.Text(card.transform, "", 50, Color.white, TextAlignmentOptions.TopLeft);
            resultsHead.fontStyle = FontStyles.Bold;
            UIKit.Place(resultsHead.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -30), new Vector2(1100, 70));
            resultsTotal = UIKit.Text(card.transform, "", 100, Color.white, TextAlignmentOptions.TopLeft);
            resultsTotal.fontStyle = FontStyles.Bold;
            resultsTotal.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(resultsTotal.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(50, -95), new Vector2(1100, 130));
            for (int i = 0; i < 3; i++)
            {
                stars[i] = UIKit.Image(card.transform, new Color(1f, 1f, 1f, 0.15f), UIKit.Star, "Star" + i);
                stars[i].raycastTarget = false;
                UIKit.Place(stars[i].rectTransform, new Vector2(1, 1), new Vector2(0.5f, 0.5f), new Vector2(-370 + i * 135, -95 - (i == 1 ? 18 : 0)), new Vector2(i == 1 ? 150 : 125, i == 1 ? 150 : 125));
            }
            resultsRecord = UIKit.Text(card.transform, "", 34, new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.Top);
            resultsRecord.fontStyle = FontStyles.Bold;
            UIKit.Place(resultsRecord.rectTransform, new Vector2(1, 1), new Vector2(0.5f, 1), new Vector2(-235, -185), new Vector2(560, 50));
            resultsText = UIKit.Text(card.transform, "", 34, UIKit.TextLight, TextAlignmentOptions.TopLeft);
            UIKit.Stretch(resultsText.rectTransform, 50, 250, 50, 30);
            resultsText.richText = true;
            var row = UIKit.Rect("Buttons", p.transform);
            UIKit.Place(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1500, 110));
            UIKit.HLayout(row, 24, 0);
            UIKit.Button(row, "REMATCH", UIKit.Accent, () => net.StartMatch(), 44);
            UIKit.Button(row, "LOBBY", UIKit.Accent2, () => { Show(UiScreen.Lobby); RefreshLobbyUi(); }, 44);
            UIKit.Button(row, "NETWORK LAB", UIKit.Muted, () => Show(UiScreen.Lab), 40);
        }

        void FillResults()
        {
            var r = results;
            var s = net.stats;
            var ni = NetworkInfo.Provider;
            resultsHead.text = r.reason == EndReason.Crashed ? "<color=#FF7A5C>GAME OVER - you hit an obstacle!</color>" : r.reason == EndReason.TimeUp ? "<color=#7CFC9A>TIME UP - great paddling!</color>" : "Match ended";
            resultsTarget = r.total;
            resultsShownAt = Time.unscaledTime;
            starsShown = 0;
            var thresholds = GameTuning.Current.starScores;
            starsEarned = 0;
            for (int i = 0; i < 3 && thresholds != null && i < thresholds.Length; i++) if (r.total >= thresholds[i]) starsEarned = i + 1;
            for (int i = 0; i < 3; i++) { stars[i].color = new Color(1f, 1f, 1f, 0.15f); stars[i].rectTransform.localScale = Vector3.one; }
            int best = PlayerPrefs.GetInt("best_total", 0);
            if (r.total > best && r.total > 0)
            {
                PlayerPrefs.SetInt("best_total", r.total);
                PlayerPrefs.Save();
                resultsRecord.text = best > 0 ? $"NEW PERSONAL BEST! (was {best})" : "FIRST RECORD SET!";
            }
            else resultsRecord.text = $"Personal best: {best}";
            resultsTotal.text = "TOTAL 0";

            sb.Clear();
            sb.Append($"<size=34>catches <b>{r.catchScore}</b>   <color=#FF6B6B>toxic penalty -{r.toxicPenalty}</color></size>\n\n");
            for (int i = 0; i < NetConst.WasteTypeCount; i++)
            {
                sb.Append($"<color={UIKit.Hex(WasteCatalog.Colors[i])}>• {WasteCatalog.Names[i]}: {r.counts[i]}</color>   ");
                if (i == 3) sb.Append('\n');
            }
            sb.Append($"\n\nAccuracy <b>{r.Accuracy * 100f:F0}%</b> ({r.hits}/{r.throws})    Distance <b>{r.distance:F0} m</b>    Time <b>{r.duration:F0} s</b>\n");
            sb.Append($"<color=#7CFC9A><b>Ocean cleaned: {r.kg:F1} kg</b></color>\n\n");
            float avgRtt = s.nRtt > 0 ? (float)(s.sumRtt / s.nRtt) : 0;
            sb.Append($"<size=30><b>NETWORK</b>  {ni.NetworkType} (slice {ni.SliceStatus})  edge {content.EdgeColo} / room {net.RoomColo}   RTT avg {avgRtt:F0} ms  p95 max {s.maxP95:F0} ms  jitter {s.Jitter:F1} ms   " +
                      $"downlink avg {(s.nDown > 0 ? s.sumDown / s.nDown : 0):F0} kbps   received {s.TotalDownMB:F1} MB</size>");
            resultsText.text = sb.ToString();
        }

        void UpdateResults()
        {
            float t = Time.unscaledTime - resultsShownAt;
            float k = Mathf.Clamp01(t / 1.3f);
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            resultsTotal.text = $"TOTAL {Mathf.RoundToInt(resultsTarget * eased)}";
            for (int i = 0; i < 3; i++)
            {
                float appear = 1.4f + i * 0.4f;
                var rt = stars[i].rectTransform;
                if (i < starsEarned && t >= appear)
                {
                    float pt = t - appear;
                    rt.localScale = Vector3.one * (pt < 0.25f ? Mathf.Lerp(1.8f, 1f, pt / 0.25f) : 1f + Mathf.Sin(Time.unscaledTime * 3f + i) * 0.03f);
                    stars[i].color = new Color(1f, 0.82f, 0.22f, 1f);
                    if (starsShown <= i) { starsShown = i + 1; sfx.Play(i == 2 ? Sfx.Id.Valuable : Sfx.Id.Combo, 0.8f); }
                }
            }
        }

        void BuildLab()
        {
            var p = Panel(UiScreen.Lab, new Color(0.03f, 0.1f, 0.16f, 0.75f));
            var col = Column(p.transform, 1500, 900);
            UIKit.Size(UIKit.Text(col, "NETWORK LAB", 72, new Color(0.5f, 0.85f, 1f)), 100);
            labPhase = UIKit.Size(UIKit.Text(col, "Edge RTT (50 HTTPS) + room RTT (100 WebSocket pings) + pack download + 5 MB upload", 32, UIKit.TextLight), 60);
            var bar = UIKit.Size(UIKit.Image(col, new Color(1, 1, 1, 0.15f)), 40);
            labFill = UIKit.Image(bar.transform, UIKit.Accent2).rectTransform;
            labFill.anchorMin = Vector2.zero; labFill.anchorMax = new Vector2(0, 1); labFill.offsetMin = labFill.offsetMax = Vector2.zero;
            labText = UIKit.Size(UIKit.Text(col, "", 36, UIKit.TextLight), 400);
            var row = UIKit.Rect("Row", col);
            UIKit.HLayout(row, 20, 0);
            UIKit.Size(row, 110);
            UIKit.Button(row, "RUN TEST", UIKit.Accent, RunLab, 46);
            UIKit.Button(row, "BACK", UIKit.Muted, () => { if (net.Ready) { Show(UiScreen.Lobby); RefreshLobbyUi(); } else if (content.Login != null) { Show(UiScreen.Menu); RefreshMenuUi(); } else Show(UiScreen.Login); }, 46);
        }

        void RunLab()
        {
            if (lab.Running) return;
            var ni = NetworkInfo.Provider;
            StartCoroutine(lab.Run(content, ni.NetworkType, "slice " + ni.SliceStatus));
        }

        RectTransform hudSafe;

        void BuildHud()
        {
            hudSafe = UIKit.Stretch(UIKit.Rect("Safe", hudCanvas.transform));
            var root = (Transform)hudSafe;
            // v3 panels: score (top-left) and timer (top-right), ui.panel with a 2 px inner stroke at 15 % white
            var scoreBg = UIKit.Image(root, UIKit.Panel, null, "ScorePanel");
            scoreBg.raycastTarget = false;
            UIKit.Place(scoreBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(22, -14), new Vector2(520, 268));
            var so = scoreBg.gameObject.AddComponent<Outline>(); so.effectColor = new Color(1, 1, 1, 0.15f); so.effectDistance = new Vector2(2, -2);
            var timerBg = UIKit.Image(root, UIKit.Panel, null, "TimerPanel");
            timerBg.raycastTarget = false;
            UIKit.Place(timerBg.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-22, -14), new Vector2(300, 96));
            var to = timerBg.gameObject.AddComponent<Outline>(); to.effectColor = new Color(1, 1, 1, 0.15f); to.effectDistance = new Vector2(2, -2);
            scoreText = UIKit.Text(root, "0", 84, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(scoreText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -16), new Vector2(600, 100));
            scoreText.outlineWidth = 0.2f;
            scoreText.fontStyle = FontStyles.Bold;
            comboText = UIKit.Text(root, "", 34, new Color(1f, 0.85f, 0.3f), TextAlignmentOptions.TopLeft);
            UIKit.Place(comboText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -110), new Vector2(600, 50));
            var track = UIKit.Image(root, new Color(1f, 1f, 1f, 0.18f));
            track.raycastTarget = false;
            UIKit.Place(track.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -168), new Vector2(460, 22));
            progressFill = UIKit.Image(track.transform, UIKit.Accent).rectTransform;
            progressFill.GetComponent<Image>().raycastTarget = false;
            progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0, 1); progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
            distanceText = UIKit.Text(root, "", 28, Color.white, TextAlignmentOptions.TopLeft);
            UIKit.Place(distanceText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -196), new Vector2(600, 40));

            badgeCircle = UIKit.Image(root, Color.white, UIKit.Circle, "Badge");
            badgeCircle.raycastTarget = false;
            UIKit.Place(badgeCircle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -238), new Vector2(34, 34));
            badgeLetter = UIKit.Text(badgeCircle.transform, "?", 22, Color.white);
            badgeLetter.fontStyle = FontStyles.Bold;
            UIKit.Stretch(badgeLetter.rectTransform);
            badgeName = UIKit.Text(root, "", 24, Color.white, TextAlignmentOptions.Left);
            badgeName.fontStyle = FontStyles.Bold;
            UIKit.Place(badgeName.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(84, -238), new Vector2(500, 34));

            timerText = UIKit.Text(root, "2:00", 56, Color.white, TextAlignmentOptions.TopRight);
            timerText.fontStyle = FontStyles.Bold;
            UIKit.Place(timerText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-48, -26), new Vector2(260, 80));
            shieldText = UIKit.Text(root, "", 28, new Color(0.5f, 0.91f, 1f), TextAlignmentOptions.TopRight);
            shieldText.fontStyle = FontStyles.Bold;
            UIKit.Place(shieldText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-40, -112), new Vector2(400, 40));

            bigText = UIKit.Text(root, "", 160, Color.white);
            bigText.fontStyle = FontStyles.Bold;
            bigText.outlineWidth = 0.2f;
            bigText.outlineColor = new Color32(0, 30, 40, 255);
            UIKit.Place(bigText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 120), new Vector2(1400, 220));

            toastText = UIKit.Text(root, "", 32, new Color(1f, 0.95f, 0.7f));
            UIKit.Place(toastText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -200), new Vector2(1200, 50));

            var hp = UIKit.Image(root, new Color(0, 0, 0, 0.45f));
            hp.raycastTarget = false;
            UIKit.Place(hp.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 170), new Vector2(1500, 90));
            hintPanel = hp.gameObject;
            hintText = UIKit.Text(hp.transform, "", 40, Color.white);
            UIKit.Stretch(hintText.rectTransform, 20, 5, 20, 5);

            for (int i = 0; i < 20; i++)
            {
                var t = UIKit.Text(root, "", 52, Color.white, TextAlignmentOptions.Center, "Popup");
                t.fontStyle = FontStyles.Bold;
                t.outlineWidth = 0.25f;
                t.outlineColor = new Color32(0, 0, 0, 200);
                t.rectTransform.sizeDelta = new Vector2(500, 80);
                t.gameObject.SetActive(false);
                pops.Add(new Pop { t = t });
            }
            hudCanvas.gameObject.SetActive(false);
        }

        GameObject netPill;
        TextMeshProUGUI netPillText;

        void BuildNetHud()
        {
            var pill = UIKit.Image(netCanvas.transform, new Color32(0x1E, 0x2A, 0x34, 204), null, "NetPill");
            UIKit.Place(pill.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(560, 64));
            pill.gameObject.AddComponent<Button>().onClick.AddListener(() => netHudOn = !netHudOn);
            netPillText = UIKit.Text(pill.transform, "", 26, Color.white);
            netPillText.richText = true; netPillText.raycastTarget = false;
            netPillText.fontStyle = FontStyles.Bold;
            UIKit.Stretch(netPillText.rectTransform, 12, 2, 12, 2);
            netPill = pill.gameObject;

            var bg = UIKit.Image(netCanvas.transform, new Color(0, 0, 0, 0.6f), null, "NetHud");
            bg.raycastTarget = false;
            UIKit.Place(bg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -92), new Vector2(560, 372));
            for (int i = 0; i < 4; i++)
            {
                qualityBars[i] = UIKit.Image(bg.transform, new Color(1f, 1f, 1f, 0.2f), null, "Q" + i);
                qualityBars[i].raycastTarget = false;
                UIKit.Place(qualityBars[i].rectTransform, new Vector2(1, 1), new Vector2(0, 1), new Vector2(-78 + i * 15, -14 - (24 - 6 * i)), new Vector2(11, 12 + 6 * i));
            }
            netHudText = UIKit.Text(bg.transform, "", 21, new Color(0.75f, 1f, 0.8f), TextAlignmentOptions.TopLeft);
            netHudText.richText = true;
            UIKit.Stretch(netHudText.rectTransform, 16, 10, 10, 10);
            netHudPanel = bg.gameObject;
        }

        void SetRoleBadge(Role r)
        {
            Color c = r switch { Role.LeftPaddler => new Color(0.2f, 0.6f, 1f), Role.RightPaddler => new Color(0.95f, 0.3f, 0.45f), Role.Collector => new Color(0.3f, 0.8f, 0.35f), _ => Color.grey };
            badgeCircle.color = c;
            badgeLetter.text = RoleInfo.Letter(r);
            badgeName.text = RoleInfo.Name(r);
            hintText.text = RoleInfo.Hint(r) + (Application.isMobilePlatform ? "" : "   [keys: Q/E or W/S paddle, Space throw, H net HUD]");
        }

        void Show(UiScreen s)
        {
            screen = s;
            foreach (var kv in panels) kv.Value.SetActive(kv.Key == s);
            mainCanvas.gameObject.SetActive(s != UiScreen.Game);
            hudCanvas.gameObject.SetActive(s == UiScreen.Game);
            if (world != null && s != UiScreen.Game && s != UiScreen.Results) { world.Attract = true; world.AimAssist = false; }
            if (s != UiScreen.Game) input.active = false;
            if (musicPlayer != null) musicPlayer.SetMode(s == UiScreen.Game ? MusicPlayer.Mode.Game : s == UiScreen.Results ? MusicPlayer.Mode.Results : MusicPlayer.Mode.Menu);
        }

        void SetStatus(string text)
        {
            if (loginStatus != null) loginStatus.text = text;
            if (lobbyStatus != null) lobbyStatus.text = text;
            if (menuStatus != null) menuStatus.text = text;
        }

        void Toast(string text)
        {
            toastText.text = text;
            toastUntil = Time.time + 2.5f;
            if (lobbyStatus != null && screen == UiScreen.Lobby) lobbyStatus.text = text;
        }

        void Big(string text, Color c, float dur)
        {
            bigText.text = text;
            bigText.color = c;
            bigDur = dur;
            bigUntil = Time.time + dur;
        }

        void SyncChip(Vector3 world, int streak)
        {
            foreach (var p in pops)
            {
                if (p.on) continue;
                p.on = true; p.world = world; p.age = 0; p.life = 0.6f; p.scale = 0.62f;
                p.t.text = $"<mark=#1E2A34CC padding=14,14,4,4>x{streak}</mark>"; p.t.color = Color.white;
                p.t.gameObject.SetActive(true);
                return;
            }
        }

        void Popup(Vector3 world, string text, Color c, float scale)
        {
            foreach (var p in pops)
            {
                if (p.on) continue;
                p.on = true; p.world = world + new Vector3(Random.Range(-0.6f, 0.6f), Random.Range(0f, 0.5f), 0f); p.age = 0; p.life = 1.1f; p.scale = scale;
                p.t.text = text; p.t.color = c;
                p.t.gameObject.SetActive(true);
                return;
            }
        }

        void UpdateUi(float dt)
        {
            if (screen == UiScreen.Download)
            {
                float k = content.BytesTotal > 0 ? Mathf.Clamp01((float)content.BytesDone / content.BytesTotal) : 0f;
                downloadFill.anchorMax = new Vector2(k, 1);
                downloadInfo.text = $"{content.BytesDone / 1048576f:F1} / {content.BytesTotal / 1048576f:F1} MB    {content.Mbps:F1} MB/s ({content.Mbps * 8.39f:F0} Mbit/s)";
                downloadFile.text = content.Status;
            }
            else if (screen == UiScreen.Lab)
            {
                labFill.anchorMax = new Vector2(lab.Progress, 1);
                if (lab.Running) labPhase.text = lab.Phase;
                if (lab.Result != null)
                {
                    var r = lab.Result;
                    labText.text = $"<b>{r.netType}</b>  ({r.device})   edge <b>{r.edgeColo}</b>   room <b>{r.roomColo}</b>\n" +
                                   $"Edge RTT avg <b>{r.edgeRttAvg:F1} ms</b>   p95 <b>{r.edgeRttP95:F1}</b>   jitter {r.edgeJitter:F1}  (n={r.edgeN})\n" +
                                   $"Room RTT avg <b>{r.roomRttAvg:F1} ms</b>   p95 <b>{r.roomRttP95:F1}</b>   jitter {r.roomJitter:F1}  (n={r.roomN})\n" +
                                   $"Download <b>{r.downMbps:F1} Mbit/s</b> ({r.downMB:F1} MB)   Upload <b>{r.upMbps:F1} Mbit/s</b> ({r.upMB:F1} MB)\n" +
                                   $"<size=26>Saved: {lab.CsvPath} + {content.BaseUrl}/api/telemetry.csv</size>";
                    if (!lab.Running) labPhase.text = "Done - run again on another network (Wi-Fi / 4G / 5G NSA / 5G SA) to compare";
                }
                else if (!lab.Running) labText.text = $"Network: <b>{NetworkInfo.Provider.NetworkType}</b>   Latency slice: {NetworkInfo.Provider.SliceStatus}\n<size=26>{NetworkInfo.Provider.Detail}</size>";
            }
            else if (screen == UiScreen.Game)
            {
                scoreText.text = $"<size=34>TEAM</size>  {hudScore}";
                comboText.text = hudCombo ? "COMBO READY x1.5" : "";
                if (world != null)
                {
                    float dist = Mathf.Max(0f, world.RaftPos.z);
                    progressFill.anchorMax = new Vector2(Mathf.Clamp01(dist / GameTuning.Current.courseLength), 1);
                    distanceText.text = $"{dist:F0} m  /  {GameTuning.Current.courseLength:F0} m";
                }
                if (hudPhase == MatchPhase.Countdown) timerText.text = $"{Mathf.CeilToInt(hudTimeLeft)}";
                else { int s = Mathf.CeilToInt(hudTimeLeft); timerText.text = $"{s / 60}:{s % 60:00}"; }
                bool lowT = hudPhase == MatchPhase.Running && hudTimeLeft < 15f;
                timerText.color = lowT ? new Color32(0xFF, 0x7A, 0x1A, 255) : Color.white;
                timerText.rectTransform.localScale = Vector3.one * (lowT ? 1f + 0.06f * Mathf.Abs(Mathf.Sin(Time.time * 6f)) : 1f);
                string pw = hudShield > 0.05f ? $"<color=#7FE9FF>SHIELD {hudShield:F0}</color>  " : "";
                float ml = magnetUntil - Time.time, tl = turboUntil - Time.time;
                if (ml > 0f) pw += $"<color=#00C2A8>MAGNET {ml:F0}</color>  ";
                if (tl > 0f) pw += $"<color=#FF7A1A>TURBO {tl:F0}</color>";
                shieldText.text = pw;
                hintPanel.SetActive(Time.time < hintUntil);
            }

            if (screen == UiScreen.Results && results != null) UpdateResults();
            if (screen == UiScreen.Game || screen == UiScreen.Results)
            {
                float left = bigUntil - Time.time;
                bigText.gameObject.SetActive(left > 0f);
                if (left > 0f)
                {
                    float k = 1f - left / Mathf.Max(0.01f, bigDur);
                    bigText.rectTransform.localScale = Vector3.one * (1f + 0.25f * Mathf.Max(0f, 0.2f - k) * 5f);
                    var c = bigText.color; c.a = Mathf.Clamp01(left * 3f); bigText.color = c;
                }
                toastText.gameObject.SetActive(Time.time < toastUntil);
                var cam = world != null ? world.cam : null;
                foreach (var p in pops)
                {
                    if (!p.on) continue;
                    p.age += dt;
                    if (p.age >= p.life || cam == null) { p.on = false; p.t.gameObject.SetActive(false); continue; }
                    var sp = cam.WorldToScreenPoint(p.world + Vector3.up * (0.8f + p.age * 1.5f));
                    if (sp.z < 0) { p.t.gameObject.SetActive(false); continue; }
                    p.t.gameObject.SetActive(true);
                    p.t.rectTransform.position = sp;
                    p.t.rectTransform.localScale = Vector3.one * p.scale * (p.age < 0.12f ? Mathf.Lerp(0.5f, 1.2f, p.age / 0.12f) : 1f);
                    var c = p.t.color; c.a = Mathf.Clamp01((p.life - p.age) * 3f); p.t.color = c;
                }
            }

            netPill.SetActive(screen == UiScreen.Game);
            netHudPanel.SetActive(netHudOn && screen == UiScreen.Game);
            netHudTimer -= dt;
            bool netTick = netHudTimer <= 0f;
            if (netTick) netHudTimer = 0.25f;
            if (netTick && netPill.activeSelf)
            {
                var s = net.stats;
                var ni = NetworkInfo.Provider;
                string rttC = s.RttAvg < 30f ? "#4CC94C" : s.RttAvg < 60f ? "#FFD23F" : "#FF7A1A";
                netPillText.text = $"<color=#00C2A8>{ni.NetworkType}</color>   RTT <color={rttC}>{s.RttAvg:F0} ms</color>   <size=22>{content.EdgeColo} → {net.RoomColo}</size>";
            }
            if (netTick && netHudPanel.activeSelf)
            {
                var s = net.stats;
                var ni = NetworkInfo.Provider;
                string badgeColor = ni.NetworkType == "5G SA" ? "#39FF88" : ni.NetworkType == "5G NSA" ? "#B6FF5C" : ni.NetworkType == "LTE" ? "#FFD24C" : "#9AD0FF";
                sb.Clear();
                sb.Append($"<size=30><b><color={badgeColor}>{ni.NetworkType}</color></b></size>   slice: <b>{ni.SliceStatus}</b>\n");
                sb.Append($"Edge <b>{content.EdgeColo}</b>   Room <b>{net.RoomColo}</b>   (Cloudflare, wss)\n");
                sb.Append($"RTT avg <b>{s.RttAvg:F1}</b>  p95 <b>{s.RttP95:F1}</b>  jitter {s.Jitter:F1} ms\n");
                sb.Append($"Snapshots <b>{s.SnapshotRate:F0}</b>/s (server {net.SnapshotHz} Hz)  tick #{s.ServerTick}\n");
                sb.Append(world != null && world.PredictionLeadMs > 0 ? $"Raft prediction ON  lead {world.PredictionLeadMs:F0} ms\n" : "Raft prediction idle\n");
                sb.Append($"Down <b>{s.DownKbps:F0}</b> kbps   Up {s.UpKbps:F0} kbps   msgs {s.Pps:F0}/s\n");
                sb.Append($"Jitter buffer <b>{(world != null ? world.InterpMs : 0):F0} ms</b>  (snapshot jitter p95 {(world != null ? world.JitterMs : 0):F0} ms, late {(world != null ? world.LatePct : 0):F0}%)  TCP_NODELAY {(s.NoDelay ? "on" : "off")}\n");
                float frameMs = 1000f / Mathf.Max(1f, s.Fps);
                float teamMs = s.RttAvg + frameMs;
                float worldMs = s.RttAvg * 0.5f + (world != null ? world.InterpMs : 0f) + frameMs;
                sb.Append($"FPS {s.Fps:F0} ({frameMs:F1} ms/frame)   rx {s.TotalDownMB:F1} MB\n");
                sb.Append($"<color=#FFE38A>Latency budget</color>  my paddle>screen <b>{frameMs:F0} ms</b> (predicted)\n");
                sb.Append($"teammate stroke>screen <b>{teamMs:F0} ms</b> = RTT + frame   world <b>{worldMs:F0} ms</b>\n");
                int q = 4;
                if (s.RttP95 > 30f) q--;
                if (s.RttP95 > 60f) q--;
                if (s.Jitter > 8f) q--;
                q = Mathf.Clamp(q, 0, 4);
                var qc = q >= 4 ? new Color(0.25f, 1f, 0.5f) : q == 3 ? new Color(0.7f, 1f, 0.35f) : q == 2 ? new Color(1f, 0.85f, 0.3f) : q == 1 ? new Color(1f, 0.55f, 0.2f) : new Color(1f, 0.3f, 0.25f);
                for (int i = 0; i < 4; i++) qualityBars[i].color = i < q ? qc : new Color(1f, 1f, 1f, 0.2f);
                sb.Append("<size=20><color=#AAB>3-finger tap / H to hide</color></size>");
                netHudText.text = sb.ToString();
            }
        }
    }
}
