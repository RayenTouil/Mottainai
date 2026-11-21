import { DurableObject } from "cloudflare:workers";
import { C2S, PROTOCOL_VERSION, Reader, S2C, Writer } from "./protocol";

const TICK_HZ = 60;
const IDLE_STOP_MS = 30_000;
const RECONNECT_MS = 60_000;
const BOT = 0xff;

interface Player {
  id: number; token: string; name: string; role: number; ready: boolean;
  ws: WebSocket | null; leftAt: number; backlogMs: number; fps: number; rttMs: number; netType: number;
}

/** Authoritative room. S0: lobby + ping/pong + fake moving snapshot stream. */
export class RoomDO extends DurableObject {
  code = "";
  colo = "?";
  coloPromise: Promise<void> | null = null;
  players: Player[] = [];
  nextId = 1;
  phase = 0;
  tick = 0;
  timer: ReturnType<typeof setInterval> | null = null;
  lastHumanAt = Date.now();
  snapshotHz = 60;
  w = new Writer(4096);

  constructor(ctx: DurableObjectState, env: any) {
    super(ctx, env);
    // rebuild players from hibernated sockets
    for (const ws of ctx.getWebSockets()) {
      const a = ws.deserializeAttachment() as Partial<Player> & { code?: string } | null;
      if (!a?.token) continue;
      this.code = a.code ?? this.code;
      this.players.push({ id: a.id!, token: a.token, name: a.name ?? "?", role: a.role ?? 0, ready: false, ws, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0 });
      this.nextId = Math.max(this.nextId, (a.id ?? 0) + 1);
    }
  }

  ensureColo() {
    this.coloPromise ??= fetch("https://cloudflare.com/cdn-cgi/trace")
      .then(r => r.text())
      .then(t => { this.colo = /colo=(\w+)/.exec(t)?.[1] ?? "?"; })
      .catch(() => {});
    return this.coloPromise;
  }

  async fetch(req: Request): Promise<Response> {
    const url = new URL(req.url);
    this.code = url.searchParams.get("code") ?? this.code;
    const token = url.searchParams.get("token") || crypto.randomUUID();
    const name = (url.searchParams.get("name") || "Player").slice(0, 16);
    const wantRole = Number(url.searchParams.get("role") || 0) | 0;
    await this.ensureColo();

    const pair = new WebSocketPair();
    const [client, server] = [pair[0], pair[1]];
    this.ctx.acceptWebSocket(server);

    let p = this.players.find(x => x.token === token && (x.ws === null ? Date.now() - x.leftAt < RECONNECT_MS : true));
    if (p) {
      try { p.ws?.close(4000, "replaced"); } catch {}
      p.ws = server; p.name = name; p.leftAt = 0;
    } else {
      this.players = this.players.filter(x => x.ws !== null || Date.now() - x.leftAt < RECONNECT_MS);
      p = { id: this.nextId++, token, name, role: 0, ready: false, ws: server, leftAt: 0, backlogMs: 0, fps: 0, rttMs: 0, netType: 0 };
      this.players.push(p);
      p.role = this.pickRole(wantRole, p);
    }
    server.serializeAttachment({ id: p.id, token: p.token, name: p.name, role: p.role, code: this.code });

    const w = this.w.reset();
    w.u8w(S2C.WELCOME).u16(PROTOCOL_VERSION).u8w(p.id).str(this.code).str(this.colo).u32(this.tick).u8w(TICK_HZ).u8w(this.snapshotHz);
    server.send(w.bytes());
    this.lastHumanAt = Date.now();
    this.broadcastLobby();
    this.startLoop();
    return new Response(null, { status: 101, webSocket: client });
  }

  pickRole(want: number, p: Player) {
    const taken = (r: number) => this.players.some(x => x !== p && x.role === r && (x.ws || Date.now() - x.leftAt < RECONNECT_MS));
    if (want >= 1 && want <= 3 && !taken(want)) return want;
    for (const r of [1, 3, 2]) if (!taken(r)) return r;
    return 0; // spectator
  }

  roleOwner(r: number) {
    const p = this.players.find(x => x.role === r && x.ws);
    return p ? p.id : BOT;
  }

  broadcastLobby() {
    const w = this.w.reset();
    const live = this.players.filter(x => x.ws || Date.now() - x.leftAt < RECONNECT_MS);
    w.u8w(S2C.LOBBY).u8w(this.phase).u8w(live.length);
    for (const p of live) w.u8w(p.id).str(p.name).u8w(p.role).u8w((p.ready ? 1 : 0) | (p.ws ? 2 : 0));
    for (const r of [1, 2, 3]) w.u8w(this.roleOwner(r));
    this.broadcast(w.bytes());
  }

  broadcast(buf: ArrayBuffer) {
    for (const p of this.players) if (p.ws) { try { p.ws.send(buf); } catch {} }
  }

  async webSocketMessage(ws: WebSocket, msg: ArrayBuffer | string) {
    if (typeof msg === "string") return;
    const p = this.players.find(x => x.ws === ws);
    if (!p || msg.byteLength < 1) return;
    const r = new Reader(msg);
    try {
      switch (r.u8()) {
        case C2S.PING: {
          const t = r.f64();
          const w = this.w.reset();
          w.u8w(S2C.PONG).f64(t).u32(this.tick).f32(this.tick / TICK_HZ);
          ws.send(w.bytes());
          break;
        }
        case C2S.STATS:
          p.fps = r.u8(); p.rttMs = r.u16(); p.netType = r.u8(); p.backlogMs = r.u16();
          this.snapshotHz = this.players.some(x => x.ws && x.backlogMs > 100) ? 30 : 60;
          break;
        case C2S.ROLE: {
          const want = r.u8();
          if (this.phase === 0) { p.role = this.pickRole(want, p) === want ? want : p.role; ws.serializeAttachment({ id: p.id, token: p.token, name: p.name, role: p.role, code: this.code }); this.broadcastLobby(); }
          break;
        }
        case C2S.READY: p.ready = r.u8() !== 0; this.broadcastLobby(); break;
        default: break; // gameplay events handled by the sim (S1)
      }
    } catch { /* malformed frame: ignore */ }
  }

  async webSocketClose(ws: WebSocket) { this.drop(ws); }
  async webSocketError(ws: WebSocket) { this.drop(ws); }

  drop(ws: WebSocket) {
    const p = this.players.find(x => x.ws === ws);
    if (!p) return;
    p.ws = null; p.leftAt = Date.now(); p.ready = false;
    this.broadcastLobby();
  }

  startLoop() {
    if (this.timer) return;
    this.timer = setInterval(() => this.step(), 1000 / TICK_HZ);
  }

  step() {
    this.tick++;
    const now = Date.now();
    if (this.players.some(p => p.ws)) this.lastHumanAt = now;
    else if (now - this.lastHumanAt > IDLE_STOP_MS) { clearInterval(this.timer!); this.timer = null; return; }
    if (this.snapshotHz === 30 && (this.tick & 1)) return;
    this.sendFakeSnapshot();
  }

  /** S0 placeholder: raft drifting downstream + bobbing waste/obstacles. */
  sendFakeSnapshot() {
    const t = this.tick / TICK_HZ;
    const z = (t * 5) % 600;
    const w = this.w.reset();
    w.u8w(S2C.SNAPSHOT).u32(this.tick).f32(t).u8w(this.phase).u16(12000).u8w(this.snapshotHz);
    w.u8w(1).i32(0).u8w(0).u8w(1).u8w(0).u8w(0).u8w(0)
      .f32(Math.sin(t * 0.5) * 4).f32(z).i16(Math.cos(t * 0.5) * 200).i16(0).i16(500).i16(0);
    w.f32(z).u16(40);
    for (let i = 0; i < 40; i++) {
      const ez = ((i * 7 - z) % 75 + 75) % 75 - 15;
      const kind = i % 5 === 0 ? 2 : 1;
      w.u16(i).u8w((kind << 4) | (i % 6)).i16(Math.sin(i * 1.7 + t) * 900).i16(ez * 100).i8(Math.sin(t * 2 + i) * 5).u8w(i * 37).u8w(0);
    }
    this.broadcast(w.bytes());
  }
}
