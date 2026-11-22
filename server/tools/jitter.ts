// Netcode probe: snapshot timing jitter + stroke->effect latency against a deployed RoomDO.
// Usage: node tools/jitter.ts [baseUrl] [seconds]
const base = (process.argv[2] ?? "https://mottainai.mottainai-server.workers.dev").replace(/^http/, "ws");
const secs = Number(process.argv[3] ?? 25);
const room = "JT" + Math.floor(Math.random() * 9000 + 1000);
const ws = new WebSocket(`${base}/ws/room/${room}?token=jit${Date.now()}&name=Jitter&role=1`);
ws.binaryType = "arraybuffer";
const msg = (n: number, f: (d: DataView) => void) => { const b = new ArrayBuffer(n); f(new DataView(b)); return b; };
const arr: number[] = [], ticks: number[] = [], srv: number[] = [], rtts: number[] = [], strokeLat: number[] = [];
let running = false, runStart = 0, pendingStroke = 0;

ws.onmessage = (ev: MessageEvent) => {
  const now = performance.now();
  const dv = new DataView(ev.data as ArrayBuffer);
  switch (dv.getUint8(0)) {
    case 0x80: ws.send(msg(1, d => d.setUint8(0, 0x03))); break;
    case 0x86: rtts.push(now - dv.getFloat64(1, true)); break;
    case 0x84: { const n = dv.getUint8(1); for (let i = 0; i < n; i++) if (dv.getUint8(2 + i * 14) === 2 && dv.getUint8(4 + i * 14) === 1 && pendingStroke) { strokeLat.push(now - pendingStroke); pendingStroke = 0; } break; }
    case 0x83: {
      const phase = dv.getUint8(9);
      if (phase !== 2) break;
      if (!running) { running = true; runStart = now; }
      arr.push(now); ticks.push(dv.getUint32(1, true)); srv.push(dv.byteLength >= 8 ? dv.getFloat64(dv.byteLength - 8, true) : NaN);
      if (now - runStart > secs * 1000) report();
      break;
    }
  }
};
ws.onopen = () => {
  setInterval(() => ws.send(msg(9, d => { d.setUint8(0, 0x20); d.setFloat64(1, performance.now(), true); })), 500);
  setInterval(() => ws.send(msg(7, d => { d.setUint8(0, 0x21); d.setUint8(1, 60); d.setUint16(2, 40, true); d.setUint8(4, 1); d.setUint16(5, 0, true); })), 2000);
  setInterval(() => { if (running) { pendingStroke = performance.now(); ws.send(msg(7, d => { d.setUint8(0, 0x10); d.setUint8(1, 0); d.setUint8(2, 255); d.setFloat32(3, 0, true); })); } }, 900);
};
const pct = (a: number[], p: number) => { const s = [...a].sort((x, y) => x - y); return s[Math.min(s.length - 1, Math.floor(s.length * p))] ?? NaN; };
const f = (v: number) => v.toFixed(1);
function report() {
  const n = arr.length, dur = (arr[n - 1] - arr[0]) / 1000;
  const ia = arr.slice(1).map((t, i) => t - arr[i]);
  const gaps = new Map<number, number>();
  ticks.slice(1).forEach((t, i) => gaps.set(t - ticks[i], (gaps.get(t - ticks[i]) ?? 0) + 1));
  // one-way delay variation: arrival - tick time, relative to the best (earliest) arrival line
  const tickMs = 1000 / 60;
  const off = arr.map((t, i) => t - ticks[i] * tickMs);
  const best = Math.min(...off);
  const late = off.map(o => o - best);
  const bursts = ia.filter(x => x < 3).length;
  const sOff = srv.map((t, i) => t - ticks[i] * tickMs), sBest = Math.min(...sOff), sLate = sOff.map(o => o - sBest);
  const nOff = arr.map((t, i) => t - srv[i]), nBest = Math.min(...nOff), nLate = nOff.map(o => o - nBest);
  console.log(`room ${room}  ${n} snapshots in ${f(dur)} s = ${f(n / dur)}/s`);
  console.log(`inter-arrival ms  p50 ${f(pct(ia, 0.5))}  p95 ${f(pct(ia, 0.95))}  p99 ${f(pct(ia, 0.99))}  max ${f(Math.max(...ia))}  bursts(<3ms) ${(100 * bursts / ia.length).toFixed(0)}%`);
  console.log(`tick gaps ${[...gaps].sort((a, b) => a[0] - b[0]).map(([k, v]) => `${k}:${v}`).join(" ")}`);
  console.log(`lateness ms (interp buffer needed)  p50 ${f(pct(late, 0.5))}  p95 ${f(pct(late, 0.95))}  p99 ${f(pct(late, 0.99))}  max ${f(Math.max(...late))}`);
  if (!isNaN(sBest)) console.log(`  server timer lateness p50 ${f(pct(sLate, 0.5))} p95 ${f(pct(sLate, 0.95))} p99 ${f(pct(sLate, 0.99))} max ${f(Math.max(...sLate))} | network p50 ${f(pct(nLate, 0.5))} p95 ${f(pct(nLate, 0.95))} p99 ${f(pct(nLate, 0.99))} max ${f(Math.max(...nLate))}`);
  console.log(`rtt ms  avg ${f(rtts.reduce((a, b) => a + b, 0) / rtts.length)}  p95 ${f(pct(rtts, 0.95))}   stroke->server event ms  avg ${f(strokeLat.reduce((a, b) => a + b, 0) / strokeLat.length)}  p95 ${f(pct(strokeLat, 0.95))} (n ${strokeLat.length})`);
  ws.close(); process.exit(0);
}
ws.onerror = () => { console.error("ws error"); process.exit(1); };
setTimeout(() => { console.error("TIMEOUT"); process.exit(2); }, (secs + 30) * 1000);
