# MOTTAINAI v2 — deploy & phone test

## Server (Cloudflare Worker + Durable Objects)
1. Once per PC: `cd server && npx wrangler login` (click **Allow** in the browser).
2. `deploy_server.bat` → generates the 36 MB Content Pack, deploys, writes the URL into `Mottainai/Assets/_Mottainai/Resources/server_config.json`.
   URL: **https://mottainai.mottainai-server.workers.dev**
3. Live logs: `cd server && npx wrangler tail`
4. v4 test server: `deploy_server.bat v4` (Worker mottainai-v4). Room settings (reconnect grace, dead-socket timeout): `server/src/room_config.json`. Tests: `node tools/reconnect.ts`, `node tools/lobbytest.ts`.
5. Checks: `node server/tools/smoke.ts` (WELCOME + RTT) · `node server/tools/fakeclient.ts` (full 2:00 match) · `cd server && npm run simcheck` (20 bot matches offline).

## App
1. Close the Unity editor → `build_android.bat` → `Builds\Mottainai.apk`.
2. Plug phones (USB debugging, accept the prompt) → `install_phone.bat` (installs + launches on every device).

## Phone test checklist (5G SA)
- [ ] Wi-Fi **OFF**, mobile data **ON**, 5G SA SIM.
- [ ] Phone browser opens `<URL>/api/health` → `{"ok":true,"protocolVersion":2,"colo":"..."}`.
- [ ] App: name → PLAY → pack download shows MB/s → QUICK PLAY (room DEMO).
- [ ] Phone A = LEFT PADDLER, Phone B = COLLECTOR, bot = RIGHT → START → 2:00 match → results.
- [ ] Net HUD (3-finger tap): 5G SA badge, Edge + Room colo, RTT avg/p95/jitter, snapshot Hz.
- [ ] NETWORK LAB on Wi-Fi, 5G SA (and 4G) → CSV at `<URL>/api/telemetry.csv`.
- Hidden debug: 5 taps on the title → server URL override.

## Free-plan limits
100,000 requests/day (incoming WS messages count 1/20 → client sends events + ping 2/s only), 13,000 GB-s/day DO duration (a room runs only while humans are connected; stops 30 s after the last one leaves). Outgoing snapshots (60 Hz, ~200 kbps per phone) are free.
