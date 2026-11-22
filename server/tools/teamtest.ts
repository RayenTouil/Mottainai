// Phase D teams + leaderboard tests against a deployed Worker. Usage: node tools/teamtest.ts [baseUrl] [api] [match]
// api: create/join/leave, uniqueness, validation, 4th member rejected, leaderboard rank, no token leak.
// match: guest match not counted, team match counted once (reconnect + rematch), aborted not counted, save-crew flow.
const args = process.argv.slice(2);
const http = (args.find(a => a.startsWith("http")) ?? "https://mottainai-v4.mottainai-server.workers.dev").replace(/\/$/, "");
const wsBase = http.replace(/^http/, "ws");
const parts = args.filter(a => a === "api" || a === "match");
const want = (x: string) => !parts.length || parts.includes(x);
const dec = new TextDecoder();
const sleep = (ms: number) => new Promise(r => setTimeout(r, ms));
const now = () => performance.now();
const rnd = () => Math.random().toString(36).slice(2, 7);
const tok = (n: string) => `tt${n}${Date.now()}${Math.floor(Math.random() * 1e6)}`;
let failures = 0;
const lines: string[] = [];
const check = (ok: boolean, what: string) => { lines.push(`${ok ? "PASS" : "FAIL"}  ${what}`); if (!ok) failures++; };
const info = (s: string) => lines.push(`      ${s}`);

async function api(path: string, body?: unknown) {
  const r = await fetch(http + path, body === undefined ? {} : { method: "POST", headers: { "content-type": "application/json" }, body: JSON.stringify(body) });
  let j: any = null; try { j = await r.json(); } catch {}
  return { status: r.status, j, raw: JSON.stringify(j) };
}
const me = async (t: string) => (await api(`/api/team/me?token=${t}`)).j?.team ?? null;
const created: string[] = []; // tokens to clean up (leave -> empty teams are deleted)

class P {
  ws!: WebSocket; id = 0; token: string; open = false; pinger: any; room: string; name: string; role: number;
  lobby: { phase: number; players: { id: number; name: string; role: number; connected: boolean; team: string }[]; host: number } | null = null;
  results: { t: number; reason: number; total: number }[] = []; teamResults: { t: number; counted: boolean; team: string; points: number; rank: number; canSave: boolean }[] = [];
  invites: { team: string; from: string; current: string }[] = []; notices: string[] = []; matchStarts = 0; phase = -1;
  constructor(room: string, name: string, role: number) { this.room = room; this.name = name; this.role = role; this.token = tok(name); }
  connect() {
    return new Promise<void>((res, rej) => {
      const ws = new WebSocket(`${wsBase}/ws/room/${this.room}?token=${this.token}&name=${this.name}&role=${this.role}`);
      ws.binaryType = "arraybuffer"; this.ws = ws;
      ws.onmessage = ev => { if (ws === this.ws) this.on(new DataView(ev.data as ArrayBuffer)); };
      ws.onopen = () => { this.open = true; clearInterval(this.pinger); this.pinger = setInterval(() => this.ping(), 500); res(); };
      ws.onclose = () => { if (ws === this.ws) { this.open = false; clearInterval(this.pinger); } };
      ws.onerror = () => rej(new Error("ws " + this.name));
    });
  }
  ping() { const b = new ArrayBuffer(9); const d = new DataView(b); d.setUint8(0, 0x20); d.setFloat64(1, now(), true); this.send(b); }
  send(b: ArrayBuffer | Uint8Array) { try { if (this.ws.readyState === 1) this.ws.send(b); } catch {} }
  bytes(...b: number[]) { this.send(new Uint8Array(b)); }
  str(id: number, s: string) { const e = new TextEncoder().encode(s); this.send(new Uint8Array([id, e.length, ...e])); }
  ready() { this.bytes(0x02, 1); }
  start() { this.bytes(0x03); }
  teamCreate(name: string) { this.str(0x06, name); }
  answer(yes: boolean) { this.bytes(0x07, yes ? 1 : 0); }
  kill() { clearInterval(this.pinger); try { this.ws.close(); } catch {} this.open = false; }
  on(dv: DataView) {
    const t = now();
    const s = (o: number) => { const n = dv.getUint8(o); return [dec.decode(new Uint8Array(dv.buffer, o + 1, n)), o + 1 + n] as const; };
    switch (dv.getUint8(0)) {
      case 0x80: this.id = dv.getUint8(3); break;
      case 0x81: {
        let o = 1; const phase = dv.getUint8(o++); const n = dv.getUint8(o++); const players: any[] = [];
        for (let i = 0; i < n; i++) { const id = dv.getUint8(o++); const [name, o2] = s(o); o = o2; const role = dv.getUint8(o++); const f = dv.getUint8(o++); players.push({ id, name, role, connected: (f & 2) !== 0, team: "" }); }
        o += 3; const host = dv.getUint8(o++);
        for (let i = 0; i < n && o < dv.byteLength; i++) { const [tn, o2] = s(o); o = o2; players[i].team = tn; }
        this.lobby = { phase, players, host }; break;
      }
      case 0x82: this.matchStarts++; break;
      case 0x83: this.phase = dv.getUint8(9); break;
      case 0x85: this.results.push({ t, reason: dv.getUint8(1), total: dv.getInt32(2, true) }); break;
      case 0x87: this.notices.push(s(1)[0]); break;
      case 0x8c: { const counted = dv.getUint8(1) === 1; const [team, o] = s(2); this.teamResults.push({ t, counted, team, points: dv.getInt32(o, true), rank: dv.getUint16(o + 4, true), canSave: dv.getUint8(o + 6) === 1 }); break; }
      case 0x8d: { const [team, o] = s(1); const [from, o2] = s(o); const [current] = s(o2); this.invites.push({ team, from, current }); break; }
    }
  }
  me() { return this.lobby?.players.find(p => p.id === this.id); }
}
async function waitFor(c: () => boolean, ms: number) { const t0 = now(); while (!c()) { if (now() - t0 > ms) return false; await sleep(100); } return true; }

async function room(tag: string, specs: [string, number][], tokens?: string[]) {
  const code = tag + Math.floor(Math.random() * 9000 + 1000);
  const ps: P[] = [];
  for (let i = 0; i < specs.length; i++) { const p = new P(code, specs[i][0], specs[i][1]); if (tokens?.[i]) p.token = tokens[i]; await p.connect(); ps.push(p); await sleep(250); }
  await waitFor(() => ps.every(p => p.id > 0), 5000);
  return ps;
}
async function playToEnd(ps: P[], label: string) {
  for (const p of ps) p.ready();
  await sleep(700);
  ps[0].start();
  const n0 = ps[0].results.length;
  const ok = await waitFor(() => ps[0].results.length > n0, 140_000);
  if (!ok) throw new Error(label + ": no RESULTS");
  await waitFor(() => ps[0].teamResults.length > 0 && ps[0].teamResults.at(-1)!.t >= ps[0].results.at(-1)!.t - 50, 5000);
  return ps[0].results.at(-1)!;
}

// ---------------------------------------------------------------- API
async function apiTests() {
  const A = tok("A"), B = tok("B"), C = tok("C"), D = tok("D");
  created.push(A, B, C, D);
  const name = "zt " + rnd() + " Crew";
  let r = await api("/api/team/create", { token: A, playerName: "Ana", name });
  check(r.status === 200 && r.j.team?.name === name && /^[A-Z2-9]{6}$/.test(r.j.team.joinCode) && r.j.team.members.length === 1, `create "${name}" -> code ${r.j?.team?.joinCode}`);
  const code = r.j?.team?.joinCode;
  r = await api("/api/team/create", { token: B, playerName: "Ben", name: name.toUpperCase() });
  check(r.status === 409, `same name other case rejected (${r.status} ${r.j?.error})`);
  for (const bad of ["ab", "x".repeat(17), "bad!name", "   ", ""]) {
    r = await api("/api/team/create", { token: B, playerName: "Ben", name: bad });
    check(r.status === 400, `invalid name "${bad}" rejected (${r.status})`);
  }
  r = await api("/api/team/join", { token: B, playerName: "Ben", joinCode: code.toLowerCase() });
  check(r.status === 200 && r.j.team.members.length === 2, `join by code (any case): ${r.j?.team?.members?.length} members`);
  r = await api("/api/team/join", { token: C, playerName: "Cy", joinCode: code });
  check(r.status === 200 && r.j.team.members.length === 3, "3rd member joins");
  r = await api("/api/team/join", { token: D, playerName: "Dee", joinCode: code });
  check(r.status === 409 && /full/i.test(r.j?.error), `4th member rejected (${r.status} ${r.j?.error})`);
  r = await api("/api/team/create", { token: D, playerName: "Dee", name: "zt " + rnd() + " D" });
  const dCode = r.j?.team?.joinCode;
  r = await api("/api/team/join", { token: A, playerName: "Ana", joinCode: dCode });
  check(r.status === 409 && /already/i.test(r.j?.error), `member of a team cannot join another without leaving (${r.j?.error})`);
  r = await api("/api/team/join", { token: C, playerName: "Cy", joinCode: "ZZZZZZ" });
  check(r.status === 404 || r.status === 409, `unknown code rejected (${r.status})`);
  r = await api("/api/team/leave", { token: C });
  const ta = await me(A);
  check(r.status === 200 && ta?.members.length === 2 && (await me(C)) === null, `leave: ${ta?.members.map((m: any) => m.name).join(", ")} left, Cy is a guest`);
  check(ta?.members.some((m: any) => m.me && m.name === "Ana"), "me flag on my own member row");
  r = await api(`/api/leaderboard?limit=5&token=${A}`);
  check(r.status === 200 && r.j.me?.name === name && r.j.me.rank >= 1 && r.j.rows.length <= 5, `leaderboard: my team rank #${r.j?.me?.rank}, top ${r.j?.rows?.length}`);
  check(!r.raw.includes(A) && !(await api(`/api/team/me?token=${A}`)).raw.includes(B), "no token in API responses");
  r = await api("/api/team/leave", { token: D });
  check((await api(`/api/leaderboard?limit=100`)).j.rows.every((x: any) => !x.name.endsWith(" D") || !x.name.startsWith("zt")), "last member out deletes the team");
  r = await api("/api/team/me?token=x");
  check(r.status === 400, "bad token rejected");
}

// ---------------------------------------------------------------- matches
async function guestMatch() {
  const A = tok("A1"); created.push(A);
  const name = "zt " + rnd() + " G";
  await api("/api/team/create", { token: A, playerName: "Ana", name });
  const [a, g] = await room("TG", [["Ana", 1], ["Gus", 2]], [A]);
  await waitFor(() => a.lobby?.players.find(p => p.id === a.id)?.team === name, 3000);
  check(a.lobby?.players.find(p => p.id === a.id)?.team === name && g.lobby?.players.find(p => p.id === g.id)?.team === "", "LOBBY carries teams (Ana in team, Gus guest)");
  const res = await playToEnd([a, g], "guest");
  const tr = a.teamResults.at(-1);
  check(!!tr && !tr.counted && tr.canSave, `guest match (team + guest): not counted, can save (reason ${res.reason}, total ${res.total})`);
  const t = await me(A);
  check(t?.matches === 0 && t?.totalScore === 0, `team unchanged (${t?.matches} matches, ${t?.totalScore})`);
  a.kill(); g.kill();
}

async function teamMatch() {
  const A = tok("A2"), B = tok("B2"); created.push(A, B);
  const name = "zt " + rnd() + " T";
  const c = await api("/api/team/create", { token: A, playerName: "Ana", name });
  await api("/api/team/join", { token: B, playerName: "Ben", joinCode: c.j.team.joinCode });
  const [a, b] = await room("TT", [["Ana", 1], ["Ben", 2]], [A, B]); // humans paddle (idle), the bot collector scores
  const r1 = await playToEnd([a, b], "team #1");
  const tr = a.teamResults.at(-1)!;
  let t = await me(A);
  check(tr.counted && tr.points === r1.total && tr.team === name && tr.rank >= 1, `team match counted: +${tr.points} to ${tr.team}, rank #${tr.rank}`);
  check(t.matches === 1 && t.totalScore === r1.total && t.bestScore === r1.total, `team totals ${t.totalScore} / ${t.matches} match / best ${t.bestScore}`);
  b.kill(); await sleep(500); await b.connect();
  await waitFor(() => b.results.length >= 2, 4000);
  t = await me(A);
  check(b.results.length >= 2 && t.matches === 1 && t.totalScore === r1.total, `reconnect after the match replays RESULTS, no double count (${t.matches} match)`);
  await sleep(500);
  const r2 = await playToEnd([a, b], "team #2");
  check(r1.total !== 0 && r2.total !== 0, `non-zero totals so the sums are meaningful (${r1.total}, ${r2.total})`);
  t = await me(A);
  check(t.matches === 2 && t.totalScore === r1.total + r2.total && t.bestScore === Math.max(r1.total, r2.total), `rematch counted separately: ${r1.total} + ${r2.total} = ${t.totalScore}, ${t.matches} matches`);
  a.kill(); b.kill();
}

async function abortedMatch() {
  const A = tok("A3"), B = tok("B3"); created.push(A, B);
  const c = await api("/api/team/create", { token: A, playerName: "Ana", name: "zt " + rnd() + " X" });
  await api("/api/team/join", { token: B, playerName: "Ben", joinCode: c.j.team.joinCode });
  const [a, b] = await room("TX", [["Ana", 1], ["Ben", 3]], [A, B]);
  for (const p of [a, b]) p.ready();
  await sleep(700); a.start();
  await waitFor(() => a.phase === 2, 8000);
  await sleep(1000);
  a.kill(); b.kill();
  await sleep(34_000); // 30 s grace -> abort
  await a.connect();
  await waitFor(() => a.results.length > 0, 4000);
  const t = await me(A);
  check(a.results.at(-1)?.reason === 3 && !a.teamResults.some(x => x.counted) && t.matches === 0, `aborted match not counted (reason ${a.results.at(-1)?.reason}, ${t.matches} matches)`);
  a.kill();
}

async function saveCrew() {
  const S1 = tok("S1"), S2 = tok("S2"); created.push(S1, S2);
  const [h, x] = await room("TS", [["Sam", 1], ["Lia", 2]], [S1, S2]);
  const res = await playToEnd([h, x], "save crew");
  check(h.teamResults.at(-1)?.canSave === true, `guest crew can save (total ${res.total})`);
  const name = "zt " + rnd() + " S";
  x.teamCreate(name); await sleep(800);
  check(x.notices.some(n => /host/i.test(n)), `non-host cannot create (${x.notices.at(-1)})`);
  h.teamCreate(name);
  const inv = await waitFor(() => x.invites.length > 0, 4000);
  check(inv && x.invites[0].team === name && x.invites[0].from === "Sam", `Lia invited to "${x.invites[0]?.team}" by ${x.invites[0]?.from}`);
  check(!h.teamResults.some(r => r.counted), "not counted while Lia has not accepted");
  x.answer(true);
  await waitFor(() => h.teamResults.some(r => r.counted), 4000);
  const tr = h.teamResults.find(r => r.counted);
  let t = await me(S1);
  check(!!tr && tr.points === res.total && t?.matches === 1 && t?.totalScore === res.total && t.members.length === 2, `saved crew: +${tr?.points} counted once, team ${t?.matches} match, ${t?.members.length} members`);
  await waitFor(() => h.lobby?.players.every(p => p.team === name) ?? false, 3000);
  check(h.lobby!.players.every(p => p.team === name), "LOBBY shows both in the new team");
  x.answer(true); h.teamCreate(""); await sleep(1500);
  t = await me(S1);
  check(t.matches === 1 && t.totalScore === res.total, `repeated accept / invite: still counted once (${t.matches})`);
  h.kill(); x.kill();
}

const t0 = now();
try {
  if (want("api")) await apiTests();
  if (want("match")) await Promise.all([guestMatch(), teamMatch(), abortedMatch(), saveCrew()].map(p => p.catch(e => check(false, "crashed: " + e))));
} catch (e) { check(false, "crashed: " + e); }
for (const t of created) await api("/api/team/leave", { token: t }).catch(() => {});
console.log(lines.join("\n"));
console.log(`\n${failures ? failures + " FAILED" : "ALL PASS"}  (${((now() - t0) / 1000).toFixed(1)} s)`);
process.exit(failures ? 1 : 0);
