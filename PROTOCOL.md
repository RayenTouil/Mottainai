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

## Client → server (events only)
| id | name | payload |
|---|---|---|
| 0x01 | ROLE | u8 role |
| 0x02 | READY | u8 ready(0/1) |
| 0x03 | START | — (lobby → countdown; also REMATCH from END) |
| 0x10 | STROKE | u8 side(0 L,1 R), u8 power(0..255 = 0..1), f32 clientTime |
| 0x11 | BACKPADDLE | u8 side, u8 power, f32 clientTime |
| 0x12 | NET | u16 targetId (0xFFFF = directional), i16 dirX mm, i16 dirZ mm, f32 clientTime |
| 0x20 | PING | f64 t (client ms) — every 500 ms |
| 0x21 | STATS | u8 fps, u16 rttMs, u8 netType, u16 backlogMs — every 2 s |

`netType`: 0 unknown, 1 Wi-Fi, 2 LTE, 3 5G NSA, 4 5G SA.

## Server → client
| id | name | payload |
|---|---|---|
| 0x80 | WELCOME | u16 protocolVersion, u8 playerId, str roomCode, str roomColo, u32 serverTick, u8 tickRate, u8 snapshotHz |
| 0x81 | LOBBY | u8 phase, u8 n, n × {u8 playerId, str name, u8 role, u8 flags(1 ready, 2 connected)}, u8 roleOwner[3] (L,R,C: playerId or 0xFF = bot) |
| 0x82 | MATCH_START | u32 seed, u8 yourRole, u8 team, u16 durationS, u16 countdownMs, u8 tickRate |
| 0x83 | SNAPSHOT | see below |
| 0x84 | EVENTS | u8 n, n × {u8 type, u8 team, u8 role, u16 entity, i16 value, u8 extra, i16 x cm, f32 z} |
| 0x85 | RESULTS | u8 reason, i32 total, i32 catchScore, i32 toxicPenalty, u16 throws, u16 hits, u16 counts[7], f32 distance, f32 kg, f32 duration |
| 0x86 | PONG | f64 t (echo), u32 serverTick, f32 serverMatchTime |
| 0x87 | NOTICE | str text |

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
