import { DurableObject } from "cloudflare:workers";
import { C2S, PROTOCOL_VERSION, Reader, S2C, Writer, clamp } from "./protocol";
import { Coop2MinMode, Kind, MatchSim, NO_ENTITY, Phase, Role, TUNING } from "./sim";
import ROOM_CFG from "./room_config.json";

const TICK_HZ = 60;
const IDLE_STOP_MS = 30_000;
const RECONNECT_MS = 60_000;
const BOT = 0xff;
const GRACE_MS = ROOM_CFG.reconnectGraceS * 1000;   // involuntary drop mid-match: the match waits this long for the player
const DEAD_MS = ROOM_CFG.deadAfterMs;                 // no message (PING every 500 ms) for this long = dead socket (silent stall / half-open TCP)
const RESUME_MS = ROOM_CFG.resumeCountdownMs;         // 3-2-1 before the sim steps again
const PAUSE_EVERY_MS = ROOM_CFG.pauseBroadcastMs;
const INPUT_GRACE_S = 0.12; // input arriving this early (network jitter) waits for its cooldown instead of being dropped

interface Deferred { at: number; arrived: number; role: number; net: boolean; power: number; back: boolean; target: number; dx: number; dz: number; rewind: number; }

interface Player {
  id: number; token: string; name: string; role: number; ready: boolean;
  ws: WebSocket | null; leftAt: number; backlogMs: number; fps: number; rttMs: number; netType: number;
  lastMsgAt: number;
}

interface Missing { id: number; name: string; role: number; since: number; deadline: number; cause: string; }
/** Match frozen in room.ts (the sim is simply not stepped): waiting for missing humans, then a 3-2-1 (resumeAt). */
interface PauseState { since: number; resumeAt: number; lastSent: number; missing: Missing[]; }
interface MatchNetStats { pauses: number; reconnects: number; reconnectMs: number[]; graceExpired: number; continueWithBot: number; pausedMs: number; causes: Record<string, number>; }
const newNetStats = (): MatchNetStats => ({ pauses: 0, reconnects: 0, reconnectMs: [], graceExpired: 0, continueWithBot: 0, pausedMs: 0, causes: {} });

/** Authoritative room: LOBBY -> COUNTDOWN 3 s -> RUNNING 120 s -> END (results) -> rematch. */
export class RoomDO extends DurableObject {
  code = "";
  colo = "?";
  coloPromise: Promise<void> | null = null;
  players: Player[] = [];
  nextId = 1;
  sim: MatchSim | null = null;
  timer: ReturnType<typeof setTimeout> | null = null;
  lastHumanAt = Date.now();
  lastStepAt = 0;
  tick = 0;
  lastSnapTick = -99;
  snapshotHz = 60;
  resultsSent = false;
  hostId = 0; // first connected human; sticky while connected, then the next connected human in join order
  deferred: Deferred[] = [];
  pause: PauseState | null = null;
  mstats = newNetStats();
  w = new Writer(8192);

  constructor(ctx: DurableObjectState, env: any) {
    super(ctx, env);
    for (const ws of ctx.getWebSockets()) {
      const a = ws.deserializeAttachment() as (Partial<Player> & { code?: string }) | null;
      if (!a?.token) continue;
      this.code = a.code ?? this.code;
      this.players.push({ id: a.id!, token: a.token, name: a.name ?? "?", role: a.role ?? 0, ready: false, ws, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0, lastMsgAt: Date.now() });
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
    const clientMatch = Number(url.searchParams.get("match") || 0); // seed of the match the client thinks it is in (reconnect)
    await this.ensureColo();

    const pair = new WebSocketPair();
    const client = pair[0], server = pair[1];
    this.ctx.acceptWebSocket(server);

    this.players = this.players.filter(x => x.token === token || this.live(x));
    let p = this.players.find(x => x.token === token);
    if (p) {
      const old = p.ws;
      p.ws = server; p.name = name; p.leftAt = 0; p.lastMsgAt = Date.now();
      if (old && old !== server) { try { old.close(4000, "replaced"); } catch {} }
      if (!p.role) p.role = this.pickRole(wantRole, p);
    } else {
      p = { id: this.nextId++, token, name, role: 0, ready: false, ws: server, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0, lastMsgAt: Date.now() };
      this.players.push(p);
      p.role = this.pickRole(wantRole, p);
    }
    this.attach(p);

    const w = this.w.reset();
    w.u8w(S2C.WELCOME).u16(PROTOCOL_VERSION).u8w(p.id).str(this.code).str(this.colo).u32(this.tick).u8w(TICK_HZ).u8w(this.snapshotHz);
    server.send(w.bytes());
    this.lastHumanAt = Date.now();
    if (this.sim && this.sim.phase !== Phase.Finished && p.role) {
      // joining / reconnecting mid-match: MATCH_START + a fresh snapshot (frozen state while paused), role back from the bot
      const miss = this.pause?.missing.find(m => m.id === p!.id);
      this.sim.setBot(0, p.role, false);
      this.sendMatchStart(p);
      try { server.send(this.buildSnapshot(this.sim)); } catch {}
      if (miss) {
        this.pause!.missing = this.pause!.missing.filter(m => m !== miss);
        const ms = Date.now() - miss.since;
        this.mstats.reconnects++; this.mstats.reconnectMs.push(ms);
        this.log({ ev: "reconnect", player: p.name, ms, cause: miss.cause, stillMissing: this.pause!.missing.length });
        this.afterMissingChange();
      } else if (this.pause) this.sendPauseState(p);
    } else if (this.sim && this.sim.phase === Phase.Finished && this.sim.results) this.sendResults(p.ws!);
    else if (clientMatch && !this.sim) {
      // the client was in a match this room no longer has (Durable Object restarted / redeployed): back to the lobby cleanly
      this.notice(p, "Match lost (server restarted) - back to the lobby");
      this.log({ ev: "match_lost", player: p.name, seed: clientMatch });
    }
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

  get inLobby() { return !this.sim || this.sim.phase === Phase.Finished; }

  updateHost() {
    if (this.players.some(x => x.id === this.hostId && x.ws)) return;
    this.hostId = this.players.find(x => x.ws)?.id ?? 0;
  }

  notice(p: Player, text: string) {
    if (!p.ws) return;
    const w = this.w.reset();
    w.u8w(S2C.NOTICE).str(text);
    try { p.ws.send(w.bytes()); } catch {}
  }

  /** START / REMATCH: host only, in lobby only, every connected human READY (bots need none). */
  tryStart(p: Player) {
    if (!this.inLobby) return this.notice(p, "Match already running");
    this.updateHost();
    if (p.id !== this.hostId) return this.notice(p, "Only the host can start");
    const waiting = this.players.filter(x => x.ws && !x.ready);
    if (waiting.length) return this.notice(p, "Waiting for: " + waiting.map(x => x.name).join(", "));
    this.startMatch();
  }

  broadcastLobby() {
    this.updateHost();
    const w = this.w.reset();
    const live = this.players.filter(x => this.live(x));
    w.u8w(S2C.LOBBY).u8w(this.phase).u8w(live.length);
    for (const p of live) w.u8w(p.id).str(p.name).u8w(p.role).u8w((p.ready ? 1 : 0) | (p.ws ? 2 : 0));
    for (const r of [1, 2, 3]) w.u8w(this.roleOwner(r));
    w.u8w(this.hostId); // v3
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
    this.deferred.length = 0;
    this.pause = null;
    this.mstats = newNetStats();
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
    p.lastMsgAt = Date.now();
    if (!this.timer) this.startLoop(); // restored after eviction: the loop also runs dead-socket detection
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
          {
            const slow = this.players.find(x => x.ws && x.backlogMs > 100);
            const hz = slow ? 30 : 60;
            if (hz !== this.snapshotHz) this.log({ ev: "snapshot_hz", hz, by: slow?.name ?? null, backlogMs: slow?.backlogMs ?? 0 });
            this.snapshotHz = hz;
          }
          break;
        case C2S.ROLE: {
          const want = r.u8();
          const busy = this.sim && this.sim.phase !== Phase.Finished;
          if (!busy && !p.ready && this.pickRole(want, p) === want) { p.role = want; this.attach(p); } // locked while READY
          this.broadcastLobby();
          break;
        }
        case C2S.READY: { const on = r.u8() !== 0; if (this.inLobby) p.ready = on; this.broadcastLobby(); break; }
        case C2S.START: this.tryStart(p); break;
        case C2S.CONTINUE_BOT: this.continueWithBots(p); break;
        case C2S.LEAVE: { // voluntary: bot takes the role now, no pause, no reconnect slot
          this.drop(ws, "leave");
          this.players = this.players.filter(x => x !== p);
          this.broadcastLobby();
          try { ws.close(1000, "leave"); } catch {}
          break;
        }
        case C2S.STROKE:
        case C2S.BACKPADDLE: {
          r.u8(); // side: the player's role is authoritative
          const power = r.u8() / 255;
          if (this.sim && !this.pause && (p.role === Role.Left || p.role === Role.Right)) {
            const team = this.sim.teams[0], wait = team.strokeReady[p.role] - this.sim.time;
            if (wait > 0 && wait <= INPUT_GRACE_S && this.sim.phase === Phase.Running)
              this.deferred.push({ at: team.strokeReady[p.role], arrived: this.sim.time, role: p.role, net: false, power, back: id === C2S.BACKPADDLE, target: 0, dx: 0, dz: 0, rewind: 0 });
            else this.sim.humanStroke(team, p.role, power, id === C2S.BACKPADDLE);
          }
          this.flushEvents();
          break;
        }
        case C2S.NET: {
          const target = r.u16(), dx = r.i16() / 1000, dz = r.i16() / 1000;
          if (this.sim && !this.pause && p.role === Role.Collector) {
            const rewind = clamp(p.rttMs / 2, 0, TUNING.lagCompCapMs) / 1000; // lag compensation: RTT/2, cap 150 ms
            const team = this.sim.teams[0], wait = team.netReadyAt - this.sim.time;
            if (wait > 0 && wait <= INPUT_GRACE_S && this.sim.phase === Phase.Running)
              this.deferred.push({ at: team.netReadyAt, arrived: this.sim.time, role: Role.Collector, net: true, power: 0, back: false, target, dx, dz, rewind });
            else this.sim.applyThrow(team, target === NO_ENTITY ? NO_ENTITY : target, dx, dz, rewind);
          }
          this.flushEvents();
          break;
        }
        default: break;
      }
    } catch { /* malformed frame */ }
  }

  async webSocketClose(ws: WebSocket, code: number) { this.drop(ws, "close " + code); }
  async webSocketError(ws: WebSocket) { this.drop(ws, "error"); }

  /** Voluntary LEAVE: a bot takes the role at once. Any other drop of a human who plays a role in the running match
   *  freezes the match (PAUSE) until they are back or their grace expires. */
  drop(ws: WebSocket, cause: string) {
    const p = this.players.find(x => x.ws === ws);
    if (!p) return;
    p.ws = null; p.leftAt = Date.now(); p.ready = false;
    const sim = this.sim;
    if (sim && sim.phase !== Phase.Finished && p.role && !sim.teams[0].bot[p.role] && this.roleOwner(p.role) === BOT) {
      if (cause === "leave") { sim.setBot(0, p.role, true); this.flushEvents(); }
      else this.addMissing(p, cause);
    }
    this.broadcastLobby();
  }

  // ---------------- pause / resume (room.ts only: the sim is not stepped while paused) ----------------
  addMissing(p: Player, cause: string) {
    const now = Date.now();
    if (!this.pause) {
      this.pause = { since: now, resumeAt: 0, lastSent: 0, missing: [] };
      this.deferred.length = 0;
      this.mstats.pauses++;
    }
    this.pause.resumeAt = 0; // a drop during the 3-2-1 cancels it
    this.pause.missing = this.pause.missing.filter(m => m.id !== p.id);
    this.pause.missing.push({ id: p.id, name: p.name, role: p.role, since: now, deadline: now + GRACE_MS, cause });
    const kind = cause.startsWith("close") ? "close" : cause;
    this.mstats.causes[kind] = (this.mstats.causes[kind] ?? 0) + 1;
    this.log({ ev: "pause", player: p.name, role: p.role, cause, missing: this.pause.missing.length, simTime: this.sim?.time });
    this.broadcastPause();
  }

  /** Someone came back / was replaced by a bot: keep waiting, start the 3-2-1, or abort if no human is left. */
  afterMissingChange() {
    const ps = this.pause;
    if (!ps) return;
    if (ps.missing.length) { this.broadcastPause(); return; }
    if (this.players.some(x => x.ws)) {
      ps.resumeAt = Date.now() + RESUME_MS;
      this.broadcastResume();
    } else {
      this.mstats.pausedMs += Date.now() - ps.since;
      this.pause = null;
      this.lastHumanAt = Date.now(); // keep the aborted result for players reconnecting in the next IDLE_STOP_MS
      this.log({ ev: "abort", reason: "all humans gone", ...this.mstats });
      this.sim?.abort(); // EndReason Aborted: no game over, no score
    }
  }

  pauseTick(now: number) {
    const ps = this.pause!;
    const expired = ps.missing.filter(m => now >= m.deadline);
    if (expired.length) {
      ps.missing = ps.missing.filter(m => now < m.deadline);
      for (const m of expired) {
        if (this.sim && this.roleOwner(m.role) === BOT) this.sim.setBot(0, m.role, true);
        this.mstats.graceExpired++;
        this.log({ ev: "grace_expired", player: m.name, role: m.role });
      }
      this.flushEvents();
      this.afterMissingChange();
    }
    if (!this.pause) return;
    if (ps.resumeAt) {
      if (now >= ps.resumeAt) {
        this.mstats.pausedMs += now - ps.since;
        this.log({ ev: "resume", pausedMs: now - ps.since, simTime: this.sim?.time });
        this.pause = null;
      }
    } else if (now - ps.lastSent >= PAUSE_EVERY_MS) this.broadcastPause();
  }

  /** Host skips the wait: bots take every missing role, then 3-2-1. */
  continueWithBots(p: Player) {
    const ps = this.pause;
    if (!ps || ps.resumeAt || !ps.missing.length) return;
    this.updateHost();
    if (p.id !== this.hostId) return this.notice(p, "Only the host can continue with bots");
    for (const m of ps.missing) if (this.sim && this.roleOwner(m.role) === BOT) this.sim.setBot(0, m.role, true);
    this.mstats.continueWithBot++;
    this.log({ ev: "continue_with_bot", by: p.name, missing: ps.missing.map(m => m.name) });
    ps.missing = [];
    this.flushEvents();
    this.afterMissingChange();
  }

  pauseBytes() {
    const ps = this.pause!, now = Date.now(), w = this.w.reset();
    w.u8w(S2C.PAUSE).u8w(1).u8w(ps.missing.length);
    for (const m of ps.missing) w.u8w(m.id).str(m.name).u8w(m.role).u8w(clamp(Math.ceil((m.deadline - now) / 1000), 0, 255));
    return w.bytes();
  }

  resumeBytes() {
    const w = this.w.reset();
    w.u8w(S2C.RESUME).u16(clamp(this.pause!.resumeAt - Date.now(), 0, 65535));
    return w.bytes();
  }

  broadcastPause() { this.pause!.lastSent = Date.now(); this.broadcast(this.pauseBytes()); }
  broadcastResume() { this.broadcast(this.resumeBytes()); }
  sendPauseState(p: Player) { if (!p.ws || !this.pause) return; try { p.ws.send(this.pause.resumeAt ? this.resumeBytes() : this.pauseBytes()); } catch {} }

  log(o: Record<string, unknown>) { console.log(JSON.stringify({ room: this.code, ...o })); }

  // ---------------- loop ----------------
  // Drift-free scheduling: each timeout aims at the next ideal tick time (setInterval drifted ~0.3 ms/tick,
  // producing a 0-16 ms sawtooth in snapshot timing and 2-snapshot bursts).
  startLoop() {
    if (this.timer) return;
    this.lastStepAt = Date.now();
    this.schedule();
  }

  schedule() {
    const next = this.lastStepAt + 1000 / TICK_HZ;
    this.timer = setTimeout(() => this.loop(), Math.max(0, Math.ceil(next - Date.now())));
  }

  loop() {
    this.timer = null;
    const now = Date.now();
    // dead-socket detection: mobile links often die silently (half-open TCP, screen lock, handover) and never fire webSocketClose
    for (const p of this.players) {
      if (!p.ws || now - p.lastMsgAt <= DEAD_MS) continue;
      const ws = p.ws;
      this.log({ ev: "dead_socket", player: p.name, silentMs: now - p.lastMsgAt });
      try { ws.close(4001, "timeout"); } catch {}
      this.drop(ws, "timeout");
    }
    if (this.players.some(p => p.ws)) this.lastHumanAt = now;
    else if (!this.pause && now - this.lastHumanAt > IDLE_STOP_MS) {
      this.sim?.abort(); this.sim = null;
      return;
    }
    // fixed-step catch-up, max 4 steps per callback, one snapshot per callback (no bursts)
    let steps = Math.floor((now - this.lastStepAt) * TICK_HZ / 1000);
    if (steps > 4) { this.lastStepAt = now - 4000 / TICK_HZ; steps = 4; }
    if (this.pause) {
      // frozen: room clock keeps ticking, the sim does not (no time, obstacle or collision advances)
      this.tick += steps;
      this.lastStepAt += steps * 1000 / TICK_HZ;
      this.pauseTick(now);
      this.schedule();
      return;
    }
    for (let i = 0; i < steps; i++) this.step(i === steps - 1);
    this.lastStepAt += steps * 1000 / TICK_HZ;
    this.schedule();
  }

  step(sendSnapshot: boolean) {
    this.tick++;
    const sim = this.sim;
    if (!sim) return;
    if (this.deferred.length) this.applyDeferred(sim);
    if (sim.phase !== Phase.Finished) sim.step(1 / TICK_HZ);
    this.flushEvents();
    if (sim.phase === Phase.Finished) {
      if (!this.resultsSent) {
        this.resultsSent = true;
        for (const p of this.players) { p.ready = false; if (p.ws) this.sendResults(p.ws); } // rematch needs READY again
        this.log({ ev: "match_end", reason: sim.results?.reason, ...this.mstats });
        this.broadcastLobby();
      }
      return;
    }
    if (!sendSnapshot || this.tick - this.lastSnapTick < TICK_HZ / this.snapshotHz) return;
    this.lastSnapTick = this.tick;
    this.broadcast(this.buildSnapshot(sim));
  }

  applyDeferred(sim: MatchSim) {
    const team = sim.teams[0];
    const keep: Deferred[] = [];
    for (const d of this.deferred) {
      if (d.at > sim.time + 1e-6) { keep.push(d); continue; }
      if (sim.phase !== Phase.Running) continue;
      // lag comp grows by the time the input waited (still capped)
      if (d.net) sim.applyThrow(team, d.target, d.dx, d.dz, Math.min(d.rewind + (sim.time - d.arrived), TUNING.lagCompCapMs / 1000));
      else sim.humanStroke(team, d.role, d.power, d.back);
    }
    this.deferred = keep;
  }

  /** Broadcast pending sim events now (also called right after input so teammates see strokes ~half a tick sooner). */
  flushEvents() {
    const evs = this.sim?.events;
    if (!evs || !evs.length) return;
    for (let i = 0; i < evs.length; i += 255) {
      const n = Math.min(255, evs.length - i);
      const w = this.w.reset();
      w.u8w(S2C.EVENTS).u8w(n);
      for (let k = i; k < i + n; k++) { const e = evs[k]; w.u8w(e.type).u8w(e.team).u8w(e.role).u16(e.entity).i16(e.value).u8w(e.extra).i16(e.x * 100).f32(e.z); }
      this.broadcast(w.bytes());
    }
    evs.length = 0;
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
    w.f64(Date.now()); // trailing server send time (ignored by v2 clients; used by tools/jitter.ts)
    return w.bytes();
  }
}
