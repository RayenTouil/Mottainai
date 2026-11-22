# HANDOFF — branch `art/v3` (base 857577f = main; spec ART_DIRECTION_v3.md)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
Worktree: `../Mottainai_v3` (separate project folder; batch builds work while the main editor is open).
## Done (art v3, visual-only)
- P0 declutter: no center SYNC text (chip x3/x5/x10), Net pill top-center (tap -> full panel, closed by default), role/power chips, thin shield, cyan magnet, narrow wake, camera 9/6/look-ahead 4/FOV 50.
- P1 lighting/sky/fog/post + water shader v3 (bank gradient, fresnel, edge foam, caustics, crossed waves). ArtV3.cs = tokens + tier (PlayerPrefs artTier, 0 Low default).
- P2 waste rings/silhouettes/anim/pickup FX, valuable purple, obstacle palette. P3 yellow raft + bean crew + blob shadows.
- P4 banks (cliff faces, tree clusters, rocks, fences, cottages, dock). P5 UI skin (Fredoka, panels, pill buttons).
- PC check: full match on Windows OK (TimeUp, 60 fps, worst 33.9 ms); shots in Docs/art_v3/P5_pc_*.png.
## In progress
- Phone A9 PASS: full match 81.8 fps avg, worst 41.6 ms (Low tier, Wi-Fi), Docs/art_v3/P6.png. A4 (teammate naming test) still open.
## Exact next step
- A4 with a teammate; optional: shrink center "SHIELD!/MAGNET NET!" Big text. Merge art/v3 -> main is the team's call.
## Resume commands
- `bash build_android.sh` ; Windows check: run BuildTools.BuildWindows, then `Builds/Win/Mottainai.exe -autoplay -room AV3X -shots <abs dir> -quitafter 27`
# HANDOFF (v2 Cloudflare) — branch `dev` (main = last known-good)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
## Done
- S0/S1/C1/C2: Worker + RoomDO sim/bots/lag comp, content pack, lab/telemetry, Unity WS client (see git log).
- NET (dev): drift-free DO tick loop (server jitter p95 14.7 -> 0.7 ms), immediate event flush, early-input deferral; client TCP_NODELAY, socket-thread RTT, tick-timeline prediction + event-acked strokes, entity extrapolation, adaptive buffer. Probe: `node server/tools/jitter.ts`.
- ART P1-P6 (dev): lighting/sky/post + Quality High/Low; water v2 (caustics, foam, ripples, bow wave, sea end); toy crew/raft; park kit (ground shader, stone edges, trees, props, bridges, lighthouse); waste prop kit; UI restyle (Fredoka/Nunito, title, settings overlay, HUD, onboarding, results).
- FIX: content-pack textures never loaded before (mip chain) -> water normal maps + raft skin now applied.
## In progress
- I1 not yet passed: phone R5CY30BPV8B on 5G SA (Wi-Fi OFF), 1 human + 2 bots, 3 full matches in a row (LEFT PADDLER, then COLLECTOR).
- P8 polish/acceptance (reference compare, FPS High/Low on phone).
## Exact next step
- Plug phone -> `bash tools/build_copy.sh android` -> `install_phone.bat` -> I1 runs; read Net HUD (5G SA, colos, RTT, buffer).
## Resume commands
- `git pull --rebase --autostash` ; `bash tools/build_copy.sh android|win` (works with the editor open) ; `install_phone.bat`
- Visual check: `Builds/Win/Mottainai.exe -autoplay -room SH1234 -role 3 -shots <dir> -quitafter 24`
