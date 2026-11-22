# HANDOFF — single folder `greenhack/Mottainai`, branch `main` = current version (no worktree any more)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
## Done
- main = art v3 visuals (ART_DIRECTION_v3.md: yellow raft, bean crew, turquoise water v3, Net pill top-center, Fredoka UI) + all dev fixes.
- dev fixes kept: IL2CPP crash-on-PLAY guard (WsClient DisableNagle, link.xml PhysicsModule), TCP_NODELAY (Mono/editor), socket-thread RTT, raft prediction + event-acked strokes, adaptive jitter buffer, 60 FPS pacing lock, MSAA<=2, drift-free DO tick loop (server/src/room.ts), jitter probe.
- dev v2 art dropped (v3 wins): ParkKit/SceneLook removed, Toon/WaterFx/Sky/Water/UI = v3. Unused v2 models stay in Resources.
- Merge check (2026-11-22): compile OK; Windows autoplay 60 fps, nodelay on; phone R5CY30BPV8B full match TimeUp, 60.1 fps avg, worst 33.2 ms, no crash (Docs/art_v3/merge_phone.png).
- Backups: tags backup/before-merge-{main,artv3,dev}; branches art/v3, dev, wip/* kept.
## In progress
- v4 Phase D done: TeamsDO (teams, leaderboard, server-authoritative scores, once per match), lobby banner + invites, save crew on results, TEAM + LEADERBOARD screens (tools/teamtest.ts PASS on mottainai-v4).
- v4 Phase C done: pause/resume on disconnect, 1.5 s dead-socket rule, 30 s grace, Continue with bot, abort when all gone, DO-restart notice (server/tools/reconnect.ts T1-T9 PASS on mottainai-v4; phone: airplane + screen-off PASS). Report: Docs/v4/reconnect.md. Pending: 3-phone guided tests (same doc).
- v4 Phase A+B (feature/v4, server mottainai-v4): onboarding cards, collect/avoid pop-up, legend bar, safe-area score, pause/LEAVE; host + READY/START rules (tools/lobbytest.ts 18/18).
- I1 (5G SA, Wi-Fi OFF, 3 full matches in a row) never formally run. A4 (teammate readability) open.
## Exact next step
- Run the 3-phone checklist in Docs/v4/reconnect.md, then Phase E (voice, Docs/prompts/VOICE.md).
## Resume commands
- `git pull --rebase --autostash` ; close editor -> `build_android.bat` -> `install_phone.bat`
- `bash tools/compile_check.sh` (works with editor open) ; phone autoplay: `bash tools/phone_art_test.sh <name>`
- v4 server tests: `cd server && node tools/reconnect.ts` (T1-T9, 45 s) ; `node tools/lobbytest.ts` ; phone: `bash tools/phone_reconnect_test.sh airplane|lock|wifi2data`
- Windows check: BuildTools.BuildWindows, then `Builds/Win/Mottainai.exe -autoplay -room AV3X -shots <abs dir> -quitafter 27`
