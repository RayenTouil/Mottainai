# HANDOFF (v2 Cloudflare)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (`npx wrangler login` once per PC)
## Done
- S0: PROTOCOL.md v2, server/ Worker + RoomDO (WELCOME/LOBBY/PING/PONG/fake snapshots) + TelemetryDO. Smoke: `node server/tools/smoke.ts`.
- C1 code: NGO/UTP + Server assembly + local-server scripts removed; Net/WsClient (ClientWebSocket, backoff), Net/Codecs (PROTOCOL v2), NetClient, ContentService (server_config.json, /api/*, /pack sha256), NetworkLab (edge/room RTT, down/up, telemetry), screens Title(5-tap URL)/Pack/Menu/Lobby/Game/Results, Net HUD (edge/room colo). `tools/compile_check.sh` OK.
## In progress
- C1 build: needs Unity editor CLOSED -> `build_android.bat` then `install_phone.bat`.
- S1: TS port of MatchSim/Course/Raft/Bot into server/src/sim, real pack (server/tools/genpack.ts).
## Exact next step
- Build APK + early phone check (5G SA, Wi-Fi off): shows RTT + edge/room colo. Then S1.
## Resume commands
- `git pull --rebase --autostash` ; `deploy_server.bat` ; `build_android.bat` ; `install_phone.bat`
