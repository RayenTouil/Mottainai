# MOTAINE — Deployment change: playit.gg (append to CLAUDE.md rules; §0 operating rules still apply)

## Context
- Work ONLY on git branch `playit`. Never pull, merge or rebase `main` (another team PC works on main).
- The dedicated server stays on a team PC (Windows) and is exposed to the internet with playit.gg free tunnels:
  - Custom UDP tunnel → local UDP 7777 (game, Unity Transport)
  - TCP tunnel → local TCP 8080 (HTTP: login, rooms, content pack, telemetry, Network Lab)
- Phones connect over 4G/5G through the public playit addresses (format `name.ply.gg:PORT`, PORT ≠ 7777/8080).
- Do NOT change netcode, gameplay, physics, bots, UI flow or architecture. Minimal edits only.

## Tasks
1. Login screen: 4 fields: Game host, Game port (UDP), API host, API port (HTTP). Save in PlayerPrefs, prefill from last values. Defaults: empty host = 127.0.0.1, ports 7777/8080 (Editor/LAN still works).
2. Before `UnityTransport.SetConnectionData`, resolve a host name to IPv4 with `System.Net.Dns.GetHostAddresses` (async, timeout 3 s). If it fails, show a clear error on the login screen (no crash, no freeze).
3. All HTTP calls use `http://{apiHost}:{apiPort}/...`. The server must never return absolute `localhost`/LAN URLs: make content pack, server.json, telemetry and Network Lab URLs relative, or build them on the client from the API host/port.
4. Network Lab UDP echo: route it through the game UDP endpoint (same host/port as the game) so it goes through the same tunnel. If the echo uses a separate port, make it a message type on the game socket instead.
5. Server binds `0.0.0.0` for UDP 7777 and HTTP 8080 (HttpListener prefix `http://+:8080/`). If HttpListener needs admin rights on Windows, fall back to `http://localhost:8080/` (playit agent connects locally, so localhost is enough) and log it.
6. While the server runs on Windows: prevent sleep (`SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED)`), print the local ports and "Waiting for playit tunnels" at startup.
7. Server console prints one status line every 5 s: players connected, rooms, tick rate, avg RTT.
8. Add `PLAYIT.md` (≤ 10 lines): install agent, create the 2 tunnels, where to read the public addresses, what to type on the phones.
9. Make sure `build_server.bat`, `run_server.bat`, `build_android.bat` still work. Android: INTERNET permission, allow cleartext HTTP (we use plain http to the playit TCP tunnel).
10. Update `DECISIONS.md`. Commit and push to branch `playit`.

## Report (only this)
Changed files (paths only) + exact build/run commands + known issues.
