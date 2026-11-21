# HANDOFF (v2 Cloudflare)
Deployed URL: https://mottainai.mottainai-server.workers.dev  (account: Cloudflare OAuth via `npx wrangler login`)
## Done
- S0: PROTOCOL.md (v2), server/ Worker + RoomDO (WELCOME, LOBBY, PING/PONG, fake 60 Hz snapshots) + TelemetryDO, health/login/rooms/lab endpoints, pack placeholder. Smoke test: `node server/tools/smoke.ts` (WELCOME ok, ~60 snap/s, RTT ~57 ms from dev PC).
## In progress
- C1: Unity cleanup (remove NGO/UTP + local server), WsClient/Protocol/SnapshotBuffer, screens, HUD, Network Lab.
## Exact next step
- C1 start: delete Scripts/Server, NGO+UTP packages, run_server/build_server/firewall_open; add Net/WsClient.cs.
## Resume commands
- `git pull --rebase --autostash` ; `cd server && npm i && npx wrangler deploy` (or `deploy_server.bat`)
- Smoke: `node server/tools/smoke.ts` ; Unity editor must be closed for `build_android.bat`.
