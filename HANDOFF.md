# HANDOFF — single folder `greenhack/Mottainai`, branch `main` = current version (no worktree any more)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
## Done
- main = art v3 visuals (ART_DIRECTION_v3.md: yellow raft, bean crew, turquoise water v3, Net pill top-center, Fredoka UI) + all dev fixes.
- dev fixes kept: IL2CPP crash-on-PLAY guard (WsClient DisableNagle, link.xml PhysicsModule), TCP_NODELAY (Mono/editor), socket-thread RTT, raft prediction + event-acked strokes, adaptive jitter buffer, 60 FPS pacing lock, MSAA<=2, drift-free DO tick loop (server/src/room.ts), jitter probe.
- dev v2 art dropped (v3 wins): ParkKit/SceneLook removed, Toon/WaterFx/Sky/Water/UI = v3. Unused v2 models stay in Resources.
- Merge check (2026-11-22): compile OK; Windows autoplay 60 fps, nodelay on; phone R5CY30BPV8B full match TimeUp, 60.1 fps avg, worst 33.2 ms, no crash (Docs/art_v3/merge_phone.png).
- Backups: tags backup/before-merge-{main,artv3,dev}; branches art/v3, dev, wip/* kept.
- Pitch: final deck Pitch/MOTTAINAI_Final.pptx + .pdf + FINAL_GUIDE.md (rebuild: header of Pitch/build_final.js); first draft MOTTAINAI_Pitch.* kept.
## In progress
- I1 (5G SA, Wi-Fi OFF, 3 full matches in a row) never formally run. A4 (teammate readability) open.
- Voice chat task (feature/voice) paused: audit not delivered yet.
## Exact next step
- I1 on 5G SA with the current APK; then resume voice task from main.
## Resume commands
- `git pull --rebase --autostash` ; close editor -> `build_android.bat` -> `install_phone.bat`
- `bash tools/compile_check.sh` (works with editor open) ; phone autoplay: `bash tools/phone_art_test.sh <name>`
- Windows check: BuildTools.BuildWindows, then `Builds/Win/Mottainai.exe -autoplay -room AV3X -shots <abs dir> -quitafter 27`
