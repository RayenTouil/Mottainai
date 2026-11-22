# MOTTAINAI wire protocol — `PROTOCOL_VERSION = 3`

Transport: one WebSocket (wss) per player, `GET /ws/room/:code?token=&name=&role=` (role 1 L, 2 R, 3 C, 0 any).
All game frames are **binary**, little-endian. Byte 0 = message id. `str` = u8 length + UTF-8 bytes.
Quantization: positions `i16` cm (x) / `i16` cm relative to `originZ` (z), angles `u8` (360/256°) or `i16` mrad, speeds `i16` cm/s.

## HTTP (JSON)
| Route | Reply |
|---|---|
| `GET /api/health` | `{ok, protocolVersion, colo}` |
| `POST /api/login {name}` | `{token, name}` |
| `POST /api/rooms` | `{code}` (4 letters; `DEMO` = Quick Play) |
| `GET /pack/manifest.json` | `{version, files:[{name, bytes, sha256}], total}` ; files at `/pack/<name>` |
| `GET /api/lab/ping` | `{t}` no-store |
| `POST /api/lab/upload` | `{bytes, ms}` |
| `POST /api/telemetry` | `{ok}` ; `GET /api/telemetry.csv` |
| `POST /api/team/create {token, playerName, name}` | `{ok, team}` ; 400 bad name (3-16 letters/digits/spaces), 409 name taken / already in a team |
| `POST /api/team/join {token, playerName, joinCode}` | `{ok, team}` ; 404 unknown code, 409 full (3) / already in a team |
| `POST /api/team/leave {token}` | `{ok, team:null}` (last member out deletes the team) |
| `GET /api/team/me?token=` | `{ok, team}` ; team = `{id, name, joinCode, totalScore, matches, bestScore, rank, members:[{name, me}]}` or null |
| `GET /api/leaderboard?limit=20&token=` | `{ok, rows:[{rank, name, totalScore, matches, bestScore, members, me}], me}` (order: total, best, oldest) |

## Client → server (events only)
| id | name | payload |
|---|---|---|
| 0x01 | ROLE | u8 role |
| 0x02 | READY | u8 ready(0/1) — ignored outside the lobby; reset for everyone on match start and on match end. ROLE is refused while READY. |
| 0x03 | START | — host only, lobby only, every connected human READY; else NOTICE ("Only the host can start" / "Waiting for: A, B" / "Match already running"). Also REMATCH from END. |
| 0x04 | LEAVE | — (v3: voluntary leave, bot takes the role immediately, no pause, no reconnect slot) |
| 0x06 | TEAM_CREATE | v3: str name — host, lobby/results only: create a team from the room (empty name = invite the room to the host's team) |
| 0x07 | TEAM_ANSWER | v3: u8 accept — answer to TEAM_INVITE (accept = leave the current team, join this one) |
| 0x05 | CONTINUE_BOT | — (v3: host only, while PAUSED: bots take every missing role, then RESUME) |
| 0x10 | STROKE | u8 side(0 L,1 R), u8 power(0..255 = 0..1), f32 clientTime |
| 0x11 | BACKPADDLE | u8 side, u8 power, f32 clientTime |
| 0x12 | NET | u16 targetId (0xFFFF = directional), i16 dirX mm, i16 dirZ mm, f32 clientTime |
| 0x20 | PING | f64 t (client ms) — every 500 ms, sent by the socket thread while the game loop is alive (any message keeps the socket alive) |
| 0x21 | STATS | u8 fps, u16 rttMs, u8 netType, u16 backlogMs — every 2 s |

`netType`: 0 unknown, 1 Wi-Fi, 2 LTE, 3 5G NSA, 4 5G SA.

## Server → client
| id | name | payload |
|---|---|---|
| 0x80 | WELCOME | u16 protocolVersion, u8 playerId, str roomCode, str roomColo, u32 serverTick, u8 tickRate, u8 snapshotHz |
| 0x81 | LOBBY | u8 phase, u8 n, n × {u8 playerId, str name, u8 role, u8 flags(1 ready, 2 connected)}, u8 roleOwner[3] (L,R,C: playerId or 0xFF = bot), u8 hostId (v3), n × str teamName (v3, "" = guest) |
| 0x82 | MATCH_START | u32 seed, u8 yourRole, u8 team, u16 durationS, u16 countdownMs, u8 tickRate |
| 0x83 | SNAPSHOT | see below |
| 0x84 | EVENTS | u8 n, n × {u8 type, u8 team, u8 role, u16 entity, i16 value, u8 extra, i16 x cm, f32 z} |
| 0x85 | RESULTS | u8 reason, i32 total, i32 catchScore, i32 toxicPenalty, u16 throws, u16 hits, u16 counts[7], f32 distance, f32 kg, f32 duration |
| 0x86 | PONG | f64 t (echo), u32 serverTick, f32 serverMatchTime |
| 0x87 | NOTICE | str text |
| 0x8A | PAUSE | v3: u8 reason (1 = player disconnected), u8 n, n × {u8 playerId, str name, u8 role, u8 graceLeftS} — on every change and every 1 s while waiting |
| 0x8C | TEAM_RESULT | v3: u8 counted, str teamName, i32 points, u16 rank, u8 canSave (guest match kept 10 min, "save this crew") — after RESULTS and after a successful save |
| 0x8D | TEAM_INVITE | v3: str teamName, str fromName, str currentTeam ("" = guest) |
| 0x8B | RESUME | v3: u16 countdownMs — everyone is back (or replaced by a bot): the sim steps again after this 3-2-1 |

### SNAPSHOT 0x83 (full state, no deltas)
```
u32 tick, f32 matchTime, u8 phase, u16 timeLeft (cs), u8 snapshotHz
u8 teamCount, per team:
  i32 score, u8 flags(1 comboArmed, 2 shield, 4 magnet, 8 turbo), u8 hp,
  u8 shieldLeft (0.05 s), u8 magnetLeft (0.05 s), u8 turboLeft (0.05 s),
  f32 x, f32 z, i16 heading mrad, i16 vx cm/s, i16 vz cm/s, i16 yawRate mrad/s
f32 originZ, u16 n, n × entity (10 B):
  u16 id, u8 code (kind<<4 | sub), i16 x cm, i16 (z-originZ) cm, i8 y (2 cm), u8 yaw, u8 flags
```
Entity window: team raft z −15 m … +60 m. `code` kinds: 1 Waste (sub = WasteType), 2 Obstacle (sub = ObstacleType), 3 Power-up bubble (sub 0 Shield, 1 Magnet, 2 Turbo). Shark `flags`: 0 circling, 1 telegraph, 2 attacking.
Rate: 60 Hz, 30 Hz when any client reports `backlogMs > 100`.

### Event types (EVENTS.type)
0 Countdown, 1 Go, 2 Stroke, 3 Sync, 4 NetThrow, 5 Catch, 6 Miss, 7 Hit, 8 ShieldOn, 9 ShieldOff, 10 GameOver, 11 Combo, 12 BotTakeover, 13 PowerOn, 14 PowerOff.

### Phases
0 Lobby, 1 Countdown, 2 Running, 3 Finished. End reasons: 1 TimeUp, 2 Crashed, 3 Aborted.

### Disconnection / reconnection (v3)
- WS URL extras: `&match=<seed>` = the client thinks it is in that match (reconnect); `&spectate=1` = no role, never pauses (test watcher / jury screen).
- Dead socket: no message from a player for `deadAfterMs` (1500) → the server closes it (code 4001) and treats it as a disconnect.
- Involuntary disconnect of a human who plays a role in the running match (countdown or running) → PAUSE: the room stops stepping the sim
  (no time, obstacle or collision advances), inputs are ignored, no snapshots are broadcast (PONG keeps the frozen match time).
- Reconnect with the same token within `reconnectGraceS` (30) → MATCH_START + one fresh snapshot to that player; when nobody is missing any more → RESUME `resumeCountdownMs` (3000) to all.
  A drop during the 3-2-1 cancels it (PAUSE again). Several missing players: the match waits for all of them.
- Grace expires → BotTakeover for that role (then RESUME, or keep waiting for the others). Host CONTINUE_BOT skips the wait.
- Nobody connected when the last grace expires → match aborted (RESULTS reason 3 Aborted, no score); kept for reconnecting players for 30 s.
- Reconnect with `&match=` into a room without a match (Durable Object restarted) → NOTICE "Match lost (server restarted) - back to the lobby" + LOBBY phase 0.
- Settings: `server/src/room_config.json` (room orchestration, not game tuning).

### Teams + scores (v3)
- TeamsDO (one global SQLite DO) is the only authority; clients never send scores. RoomDO reports `{matchId = room-seed-startMs, score = RESULTS total, tokens of every human who held a role}` at match end; aborted matches are never reported.
- Counted when all those humans are in the same team, once per matchId (PRIMARY KEY): reconnects, double reports and double claims add nothing; a rematch is a new matchId.
- Otherwise the result is kept per room (last one, 10 min); it counts for a team created from the room once every player of that match has joined it.

### Lobby rules (v3)
Host = first connected human in join order; sticky while connected, then the next connected human. Bots need no READY; a single human can READY + START alone.
