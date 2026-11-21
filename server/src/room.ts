import { DurableObject } from "cloudflare:workers";
import { C2S, PROTOCOL_VERSION, Reader, S2C, Writer, clamp } from "./protocol";
import { Coop2MinMode, Kind, MatchSim, NO_ENTITY, Phase, Role, TUNING } from "./sim";

const TICK_HZ = 60;
const IDLE_STOP_MS = 30_000;
const RECONNECT_MS = 60_000;
const BOT = 0xff;

interface Player {
  id: number; token: string; name: string; role: number; ready: boolean;
  ws: WebSocket | null; leftAt: number; backlogMs: number; fps: number; rttMs: number; netType: number;
}

/** Authoritative room: LOBBY -> COUNTDOWN 3 s -> RUNNING 120 s -> END (results) -> rematch. */
export class RoomDO extends DurableObject {
  code = "";
  colo = "?";
  coloPromise: Promise<void> | null = null;
  players: Player[] = [];
  nextId = 1;
  sim: MatchSim | null = null;
  timer: ReturnType<typeof setInterval> | null = null;
  lastHumanAt = Date.now();
  lastStepAt = 0;
  tick = 0;
  snapshotHz = 60;
  resultsSent = false;
  w = new Writer(8192);

  constructor(ctx: DurableObjectState, env: any) {
    super(ctx, env);
    for (const ws of ctx.getWebSockets()) {
      const a = ws.deserializeAttachment() as (Partial<Player> & { code?: string }) | null;
      if (!a?.token) continue;
      this.code = a.code ?? this.code;
      this.players.push({ id: a.id!, token: a.token, name: a.name ?? "?", role: a.role ?? 0, ready: false, ws, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0 });
      this.nextId = Math.max(this.nextId, (a.id ?? 0) + 1);
    }
  }

  get phase() { return this.sim ? this.sim.phase : Phase.Lobby; }

  ensureColo() {
    this.coloPromise ??= fetch("https://cloudflare.com/cdn-cgi/trace")
      .then(r => r.text())
      .then(t => { this.colo = /colo=(\w+)/.exec(t)?.[1] ?? "?"; })
      .catch(() => {});
    return this.coloPromise;
  }

  attach(p: Player) { try { p.ws?.serializeAttachment({ id: p.id, token: p.token, name: p.name, role: p.role, code: this.code }); } catch {} }
  live(p: Player) { return p.ws !== null || Date.now() - p.leftAt < RECONNECT_MS; }

  async fetch(req: Request): Promise<Response> {
    const url = new URL(req.url);
    this.code = url.searchParams.get("code") ?? this.code;
    const token = url.searchParams.get("token") || crypto.randomUUID();
    const name = (url.searchParams.get("name") || "Player").slice(0, 16);
    const wantRole = Number(url.searchParams.get("role") || 0) | 0;
    await this.ensureColo();

    const pair = new WebSocketPair();
    const client = pair[0], server = pair[1];
    this.ctx.acceptWebSocket(server);

    this.players = this.players.filter(x => x.token === token || this.live(x));
    let p = this.players.find(x => x.token === token);
    if (p) {
      const old = p.ws;
      p.ws = server; p.name = name; p.leftAt = 0;
      if (old && old !== server) { try { old.close(4000, "replaced"); } catch {} }
      if (!p.role) p.role = this.pickRole(wantRole, p);
    } else {
      p = { id: this.nextId++, token, name, role: 0, ready: false, ws: server, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0 };
      this.players.push(p);
      p.role = this.pickRole(wantRole, p);
    }
    this.attach(p);

    const w = this.w.reset();
    w.u8w(S2C.WELCOME).u16(PROTOCOL_VERSION).u8w(p.id).str(this.code).str(this.colo).u32(this.tick).u8w(TICK_HZ).u8w(this.snapshotHz);
    server.send(w.bytes());
    this.lastHumanAt = Date.now();
    // joining / reconnecting mid-match: take the role back from the bot
    if (this.sim && this.sim.phase !== Phase.Finished && p.role) {
      this.sim.setBot(0, p.role, false);
      this.sendMatchStart(p);
    } else if (this.sim && this.sim.phase === Phase.Finished && this.sim.results) this.sendResults(p.ws!);
    this.broadcastLobby();
    this.startLoop();
    return new Response(null, { status: 101, webSocket: client });
  }

  pickRole(want: number, p: Player) {
    const taken = (r: number) => this.players.some(x => x !== p && x.role === r && this.live(x));
    if (want >= 1 && want <= 3 && !taken(want)) return want;
    for (const r of [Role.Left, Role.Collector, Role.Right]) if (!taken(r)) return r;
    return 0;
  }

  roleOwner(r: number) {
    const p = this.players.find(x => x.role === r && x.ws);
    return p ? p.id : BOT;
  }

  broadcastLobby() {
    const w = this.w.reset();
    const live = this.players.filter(x => this.live(x));
    w.u8w(S2C.LOBBY).u8w(this.phase).u8w(live.length);
    for (const p of live) w.u8w(p.id).str(p.name).u8w(p.role).u8w((p.ready ? 1 : 0) | (p.ws ? 2 : 0));
    for (const r of [1, 2, 3]) w.u8w(this.roleOwner(r));
    this.broadcast(w.bytes());
  }

  broadcast(buf: ArrayBuffer) {
    for (const p of this.players) if (p.ws) { try { p.ws.send(buf); } catch {} }
  }

  // ---------------- match ----------------
  startMatch() {
    if (this.sim && this.sim.phase !== Phase.Finished) return;
    const seed = (crypto.getRandomValues(new Uint32Array(1))[0] & 0x7fffffff) || 1;
    const sim = new MatchSim(seed, TUNING, new Coop2MinMode(TUNING));
    sim.tickRate = TICK_HZ;
    for (const r of [Role.Left, Role.Right, Role.Collector]) sim.teams[0].bot[r] = this.roleOwner(r) === BOT;
    this.sim = sim;
    this.resultsSent = false;
    for (const p of this.players) { p.ready = false; if (p.ws) this.sendMatchStart(p); }
    this.broadcastLobby();
  }

  sendMatchStart(p: Player) {
    if (!this.sim || !p.ws) return;
    const w = this.w.reset();
    w.u8w(S2C.MATCH_START).u32(this.sim.seed).u8w(p.role).u8w(0).u16(this.sim.mode.config.duration).u16(Math.max(0, this.sim.countdownLeft) * 1000).u8w(TICK_HZ);
    try { p.ws.send(w.bytes()); } catch {}
  }

  sendResults(ws: WebSocket) {
    const r = this.sim?.results;
    if (!r) return;
    const w = this.w.reset();
    w.u8w(S2C.RESULTS).u8w(r.reason).i32(r.total).i32(r.catchScore).i32(r.toxicPenalty).u16(r.throws).u16(r.hits);
    for (const c of r.counts) w.u16(c);
    w.f32(r.distance).f32(r.kg).f32(r.duration);
    try { ws.send(w.bytes()); } catch {}
  }

  async webSocketMessage(ws: WebSocket, msg: ArrayBuffer | string) {
    if (typeof msg === "string" || msg.byteLength < 1) return;
    const p = this.players.find(x => x.ws === ws);
    if (!p) return;
    const r = new Reader(msg);
    try {
      const id = r.u8();
      switch (id) {
        case C2S.PING: {
          const t = r.f64();
          const w = this.w.reset();
          w.u8w(S2C.PONG).f64(t).u32(this.tick).f32(this.sim?.time ?? 0);
          ws.send(w.bytes());
          break;
        }
        case C2S.STATS:
          p.fps = r.u8(); p.rttMs = r.u16(); p.netType = r.u8(); p.backlogMs = r.u16();
          this.snapshotHz = this.players.some(x => x.ws && x.backlogMs > 100) ? 30 : 60;
          break;
        case C2S.ROLE: {
          const want = r.u8();
          const busy = this.sim && this.sim.phase !== Phase.Finished;
          if (!busy && this.pickRole(want, p) === want) { p.role = want; this.attach(p); }
          this.broadcastLobby();
          break;
        }
        case C2S.READY: p.ready = r.u8() !== 0; this.broadcastLobby(); break;
        case C2S.START: this.startMatch(); break;
        case C2S.STROKE:
        case C2S.BACKPADDLE: {
          r.u8(); // side: the player's role is authoritative
          const power = r.u8() / 255;
          if (this.sim && (p.role === Role.Left || p.role === Role.Right)) this.sim.humanStroke(this.sim.teams[0], p.role, power, id === C2S.BACKPADDLE);
          break;
        }
        case C2S.NET: {
          const target = r.u16(), dx = r.i16() / 1000, dz = r.i16() / 1000;
          if (this.sim && p.role === Role.Collector) {
            const rewind = clamp(p.rttMs / 2, 0, TUNING.lagCompCapMs) / 1000; // lag compensation: RTT/2, cap 150 ms
            this.sim.applyThrow(this.sim.teams[0], target === NO_ENTITY ? NO_ENTITY : target, dx, dz, rewind);
          }
          break;
        }
        default: break;
      }
    } catch { /* malformed frame */ }
  }

  async webSocketClose(ws: WebSocket) { this.drop(ws); }
  async webSocketError(ws: WebSocket) { this.drop(ws); }

  drop(ws: WebSocket) {
    const p = this.players.find(x => x.ws === ws);
    if (!p) return;
    p.ws = null; p.leftAt = Date.now(); p.ready = false;
    if (this.sim && this.sim.phase !== Phase.Finished && p.role && this.roleOwner(p.role) === BOT) this.sim.setBot(0, p.role, true); // bot takes over
    this.broadcastLobby();
  }

  // ---------------- loop ----------------
  startLoop() {
    if (this.timer) return;
    this.lastStepAt = Date.now();
    this.timer = setInterval(() => this.loop(), 1000 / TICK_HZ);
  }

  loop() {
    const now = Date.now();
    if (this.players.some(p => p.ws)) this.lastHumanAt = now;
    else if (now - this.lastHumanAt > IDLE_STOP_MS) {
      clearInterval(this.timer!); this.timer = null;
      this.sim?.abort(); this.sim = null;
      return;
    }
    // fixed-step catch-up (timer jitter), max 4 steps per callback
    let steps = Math.floor((now - this.lastStepAt) * TICK_HZ / 1000);
    if (steps <= 0) return;
    if (steps > 4) { steps = 4; this.lastStepAt = now; } else this.lastStepAt += steps * 1000 / TICK_HZ;
    for (let i = 0; i < steps; i++) this.step();
  }

  step() {
    this.tick++;
    const sim = this.sim;
    if (!sim) return;
    if (sim.phase !== Phase.Finished) sim.step(1 / TICK_HZ);
    if (sim.events.length) {
      const evs = sim.events;
      for (let i = 0; i < evs.length; i += 255) {
        const n = Math.min(255, evs.length - i);
        const w = this.w.reset();
        w.u8w(S2C.EVENTS).u8w(n);
        for (let k = i; k < i + n; k++) { const e = evs[k]; w.u8w(e.type).u8w(e.team).u8w(e.role).u16(e.entity).i16(e.value).u8w(e.extra).i16(e.x * 100).f32(e.z); }
        this.broadcast(w.bytes());
      }
      sim.events.length = 0;
    }
    if (sim.phase === Phase.Finished) {
      if (!this.resultsSent) {
        this.resultsSent = true;
        for (const p of this.players) if (p.ws) this.sendResults(p.ws);
        this.broadcastLobby();
      }
      return;
    }
    if (this.snapshotHz === 30 && (this.tick & 1)) return;
    this.broadcast(this.buildSnapshot(sim));
  }

  buildSnapshot(sim: MatchSim): ArrayBuffer {
    const T = sim.T, w = this.w.reset();
    const left = sim.phase === Phase.Countdown ? sim.countdownLeft : Math.max(0, sim.mode.config.duration - sim.time);
    w.u8w(S2C.SNAPSHOT).u32(sim.tick).f32(sim.time).u8w(sim.phase).u16(left * 100).u8w(this.snapshotHz);
    w.u8w(sim.teams.length);
    const q = (v: number) => clamp(Math.round(v * 20), 0, 255);
    for (const t of sim.teams) {
      const r = t.raft;
      const flags = (t.score.comboArmed ? 1 : 0) | (t.shieldActive ? 2 : 0) | (t.magnetActive ? 4 : 0) | (t.turboActive ? 8 : 0);
      w.i32(t.score.catchScore).u8w(flags).u8w(Math.max(0, t.hp))
        .u8w(t.shieldActive ? q(t.shieldUntil - sim.time) : 0).u8w(t.magnetActive ? q(t.magnetUntil - sim.time) : 0).u8w(t.turboActive ? q(t.turboUntil - sim.time) : 0)
        .f32(r.x).f32(r.z).i16(r.heading * 1000).i16(r.worldVx * 100).i16(r.worldVz * 100).i16(r.yawRate * 1000);
    }
    const r0 = sim.teams[0].raft, originZ = r0.z;
    w.f32(originZ);
    const countPos = w.pos;
    w.u16(0);
    let n = 0;
    for (let i = sim.lowerBound(originZ - T.snapshotBehind); i < sim.count && sim.E[i].z < originZ + T.snapshotAhead; i++) {
      const e = sim.E[i];
      if (!e.alive) continue;
      w.u16(e.id).u8w((e.kind << 4) | (e.sub & 0xf)).i16(e.x * 100).i16((e.z - originZ) * 100).i8(e.y * 50)
        .u8w(Math.round((((e.yaw % 360) + 360) % 360) / 360 * 256) & 0xff).u8w(e.kind === Kind.Obstacle ? e.flags : 0);
      n++;
    }
    w.dv.setUint16(countPos, n, true);
    return w.bytes();
  }
}
