// Phase C disconnection / reconnection tests against a deployed RoomDO.
// Usage: node tools/reconnect.ts [baseUrl] [T0|T1|...|T7 ...]   (default: v4 Worker, all of T1..T7)
//   T0 = root-cause demonstration (silent stall vs hard close), prints observations, never fails.
const args = process.argv.slice(2);
const base = (args.find(a => a.startsWith("http")) ?? "https://mottainai-v4.mottainai-server.workers.dev").replace(/^http/, "ws");
const only = args.filter(a => /^T\d+$/.test(a));
const dec = new TextDecoder();
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
const now = () => performance.now();
const GRACE_S = 30;

type Snap = { t: number; tick: number; time: number; phase: number; score: number; x: number; z: number };
type Pause = { t: number; reason: number; missing: { id: number; name: string; role: number; graceS: number }[] };

class P {
  ws!: WebSocket; id = 0; token: string; name: string; role: number; room: string;
  stalled = false; open = false; pinger: ReturnType<typeof setInterval> | null = null;
  lobby: { t: number; phase: number; players: { id: number; name: string; role: number; ready: boolean; connected: boolean }[]; host: number } | null = null;
  snaps: Snap[] = []; events: { t: number; type: number; role: number }[] = []; pauses: Pause[] = []; resumes: { t: number; ms: number }[] = [];
  results: { t: number; reason: number }[] = []; notices: string[] = []; matchStarts: number[] = []; pongs: { t: number; simTime: number }[] = []; welcomes: number[] = [];
  closedAt = 0; extra = "";
  constructor(room: string, name: string, role: number) { this.room = room; this.name = name; this.role = role; this.token = `rc${name}${Date.now()}${Math.floor(Math.random() * 1e6)}`; }

  connect() {
    return new Promise<void>((res, rej) => {
      this.stalled = false;
      const ws = new WebSocket(`${base}/ws/room/${this.room}?token=${this.token}&name=${this.name}&role=${this.role}${this.extra}`);
      ws.binaryType = "arraybuffer";
      this.ws = ws;
      ws.onmessage = ev => { if (!this.stalled && ws === this.ws) this.on(new DataView(ev.data as ArrayBuffer)); };
      ws.onopen = () => { this.open = true; this.startPing(); res(); };
      ws.onclose = () => { if (ws === this.ws) { this.open = false; this.closedAt = now(); this.stopPing(); } };
      ws.onerror = () => rej(new Error("ws error " + this.name));
    });
  }
  startPing() { this.stopPing(); this.pinger = setInterval(() => { if (!this.stalled) this.ping(); }, 500); this.ping(); }
  stopPing() { if (this.pinger) clearInterval(this.pinger); this.pinger = null; }
  ping() { const b = new ArrayBuffer(9); const d = new DataView(b); d.setUint8(0, 0x20); d.setFloat64(1, now(), true); this.send(b); }
  send(b: ArrayBuffer | Uint8Array) { try { if (this.ws.readyState === 1) this.ws.send(b); } catch {} }
  bytes(...b: number[]) { this.send(new Uint8Array(b)); }
  ready() { this.bytes(0x02, 1); }
  start() { this.bytes(0x03); }
  leave() { this.bytes(0x04); }
  continueWithBot() { this.bytes(0x05); }
  /** Silent stall: keep the TCP/WebSocket open but stop sending and ignore everything received. */
  stall() { this.stalled = true; this.stopPing(); }
  /** Hard close (app killed / socket closed). */
  kill() { this.stopPing(); try { this.ws.close(); } catch {} this.open = false; this.closedAt = now(); }

  on(dv: DataView) {
    const t = now();
    const str = (o: number) => { const n = dv.getUint8(o); return [dec.decode(new Uint8Array(dv.buffer, o + 1, n)), o + 1 + n] as const; };
    switch (dv.getUint8(0)) {
      case 0x80: this.id = dv.getUint8(3); this.welcomes.push(t); break;
      case 0x81: {
        let o = 1; const phase = dv.getUint8(o++); const n = dv.getUint8(o++); const players = [];
        for (let i = 0; i < n; i++) { const id = dv.getUint8(o++); const [name, o2] = str(o); o = o2; const role = dv.getUint8(o++); const f = dv.getUint8(o++); players.push({ id, name, role, ready: (f & 1) !== 0, connected: (f & 2) !== 0 }); }
        o += 3; const host = o < dv.byteLength ? dv.getUint8(o) : 0;
        this.lobby = { t, phase, players, host }; break;
      }
      case 0x82: this.matchStarts.push(t); break;
      case 0x83: this.snaps.push({ t, tick: dv.getUint32(1, true), time: dv.getFloat32(5, true), phase: dv.getUint8(9), score: dv.getInt32(14, true), x: dv.getFloat32(23, true), z: dv.getFloat32(27, true) }); break;
      case 0x84: { const n = dv.getUint8(1); for (let i = 0; i < n; i++) this.events.push({ t, type: dv.getUint8(2 + i * 14), role: dv.getUint8(4 + i * 14) }); break; }
      case 0x85: this.results.push({ t, reason: dv.getUint8(1) }); break;
      case 0x86: this.pongs.push({ t, simTime: dv.getFloat32(13, true) }); break;
      case 0x87: this.notices.push(str(1)[0]); break;
      case 0x8a: {
        let o = 1; const reason = dv.getUint8(o++); const n = dv.getUint8(o++); const missing = [];
        for (let i = 0; i < n; i++) { const id = dv.getUint8(o++); const [name, o2] = str(o); o = o2; const role = dv.getUint8(o++); const graceS = dv.getUint8(o++); missing.push({ id, name, role, graceS }); }
        this.pauses.push({ t, reason, missing }); break;
      }
      case 0x8b: this.resumes.push({ t, ms: dv.getUint16(1, true) }); break;
    }
  }
  lastSnap() { return this.snaps[this.snaps.length - 1]; }
  snapsAfter(t: number) { return this.snaps.filter(s => s.t > t); }
  connected(id: number) { return this.lobby?.players.find(p => p.id === id)?.connected ?? false; }
}

const room = (tag: string) => tag + Math.floor(Math.random() * 9000 + 1000);
async function waitFor(cond: () => boolean, ms: number, step = 50) { const t0 = now(); while (!cond()) { if (now() - t0 > ms) return false; await sleep(step); } return true; }

/** Join players in order (first = host), READY all, START, wait until RUNNING. */
async function startMatch(rm: string, specs: [string, number][]) {
  const ps: P[] = [];
  for (const [n, r] of specs) { const p = new P(rm, n, r); await p.connect(); ps.push(p); await sleep(250); }
  await waitFor(() => ps.every(p => p.id > 0), 5000);
  for (const p of ps) p.ready();
  await sleep(600);
  ps[0].start();
  const ok = await waitFor(() => ps.every(p => p.lastSnap()?.phase === 2), 8000);
  if (!ok) throw new Error(`${rm}: match did not reach RUNNING`);
  await sleep(1500);
  return ps;
}

type Result = { name: string; pass: boolean; lines: string[] };
const results: Result[] = [];
function report(name: string) {
  const r: Result = { name, pass: true, lines: [] };
  results.push(r);
  return {
    check(ok: boolean, what: string) { r.lines.push(`${ok ? "PASS" : "FAIL"}  ${what}`); if (!ok) r.pass = false; },
    info(s: string) { r.lines.push(`      ${s}`); },
  };
}
const f1 = (x: number) => x.toFixed(1), f3 = (x: number) => x.toFixed(3);

// ---------------------------------------------------------------- T0 root cause
async function T0() {
  const r = report("T0 root cause: silent stall vs hard close (observations)");
  // (a) silent stall of the LEFT paddler, COLLECTOR human observes for 20 s
  {
    const [a, b] = await startMatch(room("RA"), [["Ana", 1], ["Ben", 3]]);
    const tStall = now(), simAt = b.lastSnap().time;
    a.stall();
    await waitFor(() => b.results.length > 0 || b.pauses.length > 0, Number(process.env.T0_MS ?? 20000));
    const dt = (now() - tStall) / 1000;
    const bot = b.events.find(e => e.t > tStall && e.type === 12 && e.role === 1);
    const go = b.events.find(e => e.t > tStall && e.type === 10);
    r.info(`silent stall: after ${f1(dt)} s Ana still "connected" in LOBBY = ${b.connected(a.id)}, bot took LEFT = ${!!bot}, PAUSE = ${b.pauses.length > 0}`);
    r.info(`silent stall: sim advanced ${f1(b.lastSnap().time - simAt)} s while Ana sent nothing; GAME OVER (hit) = ${!!go}${go ? ` after ${f1((go.t - tStall) / 1000)} s` : ""}; results ${b.results.map(x => x.reason).join(",") || "-"}`);
    a.kill(); b.kill();
  }
  // (b) hard close of the LEFT paddler
  {
    const [a, b] = await startMatch(room("RB"), [["Ana", 1], ["Ben", 3]]);
    const tKill = now();
    a.kill();
    await waitFor(() => b.events.some(e => e.t > tKill && e.type === 12) || b.pauses.length > 0, 5000);
    const bot = b.events.find(e => e.t > tKill && e.type === 12);
    r.info(`hard close: bot took LEFT after ${bot ? f1((bot.t - tKill)) + " ms" : "-"}, PAUSE = ${b.pauses.length > 0}`);
    b.kill();
  }
}

// ---------------------------------------------------------------- T1 + T7
async function T1() {
  const r = report("T1 hard close -> PAUSE -> reconnect 5 s -> RESUME 3-2-1 -> continues (T7: sim frozen)");
  const [a, b] = await startMatch(room("RC"), [["Ana", 1], ["Ben", 3]]);
  const tKill = now();
  a.kill();
  const paused = await waitFor(() => b.pauses.length > 0, 3000);
  r.check(paused, `PAUSE received by Ben ${paused ? f1(b.pauses[0].t - tKill) + " ms" : "never"} after Ana's socket closed`);
  r.check(paused && b.pauses[0].missing.some(m => m.id === a.id && m.name === "Ana" && m.graceS >= GRACE_S - 2), `PAUSE lists Ana with ~${GRACE_S} s grace (${JSON.stringify(b.pauses[0]?.missing)})`);
  const before = b.snaps.filter(s => s.t < (b.pauses[0]?.t ?? now())).at(-1)!;
  await sleep(1000);
  const pongsDuring0 = b.pongs.filter(p => p.t > (b.pauses[0]?.t ?? 0) + 200);
  await sleep(4000 - 1000);
  const pongsDuring = b.pongs.filter(p => p.t > (b.pauses[0]?.t ?? 0) + 200);
  const snapsDuring = b.snaps.filter(s => s.t > (b.pauses[0]?.t ?? 0) + 200);
  r.check(snapsDuring.every(s => s.time === before.time), `T7 no sim progress during pause (${snapsDuring.length} snapshots, times ${[...new Set(snapsDuring.map(s => f3(s.time)))].join(",") || "-"})`);
  r.check(pongsDuring.length >= 4 && pongsDuring.every(p => Math.abs(p.simTime - before.time) < 1e-4), `T7 server match time frozen at ${f3(before.time)} s in ${pongsDuring.length} PONGs (${pongsDuring0.length} in 1st s)`);
  const tBack = now();
  await a.connect();
  const resumed = await waitFor(() => a.resumes.length > 0 && b.resumes.length > 0, 3000);
  r.check(resumed, `RESUME to both ${resumed ? f1(b.resumes[0].t - tBack) + " ms" : "never"} after Ana reconnected (countdown ${b.resumes[0]?.ms} ms)`);
  const aSnap = a.snapsAfter(tBack)[0];
  r.check(a.matchStarts.some(t => t > tBack) && !!aSnap && aSnap.time === before.time, `Ana got MATCH_START + fresh snapshot at the frozen time (${aSnap ? f3(aSnap.time) : "-"} vs ${f3(before.time)})`);
  const tRes = b.resumes[0]?.t ?? now();
  await waitFor(() => b.snaps.some(s => s.t > tRes && s.time > before.time), 6000);
  const firstAfter = b.snaps.find(s => s.t > tRes && s.time > before.time);
  const waited = firstAfter ? firstAfter.t - tRes : -1;
  r.check(!!firstAfter && waited > 2600 && waited < 4000, `sim steps again after the 3-2-1 (${f1(waited)} ms after RESUME)`);
  if (firstAfter) {
    const dTime = firstAfter.time - before.time, dPos = Math.hypot(firstAfter.x - before.x, firstAfter.z - before.z);
    r.check(dTime <= 3 / 60 + 1e-4, `T7 match time continues from the frozen value (+${f3(dTime)} s at first snapshot)`);
    r.check(dPos < 0.4, `T7 raft position continues from the frozen value (moved ${f3(dPos)} m)`);
    r.check(firstAfter.score === before.score, `score unchanged across the pause (${before.score} -> ${firstAfter.score})`);
  }
  await sleep(3000);
  r.check(b.lastSnap().phase === 2 && b.results.length === 0, `match still running 3 s later (phase ${b.lastSnap().phase}, time ${f1(b.lastSnap().time)} s)`);
  r.check(!a.events.concat(b.events).some(e => e.type === 10), "no GAME OVER");
  a.kill(); b.kill();
}

// ---------------------------------------------------------------- T2
async function T2() {
  const r = report("T2 silent stall (socket open, nothing sent/read) -> detected <= 1.5 s -> PAUSE");
  const [a, b] = await startMatch(room("RD"), [["Ana", 1], ["Ben", 3]]);
  await sleep(300); // let a ping go out
  const tStall = now();
  a.stall();
  const ok = await waitFor(() => b.pauses.length > 0, 6000, 10);
  const dt = ok ? b.pauses[0].t - tStall : -1;
  r.check(ok && dt <= 1500 + 400, `PAUSE ${ok ? f1(dt) + " ms" : "never"} after Ana went silent (server rule 1.5 s after the last message; + one-way delay)`);
  r.check(ok && b.pauses[0].missing.some(m => m.id === a.id), "PAUSE lists Ana");
  r.check(!b.connected(a.id), "LOBBY shows Ana disconnected");
  r.check(!b.events.some(e => e.t > tStall && e.type === 10) && b.results.length === 0, "no GAME OVER while Ana is missing");
  a.kill(); b.kill();
}

// ---------------------------------------------------------------- T3
async function T3() {
  const r = report("T3 two players drop, one returns -> still paused, no GAME OVER");
  const [a, b, c] = await startMatch(room("RE"), [["Ana", 1], ["Ben", 2], ["Cy", 3]]);
  a.kill(); await sleep(200); b.kill();
  await waitFor(() => (c.pauses.at(-1)?.missing.length ?? 0) === 2, 4000);
  const before = c.snaps.filter(s => s.t < (c.pauses[0]?.t ?? now())).at(-1)!; // state frozen at pause start
  r.check((c.pauses.at(-1)?.missing.length ?? 0) === 2, `PAUSE lists both missing (${c.pauses.at(-1)?.missing.map(m => m.name).join(", ")})`);
  await sleep(2000);
  await a.connect();
  await sleep(6000);
  const last = c.pauses.at(-1);
  r.check(!!last && last.missing.length === 1 && last.missing[0].id === b.id, `after Ana returns: still waiting for Ben only (${last?.missing.map(m => m.name).join(", ")})`);
  r.check(a.resumes.length === 0 && c.resumes.length === 0, "no RESUME while Ben is missing");
  r.check(a.pongs.at(-1)!.simTime === before.time || Math.abs(a.pongs.at(-1)!.simTime - before.time) < 1e-4, `sim still frozen (${f3(a.pongs.at(-1)!.simTime)} vs ${f3(before.time)})`);
  r.check(!a.events.concat(c.events).some(e => e.type === 10) && a.results.length + c.results.length === 0, "returned player sees no GAME OVER / RESULTS");
  r.check(a.pauses.length > 0 && a.pauses.at(-1)!.missing.some(m => m.id === b.id), "returned player is told who is still missing");
  await b.connect();
  const ok = await waitFor(() => c.resumes.length > 0, 3000);
  r.check(ok, "RESUME once Ben is back too");
  a.kill(); b.kill(); c.kill();
}

// ---------------------------------------------------------------- T4
async function T4() {
  const r = report(`T4 grace (${GRACE_S} s) expires -> bot takes the role -> RESUME`);
  const [a, b] = await startMatch(room("RF"), [["Ana", 1], ["Ben", 3]]);
  const tKill = now();
  a.kill();
  await waitFor(() => b.pauses.length > 0, 3000);
  const graces = new Set<number>();
  const ok = await waitFor(() => { b.pauses.forEach(p => p.missing.forEach(m => graces.add(m.graceS))); return b.resumes.length > 0; }, (GRACE_S + 6) * 1000, 100);
  const dt = ok ? (b.resumes[0].t - tKill) / 1000 : -1;
  r.check(ok && dt > GRACE_S - 1 && dt < GRACE_S + 3, `RESUME ${f1(dt)} s after Ana left`);
  r.check(graces.size >= 5, `PAUSE updates count the grace down (${[...graces].sort((x, y) => y - x).slice(0, 6).join(", ")}...)`);
  r.check(b.events.some(e => e.type === 12 && e.role === 1), "BotTakeover event for LEFT");
  const tRes = b.resumes[0]?.t ?? now();
  await waitFor(() => b.snaps.some(s => s.t > tRes + 3000), 6000);
  const after = b.snaps.filter(s => s.t > tRes + 3000);
  const frozen = b.snaps.filter(s => s.t < (b.pauses[0]?.t ?? now())).at(-1)!.time;
  await sleep(2000);
  r.check(after.length > 0 && b.lastSnap().phase === 2 && b.lastSnap().time > frozen + 1.5, `match running again with the bot (time ${f3(frozen)} -> ${f1(b.lastSnap().time)} s)`);
  b.kill();
}

// ---------------------------------------------------------------- T5
async function T5() {
  const r = report("T5 all humans gone -> grace expires -> Aborted, no score");
  const [a, b] = await startMatch(room("RG"), [["Ana", 1], ["Ben", 3]]);
  a.kill(); await sleep(300); b.kill();
  await sleep((GRACE_S + 4) * 1000);
  const tBack = now();
  await a.connect(); // within the 60 s reconnect window: the room tells us how the match ended
  await waitFor(() => a.results.some(x => x.t > tBack), 4000);
  const res = a.results.find(x => x.t > tBack);
  r.check(!!res && res.reason === 3, `RESULTS reason = ${res?.reason ?? "none"} (3 = Aborted)`);
  r.check(!a.matchStarts.some(t => t > tBack) && !a.snapsAfter(tBack).length, "no MATCH_START / snapshots: the aborted match is not resumed");
  r.check(!a.events.some(e => e.type === 10), "no GAME OVER");
  a.kill();
}

// ---------------------------------------------------------------- T6
async function T6() {
  const r = report("T6 voluntary LEAVE -> bot immediately, no pause");
  const [a, b] = await startMatch(room("RH"), [["Ana", 1], ["Ben", 3]]);
  const t0 = now(), simAt = b.lastSnap().time;
  a.leave();
  const bot = await waitFor(() => b.events.some(e => e.t > t0 && e.type === 12 && e.role === 1), 2000);
  r.check(bot, `BotTakeover for LEFT ${bot ? f1(b.events.find(e => e.t > t0 && e.type === 12)!.t - t0) + " ms" : "never"} after LEAVE`);
  await sleep(3000);
  r.check(b.pauses.length === 0, "no PAUSE");
  r.check(b.lastSnap().time > simAt + 2.5, `sim kept running (+${f1(b.lastSnap().time - simAt)} s in 3 s)`);
  b.kill();
}

// ---------------------------------------------------------------- T8
async function T8() {
  const r = report("T8 host presses Continue with bot -> bot + RESUME without waiting");
  const [a, b] = await startMatch(room("RJ"), [["Ana", 1], ["Ben", 3]]);
  b.kill();
  await waitFor(() => a.pauses.length > 0, 3000);
  const t0 = now();
  a.continueWithBot();
  const ok = await waitFor(() => a.resumes.length > 0, 2000);
  r.check(ok, `RESUME ${ok ? f1(a.resumes[0].t - t0) + " ms" : "never"} after Continue with bot`);
  r.check(a.events.some(e => e.type === 12 && e.role === 3), "BotTakeover for COLLECTOR");
  a.kill();
}

// ---------------------------------------------------------------- T9
async function T9() {
  const r = report("T9 reconnect into a room that lost its match (DO restart) -> NOTICE + lobby, no stale match");
  const p = new P(room("RK"), "Ana", 1);
  p.extra = "&match=123456";
  await p.connect();
  await waitFor(() => p.notices.length > 0 && p.lobby !== null, 3000);
  r.check(p.notices.some(n => /lost/i.test(n)), `NOTICE "${p.notices.join(" | ")}"`);
  r.check(p.lobby?.phase === 0 && p.matchStarts.length === 0, `LOBBY phase ${p.lobby?.phase} (0 = lobby), no MATCH_START`);
  p.kill();
}

// ---------------------------------------------------------------- T10 (end-to-end with a Unity client in E2E_ROOM)
// E2E_MODE=drop: this Node collector drops 5 s into the match and returns 6 s later (Unity client shows the pause overlay)
// E2E_MODE=observe: Node collector watches the Unity client (-suspendat) disappear and come back
async function T10() {
  const r = report("T10 end-to-end with the Unity client (" + (process.env.E2E_MODE ?? "observe") + ")");
  const rm = process.env.E2E_ROOM!;
  const b = new P(rm, "NodeBen", 3);
  if (process.env.E2E_MODE === "watch") b.extra = "&spectate=1"; // watcher holds no role (an idle human paddler would crash the raft)
  await b.connect(); await sleep(300); b.ready();
  r.info("waiting for the Unity client in room " + rm);
  const joined = await waitFor(() => (b.lobby?.players.filter(p => p.connected).length ?? 0) >= 2 && b.lobby!.players.every(p => p.ready || !p.connected), 90000, 200);
  r.check(joined, "Unity client joined and is READY");
  b.start();
  await waitFor(() => b.lastSnap()?.phase === 2, 10000);
  const tRun = now();
  if (process.env.E2E_MODE === "watch") {
    // timeline for the phone tests: every pause / resume, snapshot gaps, until E2E_SECONDS
    const secs = Number(process.env.E2E_SECONDS ?? 75);
    let seenP = 0, seenR = 0, pausedSince = 0, episodes = 0, lastKey = "";
    const until = now() + secs * 1000;
    while (now() < until && b.results.length === 0) {
      while (seenP < b.pauses.length) {
        const p = b.pauses[seenP++];
        const key = p.missing.map(m => m.name).join(",");
        if (!pausedSince) { pausedSince = p.t; episodes++; }
        if (key !== lastKey) r.info(`+${f1((p.t - tRun) / 1000)} s  PAUSE  missing: ${key || "-"}`);
        lastKey = key;
      }
      while (seenR < b.resumes.length) {
        const x = b.resumes[seenR++];
        r.info(`+${f1((x.t - tRun) / 1000)} s  RESUME 3-2-1  (match frozen ${f1((x.t - (pausedSince || x.t)) / 1000)} s before the countdown)`);
        pausedSince = 0; lastKey = "";
      }
      await sleep(50);
    }
    r.info(`pause episodes ${episodes}, resumes ${b.resumes.length}`);
    r.check(!b.events.some(e => e.type === 10) && !b.results.some(x => x.reason === 2), "no GAME OVER during the phone test");
    b.kill();
    return;
  }
  if (process.env.E2E_MODE === "drop") {
    await sleep(5000);
    b.kill(); r.info("Node collector dropped");
    await sleep(6000);
    await b.connect(); r.info("Node collector back");
    await sleep(10000);
    r.check(b.resumes.length > 0, `RESUME after the Node collector returned`);
  } else {
    const ok = await waitFor(() => b.pauses.length > 0, 25000, 20);
    const unity = b.pauses[0]?.missing[0];
    r.check(ok, `PAUSE when the Unity client went to background (${unity ? unity.name : "-"}, ${f1((b.pauses[0]?.t ?? 0) - tRun)} ms after RUNNING)`);
    const back = await waitFor(() => b.resumes.length > 0, 20000, 20);
    r.check(back, `RESUME ${back ? f1(b.resumes[0].t - b.pauses[0].t) + " ms" : "never"} after the PAUSE (= Unity suspend time + reconnect)`);
    await sleep(6000);
  }
  const tail = b.snaps.at(-1);
  r.check(!!tail && tail.phase === 2 && !b.events.some(e => e.type === 10), `match running after the round trip (time ${tail ? f1(tail.time) : "-"} s, no GAME OVER)`);
  b.kill();
}

const all: Record<string, () => Promise<void>> = { T0, T1, T2, T3, T4, T5, T6, T8, T9, T10 };
const run = only.length ? only : ["T1", "T2", "T3", "T4", "T5", "T6", "T8", "T9"];
const t0 = now();
await Promise.all(run.map(n => all[n]().catch(e => { const r = report(n + " crashed"); r.check(false, String(e)); })));
for (const r of results.sort((x, y) => x.name.localeCompare(y.name))) { console.log(`\n${r.pass ? "PASS" : "FAIL"} ${r.name}`); for (const l of r.lines) console.log("  " + l); }
const failed = results.filter(r => !r.pass).length;
console.log(`\n${failed ? failed + " FAILED" : "ALL PASS"}  (${f1((now() - t0) / 1000)} s)`);
process.exit(failed ? 1 : 0);
