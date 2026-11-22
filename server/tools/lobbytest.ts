// Phase B READY/START rules, 3 clients in one fresh room. Usage: node tools/lobbytest.ts [baseUrl]
// Checks: host = first human, START from non-host rejected, START with one unready rejected, all ready -> starts,
// READY reset on match start, role locked while READY, rematch requires READY again, host handover on leave.
const base = (process.argv[2] ?? "https://mottainai-v4.mottainai-server.workers.dev").replace(/^http/, "ws");
const room = "LB" + Math.floor(Math.random() * 9000 + 1000);
const dec = new TextDecoder();
type Lobby = { phase: number; players: { id: number; name: string; role: number; ready: boolean; connected: boolean }[]; host: number };
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
let failures = 0;
const check = (ok: boolean, what: string) => { console.log(`${ok ? "PASS" : "FAIL"}  ${what}`); if (!ok) failures++; };

class C {
  ws: WebSocket; id = 0; lobby: Lobby | null = null; notices: string[] = []; matchStarts = 0; results = 0;
  name: string;
  constructor(name: string, role: number, rm = room) {
    this.name = name;
    this.ws = new WebSocket(`${base}/ws/room/${rm}?token=lb${name}${Date.now()}&name=${name}&role=${role}`);
    this.ws.binaryType = "arraybuffer";
    this.ws.onmessage = ev => this.on(new DataView(ev.data as ArrayBuffer));
  }
  on(dv: DataView) {
    const s = (o: number) => { const n = dv.getUint8(o); return [dec.decode(new Uint8Array(dv.buffer, o + 1, n)), o + 1 + n] as const; };
    switch (dv.getUint8(0)) {
      case 0x80: this.id = dv.getUint8(3); break;
      case 0x81: {
        let o = 1; const phase = dv.getUint8(o++); const n = dv.getUint8(o++); const players: Lobby["players"] = [];
        for (let i = 0; i < n; i++) { const id = dv.getUint8(o++); const [name, o2] = s(o); o = o2; const role = dv.getUint8(o++); const f = dv.getUint8(o++); players.push({ id, name, role, ready: (f & 1) !== 0, connected: (f & 2) !== 0 }); }
        o += 3;
        const host = o < dv.byteLength ? dv.getUint8(o) : 0;
        this.lobby = { phase, players, host };
        break;
      }
      case 0x82: this.matchStarts++; break;
      case 0x85: this.results++; break;
      case 0x87: this.notices.push(s(1)[0]); break;
    }
  }
  send(...bytes: number[]) { this.ws.send(new Uint8Array(bytes).buffer); }
  ready(on: boolean) { this.send(0x02, on ? 1 : 0); }
  start() { this.send(0x03); }
  role(r: number) { this.send(0x01, r); }
  me() { return this.lobby?.players.find(p => p.id === this.id); }
  open() { return new Promise<void>((res, rej) => { this.ws.onopen = () => res(); this.ws.onerror = () => rej(new Error("ws " + this.name)); }); }
}

async function main() {
  const a = new C("Ana", 1); await a.open(); await sleep(600);
  const b = new C("Ben", 3); await b.open(); await sleep(300);
  const c = new C("Cy", 2); await c.open(); await sleep(1200);
  check(a.lobby?.host === a.id && b.lobby?.host === a.id, `host = first human (host ${b.lobby?.host}, Ana ${a.id})`);

  b.start(); await sleep(800);
  check(a.matchStarts === 0 && b.notices.length > 0, `START from non-host rejected (notice: "${b.notices.at(-1)}")`);

  a.ready(true); b.ready(true); await sleep(800);
  a.start(); await sleep(800);
  check(a.matchStarts === 0 && /Cy/.test(a.notices.at(-1) ?? ""), `START with one unready rejected (notice: "${a.notices.at(-1)}")`);


  c.ready(true); await sleep(800);
  check(a.lobby!.players.every(p => p.ready), "all 3 shown READY in LOBBY");
  a.start(); await sleep(1500);
  check(a.matchStarts === 1 && b.matchStarts === 1 && c.matchStarts === 1, "all ready + host START -> match starts for everyone");
  check(a.lobby!.players.every(p => !p.ready), "READY reset to false when the match starts");
  a.ready(true); await sleep(500);
  check(!a.me()?.ready, "READY ignored during the match");
  a.start(); await sleep(500);
  check(a.matchStarts === 1, "START ignored while a match is running");

  // host leaves voluntarily -> next connected human becomes host
  a.send(0x04); await sleep(1200);
  check(b.lobby?.host === b.id, `host handover after the host leaves (host ${b.lobby?.host}, Ben ${b.id})`);
  b.start(); await sleep(500);
  check(b.matchStarts === 1, "new host START during a running match ignored");

  b.ws.close(); c.ws.close();
  await rematch();
  console.log(failures ? `${failures} FAILED` : "ALL PASS");
  process.exit(failures ? 1 : 0);
}

/// Rematch: a solo host plays a whole match (bots crash or time out), then must READY again before START works.
async function rematch() {
  const roomR = "LR" + Math.floor(Math.random() * 9000 + 1000);
  const s = new C("Solo", 1, roomR);
  await s.open(); await sleep(800);
  s.start(); await sleep(600);
  check(s.matchStarts === 0, "solo START without READY rejected");
  s.ready(true); await sleep(400); s.role(3); await sleep(600);
  check(s.me()?.role === 1, "role change refused while READY");
  s.ready(false); await sleep(400); s.role(3); await sleep(600);
  check(s.me()?.role === 3, "role change allowed after un-READY");
  s.ready(true); await sleep(400); s.start(); await sleep(800);
  check(s.matchStarts === 1, "solo READY + START works");
  const ping = setInterval(() => { const b = new ArrayBuffer(9); const d = new DataView(b); d.setUint8(0, 0x20); d.setFloat64(1, performance.now(), true); s.ws.send(b); }, 500);
  const t0 = Date.now();
  while (s.results === 0 && Date.now() - t0 < 140_000) await sleep(500);
  check(s.results === 1, `match ended with RESULTS after ${((Date.now() - t0) / 1000).toFixed(0)} s`);
  await sleep(800);
  check(s.lobby?.phase === 3 && !s.me()?.ready, "after the match: back in lobby state, READY reset");
  s.start(); await sleep(800);
  check(s.matchStarts === 1, "REMATCH without READY rejected");
  s.ready(true); await sleep(400); s.start(); await sleep(800);
  check(s.matchStarts === 2, "REMATCH after READY starts a new match");
  clearInterval(ping); s.ws.close();
}

main().catch(e => { console.error(e); process.exit(1); });
setTimeout(() => { console.error("TIMEOUT"); process.exit(2); }, 240_000);
