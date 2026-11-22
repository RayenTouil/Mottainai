# MOTTAINAI v4 — playtest fixes, teams, leaderboard, voice (spec for Claude Code)

Source: playtest on 3 phones, 2026-11-22. Repo = `greenhack/Mottainai` (single folder; art v3 + dev are already merged in `main`, the `Mottainai_v3` worktree is gone).

## 0. Rules for this update (override CLAUDE.md where they conflict)
- **Branch:** create `feature/v4` from the current `main` and do everything there. Run `git push -u origin feature/v4` immediately, so the Stop hook's auto-push goes to this branch. **Never merge into `main`**: the team will test and confirm first. Ignore the old local `feature/voice` branch (stale).
- **Separate server:** the deployed Worker `mottainai` serves the current `main` APK used for the demo, so do not break it. Add a wrangler environment `v4`, deployed as its own Worker (e.g. `mottainai-v4`) with its own Durable Objects. Point this branch's `server_config.json` to the v4 URL, and make `deploy_server.bat v4` deploy it. Bump `PROTOCOL_VERSION` to 3.
- **Do NOT touch the game engine:** `sim.ts` gameplay rules, `MatchSim`/`RaftSim`/`CourseGenerator`/`TeamModel`/`BotBrain`, physics, collisions, scoring rules, `tuning.json` values.
- **Do NOT touch graphical objects:** models, shaders, materials, `VisualFactory` meshes, `Scenery`, `WaterFx`, `PostFx`, `ArtV3` tokens, camera.
- **Allowed:** UI overlays/screens (`ClientScreens`, `UIKit`, `ClientApp`), networking/orchestration (`room.ts`, `index.ts`, `protocol.ts`, `WsClient`, `NetClient`, `Codecs`), new Durable Objects, new docs.
- New icons must be made from objects already in the game (render the existing waste/obstacle/power-up visuals to small RenderTextures or sprites at startup) plus the existing category colors from `WasteCatalog.Colors`. No new art. Respect ART_DIRECTION_v3: **red only for toxic**, so "avoid" marks use `hazard.orange #FF7A1A`, never red.
- Log each decision in DECISIONS.md (one line). Update PROTOCOL.md, HANDOFF.md, DEPLOY.md at the end of every phase.
- **Effort tags:** `[FAST]` = keep it simple, no deep analysis, finish quickly. `[MAX]` = think hard: find root causes, write tests first, verify on devices, consider edge cases. Each phase below has its tag.
- After each phase reply only: files changed, commands run, test results, open issues. Then stop and wait for the next phase.

---

## Phase A [FAST] — onboarding, HUD legend, score, leave button
A1. **Role instructions (icon-based, almost no text).** Read `GestureInput.cs` and `RoleInfo.Hint` and use the REAL gestures (per CLAUDE.md: paddlers swipe DOWN = stroke, swipe UP = back-paddle, both paddlers within 0.25 s = SYNC boost; collector TAP a waste = throw the net, swipe = throw in a direction).
  - One card per role: role icon + an animated hand (looping swipe/tap arrow) on a phone silhouette + a small raft arrow showing the effect (left stroke → raft forward + turns right, etc.).
  - Shown in the lobby when a role is assigned (full card, "OK" button), and again as a compact strip during the 3 s countdown.
  - A small "?" button in the HUD reopens the card for my role. The game keeps running (multiplayer); the overlay must not block gestures except its own button.
  - Show the full card automatically the first time per role (PlayerPrefs). The countdown strip is shown every match.
A2. **"Collect / Avoid" pop-up at match start** (lobby → countdown), with colored icons and numbers, no sentences:
  - ✓ COLLECT row: Plastic +10, Paper +10, Glass +15, Organic +5, Metal +20, Valuable +100 (take the values from `GameTuning.Current.wasteScores`, never hard-code).
  - ✗ AVOID row: Toxic −30 (red ring, the only red); obstacles Rock, Big Rock, Iceberg, Old Boat, Log, Shark with an orange ✗ = GAME OVER (skull/crash icon).
  - ★ POWER-UPS row: Shield, Magnet, Turbo icons + duration.
  - Auto-dismiss when GO! appears; reopenable from the "?" button.
A3. **Bottom legend bar during the match:** 7 waste icons, each with its score and a live collected count badge. It replaces the current bottom-center category pills. `raycastTarget = false` so the collector's taps go through. It must not overlap the NET action button (bottom-right).
A4. **Total team score top-left:** the v3 score panel already exists (`ScorePanel` / `scoreText`). Check why testers didn't see it (safe area, aspect ratio, overlap, size) on all 3 phone aspect ratios, and make the total clearly readable. Timer stays top-right.
A5. **Leave button:** a menu button next to the timer (top-right) opens an overlay with RESUME / LEAVE MATCH (+ confirm). LEAVE sends a new C2S `LEAVE (0x04)` (voluntary: the server gives the role to a bot immediately, no waiting; see Phase C), closes the room socket and returns to the main menu. Android Back button = same overlay.
Test: Windows autoplay screenshots + 1 phone; FPS unchanged (≥ 55).

## Phase B [high effort] — READY / START rules (server-authoritative)
Bug: the first player to press START starts the match even if others are not ready (server `case C2S.START: this.startMatch()` has no check; the lobby text even says "Anyone can press START").
- **Host** = first human in the room. If the host leaves, the next connected human becomes host. Send the host id in LOBBY (protocol v3).
- The server accepts START (and REMATCH) only from the host, and only when **every connected human is READY**. Otherwise it sends a NOTICE ("Waiting for: Sami, Lina"). Bots need no ready. A single human can ready + start alone.
- READY resets to false for everyone when a match starts and when it ends (back in the lobby). REMATCH = back to the lobby, everyone presses READY again.
- Client: START visible only to the host, disabled until all are ready, with "2/3 ready". Non-hosts see "Waiting for host". The READY button toggles visually (fix the label/colour logic) and its state comes from the server's LOBBY, not local state. Replace the "Anyone can press START" text.
- Players can't change role after pressing READY (unready first).
Test: extend `server/tools/fakeclient.ts` with 3 clients: START from non-host rejected, START with one unready rejected, all ready → starts, rematch requires ready again.

## Phase C [MAX] — disconnection / reconnection (the big one)
Observed: one phone dropped mid-match. When I reconnected but my friend did not, instead of waiting for all players the game said GAME OVER: hit an obstacle.
**Likely root cause (verify it, don't assume):** the server only detects a disconnect through `webSocketClose`/`webSocketError` (`room.ts` `drop()`). On mobile (5G handover, screen lock, app in background, Wi-Fi ↔ data switch) the TCP connection often dies **silently** (half-open), so for many seconds the server still thinks the player is connected. No bot takes over, nobody paddles, the raft drifts into an obstacle → GAME OVER. The client side has the same problem: `ClientWebSocket` KeepAlive is 15 s.

Required behaviour:
1. **Fast dead-connection detection.**
   - Server: a player is considered disconnected if no message (PING every 500 ms) has arrived for 1.5 s; close that socket.
   - Client: if no snapshot or PONG arrives for 1.0 s during a match, show "Reconnecting…" and force a reconnect, without waiting for the TCP timeout.
   - On `OnApplicationPause(false)`/focus regain: reconnect immediately.
   - Reconnect backoff starts at 0 ms, then 250 ms…, max 2 s during a match.
2. **Pause, don't crash.** When a human who is part of the running match disconnects involuntarily, the server **freezes the match**: it stops stepping the sim. This happens in `room.ts` only, the sim code stays untouched, and no time, obstacle or collision advances. It broadcasts a new S2C `PAUSE (0x8A)` {reason, missing playerIds+names, graceLeft s}. Every client shows an overlay "Waiting for Sami… 25 s" (+ icons of connected/missing players).
3. **Grace period 30 s** (tuning value `reconnectGraceS`, in a new server config, not the game tuning):
   - The missing player comes back with the same token → MATCH_START + fresh snapshot for them. Then **everyone** gets a 3-2-1 resume countdown (S2C `RESUME (0x8B)` {countdownMs}) before the sim steps again. Inputs during pause/countdown are ignored.
   - If several players are missing, wait until **all** of them are back (or their grace expires). A player who returned must never see GAME OVER because someone else is still missing.
   - Grace expires → a bot takes over the missing role, then the same 3-2-1 resume. The host may also press "Continue with bot" in the overlay to skip the wait.
   - All humans gone and grace expired → abort the match (EndReason Aborted): no game over, no score recorded.
4. **Voluntary LEAVE (0x04):** no pause; the bot takes over immediately.
5. **State consistency on reconnect:** the client resets interpolation/prediction buffers (`WorldView` clear path) so there are no jumps, ghost entities or stale raft prediction. Events missed during the gap don't replay old Big texts. The HUD shows the correct score/time.
6. **DO restart safety:** if the Durable Object restarts (redeploy, eviction) the sim is lost. Detect that on reconnect (room has players restored from attachments but no sim while clients think they're in a match) and send them cleanly to the lobby with a NOTICE, instead of an inconsistent state.
7. Telemetry: count pauses, reconnect times and causes; show the last reconnect duration in the Net HUD debug panel.

Tests (write them first, in `server/tools/`, runnable with `node`):
- T1 hard close mid-match → PAUSE → reconnect in 5 s → RESUME countdown → match continues, same score/time.
- T2 **silent stall** (client stops sending and reading but keeps the socket open) → detected in ≤ 1.5 s → PAUSE.
- T3 two players drop, one returns → still paused, waiting for the other; no GAME OVER.
- T4 grace expires → bot takes the role → resume.
- T5 all humans gone → Aborted, no score.
- T6 voluntary LEAVE → immediate bot, no pause.
- T7 the sim time and raft position are identical before PAUSE and after RESUME (the sim did not advance).
- Phone test: 3 phones in a match; toggle airplane mode on one phone for 10 s, then on two phones; lock one screen for 20 s; switch Wi-Fi → 5G mid-match. Record the results in `Docs/v4/reconnect.md`.

## Phase D [high effort] — teams + leaderboard
Identity: the device's stable token (PlayerPrefs, already used for reconnect) is the player account. No password. Default = **guest**.
- New SQLite Durable Object `TeamsDO` (global instance, same pattern as `TelemetryDO`):
  - `players(token, name, teamId)`
  - `teams(id, name UNIQUE NOCASE, joinCode, createdAt, totalScore, matches, bestScore)`
  - `members(teamId, token)`
  - `matches(id, teamId, score, ts, roomCode)`
  - A team has **1 to 3 members** (the demo often has 2 phones + 1 bot). A player is in at most one team.
- HTTP API (JSON): `POST /api/team/create {token, name}` · `POST /api/team/join {token, joinCode}` · `POST /api/team/leave {token}` · `GET /api/team/me?token=` · `GET /api/leaderboard?limit=20&token=` (top teams by totalScore + my team's rank). Name: 3–16 chars, letters/digits/spaces, unique (case-insensitive), trimmed.
- **Scores are server-authoritative:** at match end, `RoomDO` (not the client) sends the result to `TeamsDO`. A match counts for a team when **all humans in the room belong to that same team** (bots may fill missing roles). Aborted matches never count. Team score = sum of match totals; also keep match count and best score.
- **Lobby flow:** the LOBBY message carries each player's team. If all humans are in the same team → banner "Team <name>" (score will count). Otherwise → banner "Playing as guests" + buttons [CREATE TEAM WITH THIS ROOM] [PLAY AS GUESTS].
  - CREATE TEAM: the host enters a name; the other humans get an accept/decline pop-up; those who accept join. Players already in another team are asked to leave it first.
- **After a guest match:** the results screen offers "Save this crew as a team?" (same flow). The just-played score counts as the team's first match: the server keeps the last result of that room for 10 min.
- **Main menu:** new TEAM screen (my team, members, join code, create / join with code / leave) and LEADERBOARD screen (rank, team name, total, matches, best; my team highlighted). Results screen: "+X points to Team Y · rank #N".
- Tests: fakeclient scenarios for create/join/leave, uniqueness, 4th member rejected, guest match not counted, team match counted once (no double count after reconnect/rematch), aborted not counted.

## Phase E [MAX] — voice chat per team
Follow `Docs/prompts/VOICE.md` exactly. Voice goes only to the humans of the same room team. A mic ON/OFF button on the HUD + in the lobby (default OFF).

## Phase F [high effort] — integration on 3 phones
- `build_android.bat` → `install_phone.bat` on the 3 phones. Mobile data on 5G SA where possible, plus one run on Wi-Fi.
- Scripted checklist in `Docs/v4/TEST_PLAN.md`, results + screenshots in `Docs/v4/`:
  - onboarding shown; legend correct;
  - leave works;
  - READY/START rules;
  - all reconnect phone tests from Phase C;
  - team create/join, leaderboard updates after a team match, guest not counted;
  - voice V1–V10;
  - 60 FPS, snapshot 60 Hz, RTT unchanged vs `main`.
- Update HANDOFF.md: what's done, what's left, how to deploy v4, **how the team can merge `feature/v4` into `main` after confirmation** (merge + switch `server_config.json` back to the main Worker URL + `deploy_server.bat`).
