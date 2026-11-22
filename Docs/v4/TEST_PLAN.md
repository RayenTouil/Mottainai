# v4 integration test plan (Phase F)

Branch `feature/v4`, server https://mottainai-v4.mottainai-server.workers.dev. Install with `install_phone.bat` on all phones.

## Automated (run on 2026-11-22)
| Check | Command | Result |
|---|---|---|
| Reconnect T1–T9 | `cd server && node tools/reconnect.ts` | PASS |
| READY/START rules | `node tools/lobbytest.ts` | PASS |
| Teams + leaderboard (API + matches) | `node tools/teamtest.ts` | PASS |
| Phone full match (R5CY30BPV8B, Wi-Fi, Collector, autoplay) | `bash tools/phone_art_test.sh v4_phaseF` | TimeUp, 59.9 fps average; **one hitch, worst frame 333 ms** (previous build: 33 ms) — open |
| Voice loopback (phone) | `-autoplay -voiceloop` | own socket, Nagle off, 0% loss, mouth-to-ear 177 ms average / 188 ms worst 5% (target < 150) |
| Voice loopback (PC) | same | 159 / 182 ms |
| Reconnect on the phone | `tools/phone_reconnect_test.sh airplane / lock` | PASS (Phase C) |

## Manual on 3 phones (fill in)
| # | Step | Expected | Result |
|---|---|---|---|
| F1 | Start the app on each phone | Role card the first time; collect/avoid pop-up at the countdown; legend bar | |
| F2 | Non-host presses START; host presses START before everyone is READY | Refused ("Only the host…", "Waiting for: …"); starts once all are READY | |
| F3 | Pause button → LEAVE MATCH | Back to the menu; a bot takes over, no pause for the others | |
| F4 | Reconnect checklist P1–P6 | See `reconnect.md` | |
| F5 | CREATE TEAM WITH THIS ROOM, others accept | Lobby banner shows TEAM; after the match "+X points to Team … rank #N"; LEADERBOARD updated | |
| F6 | Guest match → SAVE THIS CREW AS A TEAM | Counted once | |
| F7 | MIC ON on 2 phones, talk during a match (Wi-Fi off, 5G SA) | Clear audio, no echo at 1 m, 60 fps, 60 snapshots/s | |
| F8 | MIC OFF | Android mic indicator off | |
| F9 | Deny the mic permission | Toast, mic stays OFF, game OK | |
| F10 | One run on Wi-Fi, one on 5G SA | Net pill shows the network type; RTT no worse than on `main` | |

## Known gaps
- No lobby mic button, per-teammate mute, voice sliders, speaking indicator or Net HUD voice section yet. The Network Lab loopback runs from the command line only.
- One 333 ms frame hitch in the phone match (new in this build, not investigated).
