using System;
using System.Collections;
using System.Text;
using Mottainai.Gameplay;
using Mottainai.Net;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Mottainai.Client
{
    [Serializable] public class TeamMemberDto { public string name; public bool me; }
    [Serializable] public class TeamDto { public int id; public string name; public string joinCode; public int totalScore, matches, bestScore, rank; public TeamMemberDto[] members; }
    [Serializable] public class TeamResp { public bool ok; public string error; public TeamDto team; }
    [Serializable] public class BoardRowDto { public int rank; public string name; public int totalScore, matches, bestScore; public string members; public bool me; }
    [Serializable] public class BoardResp { public bool ok; public string error; public BoardRowDto[] rows; public BoardRowDto me; }

    /// v4 Phase D: teams + leaderboard UI (TEAM / LEADERBOARD screens, lobby banner, invite + name dialogs, results line).
    /// Scores are never sent by the client: the room reports results to the TeamsDO.
    public partial class ClientApp
    {
        Canvas teamCanvas;
        GameObject nameDialog, inviteDialog, bannerCreate, bannerGuests, resultsSave;
        TMP_InputField nameDialogInput, teamNameInput, teamCodeInput;
        TextMeshProUGUI inviteText, bannerText, resultsTeamText, teamInfoText, teamStatus, boardText;
        GameObject teamNoTeamRow, teamLeaveBtn;
        TeamResultInfo lastTeamResult;
        bool guestsChosen, leaveArmed;
        TeamDto myTeam;

        static bool HasTeam(TeamDto t) => t != null && !string.IsNullOrEmpty(t.name);

        void BuildTeamUi()
        {
            net.OnTeamResult += r => { lastTeamResult = r; RefreshResultsTeam(); };
            net.OnTeamInvite += ShowInvite;

            // ---- menu buttons
            var menuCol = panels[UiScreen.Menu].transform.Find("Column");
            var row = UIKit.Rect("TeamRow", menuCol);
            UIKit.HLayout(row, 16, 0); UIKit.Size(row, 90);
            row.SetSiblingIndex(Mathf.Max(0, menuCol.childCount - 2));
            UIKit.Button(row, "TEAM", UIKit.Accent2, () => { Show(UiScreen.Team); RefreshTeamScreen(); }, 34);
            UIKit.Button(row, "LEADERBOARD", UIKit.Accent2, () => { Show(UiScreen.Leaderboard); RefreshBoard(); }, 34);

            // ---- TEAM screen
            var tp = Panel(UiScreen.Team, new Color(0.03f, 0.12f, 0.16f, 0.85f));
            var tc = Column(tp.transform, 1200, 900, 0);
            UIKit.Size(UIKit.Text(tc, "MY TEAM", 64, Color.white), 90).fontStyle = FontStyles.Bold;
            teamInfoText = UIKit.Size(UIKit.Text(tc, "", 38, Color.white), 330);
            teamInfoText.richText = true;
            var nt = UIKit.Rect("NoTeam", tc); UIKit.Size(nt, 210); UIKit.VLayout(nt, 14, 0);
            var r1 = UIKit.Rect("Create", nt); UIKit.Size(r1, 96); UIKit.HLayout(r1, 16, 0);
            teamNameInput = UIKit.Input(r1, "Team name (3-16)", "", 40); teamNameInput.characterLimit = 16;
            UIKit.Button(r1, "CREATE", UIKit.Accent, () => StartCoroutine(TeamCall("/api/team/create", $"{{\"token\":\"{token}\",\"playerName\":{Json(playerName)},\"name\":{Json(teamNameInput.text)}}}")), 38);
            var r2 = UIKit.Rect("Join", nt); UIKit.Size(r2, 96); UIKit.HLayout(r2, 16, 0);
            teamCodeInput = UIKit.Input(r2, "Join code", "", 40); teamCodeInput.characterLimit = 6;
            teamCodeInput.onValueChanged.AddListener(v => { var up = v.ToUpperInvariant(); if (up != v) teamCodeInput.text = up; });
            UIKit.Button(r2, "JOIN", UIKit.Accent2, () => StartCoroutine(TeamCall("/api/team/join", $"{{\"token\":\"{token}\",\"playerName\":{Json(playerName)},\"joinCode\":{Json(teamCodeInput.text)}}}")), 38);
            teamNoTeamRow = nt.gameObject;
            var r3 = UIKit.Rect("Actions", tc); UIKit.Size(r3, 100); UIKit.HLayout(r3, 16, 0);
            var lb = UIKit.Button(r3, "LEAVE TEAM", UIKit.Warn, OnLeaveTeam, 36);
            teamLeaveBtn = lb.gameObject;
            UIKit.Button(r3, "BACK", UIKit.Muted, () => { Show(UiScreen.Menu); RefreshMenuUi(); }, 36);
            teamStatus = UIKit.Size(UIKit.Text(tc, "", 32, new Color(1f, 0.9f, 0.6f)), 60);

            // ---- LEADERBOARD screen
            var bp = Panel(UiScreen.Leaderboard, new Color(0.03f, 0.12f, 0.16f, 0.85f));
            var bc = Column(bp.transform, 1500, 960, 0);
            UIKit.Size(UIKit.Text(bc, "LEADERBOARD", 64, Color.white), 90).fontStyle = FontStyles.Bold;
            boardText = UIKit.Size(UIKit.Text(bc, "", 34, Color.white, TextAlignmentOptions.TopLeft), 700);
            boardText.richText = true;
            var r4 = UIKit.Rect("Actions", bc); UIKit.Size(r4, 100); UIKit.HLayout(r4, 16, 0);
            UIKit.Button(r4, "REFRESH", UIKit.Accent2, RefreshBoard, 36);
            UIKit.Button(r4, "BACK", UIKit.Muted, () => { Show(UiScreen.Menu); RefreshMenuUi(); }, 36);

            // ---- lobby banner
            var lp = panels[UiScreen.Lobby].transform;
            bannerText = UIKit.Text(lp, "", 34, Color.white, TextAlignmentOptions.Left);
            bannerText.richText = true;
            UIKit.Place(bannerText.rectTransform, new Vector2(0, 1), new Vector2(0, 1), new Vector2(40, -82), new Vector2(1000, 60));
            var bcb = UIKit.Button(lp, "CREATE TEAM WITH THIS ROOM", UIKit.Accent, OnBannerCreate, 30);
            UIKit.Place((RectTransform)bcb.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-380, -76), new Vector2(620, 72));
            bannerCreate = bcb.gameObject;
            var bgb = UIKit.Button(lp, "PLAY AS GUESTS", UIKit.Muted, () => { guestsChosen = true; RefreshTeamBanner(); }, 30);
            UIKit.Place((RectTransform)bgb.transform, new Vector2(1, 1), new Vector2(1, 1), new Vector2(-30, -76), new Vector2(330, 72));
            bannerGuests = bgb.gameObject;

            // ---- results line + save crew
            var rp = panels[UiScreen.Results].transform;
            resultsTeamText = UIKit.Text(rp, "", 38, Color.white);
            resultsTeamText.richText = true;
            UIKit.Place(resultsTeamText.rectTransform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(-250, 150), new Vector2(1100, 70));
            var sb2 = UIKit.Button(rp, "SAVE THIS CREW AS A TEAM", UIKit.Accent, () => OpenNameDialog(), 30);
            UIKit.Place((RectTransform)sb2.transform, new Vector2(0.5f, 0), new Vector2(0.5f, 0), new Vector2(520, 150), new Vector2(560, 76));
            resultsSave = sb2.gameObject;

            // ---- dialogs (own canvas above the menus)
            teamCanvas = UIKit.Canvas("UI_Team", 12);
            nameDialog = Dialog("NameDialog", out var nd);
            UIKit.Size(UIKit.Text(nd, "Name your team", 52, Color.white), 80).fontStyle = FontStyles.Bold;
            nameDialogInput = UIKit.Input(nd, "3-16 letters, digits, spaces", "", 44);
            nameDialogInput.characterLimit = 16;
            UIKit.Size(nameDialogInput, 100);
            var nr = UIKit.Rect("Buttons", nd); UIKit.Size(nr, 100); UIKit.HLayout(nr, 20, 0);
            UIKit.Button(nr, "CREATE", UIKit.Accent, () => { net.SendTeamCreate(nameDialogInput.text); nameDialog.SetActive(false); }, 40);
            UIKit.Button(nr, "CANCEL", UIKit.Muted, () => nameDialog.SetActive(false), 40);
            inviteDialog = Dialog("InviteDialog", out var idl);
            inviteText = UIKit.Size(UIKit.Text(idl, "", 42, Color.white), 200);
            inviteText.richText = true;
            var ir = UIKit.Rect("Buttons", idl); UIKit.Size(ir, 100); UIKit.HLayout(ir, 20, 0);
            UIKit.Button(ir, "JOIN TEAM", UIKit.Accent, () => { net.SendTeamAnswer(true); inviteDialog.SetActive(false); }, 40);
            UIKit.Button(ir, "NO THANKS", UIKit.Muted, () => { net.SendTeamAnswer(false); inviteDialog.SetActive(false); }, 40);
            nameDialog.SetActive(false); inviteDialog.SetActive(false);
        }

        GameObject Dialog(string name, out RectTransform col)
        {
            var dim = UIKit.Image(teamCanvas.transform, new Color(0, 0, 0, 0.6f), null, name);
            UIKit.Stretch(dim.rectTransform);
            var card = UIKit.Image(dim.transform, UIKit.Dark, UIKit.Rounded);
            card.type = Image.Type.Sliced;
            UIKit.Place(card.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0, 80), new Vector2(1100, 460));
            col = UIKit.Rect("Col", card.transform);
            UIKit.Stretch(col, 50, 40, 50, 40);
            UIKit.VLayout(col, 20, 0, TextAnchor.MiddleCenter);
            return dim.gameObject;
        }

        static string Json(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s ?? "") { if (c == '"' || c == '\\') b.Append('\\'); if (c >= ' ') b.Append(c); }
            return b.Append('"').ToString();
        }

        // ---------------- lobby ----------------
        bool IsHost => lobby != null && lobby.hostId != 0 && lobby.hostId == net.ClientId;
        LobbyPlayer Me => lobby?.players.Find(x => x.clientId == net.ClientId);

        void RefreshTeamBanner()
        {
            if (bannerText == null || lobby == null) return;
            string team = null; bool all = true; int humans = 0;
            foreach (var p in lobby.players)
            {
                if (!p.connected || p.role == Role.None) continue;
                humans++;
                if (string.IsNullOrEmpty(p.team)) { all = false; continue; }
                if (team == null) team = p.team; else if (team != p.team) all = false;
            }
            bool teamRoom = humans > 0 && all && team != null;
            bannerText.text = teamRoom ? $"<color=#4CC94C><b>TEAM {team}</b></color>  - this match counts for the leaderboard"
                                       : "<color=#FFD23F><b>Playing as guests</b></color>  - score not saved";
            bool offer = !teamRoom && !guestsChosen && IsHost && lobby.phase != MatchPhase.Running && lobby.phase != MatchPhase.Countdown;
            bannerCreate.SetActive(offer); bannerGuests.SetActive(offer);
            var me = Me;
            UIKit.Label(bannerCreate.GetComponent<Button>()).text = me != null && !string.IsNullOrEmpty(me.team) ? $"INVITE TO TEAM {me.team}" : "CREATE TEAM WITH THIS ROOM";
        }

        void OnBannerCreate()
        {
            var me = Me;
            if (me != null && !string.IsNullOrEmpty(me.team)) net.SendTeamCreate(""); // invite the room to my team
            else OpenNameDialog();
        }

        void OpenNameDialog() { nameDialogInput.text = ""; nameDialog.SetActive(true); }

        void ShowInvite(TeamInviteInfo i)
        {
            inviteText.text = $"<b>{i.fromName}</b> invites you to team\n<size=56><color=#4CC94C><b>{i.teamName}</b></color></size>" +
                              (string.IsNullOrEmpty(i.currentTeam) ? "" : $"\n<size=30><color=#FF7A1A>You will leave team {i.currentTeam}</color></size>");
            inviteDialog.SetActive(true);
        }

        // ---------------- results ----------------
        void RefreshResultsTeam()
        {
            if (resultsTeamText == null) return;
            var r = lastTeamResult;
            if (r == null) { resultsTeamText.text = ""; resultsSave.SetActive(false); return; }
            resultsTeamText.text = r.counted
                ? $"<color=#4CC94C><b>+{r.points} points to Team {r.teamName}</b></color>  -  rank <b>#{r.rank}</b>"
                : "<color=#FFD23F>Guest match - not on the leaderboard</color>";
            resultsSave.SetActive(!r.counted && r.canSave && IsHost);
        }

        // ---------------- TEAM screen ----------------
        IEnumerator TeamCall(string path, string body)
        {
            teamStatus.text = "...";
            long code = 0; string text = "";
            yield return content.Api(path, body, (c, t) => { code = c; text = t; });
            TeamResp r = null;
            try { r = JsonUtility.FromJson<TeamResp>(text); } catch { }
            if (code != 200 || r == null || !r.ok) { teamStatus.text = r?.error ?? $"Network error ({code})"; yield break; }
            myTeam = HasTeam(r.team) ? r.team : null;
            teamStatus.text = "";
            leaveArmed = false;
            FillTeamScreen();
        }

        void RefreshTeamScreen() { leaveArmed = false; StartCoroutine(TeamCall("/api/team/me?token=" + Uri.EscapeDataString(token ?? ""), null)); }

        void FillTeamScreen()
        {
            bool has = HasTeam(myTeam);
            teamNoTeamRow.SetActive(!has);
            teamLeaveBtn.SetActive(has);
            UIKit.Label(teamLeaveBtn.GetComponent<Button>()).text = "LEAVE TEAM";
            if (!has) { teamInfoText.text = "You play as a <b>guest</b>.\nCreate a team or join one with its code (max 3 players)."; return; }
            var sb = new StringBuilder();
            sb.Append($"<size=60><color=#4CC94C><b>{myTeam.name}</b></color></size>\n");
            sb.Append($"Join code  <size=56><b>{myTeam.joinCode}</b></size>\n");
            sb.Append("Members: ");
            if (myTeam.members != null) for (int i = 0; i < myTeam.members.Length; i++) sb.Append(i > 0 ? ", " : "").Append(myTeam.members[i].me ? $"<b>{myTeam.members[i].name}</b>" : myTeam.members[i].name);
            sb.Append($"  ({myTeam.members?.Length ?? 0}/3)\n");
            sb.Append($"Total <b>{myTeam.totalScore}</b>   matches {myTeam.matches}   best {myTeam.bestScore}   rank <b>#{myTeam.rank}</b>");
            teamInfoText.text = sb.ToString();
        }

        void OnLeaveTeam()
        {
            if (!leaveArmed) { leaveArmed = true; UIKit.Label(teamLeaveBtn.GetComponent<Button>()).text = "TAP AGAIN TO LEAVE"; return; }
            StartCoroutine(TeamCall("/api/team/leave", $"{{\"token\":\"{token}\"}}"));
        }

        // ---------------- LEADERBOARD screen ----------------
        void RefreshBoard() => StartCoroutine(LoadBoard());

        IEnumerator LoadBoard()
        {
            boardText.text = "Loading...";
            long code = 0; string text = "";
            yield return content.Api("/api/leaderboard?limit=20&token=" + Uri.EscapeDataString(token ?? ""), null, (c, t) => { code = c; text = t; });
            BoardResp r = null;
            try { r = JsonUtility.FromJson<BoardResp>(text); } catch { }
            if (code != 200 || r == null || r.rows == null) { boardText.text = $"Network error ({code})"; yield break; }
            var sb = new StringBuilder("<color=#9AA><size=28>#<pos=7%>TEAM<pos=55%>TOTAL<pos=70%>MATCHES<pos=86%>BEST</size></color>\n");
            foreach (var x in r.rows) Row(sb, x);
            if (r.rows.Length == 0) sb.Append("No team yet - be the first!\n");
            bool meListed = Array.Exists(r.rows, x => x.me);
            if (!meListed && r.me != null && !string.IsNullOrEmpty(r.me.name)) { sb.Append("<color=#9AA>...</color>\n"); Row(sb, r.me); }
            boardText.text = sb.ToString();
        }

        static void Row(StringBuilder sb, BoardRowDto x)
        {
            string c = x.me ? "<color=#4CC94C><b>" : "", e = x.me ? "</b></color>" : "";
            sb.Append($"{c}{x.rank}<pos=7%>{x.name}<pos=55%>{x.totalScore}<pos=70%>{x.matches}<pos=86%>{x.bestScore}{e}\n");
        }
    }
}
