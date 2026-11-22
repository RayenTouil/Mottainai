using System.Collections.Generic;
using Mottainai.Gameplay;
using Mottainai.Net;
using Mottainai.Platform;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mottainai.Client
{
    /// v4 Phase A: icons rendered from in-game visuals, role cards, collect/avoid pop-up, bottom legend, "?" + menu buttons, leave overlay.
    public partial class ClientApp
    {
        Canvas v4Canvas;
        RectTransform v4Safe;
        GameObject roleCard, legendPop, leaveMenu, leaveConfirm, legendBar, helpBtn, menuBtn;
        TextMeshProUGUI roleCardTitle, roleCardEffect, roleCardGesture, legendBarHint;
        RectTransform roleHand;
        Image roleBadge;
        readonly TextMeshProUGUI[] legendCounts = new TextMeshProUGUI[7];
        readonly int[] hudCounts = new int[7];
        Role cardRole;
        static readonly Dictionary<byte, Texture> icons = new Dictionary<byte, Texture>();
        static readonly string[] ObstacleNames = { "Rock", "Big Rock", "Iceberg", "Old Boat", "Log", "Shark" };

        /// 128 px snapshot of the real in-game visual for an entity code (rendered once, cached).
        static Texture Icon(byte code)
        {
            if (icons.TryGetValue(code, out var t)) return t;
            var go = VisualFactory.Create(code);
            go.transform.position = new Vector3(code * 40f, -500f, 0); // own spot: Destroy is deferred to end of frame
            foreach (var b in go.GetComponentsInChildren<Behaviour>()) b.enabled = false;
            // icon = the solid object only: hide transparent decorations (rings, glow, blob shadow, bubble)
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.sharedMaterial == null || r.sharedMaterial.renderQueue >= 2500) r.enabled = false;
            bool any = false; foreach (var r in go.GetComponentsInChildren<Renderer>()) any |= r.enabled;
            if (!any) foreach (var r in go.GetComponentsInChildren<Renderer>()) r.enabled = r.gameObject.name != "bubble"; // power-ups: symbol only
            var bounds = new Bounds(go.transform.position, Vector3.one * 0.1f);
            bool first = true;
            foreach (var r in go.GetComponentsInChildren<Renderer>()) if (r.enabled) { if (first) { bounds = r.bounds; first = false; } else bounds.Encapsulate(r.bounds); }
            var camGo = new GameObject("IconCam");
            var cam = camGo.AddComponent<Camera>();
            cam.enabled = false; cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0, 0, 0, 0);
            cam.orthographic = true; cam.orthographicSize = bounds.extents.magnitude * 0.9f;
            cam.transform.position = bounds.center + new Vector3(0.35f, 0.45f, -1f).normalized * 20f;
            cam.transform.LookAt(bounds.center);
            cam.nearClipPlane = 1f; cam.farClipPlane = 40f; cam.cullingMask = ~0;
            var rt = new RenderTexture(128, 128, 16, RenderTextureFormat.ARGB32) { name = "Icon_" + code };
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = null;
            Object.Destroy(camGo); Object.Destroy(go);
            icons[code] = rt;
            return rt;
        }

        static byte WasteCode(int i) => EntityCode.Pack(EntityKind.Waste, (byte)i);

        RawImage IconImg(Transform parent, byte code, float size)
        {
            var go = new GameObject("icon", typeof(RectTransform), typeof(RawImage));
            go.transform.SetParent(parent, false);
            var ri = go.GetComponent<RawImage>();
            ri.texture = Icon(code); ri.raycastTarget = false;
            UIKit.Size(ri, size, size);
            return ri;
        }

        TextMeshProUGUI Label(Transform p, string s, float size, Color c, float w)
        {
            var t = UIKit.Text(p, s, size, c);
            t.fontStyle = FontStyles.Bold; t.raycastTarget = false;
            UIKit.Size(t, size * 1.3f, w);
            return t;
        }

        Image Box(Transform p, Vector2 anchor, Vector2 pos, Vector2 size, Color c, string name)
        {
            var img = UIKit.Image(p, c, UIKit.Rounded, name);
            img.type = Image.Type.Sliced; img.raycastTarget = false;
            UIKit.Place(img.rectTransform, anchor, anchor, pos, size);
            return img;
        }

        void BuildV4()
        {
            v4Canvas = UIKit.Canvas("UI_V4", 8);
            v4Safe = UIKit.Stretch(UIKit.Rect("Safe", v4Canvas.transform));
            var root = v4Safe;
            var dim = new Color(0.04f, 0.08f, 0.11f, 0.9f);

            // --- HUD buttons: "?" + menu next to the timer (top-right)
            var hb = UIKit.Button(root, "?", UIKit.Accent2, ToggleHelp, 48, "HelpBtn");
            UIKit.Place((RectTransform)hb.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-438, -18), new Vector2(88, 88));
            helpBtn = hb.gameObject;
            var mb = UIKit.Button(root, "II", UIKit.Muted, () => SetLeaveMenu(true), 40, "MenuBtn");
            UIKit.Place((RectTransform)mb.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-336, -18), new Vector2(88, 88));
            menuBtn = mb.gameObject;

            // --- bottom legend bar: 7 waste icons, score, live count
            var bar = Box(root, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(1180, 132), new Color(0.12f, 0.16f, 0.2f, 0.7f), "LegendBar");
            UIKit.HLayout(bar, 8, 10);
            for (int i = 0; i < 7; i++)
            {
                var cell = UIKit.Rect("w" + i, bar.transform);
                UIKit.Size(cell, 112, 150);
                UIKit.VLayout(cell, 0, 0).childForceExpandWidth = false;
                var ic = IconImg(cell, WasteCode(i), 72);
                if (i == (int)WasteType.Toxic) ToxicRing(ic);
                int sc = GameTuning.Current.wasteScores[i];
                Label(cell, (sc > 0 ? "+" : "") + sc, 26, WasteCatalog.Colors[i], 150).outlineWidth = 0.2f;
                var badge = Box(ic.transform, new Vector2(1, 1), new Vector2(10, 6), new Vector2(46, 34), new Color(0, 0, 0, 0.7f), "count");
                legendCounts[i] = UIKit.Text(badge.transform, "0", 24, Color.white);
                legendCounts[i].raycastTarget = false; legendCounts[i].fontStyle = FontStyles.Bold;
                UIKit.Stretch(legendCounts[i].rectTransform);
            }
            legendBar = bar.gameObject;

            // --- role card (lobby full card / reopen via "?")
            var card = Box(root, new Vector2(0.5f, 0.5f), new Vector2(0, 0), new Vector2(1100, 620), dim, "RoleCard");
            roleCard = card.gameObject;
            roleBadge = UIKit.Image(card.transform, Color.white, UIKit.Circle); roleBadge.raycastTarget = false;
            UIKit.Place(roleBadge.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -30), new Vector2(90, 90));
            roleCardTitle = UIKit.Text(card.transform, "", 56, Color.white, TextAlignmentOptions.Left); roleCardTitle.fontStyle = FontStyles.Bold;
            UIKit.Place(roleCardTitle.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(150, -40), new Vector2(900, 80));
            var phone = UIKit.Image(card.transform, new Color(0.9f, 0.95f, 1f, 0.95f), UIKit.Rounded); phone.type = Image.Type.Sliced; phone.raycastTarget = false;
            UIKit.Place(phone.rectTransform, new Vector2(0, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(280, -30), new Vector2(380, 220));
            var screenIn = UIKit.Image(phone.transform, new Color(0.25f, 0.75f, 0.85f), UIKit.Rounded); screenIn.type = Image.Type.Sliced; screenIn.raycastTarget = false;
            UIKit.Stretch(screenIn.rectTransform, 18, 14, 18, 14);
            roleCardGesture = UIKit.Text(phone.transform, "", 120, Color.white); roleCardGesture.raycastTarget = false; roleCardGesture.fontStyle = FontStyles.Bold;
            UIKit.Stretch(roleCardGesture.rectTransform);
            var hand = UIKit.Image(phone.transform, new Color(1f, 0.85f, 0.65f), UIKit.Circle); hand.raycastTarget = false;
            roleHand = hand.rectTransform; UIKit.Place(roleHand, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(56, 56));
            var raftIcon = UIKit.Image(card.transform, ArtV3.RaftBody, UIKit.Circle); raftIcon.raycastTarget = false;
            UIKit.Place(raftIcon.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-280, -30), new Vector2(150, 150));
            roleCardEffect = UIKit.Text(card.transform, "", 64, Color.white); roleCardEffect.raycastTarget = false; roleCardEffect.fontStyle = FontStyles.Bold;
            UIKit.Place(roleCardEffect.rectTransform, new Vector2(1, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(-280, -30), new Vector2(420, 260));
            var ok = UIKit.Button(card.transform, "OK", UIKit.Accent, () => roleCard.SetActive(false), 48);
            UIKit.Place((RectTransform)ok.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(260, 100));
            roleCard.SetActive(false);

            // --- collect / avoid / power-ups pop-up
            var lp = Box(root, new Vector2(0.5f, 0.5f), new Vector2(0, 90), new Vector2(1500, 560), dim, "LegendPop");
            legendPop = lp.gameObject;
            var col = UIKit.Rect("rows", lp.transform); UIKit.Stretch(col, 30, 20, 30, 20);
            UIKit.VLayout(col, 12, 0, TextAnchor.MiddleCenter);
            var rowC = Row(col, "+", UIKit.Accent);
            for (int i = 0; i < 6; i++) { int sc = GameTuning.Current.wasteScores[i]; Item(rowC, WasteCode(i), "+" + sc, WasteCatalog.Colors[i]); }
            var rowA = Row(col, "X", UIKit.Warn);
            Item(rowA, WasteCode((int)WasteType.Toxic), GameTuning.Current.wasteScores[(int)WasteType.Toxic].ToString(), WasteCatalog.Colors[(int)WasteType.Toxic]);
            for (int i = 0; i < 6; i++) Item(rowA, EntityCode.Pack(EntityKind.Obstacle, (byte)i), ObstacleNames[i], UIKit.Warn, true);
            Label(rowA, "= GAME OVER", 30, UIKit.Warn, 220);
            var rowP = Row(col, "!", new Color(1f, 0.85f, 0.3f));
            var tu = GameTuning.Current;
            Item(rowP, EntityCode.Pack(EntityKind.Shield, 0), $"{tu.shieldDuration:F0} s", new Color(0.5f, 0.91f, 1f));
            Item(rowP, EntityCode.Pack(EntityKind.Shield, 1), $"{tu.magnetDuration:F0} s", ArtV3.NetBadge);
            Item(rowP, EntityCode.Pack(EntityKind.Shield, 2), $"{tu.turboDuration:F0} s", UIKit.Warn);
            var close = UIKit.Button(lp.transform, "OK", UIKit.Accent, () => legendPop.SetActive(false), 40);
            UIKit.Place((RectTransform)close.transform, new Vector2(1, 0), new Vector2(1, 0), new Vector2(-24, 24), new Vector2(160, 90));
            legendPop.SetActive(false);

            // --- leave overlay
            leaveMenu = Box(root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(700, 420), dim, "LeaveMenu").gameObject;
            leaveMenu.GetComponent<Image>().raycastTarget = true;
            var lm = UIKit.Rect("col", leaveMenu.transform); UIKit.Stretch(lm, 60, 50, 60, 50); UIKit.VLayout(lm, 30, 0, TextAnchor.MiddleCenter);
            UIKit.Size(UIKit.Button(lm, "RESUME", UIKit.Accent, () => SetLeaveMenu(false), 48), 120);
            UIKit.Size(UIKit.Button(lm, "LEAVE MATCH", UIKit.Warn, () => { leaveMenu.SetActive(false); leaveConfirm.SetActive(true); }, 48), 120);
            leaveConfirm = Box(root, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(800, 380), dim, "LeaveConfirm").gameObject;
            leaveConfirm.GetComponent<Image>().raycastTarget = true;
            var lc = UIKit.Rect("col", leaveConfirm.transform); UIKit.Stretch(lc, 50, 40, 50, 40); UIKit.VLayout(lc, 24, 0, TextAnchor.MiddleCenter);
            UIKit.Size(UIKit.Text(lc, "Leave? A bot takes your role.", 40, Color.white), 70);
            var yn = UIKit.Rect("yn", lc); UIKit.Size(yn, 120); UIKit.HLayout(yn, 30, 0);
            UIKit.Button(yn, "LEAVE", UIKit.Warn, LeaveMatch, 44);
            UIKit.Button(yn, "CANCEL", UIKit.Muted, () => SetLeaveMenu(false), 44);
            leaveMenu.SetActive(false); leaveConfirm.SetActive(false);

            BuildPauseCard(root, dim);
            input.Blocked = pos => V4Blocks(pos);
            v4Canvas.gameObject.SetActive(false);
        }

        /// Red ring (toxic only) drawn behind the icon.
        void ToxicRing(RawImage ic)
        {
            var ring = UIKit.Image(ic.transform.parent, new Color(0.84f, 0.16f, 0.16f, 1f), UIKit.Circle, "toxicRing");
            ring.raycastTarget = false;
            ring.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
            ring.transform.SetSiblingIndex(ic.transform.GetSiblingIndex());
            var hole = UIKit.Image(ring.transform, new Color(0.12f, 0.16f, 0.2f, 1f), UIKit.Circle); hole.raycastTarget = false;
            UIKit.Stretch(hole.rectTransform, 7, 7, 7, 7);
            var rt = ring.rectTransform; rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f); rt.pivot = new Vector2(0.5f, 1f);
            float s = ((RectTransform)ic.transform).sizeDelta.x; rt.sizeDelta = new Vector2(s, s); rt.anchoredPosition = Vector2.zero;
        }

        // ---------------- Phase C: pause / resume / reconnecting overlay ----------------
        GameObject pauseCard;
        TextMeshProUGUI pauseTitle, pauseBody, pauseBig;
        Button pauseContinue;
        PauseInfo netPause;
        float netPauseAt, resumeAt;
        bool netPaused;
        int pauseCount;
        /// Inputs are ignored by the server while paused / during the 3-2-1, and cannot reach it while reconnecting.
        bool NetFrozen => netPaused || (screen == UiScreen.Game && results == null && !net.Ready);

        void BuildPauseCard(RectTransform root, Color dim)
        {
            var pc = Box(root, new Vector2(0.5f, 0.5f), new Vector2(0, 30), new Vector2(1150, 560), dim, "PauseCard");
            pc.raycastTarget = true;
            pauseTitle = UIKit.Text(pc.transform, "", 60, Color.white); pauseTitle.fontStyle = FontStyles.Bold; pauseTitle.raycastTarget = false;
            UIKit.Place(pauseTitle.rectTransform, new Vector2(0.5f, 1), new Vector2(0.5f, 1), new Vector2(0, -30), new Vector2(1050, 90));
            pauseBody = UIKit.Text(pc.transform, "", 44, Color.white); pauseBody.richText = true; pauseBody.raycastTarget = false;
            UIKit.Place(pauseBody.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 10), new Vector2(1050, 300));
            pauseBig = UIKit.Text(pc.transform, "", 220, ArtV3.RaftBody); pauseBig.fontStyle = FontStyles.Bold; pauseBig.raycastTarget = false;
            UIKit.Place(pauseBig.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, -20), new Vector2(600, 280));
            pauseContinue = UIKit.Button(pc.transform, "CONTINUE WITH BOT", UIKit.Warn, () => net.ContinueWithBot(), 40);
            UIKit.Place((RectTransform)pauseContinue.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(560, 100));
            pauseCard = pc.gameObject;
            pauseCard.SetActive(false);
        }

        void OnNetPause(PauseInfo p)
        {
            if (!netPaused || resumeAt > 0) pauseCount++;
            netPause = p; netPauseAt = Time.realtimeSinceStartup; netPaused = true; resumeAt = 0f;
            world?.SetNetPaused();
            legendPop.SetActive(false);
        }

        void OnNetResume(int ms)
        {
            netPaused = true; netPause = null;
            resumeAt = Time.realtimeSinceStartup + ms / 1000f;
            world?.SetNetPaused();
        }

        void ClearNetPause() { netPaused = false; resumeAt = 0f; netPause = null; }

        static string RoleHex(Role r) => r == Role.LeftPaddler ? "#2F6BFF" : r == Role.RightPaddler ? "#FFD23F" : "#4CC94C";

        void UpdatePauseCard()
        {
            if (pauseCard == null) return;
            float now = Time.realtimeSinceStartup;
            if (resumeAt > 0f && now >= resumeAt) ClearNetPause(); // 3-2-1 done: inputs flow again (world unfreezes on the next snapshot)
            bool game = screen == UiScreen.Game && results == null;
            bool reconnecting = game && !net.Ready;
            bool show = game && (netPaused || reconnecting);
            if (pauseCard.activeSelf != show) pauseCard.SetActive(show);
            if (!show) return;
            pauseBig.text = ""; pauseBody.text = "";
            pauseContinue.gameObject.SetActive(false);
            if (reconnecting)
            {
                pauseTitle.text = "RECONNECTING...";
                pauseBody.text = "Connection lost. The match waits for you.\n<size=30>" + NetworkInfo.Provider.NetworkType + "</size>";
            }
            else if (resumeAt > 0f)
            {
                pauseTitle.text = "GET READY";
                pauseBig.text = Mathf.CeilToInt(resumeAt - now).ToString();
            }
            else if (netPause != null)
            {
                pauseTitle.text = "WAITING FOR PLAYERS";
                sb.Clear();
                float el = now - netPauseAt;
                foreach (var m in netPause.missing)
                    sb.Append($"<color={RoleHex(m.role)}>{RoleInfo.Name(m.role)}</color>  <b>{m.name}</b>   <color=#FF7A1A>{Mathf.Max(0, Mathf.CeilToInt(m.graceS - el))} s</color>\n");
                if (lobby != null)
                {
                    sb.Append("<size=30>here: ");
                    foreach (var pl in lobby.players) if (pl.connected) sb.Append(pl.name).Append("  ");
                    sb.Append("</size>");
                }
                pauseBody.text = sb.ToString();
                pauseContinue.gameObject.SetActive(lobby != null && lobby.hostId != 0 && lobby.hostId == net.ClientId);
            }
        }

        RectTransform Row(Transform col, string mark, Color c)
        {
            var r = UIKit.Rect("row", col); UIKit.Size(r, 160); UIKit.HLayout(r, 14, 0).childForceExpandWidth = false;
            Label(r, mark, 64, c, 80);
            return r;
        }

        void Item(Transform row, byte code, string val, Color c, bool cross = false)
        {
            var cell = UIKit.Rect("it", row); UIKit.Size(cell, 150, 150); UIKit.VLayout(cell, 0, 0).childForceExpandWidth = false;
            var ic = IconImg(cell, code, 100);
            if (code == WasteCode((int)WasteType.Toxic)) ToxicRing(ic);
            if (cross) { var x = UIKit.Text(ic.transform, "X", 64, UIKit.Warn); x.raycastTarget = false; x.fontStyle = FontStyles.Bold; UIKit.Stretch(x.rectTransform); }
            Label(cell, val, 26, c, 150).outlineWidth = 0.2f;
        }

        bool V4Blocks(Vector2 pos)
        {
            if (v4Canvas == null || !v4Canvas.gameObject.activeSelf) return false;
            if (leaveMenu.activeSelf || leaveConfirm.activeSelf || pauseCard.activeSelf) return true;
            foreach (var g in new[] { helpBtn, menuBtn })
                if (g.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint((RectTransform)g.transform, pos, null)) return true;
            foreach (var g in new[] { roleCard, legendPop })
                if (g.activeSelf) foreach (var b in g.GetComponentsInChildren<Button>())
                    if (RectTransformUtility.RectangleContainsScreenPoint((RectTransform)b.transform, pos, null)) return true;
            return false;
        }

        void ShowRoleCard(Role r)
        {
            if (r == Role.None) return;
            cardRole = r;
            roleBadge.color = r == Role.LeftPaddler ? ArtV3.CrewBlue : r == Role.RightPaddler ? ArtV3.CrewYellow : ArtV3.CrewGreen;
            roleCardTitle.text = RoleInfo.Name(r);
            roleCardGesture.text = r == Role.Collector ? "TAP" : "v";
            roleCardEffect.text = r switch
            {
                Role.LeftPaddler => "^  >\n<size=36>L + R = BOOST</size>",
                Role.RightPaddler => "<  ^\n<size=36>L + R = BOOST</size>",
                _ => "TAP > NET\n<size=36>swipe = throw</size>"
            };
            v4Canvas.gameObject.SetActive(true);
            roleCard.SetActive(true);
        }

        void ToggleHelp()
        {
            bool on = !(roleCard.activeSelf || legendPop.activeSelf);
            if (on) { ShowRoleCard(myRole); legendPop.SetActive(false); }
            else { roleCard.SetActive(false); legendPop.SetActive(false); }
        }

        /// Lobby: full card the first time per role.
        void V4OnLobbyRole(Role r)
        {
            if (r == Role.None || screen != UiScreen.Lobby || r == cardRole) return;
            cardRole = r;
            string key = "seenCard" + (int)r;
            if (PlayerPrefs.GetInt(key, 0) == 1) return;
            PlayerPrefs.SetInt(key, 1);
            ShowRoleCard(r);
        }

        void V4OnMatchStart(bool resync)
        {
            roleCard.SetActive(false);
            SetLeaveMenu(false);
            if (resync) return; // reconnect into the same match: keep counts, no intro pop-up
            for (int i = 0; i < 7; i++) hudCounts[i] = 0;
            legendPop.SetActive(true);
        }

        void V4OnGo() => legendPop.SetActive(false);
        void V4OnCatch(WasteType t) { int i = (int)t; if (i >= 0 && i < 7) hudCounts[i]++; }

        void SetLeaveMenu(bool on) { if (leaveMenu == null) return; leaveMenu.SetActive(on); leaveConfirm.SetActive(false); }

        void LeaveMatch()
        {
            SetLeaveMenu(false);
            net.SendLeave();
            LeaveRoom();
        }

        void UpdateV4()
        {
            if (v4Canvas == null) return;
            bool game = screen == UiScreen.Game, lobbyS = screen == UiScreen.Lobby;
            if (!game && (legendPop.activeSelf || leaveMenu.activeSelf || leaveConfirm.activeSelf)) { legendPop.SetActive(false); SetLeaveMenu(false); }
            if (!game && !lobbyS) roleCard.SetActive(false);
            v4Canvas.gameObject.SetActive(game || (lobbyS && roleCard.activeSelf));
            UpdatePauseCard();
            helpBtn.SetActive(game); menuBtn.SetActive(game); legendBar.SetActive(game);
            // safe area (notches / punch holes) for every v4 widget
            var sa = Screen.safeArea; float k = 1080f / Mathf.Max(1, Screen.height);
            v4Safe.offsetMin = new Vector2(sa.xMin * k, sa.yMin * k);
            v4Safe.offsetMax = new Vector2(-(Screen.width - sa.xMax) * k, -(Screen.height - sa.yMax) * k);
            if (hudSafe != null) { hudSafe.offsetMin = v4Safe.offsetMin; hudSafe.offsetMax = v4Safe.offsetMax; }
            if (game) for (int i = 0; i < 7; i++) legendCounts[i].text = hudCounts[i].ToString();
            if (roleCard.activeSelf)
            {
                float t = Time.time % 1.2f / 1.2f;
                if (cardRole == Role.Collector) { roleHand.anchoredPosition = Vector2.zero; roleHand.localScale = Vector3.one * (1f + 0.4f * Mathf.Sin(t * Mathf.PI)); }
                else { roleHand.localScale = Vector3.one; roleHand.anchoredPosition = new Vector2(70, Mathf.Lerp(60, -60, Mathf.SmoothStep(0, 1, t))); }
            }
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (game && kb != null && kb.escapeKey.wasPressedThisFrame) SetLeaveMenu(!(leaveMenu.activeSelf || leaveConfirm.activeSelf));
        }
    }
}
