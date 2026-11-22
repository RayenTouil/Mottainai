# v4 Phase C: disconnection / reconnection

## Root cause (proven before any change: `node server/tools/reconnect.ts T0`)
- A phone that drops **silently** never fires `webSocketClose`. This happens with a half-open TCP connection, a screen lock, an app switch, or a 5G handover.
  - The server kept the player as "connected", so no bot took the role.
  - The sim kept running with that paddle idle.
  - Reproduced: the server still showed the player connected after 33 s, the raft hit an obstacle, GAME OVER (results reason 2).
  - By contrast, a clean close got a bot in 70 ms.
- The client had the same gap:
  - The WebSocket keep-alive runs only every 15 s.
  - `OnApplicationPause(false)` reconnected only if the socket *looked* closed, so it never reconnected a socket that died silently.

## Fix
- **Server (`room.ts`, sim untouched):**
  - No message for 1.5 s: the server closes the socket and counts it as a disconnect.
  - A human with a role disconnects mid-match: **PAUSE** (the sim is not stepped).
  - Wait up to 30 s for **all** missing players, then **RESUME** with a 3-2-1.
  - Grace expires: a bot takes that role.
  - The host can press **Continue with bot**.
  - Nobody left: the match is **Aborted** (no score).
  - LEAVE: a bot takes over immediately, no pause.
  - A reconnect into a room that lost its match (Durable Object restart): NOTICE, then back to the lobby.
  - Settings are in `server/src/room_config.json`.
- **Client:**
  - **PING:** every 500 ms from the socket thread, and only while the game loop is alive.
  - **Stall rule:** no data for 1.0 s in a match, so it reconnects immediately. Backoff in a match: 0 / 250 / 500 / 1000 / 2000 ms.
  - **App lifecycle:** the socket closes when the app goes to the background, and reconnects as soon as the app is back.
  - **Overlays:** "Waiting for players" (with names and seconds left), "GET READY 3-2-1", "Reconnecting…".
  - **Inputs and world:** inputs are blocked while paused. The world holds its frozen state, then re-syncs its clock on the first new snapshot, with no jumps.
  - **Same-match reconnect:** score, legend counts and time are kept, and there is no intro pop-up.
  - **Net HUD:** shows the reconnect count and the last reconnect time and cause.

## Automated results (v4 Worker, 2026-11-22)

| Test | Result |
|---|---|
| T1 hard close → PAUSE → back in 5 s → RESUME → continues | PASS: PAUSE after 40–60 ms, RESUME 150–180 ms after the reconnect, sim steps again 3.03 s later |
| T2 silent stall (socket open, nothing sent) | PASS: PAUSE after 1.23–1.39 s |
| T3 two drop, one returns | PASS: still paused, no GAME OVER, RESUME only when both are back |
| T4 grace expires | PASS: bot takes over at 30.0 s, then RESUME |
| T5 all humans gone | PASS: RESULTS = Aborted, no resume |
| T6 voluntary LEAVE | PASS: bot after 50–65 ms, no pause |
| T7 sim frozen during pause | PASS: match time identical, +1 tick at resume, raft moved < 0.11 m |
| T8 host Continue with bot | PASS: RESUME 40–60 ms later |
| T9 Durable Object restart | PASS: NOTICE "Match lost", lobby |
| Unity PC: teammate drops | PASS: overlay shown (`reconnect_pc_waiting.png`, `reconnect_pc_getready.png`), 60 fps |
| Unity PC: own app suspended 6 s | PASS: teammate paused at once, reconnected in 317 ms (`reconnect_pc_reconnecting.png`) |
| Phone: airplane + Wi-Fi off 11 s | PASS: PAUSE ≤ 1.5 s, RESUME about 4 s after the network returned, score and counts kept (`reconnect_airplane_*.png`) |
| Phone: screen off | PASS: PAUSE at once. The app returned with one reconnect of 458 ms, then the 3-2-1 |

Commands: `cd server && node tools/reconnect.ts` (T1–T9); `bash tools/phone_reconnect_test.sh airplane|lock|wifi2data` (1 phone plus a Node spectator).

Note: Samsung keeps Wi-Fi on in aeroplane mode. For a real cut, turn Wi-Fi off too.

## 3-phone guided test (to fill in)
Install the v4 APK on all 3 phones (`install_phone.bat`). Use one room; each phone takes a role; everyone presses READY; the host starts.

| # | Do this, about 20 s into the match | Expected | Result |
|---|---|---|---|
| P1 | Phone B: aeroplane mode ON **and** Wi-Fi OFF for 10 s, then both back | A and C see "Waiting for B… 2x s" within about 1.5 s. B sees "Reconnecting…". Then 3-2-1, the match continues with the same score and time | |
| P2 | Phones B **and** C: aeroplane mode for 10 s. C comes back first, B 5 s later | Still waiting after C returns (only B listed), no GAME OVER; 3-2-1 once B is back | |
| P3 | Phone B: lock the screen for 20 s, then unlock | Pause the moment the screen locks; on unlock "Reconnecting…", then 3-2-1 | |
| P4 | Phone B on Wi-Fi with mobile data ON: switch Wi-Fi OFF | Short pause (≤ 3 s), the match continues on 5G (the Net pill shows 5G SA/NSA) | |
| P5 | Phone B: aeroplane mode for 40 s | After 30 s a bot takes B's role, 3-2-1, and when B returns it takes its role back | |
| P6 | Host: during a pause press CONTINUE WITH BOT | Bot plus 3-2-1 straight away | |
