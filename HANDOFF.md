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
