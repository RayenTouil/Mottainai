# HANDOFF (v2 Cloudflare)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
## Done
- S0: PROTOCOL.md v2, server/ Worker + RoomDO (WELCOME/LOBBY/PING/PONG/fake snapshots) + TelemetryDO. Smoke: `node server/tools/smoke.ts`.
- C1 code: NGO/UTP + Server assembly + local-server scripts removed; Net/WsClient (ClientWebSocket, backoff), Net/Codecs (PROTOCOL v2), NetClient, ContentService (server_config.json, /api/*, /pack sha256), NetworkLab (edge/room RTT, down/up, telemetry), screens Title(5-tap URL)/Pack/Menu/Lobby/Game/Results, Net HUD (edge/room colo). `tools/compile_check.sh` OK.
## Done (S1)
- server/src/sim.ts full sim + bots + lag comp + scoring; RoomDO lobby/countdown/run/results/rematch, bot takeover/reconnect; real 36 MB pack (genpack.mjs); lab + telemetry endpoints. Tests: `npm run simcheck` (0/20 crashes), `node tools/fakeclient.ts` (full match on Cloudflare: 60 snap/s, ~200 kbps, RTT ~60 ms).
## Done (P1 models, early)
- CC0 models in Resources/Models + glTFast + ModelImportRules + VisualFactory mapping (primitive fallback). Open Unity once to import, check raft/paddle/shark orientation.
## In progress
- C1/C2 build: Unity editor must be CLOSED -> build_android.bat -> install_phone.bat (phones on 5G SA, Wi-Fi OFF).
## Exact next step
- Close Unity editor, run build_android.bat, plug phones, install_phone.bat, Quick Play on both phones (I1).
## Resume commands
- `git pull --rebase --autostash` ; `deploy_server.bat` ; `build_android.bat` ; `install_phone.bat`
