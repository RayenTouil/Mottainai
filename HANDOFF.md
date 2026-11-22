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
