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
    /// Screens + HUD (ART_DIRECTION Phase 6): Title/main menu, content pack, private rooms, lobby, game HUD, results,
    /// network lab, settings overlay (never pauses the online match), compact Net HUD (jury showcase).
    public partial class ClientApp
    {
        Canvas mainCanvas, hudCanvas, netCanvas, overlayCanvas;
        readonly Dictionary<UiScreen, GameObject> panels = new Dictionary<UiScreen, GameObject>();
        static DesignTokens Tk => DesignTokens.Current;

        // title / menu
        TMP_InputField nameInput, serverInput;
        TextMeshProUGUI loginStatus, menuHeader, menuStatus, logoText;
        GameObject debugPanel;
        int titleTaps; float titleTapAt;
        bool loggedIn, quickAfterLogin;
        // download
        TextMeshProUGUI downloadTitle, downloadInfo, downloadFile;
        RectTransform downloadFill;
        // lobby
        TextMeshProUGUI lobbyHeader, lobbyRoomTitle, lobbyPlayers, lobbyStatus;
        TMP_InputField codeInput;
        readonly Button[] roleButtons = new Button[3];
        readonly TextMeshProUGUI[] roleWho = new TextMeshProUGUI[3];
        Button readyButton, startButton;
        // hud
        RectTransform progressFill, scorePanel;
        TextMeshProUGUI distanceText;
        TextMeshProUGUI scoreText, comboText, timerText, shieldText, bigText, hintText, toastText, badgeLetter, badgeName;
        Image badgeCircle, syncFill, syncGlow;
        TextMeshProUGUI syncLabel, syncSub;
        RectTransform hudMenuButton;
        readonly TextMeshProUGUI[] wasteCounts = new TextMeshProUGUI[NetConst.WasteTypeCount];
        readonly int[] catchCounts = new int[NetConst.WasteTypeCount];
        GameObject hintPanel;
        RectTransform hintHand;
        float bigUntil, bigDur, toastUntil, scoreBump, partnerStrokeAt = -99f;
        int scoreShown, onboardActions;
        class Pop { public TextMeshProUGUI t; public Vector3 world; public float age, life, scale; public bool on; }
        readonly List<Pop> pops = new List<Pop>();
        // net hud
        GameObject netHudPanel;
        TextMeshProUGUI netHudText, netHudBadge;
        Image netHudBadgeBg;
        readonly Image[] qualityBars = new Image[4];
        float netHudTimer;
        // settings
        GameObject settingsPanel;
        Button qHigh, qLow, hudOn, hudOff, forceBtn;
        // results / lab
        TextMeshProUGUI resultsText, labText, labPhase, resultsHead, resultsSub, resultsTotal, resultsRecord, resultsStats;
        readonly TextMeshProUGUI[] resultsCounts = new TextMeshProUGUI[NetConst.WasteTypeCount];
        RawImage resultsThumb;
        RenderTexture thumbRt;
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
            overlayCanvas = UIKit.Canvas("UI_Overlay", 40);
            BuildLogin(); BuildDownload(); BuildMenu(); BuildLobby(); BuildResults(); BuildLab(); BuildHud(); BuildNetHud(); BuildSettings();
            input.Blocked = p => settingsPanel.activeSelf || (hudMenuButton.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(hudMenuButton, p));
        }

        GameObject Panel(UiScreen s, Color bg)
        {
            var rt = UIKit.Rect(s.ToString(), mainCanvas.transform);
            UIKit.Stretch(rt);
            if (bg.a > 0) { var img = rt.gameObject.AddComponent<Image>(); img.color = bg; }
            panels[s] = rt.gameObject;
            return rt.gameObject;
        }

        /// Soft top-to-bottom teal shade keeping text readable over the live 3D river.
        static void Shade(Transform p, float top, float bottom)
        {
            var c = new Color(0.08f, 0.26f, 0.3f);
            var img = UIKit.Image(p, Color.white, UIKit.Gradient(new Color(c.r, c.g, c.b, top), new Color(c.r, c.g, c.b, bottom), 2), "Shade");
            img.type = Image.Type.Sliced;
            UIKit.Stretch(img.rectTransform);
        }

        RectTransform Column(Transform parent, float w, float h, float y = 0)
        {
            var rt = UIKit.Rect("Column", parent);
            UIKit.Place(rt, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, y), new Vector2(w, h));
            UIKit.VLayout(rt, 18, 10, TextAnchor.MiddleCenter);
            return rt;
        }

        RectTransform Row(Transform parent, float h, float spacing = 16)
        {
            var row = UIKit.Rect("Row", parent);
            UIKit.HLayout(row, spacing, 0);
            UIKit.Size(row, h);
            return row;
        }

        TextMeshProUGUI Chip(Transform parent, Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 size, float font)
        {
            var bg = UIKit.Card(parent, new Color(UIKit.DeepTeal.r, UIKit.DeepTeal.g, UIKit.DeepTeal.b, 0.78f), "Chip");
            UIKit.Place(bg.rectTransform, anchor, pivot, pos, size);
            var t = UIKit.Text(bg.transform, "", font, UIKit.TextLight, TextAlignmentOptions.Left);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Stretch(t.rectTransform, 22, 4, 16, 4);
            return t;
        }

        // ---------------- title / main menu ----------------
        void BuildLogin()
        {
            var p = Panel(UiScreen.Login, Color.clear);
            Shade(p.transform, 0.62f, 0.12f);
            var logo = UIKit.Rect("Logo", p.transform);
            UIKit.Place(logo, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -50), new Vector2(1300, 230));
            logoText = UIKit.Title(logo, "MOTTAINAI", 168, Color.white);
            UIKit.Stretch(logoText.rectTransform);
            logoText.characterSpacing = 4;
            logoText.textWrappingMode = TextWrappingModes.NoWrap;
            logoText.outlineWidth = 0.16f;
            logoText.outlineColor = new Color32(0x24, 0x4A, 0x53, 255);
            logoText.raycastTarget = true;
            var tapper = logoText.gameObject.AddComponent<Button>();
            tapper.transition = Selectable.Transition.None;
            tapper.onClick.AddListener(TitleTap);
            var leaf = UIKit.Image(logo, UIKit.Primary, UIKit.Leaf, "Leaf");
            UIKit.Place(leaf.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-560, 52), new Vector2(120, 120));
            leaf.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
            var rec = UIKit.Image(logo, UIKit.Primary, UIKit.Recycle, "Recycle");
            UIKit.Place(rec.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(560, 50), new Vector2(110, 110));
            rec.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
            var tag = UIKit.Bold(p.transform, "Paddle  •  Collect  •  Recycle", 46, UIKit.Cloud);
            UIKit.Place(tag.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -275), new Vector2(1200, 70));
            tag.outlineWidth = 0.12f; tag.outlineColor = new Color32(0x24, 0x4A, 0x53, 200);

            var col = Column(p.transform, 760, 470, -120);
            nameInput = UIKit.Size(UIKit.Input(col, "Your name", playerName, 40), 86);
            UIKit.Size(UIKit.Button(col, "PLAY", Tk.playTop, Tk.playBottom, () => StartLogin(true), 78), 140);
            var row = Row(col, 86);
            UIKit.Button(row, "NETWORK LAB", UIKit.Secondary, () => Show(UiScreen.Lab), 30);
            UIKit.Button(row, "FRIENDS", UIKit.Utility, () => StartLogin(false), 30);
            UIKit.Button(row, "SETTINGS", UIKit.Utility, OpenSettings, 30);
            loginStatus = UIKit.Size(UIKit.Bold(col, "", 30, Tk.reward), 56);

            var foot = UIKit.Rect("Footer", p.transform);
            UIKit.Place(foot, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(900, 56));
            var fl = UIKit.Image(foot, UIKit.Primary, UIKit.Leaf);
            UIKit.Place(fl.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-260, 0), new Vector2(42, 42));
            var ft = UIKit.Bold(foot, "Small actions. Big impact.", 32, UIKit.Cloud);
            UIKit.Stretch(ft.rectTransform);
            ft.outlineWidth = 0.12f; ft.outlineColor = new Color32(0x24, 0x4A, 0x53, 200);
        }

        void TitleTap()
        {
            titleTaps = Time.unscaledTime - titleTapAt < 0.8f ? titleTaps + 1 : 1;
            titleTapAt = Time.unscaledTime;
            if (titleTaps < 5) return;
            titleTaps = 0;
            debugPanel.SetActive(true);
            OpenSettings();
            SetStatus("Server: " + content.BaseUrl);
        }

        void BuildDownload()
        {
            var p = Panel(UiScreen.Download, Color.clear);
            Shade(p.transform, 0.55f, 0.35f);
            var card = UIKit.Card(p.transform, UIKit.PanelDark);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1200, 470));
            var col = UIKit.Rect("Col", card.transform);
            UIKit.Stretch(col, 50, 30, 50, 30);
            UIKit.VLayout(col, 16, 0, TextAnchor.MiddleCenter);
            downloadTitle = UIKit.Size(UIKit.Title(col, "Downloading Content Pack", 56, Color.white), 84);
            var bar = UIKit.Size(UIKit.Image(col, new Color(1, 1, 1, 0.18f), UIKit.RoundedL), 40);
            var fill = UIKit.Image(bar.transform, Color.white, UIKit.Gradient(Tk.playTop, Tk.playBottom, 16));
            downloadFill = fill.rectTransform;
            downloadFill.anchorMin = Vector2.zero; downloadFill.anchorMax = new Vector2(0, 1); downloadFill.offsetMin = downloadFill.offsetMax = Vector2.zero;
            downloadInfo = UIKit.Size(UIKit.Bold(col, "", 44, Tk.accentLime), 70);
            downloadFile = UIKit.Size(UIKit.Text(col, "", 30, new Color(0.85f, 0.92f, 0.94f)), 50);
            UIKit.Size(UIKit.Text(col, "Tuning + high-res water normal maps + raft skin from the Cloudflare edge, cached by sha256", 26, new Color(0.7f, 0.82f, 0.85f)), 44);
        }

        void BuildMenu()
        {
            var p = Panel(UiScreen.Menu, Color.clear);
            Shade(p.transform, 0.6f, 0.2f);
            menuHeader = Chip(p.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), new Vector2(980, 64), 30);
            var card = UIKit.Card(p.transform, UIKit.PanelDark);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -30), new Vector2(1000, 800));
            var col = UIKit.Rect("Col", card.transform);
            UIKit.Stretch(col, 50, 36, 50, 30);
            UIKit.VLayout(col, 18, 0, TextAnchor.MiddleCenter);
            UIKit.Size(UIKit.Title(col, "Team up on one raft!", 62, Color.white), 90);
            UIKit.Size(UIKit.Button(col, "QUICK PLAY", Tk.playTop, Tk.playBottom, () => JoinRoom("DEMO"), 58), 124);
            UIKit.Size(UIKit.Button(col, "CREATE ROOM", UIKit.Secondary, CreateRoom, 42), 100);
            var row = Row(col, 100);
            codeInput = UIKit.Input(row, "CODE", "", 46);
            codeInput.characterLimit = 4;
            codeInput.characterValidation = TMP_InputField.CharacterValidation.Alphanumeric;
            codeInput.onValueChanged.AddListener(v => { var up = v.ToUpperInvariant(); if (up != v) codeInput.text = up; });
            UIKit.Button(row, "JOIN CODE", UIKit.Secondary, () => JoinRoom(codeInput.text), 38);
            var row2 = Row(col, 86);
            UIKit.Button(row2, "NETWORK LAB", UIKit.Utility, () => Show(UiScreen.Lab), 30);
            UIKit.Button(row2, "SETTINGS", UIKit.Utility, OpenSettings, 30);
            UIKit.Button(row2, "BACK", UIKit.Utility, () => Show(UiScreen.Login), 30);
            menuStatus = UIKit.Size(UIKit.Bold(col, "", 30, Tk.reward), 56);
        }

        void RefreshMenuUi()
        {
            var ni = NetworkInfo.Provider;
            menuHeader.text = $"Hi <b>{playerName}</b>   <color={NetColorHex(ni.NetworkType)}><b>{ni.NetworkType}</b></color>   slice {ni.SliceStatus}   edge <b>{content.EdgeColo}</b>";
        }

        static string NetColorHex(string t) => t == "5G SA" ? "#5CFF9D" : t == "5G NSA" ? "#C2FF6B" : t == "LTE" ? "#FFD25C" : "#9AD8FF";

        // ---------------- lobby ----------------
        void BuildLobby()
        {
            var p = Panel(UiScreen.Lobby, Color.clear);
            Shade(p.transform, 0.62f, 0.3f);
            lobbyHeader = Chip(p.transform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), new Vector2(1250, 64), 30);
            var rp = Column(p.transform, 1500, 820, -40);
            lobbyRoomTitle = UIKit.Size(UIKit.Title(rp, "", 78, Color.white), 110);
            lobbyRoomTitle.outlineWidth = 0.14f; lobbyRoomTitle.outlineColor = new Color32(0x24, 0x4A, 0x53, 255);
            var roles = Row(rp, 250, 26);
            for (int i = 0; i < 3; i++)
            {
                var r = (Role)(i + 1);
                var b = UIKit.Button(roles, "", UIKit.PanelDark, () => { preferredRole = r; PlayerPrefs.SetInt("role", (int)r); net.PickRole(r); }, 30);
                Color cap = r == Role.LeftPaddler ? Tk.capLeft : r == Role.RightPaddler ? Tk.capRight : Tk.capCollector;
                var circ = UIKit.Image(b.transform, cap, UIKit.Circle, "Cap");
                UIKit.Place(circ.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -20), new Vector2(92, 92));
                var letter = UIKit.Title(circ.transform, RoleInfo.Letter(r), 56, Color.white);
                UIKit.Stretch(letter.rectTransform);
                var nm = UIKit.Title(b.transform, RoleInfo.Name(r), 34, Color.white);
                UIKit.Place(nm.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 66), new Vector2(440, 50));
                roleWho[i] = UIKit.Bold(b.transform, "", 28, UIKit.Cloud);
                UIKit.Place(roleWho[i].rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 22), new Vector2(440, 44));
                roleButtons[i] = b;
            }
            lobbyPlayers = UIKit.Size(UIKit.Text(rp, "", 32, UIKit.TextLight), 110);
            lobbyPlayers.outlineWidth = 0.1f; lobbyPlayers.outlineColor = new Color32(0x24, 0x4A, 0x53, 200);
            var actions = Row(rp, 124, 22);
            readyButton = UIKit.Button(actions, "READY", UIKit.Utility, ToggleReady, 44);
            startButton = UIKit.Button(actions, "START", Tk.playTop, Tk.playBottom, () => net.StartMatch(), 56);
            UIKit.Button(actions, "LEAVE", UIKit.Alert, LeaveRoom, 40);
            lobbyStatus = UIKit.Bold(p.transform, "", 30, Tk.reward);
            UIKit.Place(lobbyStatus.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 26), new Vector2(1600, 56));
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
            lobbyHeader.text = $"Hi <b>{playerName}</b>   <color={NetColorHex(ni.NetworkType)}><b>{ni.NetworkType}</b></color>   slice {ni.SliceStatus}   edge <b>{content.EdgeColo}</b>   room <b>{net.RoomColo}</b>";
            lobbyRoomTitle.text = net.roomCode == "DEMO" ? "QUICK PLAY" : $"ROOM  {net.roomCode}";
            if (lobby == null) { lobbyPlayers.text = "Waiting for room state..."; return; }
            for (int i = 0; i < 3; i++)
            {
                var r = (Role)(i + 1);
                var holder = lobby.IsBot(r) ? null : lobby.players.Find(x => x.role == r && x.connected);
                bool mine = holder != null && holder.clientId == net.ClientId;
                roleWho[i].text = holder == null ? "<color=#BFD3D6>BOT</color>" : (mine ? "<color=#A8DF70>YOU</color>" : holder.name) + (holder.ready ? "  <color=#FFC857>READY</color>" : "");
                UIKit.Recolor(roleButtons[i], mine ? UIKit.Primary : holder == null ? UIKit.PanelDark : UIKit.Secondary);
            }
            var me = lobby.players.Find(x => x.clientId == net.ClientId);
            UIKit.Recolor(readyButton, me != null && me.ready ? UIKit.Primary : UIKit.Utility);
            UIKit.Label(readyButton).text = me != null && me.ready ? "READY!" : "READY?";
            sb.Clear();
            sb.Append("Crew: ");
            foreach (var pl in lobby.players) if (pl.connected) sb.Append("<b>").Append(pl.name).Append("</b>   ");
            sb.Append("\n<size=26>Tap a role to take it. Empty seats are played by bots. Anyone can press START.</size>");
            lobbyPlayers.text = sb.ToString();
            UIKit.Label(startButton).text = lobby.phase == MatchPhase.Running || lobby.phase == MatchPhase.Countdown ? "IN MATCH..." : "START";
        }

        // ---------------- results ----------------
        void BuildResults()
        {
            var p = Panel(UiScreen.Results, Color.clear);
            Shade(p.transform, 0.6f, 0.55f);
            var card = UIKit.Card(p.transform, UIKit.Cloud);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 60), new Vector2(1720, 860));
            var dark = UIKit.DeepTeal;
            resultsHead = UIKit.Title(card.transform, "", 88, UIKit.Primary, TextAlignmentOptions.TopLeft);
            UIKit.Place(resultsHead.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -30), new Vector2(1100, 110));
            resultsSub = UIKit.Bold(card.transform, "", 32, dark, TextAlignmentOptions.TopLeft);
            UIKit.Place(resultsSub.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -136), new Vector2(1000, 50));

            var lbl = UIKit.Bold(card.transform, "SCORE", 28, new Color(dark.r, dark.g, dark.b, 0.7f), TextAlignmentOptions.TopLeft);
            UIKit.Place(lbl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -200), new Vector2(400, 40));
            resultsTotal = UIKit.Bold(card.transform, "0", 120, dark, TextAlignmentOptions.TopLeft);
            resultsTotal.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(resultsTotal.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(56, -230), new Vector2(520, 140));
            for (int i = 0; i < 3; i++)
            {
                stars[i] = UIKit.Image(card.transform, new Color(0.15f, 0.29f, 0.33f, 0.15f), UIKit.Star, "Star" + i);
                UIKit.Place(stars[i].rectTransform, new Vector2(0, 1), new Vector2(0.5f, 0.5f), new Vector2(120 + i * 120, -440 - (i == 1 ? 14 : 0)), new Vector2(i == 1 ? 130 : 108, i == 1 ? 130 : 108));
            }
            resultsRecord = UIKit.Bold(card.transform, "", 30, new Color(0.85f, 0.55f, 0.1f), TextAlignmentOptions.TopLeft);
            UIKit.Place(resultsRecord.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -520), new Vector2(560, 46));
            resultsStats = UIKit.Text(card.transform, "", 32, dark, TextAlignmentOptions.TopLeft);
            UIKit.Place(resultsStats.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(60, -580), new Vector2(560, 170));

            // waste per category (real data)
            var wl = UIKit.Bold(card.transform, "WASTE COLLECTED", 28, new Color(dark.r, dark.g, dark.b, 0.7f), TextAlignmentOptions.TopLeft);
            UIKit.Place(wl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(640, -200), new Vector2(460, 40));
            for (int i = 0; i < NetConst.WasteTypeCount; i++)
            {
                var chip = UIKit.Image(card.transform, Color.white, UIKit.RoundedL, "Waste" + i);
                UIKit.Place(chip.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(640, -250 - i * 66), new Vector2(440, 56));
                var dot = UIKit.Image(chip.transform, WasteCatalog.Colors[i], UIKit.Circle);
                UIKit.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(14, 0), new Vector2(34, 34));
                var nm = UIKit.Text(chip.transform, WasteCatalog.Names[i], 28, dark, TextAlignmentOptions.Left);
                UIKit.Place(nm.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(64, 0), new Vector2(260, 50));
                resultsCounts[i] = UIKit.Bold(chip.transform, "0", 32, dark, TextAlignmentOptions.Right);
                UIKit.Place(resultsCounts[i].rectTransform, new Vector2(1, 0.5f), new Vector2(1, 0.5f), new Vector2(-20, 0), new Vector2(140, 50));
            }

            // restored-river thumbnail
            var frame = UIKit.Card(card.transform, UIKit.Primary, "ThumbFrame");
            UIKit.Place(frame.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -60), new Vector2(560, 330));
            resultsThumb = UIKit.Rect("Thumb", frame.transform).gameObject.AddComponent<RawImage>();
            UIKit.Stretch(resultsThumb.rectTransform, 8, 8, 8, 8);
            resultsThumb.raycastTarget = false;
            var cap = UIKit.Bold(card.transform, "Your river after the run", 28, dark, TextAlignmentOptions.Top);
            UIKit.Place(cap.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -398), new Vector2(560, 44));
            resultsText = UIKit.Text(card.transform, "", 24, new Color(dark.r, dark.g, dark.b, 0.85f), TextAlignmentOptions.TopLeft);
            UIKit.Place(resultsText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-50, -460), new Vector2(560, 330));

            var row = UIKit.Rect("Buttons", p.transform);
            UIKit.Place(row, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 24), new Vector2(1400, 110));
            UIKit.HLayout(row, 24, 0);
            UIKit.Button(row, "PLAY AGAIN", Tk.playTop, Tk.playBottom, () => net.StartMatch(), 46);
            UIKit.Button(row, "LOBBY", UIKit.Secondary, () => { Show(UiScreen.Lobby); RefreshLobbyUi(); }, 42);
            UIKit.Button(row, "MENU", UIKit.Utility, LeaveRoom, 42);
        }

        /// Snapshot of the river for the results card (rendered once from the world camera).
        void CaptureThumb()
        {
            if (world == null || world.cam == null) return;
            if (thumbRt == null) thumbRt = new RenderTexture(560, 315, 16, RenderTextureFormat.ARGB32) { name = "ResultsThumb" };
            var cam = world.cam;
            var prev = cam.targetTexture;
            cam.targetTexture = thumbRt;
            cam.Render();
            cam.targetTexture = prev;
            resultsThumb.texture = thumbRt;
        }

        void FillResults()
        {
            var r = results;
            var s = net.stats;
            var ni = NetworkInfo.Provider;
            bool cleaned = r.reason == EndReason.TimeUp;
            resultsHead.text = cleaned ? "RIVER CLEANED!" : "GOOD RUN!";
            resultsHead.color = cleaned ? UIKit.Primary : new Color(0.95f, 0.6f, 0.15f);
            resultsSub.text = r.reason == EndReason.Crashed ? "Bumped into an obstacle - the river still thanks you!" : r.reason == EndReason.TimeUp ? "Time's up - great teamwork on the raft!" : "Match ended";
            resultsTarget = r.total;
            resultsShownAt = Time.unscaledTime;
            starsShown = 0;
            var thresholds = GameTuning.Current.starScores;
            starsEarned = 0;
            for (int i = 0; i < 3 && thresholds != null && i < thresholds.Length; i++) if (r.total >= thresholds[i]) starsEarned = i + 1;
            for (int i = 0; i < 3; i++) { stars[i].color = new Color(0.15f, 0.29f, 0.33f, 0.15f); stars[i].rectTransform.localScale = Vector3.one; }
            int best = PlayerPrefs.GetInt("best_total", 0);
            if (r.total > best && r.total > 0)
            {
                PlayerPrefs.SetInt("best_total", r.total);
                PlayerPrefs.Save();
                resultsRecord.text = best > 0 ? $"NEW PERSONAL BEST! (was {best})" : "FIRST RECORD SET!";
            }
            else resultsRecord.text = $"Personal best: {best}";
            resultsTotal.text = "0";
            for (int i = 0; i < NetConst.WasteTypeCount; i++) resultsCounts[i].text = r.counts[i].ToString();
            resultsStats.text = $"Distance <b>{r.distance:F0} m</b>   Accuracy <b>{r.Accuracy * 100f:F0}%</b>\n" +
                                $"Catches <b>+{r.catchScore}</b>   <color=#D62828>Toxic -{r.toxicPenalty}</color>\n" +
                                $"<color=#2E9E44><b>Ocean cleaned: {r.kg:F1} kg</b></color>";
            float avgRtt = s.nRtt > 0 ? (float)(s.sumRtt / s.nRtt) : 0;
            resultsText.text = $"<b>NETWORK</b>  {ni.NetworkType} (slice {ni.SliceStatus})\nedge <b>{content.EdgeColo}</b> / room <b>{net.RoomColo}</b>\n" +
                               $"RTT avg <b>{avgRtt:F0} ms</b>   p95 max {s.maxP95:F0} ms   jitter {s.Jitter:F1} ms\n" +
                               $"downlink avg {(s.nDown > 0 ? s.sumDown / s.nDown : 0):F0} kbps   received {s.TotalDownMB:F1} MB";
        }

        void UpdateResults()
        {
            float t = Time.unscaledTime - resultsShownAt;
            float k = Mathf.Clamp01(t / 1.3f);
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            resultsTotal.text = Mathf.RoundToInt(resultsTarget * eased).ToString();
            for (int i = 0; i < 3; i++)
            {
                float appear = 1.4f + i * 0.4f;
                var rt = stars[i].rectTransform;
                if (i < starsEarned && t >= appear)
                {
                    float pt = t - appear;
                    rt.localScale = Vector3.one * (pt < 0.25f ? Mathf.Lerp(1.8f, 1f, pt / 0.25f) : 1f + Mathf.Sin(Time.unscaledTime * 3f + i) * 0.03f);
                    stars[i].color = Tk.reward;
                    if (starsShown <= i) { starsShown = i + 1; sfx.Play(i == 2 ? Sfx.Id.Valuable : Sfx.Id.Combo, 0.8f); }
                }
            }
        }

        // ---------------- network lab ----------------
        void BuildLab()
        {
            var p = Panel(UiScreen.Lab, Color.clear);
            Shade(p.transform, 0.75f, 0.6f);
            var card = UIKit.Card(p.transform, UIKit.PanelDark);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1560, 920));
            var col = UIKit.Rect("Col", card.transform);
            UIKit.Stretch(col, 50, 30, 50, 30);
            UIKit.VLayout(col, 16, 0, TextAnchor.MiddleCenter);
            UIKit.Size(UIKit.Title(col, "NETWORK LAB", 70, UIKit.Secondary), 96);
            labPhase = UIKit.Size(UIKit.Text(col, "Edge RTT (50 HTTPS) + room RTT (100 WebSocket pings) + pack download + 5 MB upload", 30, UIKit.TextLight), 56);
            var bar = UIKit.Size(UIKit.Image(col, new Color(1, 1, 1, 0.18f), UIKit.RoundedL), 36);
            labFill = UIKit.Image(bar.transform, Color.white, UIKit.Gradient(UIKit.Lighten(UIKit.Secondary, 0.2f), UIKit.Secondary, 14)).rectTransform;
            labFill.anchorMin = Vector2.zero; labFill.anchorMax = new Vector2(0, 1); labFill.offsetMin = labFill.offsetMax = Vector2.zero;
            labText = UIKit.Size(UIKit.Text(col, "", 34, UIKit.TextLight), 420);
            var row = Row(col, 110, 20);
            UIKit.Button(row, "RUN TEST", Tk.playTop, Tk.playBottom, RunLab, 46);
            UIKit.Button(row, "BACK", UIKit.Utility, () => { if (net.Ready) { Show(UiScreen.Lobby); RefreshLobbyUi(); } else if (loggedIn) { Show(UiScreen.Menu); RefreshMenuUi(); } else Show(UiScreen.Login); }, 44);
        }

        void RunLab()
        {
            if (lab.Running) return;
            var ni = NetworkInfo.Provider;
            StartCoroutine(lab.Run(content, ni.NetworkType, "slice " + ni.SliceStatus));
        }

        // ---------------- settings overlay (never pauses the online match) ----------------
        void BuildSettings()
        {
            var dim = UIKit.Image(overlayCanvas.transform, new Color(0.02f, 0.1f, 0.12f, 0.55f), null, "Settings");
            dim.sprite = null;
            dim.raycastTarget = true;
            UIKit.Stretch(dim.rectTransform);
            settingsPanel = dim.gameObject;
            var card = UIKit.Card(dim.transform, UIKit.Cloud);
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1000, 860));
            var col = UIKit.Rect("Col", card.transform);
            UIKit.Stretch(col, 56, 34, 56, 30);
            UIKit.VLayout(col, 14, 0, TextAnchor.UpperCenter);
            UIKit.Size(UIKit.Title(col, "SETTINGS", 64, UIKit.DeepTeal), 86);
            UIKit.Size(UIKit.Bold(col, "Graphics quality", 30, UIKit.DeepTeal, TextAlignmentOptions.Left), 40);
            var q = Row(col, 84);
            qHigh = UIKit.Button(q, "HIGH", UIKit.Primary, () => { Quality.High = true; RefreshSettings(); }, 34);
            qLow = UIKit.Button(q, "LOW (faster)", UIKit.Utility, () => { Quality.High = false; RefreshSettings(); }, 34);
            UIKit.Size(UIKit.Bold(col, "Network HUD (jury showcase)", 30, UIKit.DeepTeal, TextAlignmentOptions.Left), 40);
            var h = Row(col, 84);
            hudOn = UIKit.Button(h, "ON", UIKit.Primary, () => { netHudOn = true; RefreshSettings(); }, 34);
            hudOff = UIKit.Button(h, "OFF", UIKit.Utility, () => { netHudOn = false; RefreshSettings(); }, 34);
            UIKit.Size(UIKit.Bold(col, "Content pack", 30, UIKit.DeepTeal, TextAlignmentOptions.Left), 40);
            forceBtn = UIKit.Size(UIKit.Button(col, "FORCE RE-DOWNLOAD", UIKit.Secondary, () =>
            {
                if (screen == UiScreen.Game) return;
                if (loggedIn) { CloseSettings(); StartCoroutine(Redownload()); }
                else { forceDownload = !forceDownload; RefreshSettings(); }
            }, 32), 80);
            var dbg = UIKit.Rect("Debug", col);
            UIKit.HLayout(dbg, 14, 0);
            UIKit.Size(dbg, 80);
            serverInput = UIKit.Input(dbg, "Server URL (empty = default)", PlayerPrefs.GetString("serverOverride", ""), 26);
            UIKit.Size(serverInput, -1, -1, 3f);
            UIKit.Button(dbg, "APPLY", UIKit.Alert, () => { content.SetBaseUrl(serverInput.text); SetStatus("Server: " + content.BaseUrl); }, 30);
            debugPanel = dbg.gameObject;
            debugPanel.SetActive(false);
            UIKit.Size(UIKit.Text(col, "Credits: 3D models by Quaternius (CC0), Poly by Google, Danni Bittman, S. Paul Michael, jeremy, Zoe XR, Thomas DR, FloopBadModels123 (CC-BY 3.0, poly.pizza)  •  Fonts: Fredoka, Nunito Sans (SIL OFL)", 22, new Color(0.14f, 0.29f, 0.33f, 0.8f)), 70);
            UIKit.Size(UIKit.Button(col, "CLOSE", Tk.playTop, Tk.playBottom, CloseSettings, 40), 92);
            settingsPanel.SetActive(false);
        }

        void OpenSettings() { settingsPanel.SetActive(true); RefreshSettings(); }
        void CloseSettings() => settingsPanel.SetActive(false);

        void RefreshSettings()
        {
            bool hi = Quality.High;
            UIKit.Recolor(qHigh, hi ? UIKit.Primary : UIKit.Muted);
            UIKit.Recolor(qLow, hi ? UIKit.Muted : UIKit.Primary);
            UIKit.Recolor(hudOn, netHudOn ? UIKit.Primary : UIKit.Muted);
            UIKit.Recolor(hudOff, netHudOn ? UIKit.Muted : UIKit.Primary);
            UIKit.Label(forceBtn).text = loggedIn ? "RE-DOWNLOAD CONTENT PACK" : forceDownload ? "FORCE RE-DOWNLOAD: ON" : "FORCE RE-DOWNLOAD: OFF";
            forceBtn.interactable = screen != UiScreen.Game;
        }

        // ---------------- HUD ----------------
        void BuildHud()
        {
            var root = hudCanvas.transform;
            // score panel (top-left) + thin distance bar
            var sp = UIKit.Card(root, UIKit.PanelDark, "ScorePanel");
            scorePanel = sp.rectTransform;
            UIKit.Place(scorePanel, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -24), new Vector2(330, 112));
            var leaf = UIKit.Image(sp.transform, UIKit.Primary, UIKit.Leaf);
            UIKit.Place(leaf.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(18, 0), new Vector2(70, 70));
            var sl = UIKit.Bold(sp.transform, "SCORE", 22, new Color(0.85f, 0.95f, 0.95f, 0.75f), TextAlignmentOptions.TopLeft);
            UIKit.Place(sl.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(104, -12), new Vector2(200, 30));
            scoreText = UIKit.Bold(sp.transform, "0", 56, Color.white, TextAlignmentOptions.TopLeft);
            scoreText.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(scoreText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(102, -36), new Vector2(220, 70));
            var track = UIKit.Image(root, new Color(1f, 1f, 1f, 0.3f), UIKit.RoundedL);
            UIKit.Place(track.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(30, -148), new Vector2(330, 14));
            progressFill = UIKit.Image(track.transform, Color.white, UIKit.Gradient(Tk.playTop, Tk.playBottom, 6)).rectTransform;
            progressFill.anchorMin = Vector2.zero; progressFill.anchorMax = new Vector2(0, 1); progressFill.offsetMin = progressFill.offsetMax = Vector2.zero;
            distanceText = UIKit.Bold(root, "", 24, Color.white, TextAlignmentOptions.TopLeft);
            distanceText.outlineWidth = 0.15f; distanceText.outlineColor = new Color32(0x24, 0x4A, 0x53, 220);
            UIKit.Place(distanceText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -166), new Vector2(400, 36));
            comboText = UIKit.Bold(root, "", 30, Tk.reward, TextAlignmentOptions.TopLeft);
            comboText.outlineWidth = 0.15f; comboText.outlineColor = new Color32(0x24, 0x4A, 0x53, 220);
            UIKit.Place(comboText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(32, -202), new Vector2(500, 44));

            // role badge (top-center)
            badgeCircle = UIKit.Image(root, Color.white, UIKit.Circle, "Badge");
            badgeCircle.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
            UIKit.Place(badgeCircle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -16), new Vector2(96, 96));
            badgeLetter = UIKit.Title(badgeCircle.transform, "?", 54, Color.white);
            UIKit.Stretch(badgeLetter.rectTransform);
            var nameBg = UIKit.Card(root, UIKit.PanelDark, "BadgeName");
            UIKit.Place(nameBg.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -118), new Vector2(330, 44));
            badgeName = UIKit.Bold(nameBg.transform, "", 24, Color.white);
            UIKit.Stretch(badgeName.rectTransform);

            // timer pill (top-right, left of the round menu button)
            var tp = UIKit.Card(root, UIKit.PanelDark, "TimerPill");
            UIKit.Place(tp.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-136, -24), new Vector2(230, 84));
            var clock = UIKit.Image(tp.transform, Tk.reward, UIKit.Clock);
            UIKit.Place(clock.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(16, 0), new Vector2(50, 50));
            timerText = UIKit.Bold(tp.transform, "2:00", 50, Color.white, TextAlignmentOptions.Right);
            timerText.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Stretch(timerText.rectTransform, 70, 0, 20, 0);
            shieldText = UIKit.Bold(root, "", 30, new Color(0.55f, 0.92f, 1f), TextAlignmentOptions.TopRight);
            shieldText.outlineWidth = 0.15f; shieldText.outlineColor = new Color32(0x24, 0x4A, 0x53, 220);
            UIKit.Place(shieldText.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-36, -120), new Vector2(900, 44));

            // round menu button (overlay canvas: tappable; opens settings, the match keeps running)
            var mb = UIKit.Image(overlayCanvas.transform, Color.white, UIKit.Circle, "MenuButton");
            mb.sprite = UIKit.Circle;
            mb.color = UIKit.PanelDark;
            mb.raycastTarget = true;
            mb.gameObject.AddComponent<Shadow>().effectDistance = new Vector2(0, -5);
            UIKit.Place(mb.rectTransform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -24), new Vector2(86, 86));
            for (int i = 0; i < 3; i++)
            {
                var bar = UIKit.Image(mb.transform, Color.white, UIKit.RoundedL);
                UIKit.Place(bar.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 14 - i * 14), new Vector2(40, 8));
            }
            var mbtn = mb.gameObject.AddComponent<Button>();
            mbtn.onClick.AddListener(OpenSettings);
            mb.gameObject.AddComponent<PressScale>();
            hudMenuButton = mb.rectTransform;
            mb.gameObject.SetActive(false);

            // SYNC ring (bottom-right): fills while the partner's stroke is fresh -> paddle now for SYNC; collector: net ready
            var ring = UIKit.Rect("SyncRing", root);
            UIKit.Place(ring, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-40, 40), new Vector2(190, 190));
            syncGlow = UIKit.Image(ring, new Color(1f, 0.85f, 0.4f, 0f), UIKit.Circle, "Glow");
            UIKit.Stretch(syncGlow.rectTransform, -24, -24, -24, -24);
            var back = UIKit.Image(ring, UIKit.PanelDark, UIKit.Circle, "Back");
            UIKit.Stretch(back.rectTransform, 10, 10, 10, 10);
            var trackR = UIKit.Image(ring, new Color(1f, 1f, 1f, 0.22f), UIKit.RingSprite, "Track");
            UIKit.Stretch(trackR.rectTransform);
            syncFill = UIKit.Image(ring, UIKit.Secondary, UIKit.RingSprite, "Fill");
            syncFill.type = Image.Type.Filled;
            syncFill.fillMethod = Image.FillMethod.Radial360;
            syncFill.fillOrigin = (int)Image.Origin360.Top;
            syncFill.fillClockwise = true;
            UIKit.Stretch(syncFill.rectTransform);
            syncLabel = UIKit.Title(ring, "SYNC", 40, Color.white);
            UIKit.Place(syncLabel.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(170, 56));
            syncSub = UIKit.Bold(ring, "", 24, Tk.reward);
            UIKit.Place(syncSub.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -32), new Vector2(170, 34));

            // waste counters (bottom-center, below the raft: never over the lane ahead)
            var wc = UIKit.Rect("WasteCounters", root);
            UIKit.Place(wc, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 18), new Vector2(760, 60));
            UIKit.HLayout(wc, 8, 0);
            for (int i = 0; i < NetConst.WasteTypeCount; i++)
            {
                var chip = UIKit.Card(wc, new Color(UIKit.DeepTeal.r, UIKit.DeepTeal.g, UIKit.DeepTeal.b, 0.8f), "Count" + i);
                var dot = UIKit.Image(chip.transform, WasteCatalog.Colors[i], UIKit.Circle);
                UIKit.Place(dot.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(10, 0), new Vector2(30, 30));
                wasteCounts[i] = UIKit.Bold(chip.transform, "0", 28, Color.white, TextAlignmentOptions.Right);
                UIKit.Stretch(wasteCounts[i].rectTransform, 40, 0, 12, 0);
            }

            bigText = UIKit.Title(root, "", 150, Color.white);
            bigText.outlineWidth = 0.2f;
            bigText.outlineColor = new Color32(0x24, 0x4A, 0x53, 255);
            bigText.textWrappingMode = TextWrappingModes.NoWrap;
            UIKit.Place(bigText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 150), new Vector2(1500, 220));

            toastText = UIKit.Bold(root, "", 30, Tk.reward);
            toastText.outlineWidth = 0.15f; toastText.outlineColor = new Color32(0x24, 0x4A, 0x53, 230);
            UIKit.Place(toastText.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -176), new Vector2(1200, 46));

            // onboarding (left side, mid height): animated hand + key words
            var hp = UIKit.Card(root, new Color(UIKit.DeepTeal.r, UIKit.DeepTeal.g, UIKit.DeepTeal.b, 0.86f), "Onboarding");
            UIKit.Place(hp.rectTransform, new Vector2(0, 0.5f), new Vector2(0, 0.5f), new Vector2(30, 40), new Vector2(560, 250));
            hintPanel = hp.gameObject;
            var hand = UIKit.Image(hp.transform, Color.white, UIKit.Hand, "Hand");
            hintHand = hand.rectTransform;
            UIKit.Place(hintHand, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(82, 0), new Vector2(92, 92));
            hintText = UIKit.Bold(hp.transform, "", 32, Color.white, TextAlignmentOptions.Left);
            UIKit.Stretch(hintText.rectTransform, 150, 16, 20, 16);

            for (int i = 0; i < 20; i++)
            {
                var t = UIKit.Title(root, "", 52, Color.white, TextAlignmentOptions.Center, "Popup");
                t.outlineWidth = 0.25f;
                t.outlineColor = new Color32(0x14, 0x2A, 0x30, 220);
                t.textWrappingMode = TextWrappingModes.NoWrap;
                t.rectTransform.sizeDelta = new Vector2(500, 80);
                t.gameObject.SetActive(false);
                pops.Add(new Pop { t = t });
            }
            hudCanvas.gameObject.SetActive(false);
        }

        void BuildNetHud()
        {
            var bg = UIKit.Card(netCanvas.transform, new Color(UIKit.DeepTeal.r, UIKit.DeepTeal.g, UIKit.DeepTeal.b, 0.82f), "NetHud");
            UIKit.Place(bg.rectTransform, new Vector2(0, 0), new Vector2(0, 0), new Vector2(18, 18), new Vector2(500, 318));
            netHudBadgeBg = UIKit.Image(bg.transform, new Color(0.36f, 1f, 0.62f), UIKit.RoundedL, "NetBadge");
            UIKit.Place(netHudBadgeBg.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(16, -14), new Vector2(150, 44));
            netHudBadge = UIKit.Title(netHudBadgeBg.transform, "", 28, UIKit.DeepTeal);
            UIKit.Stretch(netHudBadge.rectTransform);
            var cf = UIKit.Bold(bg.transform, "Cloudflare  •  wss", 22, new Color(0.85f, 0.95f, 0.95f, 0.8f), TextAlignmentOptions.Left);
            UIKit.Place(cf.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(180, -18), new Vector2(240, 36));
            for (int i = 0; i < 4; i++)
            {
                qualityBars[i] = UIKit.Image(bg.transform, new Color(1f, 1f, 1f, 0.2f), null, "Q" + i);
                UIKit.Place(qualityBars[i].rectTransform, new Vector2(1, 1), new Vector2(0, 1), new Vector2(-74 + i * 14, -16 - (24 - 6 * i)), new Vector2(10, 12 + 6 * i));
            }
            netHudText = UIKit.Text(bg.transform, "", 21, new Color(0.9f, 0.98f, 0.95f), TextAlignmentOptions.TopLeft);
            UIKit.Stretch(netHudText.rectTransform, 18, 66, 12, 10);
            netHudPanel = bg.gameObject;
        }

        void SetRoleBadge(Role r)
        {
            Color c = r switch { Role.LeftPaddler => Tk.capLeft, Role.RightPaddler => Tk.capRight, Role.Collector => Tk.capCollector, _ => Color.grey };
            badgeCircle.color = c;
            badgeLetter.text = r == Role.Collector ? "<size=36>NET</size>" : RoleInfo.Letter(r);
            badgeName.text = "YOU: " + RoleInfo.Name(r);
            bool paddler = r == Role.LeftPaddler || r == Role.RightPaddler;
            hintText.text = paddler
                ? "<color=#FFC857>SWIPE DOWN</color> to paddle\n<color=#A8DF70>SYNC WITH YOUR TEAM</color>\nfor a <color=#FFC857>BOOST</color>"
                : "<color=#FFC857>TAP</color> floating waste\nto throw the net\n<color=#FF8360>Red = toxic</color>, skip it!";
            if (!Application.isMobilePlatform) hintText.text += "<size=20>\n[keys: W/S paddle, Space throw, H net HUD]</size>";
            for (int i = 0; i < catchCounts.Length; i++) { catchCounts[i] = 0; wasteCounts[i].text = "0"; }
            onboardActions = 0; scoreShown = 0; partnerStrokeAt = -99f;
            syncLabel.text = paddler ? "SYNC" : "NET";
            if (world != null) world.raftView.SetPartnerHalo(paddler ? (r == Role.LeftPaddler ? Role.RightPaddler : Role.LeftPaddler) : Role.None);
        }

        void Show(UiScreen s)
        {
            screen = s;
            foreach (var kv in panels) kv.Value.SetActive(kv.Key == s);
            mainCanvas.gameObject.SetActive(s != UiScreen.Game);
            hudCanvas.gameObject.SetActive(s == UiScreen.Game);
            if (hudMenuButton != null) hudMenuButton.gameObject.SetActive(s == UiScreen.Game);
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

        /// Collect feedback bookkeeping (counters chip + score bump).
        void CountCatch(WasteType t)
        {
            int i = (int)t;
            if (i < 0 || i >= catchCounts.Length) return;
            catchCounts[i]++;
            wasteCounts[i].text = catchCounts[i].ToString();
            wasteCounts[i].transform.parent.localScale = Vector3.one * 1.18f;
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
                if (hudScore != scoreShown) { if (hudScore > scoreShown) scoreBump = 1f; scoreShown = hudScore; }
                scoreText.text = hudScore.ToString();
                scoreBump = Mathf.Max(0f, scoreBump - dt * 5f);
                scoreText.rectTransform.localScale = Vector3.one * (1f + 0.22f * scoreBump);
                comboText.text = hudCombo ? "COMBO READY x1.5" : "";
                if (world != null)
                {
                    float dist = Mathf.Max(0f, world.RaftPos.z);
                    progressFill.anchorMax = new Vector2(Mathf.Clamp01(dist / GameTuning.Current.courseLength), 1);
                    distanceText.text = $"{dist:F0} / {GameTuning.Current.courseLength:F0} m";
                }
                if (hudPhase == MatchPhase.Countdown) timerText.text = $"{Mathf.CeilToInt(hudTimeLeft)}";
                else { int s = Mathf.CeilToInt(hudTimeLeft); timerText.text = $"{s / 60}:{s % 60:00}"; }
                timerText.color = hudPhase == MatchPhase.Running && hudTimeLeft < 15f ? UIKit.Alert : Color.white;
                string pw = hudShield > 0.05f ? $"SHIELD {hudShield:F1}s  " : "";
                float ml = magnetUntil - Time.time, tl = turboUntil - Time.time;
                if (ml > 0f) pw += $"<color=#FF8FE0>MAGNET {ml:F1}s</color>  ";
                if (tl > 0f) pw += $"<color=#FFB35C>TURBO {tl:F1}s</color>";
                shieldText.text = pw;
                for (int i = 0; i < wasteCounts.Length; i++)
                {
                    var tr = wasteCounts[i].transform.parent;
                    if (tr.localScale.x > 1.001f) tr.localScale = Vector3.one * Mathf.MoveTowards(tr.localScale.x, 1f, dt * 1.2f);
                }
                UpdateSyncRing();
                // onboarding: hand demonstrates the gesture; fades after a few successful actions
                bool hint = Time.time < hintUntil && onboardActions < 4;
                hintPanel.SetActive(hint);
                if (world != null) world.raftView.ShowPartnerHalo(hint);
                if (hint)
                {
                    float c = Mathf.Repeat(Time.time * 0.9f, 1f);
                    bool paddler = myRole == Role.LeftPaddler || myRole == Role.RightPaddler;
                    hintHand.anchoredPosition = paddler ? new Vector2(82, Mathf.Lerp(60f, -60f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c * 1.6f)))) : new Vector2(82, 0);
                    hintHand.localScale = Vector3.one * (paddler ? 1f : 1f - 0.15f * Mathf.Sin(c * Mathf.PI * 2f));
                    var hc = hintHand.GetComponent<Image>(); var col = hc.color; col.a = paddler ? Mathf.Clamp01(1.4f - c * 1.4f) + 0.2f : 1f; hc.color = col;
                }
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

            netHudPanel.SetActive(netHudOn && screen == UiScreen.Game);
            netHudTimer -= dt;
            if (netHudPanel.activeSelf && netHudTimer <= 0f)
            {
                netHudTimer = 0.25f;
                var s = net.stats;
                var ni = NetworkInfo.Provider;
                netHudBadge.text = ni.NetworkType;
                netHudBadgeBg.color = ni.NetworkType == "5G SA" ? new Color(0.36f, 1f, 0.62f) : ni.NetworkType == "5G NSA" ? new Color(0.76f, 1f, 0.42f) : ni.NetworkType == "LTE" ? new Color(1f, 0.82f, 0.36f) : new Color(0.6f, 0.85f, 1f);
                float frameMs = 1000f / Mathf.Max(1f, s.Fps);
                sb.Clear();
                sb.Append($"Edge <b>{content.EdgeColo}</b>  Room <b>{net.RoomColo}</b>   slice {ni.SliceStatus}\n");
                sb.Append($"RTT <b>{s.RttAvg:F0}</b> ms  p95 <b>{s.RttP95:F0}</b>  jitter <b>{s.Jitter:F1}</b>\n");
                sb.Append($"Snapshots <b>{s.SnapshotRate:F0}</b>/s   buffer <b>{(world != null ? world.InterpMs : 0):F0}</b> ms   late {(world != null ? world.LatePct : 0):F0}%\n");
                sb.Append($"Down <b>{s.DownKbps:F0}</b> kbps  Up {s.UpKbps:F0}  msgs {s.Pps:F0}/s  {s.TotalDownMB:F1} MB\n");
                sb.Append($"<color=#FFC857>Paddle>screen</color> <b>{frameMs:F0} ms</b> (predicted)   teammate <b>{s.RttAvg + frameMs:F0} ms</b>\n");
                sb.Append($"FPS <b>{s.Fps:F0}</b>   TCP_NODELAY {(s.NoDelay ? "on" : "off")}   {(Quality.High ? "High" : "Low")}\n");
                sb.Append("<size=17><color=#9FC3C8>3-finger tap / H to hide</color></size>");
                int q = 4;
                if (s.RttP95 > 30f) q--;
                if (s.RttP95 > 60f) q--;
                if (s.Jitter > 8f) q--;
                q = Mathf.Clamp(q, 0, 4);
                var qc = q >= 4 ? new Color(0.36f, 1f, 0.62f) : q == 3 ? new Color(0.75f, 1f, 0.4f) : q == 2 ? new Color(1f, 0.82f, 0.36f) : q == 1 ? new Color(1f, 0.55f, 0.3f) : new Color(1f, 0.35f, 0.3f);
                for (int i = 0; i < 4; i++) qualityBars[i].color = i < q ? qc : new Color(1f, 1f, 1f, 0.2f);
                netHudText.text = sb.ToString();
            }
        }

        /// Paddlers: ring drains over the sync window after the partner's stroke (stroke now = SYNC boost), glows when hot.
        /// Collector: ring = net cooldown, glows when ready.
        void UpdateSyncRing()
        {
            var t = GameTuning.Current;
            bool paddler = myRole == Role.LeftPaddler || myRole == Role.RightPaddler;
            float fill, glow;
            if (paddler)
            {
                float since = Time.time - partnerStrokeAt;
                fill = Mathf.Clamp01(1f - since / Mathf.Max(0.05f, t.syncWindow + 0.1f));
                glow = fill > 0f ? 1f : 0f;
                syncFill.color = Color.Lerp(UIKit.Secondary, Tk.reward, fill);
                syncSub.text = Time.time - lastSyncTime < 2f && syncStreak > 1 ? $"x{syncStreak}" : fill > 0f ? "NOW!" : "";
            }
            else
            {
                float left = localThrowReady - Time.time;
                fill = Mathf.Clamp01(1f - left / Mathf.Max(0.05f, t.netCooldown));
                glow = fill >= 1f ? 0.5f + 0.5f * Mathf.Sin(Time.time * 6f) : 0f;
                syncFill.color = fill >= 1f ? UIKit.Primary : UIKit.Secondary;
                syncSub.text = fill >= 1f ? "READY" : "";
            }
            syncFill.fillAmount = fill;
            var gc = syncGlow.color; gc.a = Mathf.MoveTowards(gc.a, glow * 0.55f, Time.deltaTime * 4f); syncGlow.color = gc;
            syncGlow.rectTransform.localScale = Vector3.one * (1f + 0.06f * Mathf.Sin(Time.time * 10f) * gc.a);
        }
    }
}
