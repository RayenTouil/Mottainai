# MOTTAINAI v2 — Cloudflare migration + playable demo (build spec for Claude Code)

## 0. Operating rules (apply always)
- You write 100% of code, architecture, configs, scripts. The team are beginners in game dev: take ALL technical decisions yourself, never ask them to choose. Humans only: plug phones, accept on-screen prompts, click the browser login of `npx wrangler login` once.
- Installed on both PCs: Unity 6.3 LTS (6000.3.29f1) + Android modules/SDK/NDK/JDK, Git, Claude Code. Check Node.js (`node -v`); if missing: `winget install OpenJS.NodeJS.LTS`. adb = Unity's `...\AndroidPlayer\SDK\platform-tools\adb.exe` (detect path).
- TOKEN ECONOMY: no explanations, no walkthroughs, no file-content echo, minimal comments. After each phase reply only: files changed (paths), commands to run, test result, known issues.
- Reuse before writing (Cloudflare templates/patterns, Unity built-ins, CC0 assets). Keep everything already working in the Unity client (visuals, UI, HUD, Android 5G plugin, VisualFactory, tuning) unless this spec replaces it.
- Deadline: playable demo tomorrow. Priority: playable over 5G SA > 5G/latency showcase > visuals.
- No scene/prefab hand-editing: generate from code/Editor scripts.
- Log every default you pick in `DECISIONS.md` (one line each).

## 0b. Git + backup PC — MANDATORY
- Repo root = folder containing this file. Unity project = `Mottainai/` (do not move/rename). Cloudflare code = `server/`.
- ONE PC does all the work (cloud code, deploy, Unity app, APK, phones). A second PC is only a BACKUP if this session's tokens run out: it must be able to resume from git alone.
- `git pull --rebase --autostash` before work. Commit + push after EVERY sub-task (`<phase>: <what>`). Never > 15 min unpushed.
- Keep `HANDOFF.md` always current (update at the end of every phase): done / in progress / exact next step / commands to resume / deployed URL. ≤ 20 lines.
- Stop + PreCompact hooks in `.claude/settings.json` auto-push; do not edit them. `CLAUDE.md` is read-only.
- Context nearly full or user writes "STOP": update HANDOFF.md, push, stop.
- RESUME mode (backup PC): `git pull`, read HANDOFF.md, run `npx wrangler login` if deploying, continue from "exact next step". Do not redo finished phases.

## 0c. Why v2 (context, do not discuss)
The local laptop server is REMOVED. Phones play on 5G SA mobile data (Wi-Fi OFF) → 5G SA core → Internet → Cloudflare edge. Constraints that shape v2:
- Cloudflare does not offer public custom UDP to us (Spectrum custom UDP = Enterprise) → transport = **WebSocket over TLS (wss)** to Workers + Durable Objects, custom compact **binary** protocol.
- Unity Transport/NGO need a UTP server and IP addresses (no hostnames) → **remove Netcode for GameObjects / Unity Transport**. Client uses `System.Net.WebSockets.ClientWebSocket` (fallback: NativeWebSocket package if it fails on device).
- Workers Free plan: Durable Objects (SQLite-backed) allowed; 100,000 requests/day, incoming WS messages billed 20:1, outgoing free; 13,000 GB-s/day. → client→server messages are EVENT-based (no per-frame input stream); server→client snapshots can be high-rate.

## 1. Stack v2
- Server (`server/`): Cloudflare Worker + Durable Objects, TypeScript, Wrangler (latest via npx), SQLite-backed DO classes (`new_sqlite_classes` migration), Workers Static Assets for the Content Pack. Deployed URL `https://mottainai.<account-subdomain>.workers.dev`.
- Client (`Mottainai/`): Unity C#, URP, IL2CPP ARM64, Android min API 26, landscape. ClientWebSocket on a background receive loop → lock-free queue → main thread. HTTPS via UnityWebRequest.
- Server URL: `Mottainai/Assets/_Mottainai/Resources/server_config.json` (`{"baseUrl":"https://..."}`). No IP field in UI anymore; hidden debug panel (5 taps on title) to override URL.

## 2. Game concept (unchanged — implement exactly)
Title MOTTAINAI ("What a waste!" tagline). English UI. Kids-friendly eco cartoon. Inspired by "Fart Together" (co-op raft, coordination is the fun).
- Team of 3 roles on one raft on a polluted river: LEFT PADDLER, RIGHT PADDLER, COLLECTOR.
- Left stroke = forward thrust + yaw right; right stroke = forward + yaw left; both within 0.25 s = "SYNC!" boost (+30%).
- Collector throws a net at waste; must aim.
- 2:00 timer, collect max waste; 1 obstacle hit = GAME OVER (`hitsToGameOver = 1`).
- Shield power-up: 6 s immunity (bubble, blinks last 1.5 s).
- **Demo reality: 2 phones → 2 humans + 1 BOT** (any role combination; 1 phone = 1 human + 2 bots must also work).
- Recommended demo setup (lobby defaults): Phone A = LEFT PADDLER, Phone B = COLLECTOR, bot = RIGHT PADDLER.

## 3. Controls (unchanged)
- No joystick/buttons in gameplay. Paddlers: swipe DOWN = stroke (power from swipe speed 0.4..1.0, cooldown 0.18 s), swipe UP = back-paddle. Haptic tick.
- Collector: TAP waste (raycast) → net (travel 0.25 s, range 7 m, cooldown 0.6 s, hit radius ×1.2); directional swipe = throw toward direction.
- Camera third-person ~35° pitch. Role icon at top; 3 s gesture hint at start.

## 4. Tuning (unchanged values; single source = `server/src/tuning.json`, also served to client in Content Pack)
- River 24 m, raft 2.4 m, current 2.5 m/s. Stroke +1.6 m/s, yaw kick ±28°/s (damping 3/s), max 7 m/s, drag 0.6.
- Procedural course from seed, 40 m chunks, ~650 m. Obstacles 1/25 m → 1/12 m at 1:45, free lane ≥ 3× raft width per chunk (validated).
- Obstacles: Rock, Big Rock, Iceberg, Moored Old Boat, Floating Log, Shark (patrol 2 m/s, ±6 m, fin telegraph 1.5 s).
- Waste clusters 3–5 every ~7 m, 30% near obstacles. Shield 1/~150 m, never before 0:20.
- Scores: Plastic yellow #F2C200 30% +10 · Paper/Cardboard blue #1E6FD9 15% +10 · Glass green #2E9E44 15% +15 · Organic brown #7A4A21 12% +5 · Metal grey #8C959F 13% +20 · Valuable gold #FFC83D 3% +100 · Toxic red #D62828 12% −30 applied at END.
- Combo: 3 non-toxic catches within 3 s → ×1.5 next catch.
- End screen: total, per-type counts (colored), toxic penalty, accuracy %, distance, "Ocean cleaned: X kg", network summary.

## 5. Cloudflare server design
Routes (Worker):
- `GET /api/health` → `{ok, protocolVersion, colo}` (colo = `request.cf.colo`).
- `POST /api/login {name}` → `{token, name}` (random token, stateless).
- `POST /api/rooms` → `{code}` (4 letters). `GET /ws/room/:code?token&name&role` → WebSocket upgrade forwarded to `RoomDO` via `idFromName(code)`. Code `DEMO` = Quick Play room.
- `GET /pack/manifest.json` + `/pack/*.bin` (Static Assets): tuning.json + skin/texture pack + generated water normal maps, total ~30 MB split in files ≤ 20 MB, sha256 in manifest.
- Network Lab: `GET /api/lab/ping` (tiny, no-store), `POST /api/lab/upload` (discard body, return bytes + server ms), `POST /api/telemetry` (store run in `TelemetryDO` SQLite), `GET /api/telemetry.csv` (all runs, for pitch graphs).
RoomDO (authoritative):
- States: LOBBY → COUNTDOWN 3 s → RUNNING 120 s → END (results) → LOBBY (rematch).
- Sim 60 Hz (`setInterval`), keep per-tick CPU small. Port gameplay logic from the existing C# server code (raft physics, course gen, obstacles/shark, waste, shield, collisions, scoring, combo, timer, game over), seed per match.
- Snapshot broadcast 60 Hz (auto-drop to 30 Hz if a client reports backlog): FULL state of all entities in a 60 m window (raft pose/vel, ~150–300 entities: id, type, quantized pos int16 cm, yaw int8, bob phase, shark state, shield timer, score, timer) — full snapshots (no deltas) so TCP needs no reliability layer. This high-rate downlink is intentional (data-volume showcase).
- Client→server messages are EVENTS only: JOIN/ROLE/READY/START, STROKE(side, power, clientTime), BACKPADDLE, NET(targetId | dirX, dirZ, clientTime), PING(t) every 500 ms, STATS(fps, rtt, netType) every 2 s.
- PING → PONG(t, serverTick) immediately. Lag compensation: keep 300 ms entity history; rewind NET hit tests by RTT/2 (cap 150 ms).
- Bots fill empty roles at START and take over a disconnected role within 1 s; reconnect with same token within 60 s restores role. Bot paddler = "sync assist": mirrors the human paddler's stroke within 0.1 s 80% of the time and steers toward the free lane / nearest safe waste. Bot collector: nearest non-toxic waste in range, 70% accuracy, human-like delay 0.3–0.6 s.
- On creation, RoomDO fetches `/cdn-cgi/trace` once to learn its colo; sends `roomColo` in WELCOME.
- No humans for 30 s → stop interval, let DO hibernate.
- `PROTOCOL.md` (first deliverable): message ids, binary layouts (little-endian), quantization, `PROTOCOL_VERSION`. Client checks it via `/api/health` and shows "Update app" on mismatch. 

## 6. Unity app design
- DELETE local-server stack: server-side C# (`#if UNITY_SERVER` code, Server assembly), dedicated server build targets/menu items, HttpListener/TcpListener, `run_server.*`, `build_server.*`, `firewall_open.bat`, `ServerData/`, NGO + Unity Transport packages and NetworkObject/NetworkPrefab generation, IP input field. Project must compile cleanly afterwards.
- Add: `Net/WsClient` (ClientWebSocket, auto-reconnect with backoff), `Net/Protocol` (codec mirroring PROTOCOL.md), `Net/SnapshotBuffer` (adaptive interpolation delay = max(2 snapshot intervals, jitter p95)), entity pool keyed by entity id + type → VisualFactory. Local immediate feedback for own gestures (paddle anim, net VFX), server authoritative.
- Screens: Title (name) → Content Pack download (live MB, MB/s, Mbit/s; cached by sha256; "Force re-download") → Main menu: QUICK PLAY (room DEMO) / CREATE ROOM / JOIN CODE / NETWORK LAB → Lobby (role pick with bot indicator, START) → Game → Results (REMATCH).
- Keep Android plugin: network type badge (5G SA / 5G NSA / LTE / Wi-Fi), latency slice request (Android 13+, `NET_CAPABILITY_PRIORITIZE_LATENCY`), READ_PHONE_STATE graceful.
- Net HUD (on by default, 3-finger tap toggles): netType, Edge colo, Room colo, RTT avg/p95/jitter, snapshot Hz, down/up kbps, msgs/s, FPS.
- Network Lab: (a) edge RTT: 50 sequential HTTPS GETs on one keep-alive connection to `/cdn-cgi/trace` (fallback `/api/lab/ping`), drop first; (b) room RTT: 100 WS pings to a lab room; (c) download: pack files with `?r=<random>`; (d) upload 5 MB to `/api/lab/upload`. Result card + CSV in `persistentDataPath` + POST to `/api/telemetry` (tag with netType + device model).
- Performance (unchanged): 60 FPS target, ≥30 min; no realtime shadows, MSAA ≤2x, render scale auto 0.8 if FPS < 45 for 3 s, pooling, no per-frame allocs, GPU instancing.

## 7. Reusability (do NOT implement Battle mode)
Future: N teams, 5 min, HP instead of instant death, raft-vs-raft collisions. So server keeps `GameMode` (Coop2Min now), `ModeConfig {duration, teams[], hitsToGameOver, raftCollisions}`, per-team state/score/health; client renders N rafts from snapshot.

## 8. Scripts & deploy
- `deploy_server.bat`: `cd server && npm i && npx wrangler deploy`, then writes the deployed URL into `server_config.json` and prints it. Also `npx wrangler tail` instructions in DEPLOY.md.
- `build_android.bat` (existing, keep working, output `Builds\Mottainai.apk`).
- `install_phone.bat`: for every device in `adb devices` → `adb install -r Builds\Mottainai.apk` + launch `com.supcom.mottainai`.
- `DEPLOY.md` (≤1 page): wrangler login/deploy, URL, phone test checklist (Wi-Fi OFF, mobile data ON, browser opens `<URL>/api/health`), Free-plan limits note.

## 9. Phases (sequential, one PC)
- **S0 (≤30 min):** `PROTOCOL.md` + Worker skeleton deployed: health, login, rooms, RoomDO accepting WS with WELCOME + PING/PONG + fake moving snapshot stream, static pack placeholder. Write URL to `server_config.json`. Push.
- **C1:** Unity cleanup (remove local-server stack, NGO/UTP), WebSocket client/codec/interpolation, screens, HUD, Network Lab; build + `install_phone.bat`. EARLY PHONE CHECK: phone on 5G SA (Wi-Fi OFF) connects to the S0 skeleton and shows RTT + Edge/Room colo. Push.
- **S1:** full sim in RoomDO + bots + scoring + lag comp + real Content Pack + Network Lab endpoints + telemetry. Test with `server/tools/fakeclient.ts` playing a full 2:00 match. Deploy. Push.
- **C2:** bind client to the full sim (all entities, events, results, rematch). Build + install on both phones. Push.
- **I1:** 2 phones on 5G SA mobile data, Wi-Fi OFF: Quick Play → full match with 1 bot → results. Fix until it passes 3 times in a row. Network Lab on Wi-Fi and 5G SA (and 4G if possible) → CSV.
- **P1 (only after I1 passes):** polish (CC0 models, water shader, VFX/SFX, combo popups, onboarding hint, perf check).
- **Definition of Done:** APK installs on both phones; both join DEMO over 5G SA; 2 humans + 1 bot complete a 2:00 match (or game over on hit); results screen; HUD shows 5G SA + Edge/Room colo + RTT; content pack download shows MB/s; Network Lab CSV downloadable from `/api/telemetry.csv`; everything pushed; HANDOFF.md current.
