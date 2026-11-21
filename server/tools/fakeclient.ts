// Plays a full match against the deployed RoomDO as LEFT PADDLER (bots fill R + C). Usage: node tools/fakeclient.ts [baseUrl] [room]
const base = (process.argv[2] ?? "https://mottainai.mottainai-server.workers.dev").replace(/^http/, "ws");
const room = process.argv[3] ?? "FK" + Math.floor(Math.random() * 9000 + 1000);
const ws = new WebSocket(`${base}/ws/room/${room}?token=fake${Date.now()}&name=Fake&role=1`);
ws.binaryType = "arraybuffer";
const dec = new TextDecoder();
const evCount = new Map<number, number>();
let snaps = 0, bytes = 0, maxEntities = 0, lastPhase = -1, started = 0, strokes = 0;
const rtts: number[] = [];
const t0 = performance.now();
const send = (b: ArrayBuffer) => ws.send(b);
const msg = (n: number, f: (d: DataView) => void) => { const b = new ArrayBuffer(n); f(new DataView(b)); return b; };
const str = (dv: DataView, o: number) => { const n = dv.getUint8(o); return [dec.decode(new Uint8Array(dv.buffer, o + 1, n)), o + 1 + n] as const; };

ws.onmessage = (ev: MessageEvent) => {
  const dv = new DataView(ev.data as ArrayBuffer); bytes += dv.byteLength;
  switch (dv.getUint8(0)) {
    case 0x80: { const [code, o] = str(dv, 4); const [colo] = str(dv, o); console.log(`WELCOME v${dv.getUint16(1, true)} player ${dv.getUint8(3)} room ${code} roomColo ${colo}`); send(msg(1, d => d.setUint8(0, 0x03))); break; }
    case 0x82: console.log(`MATCH_START seed ${dv.getUint32(1, true)} role ${dv.getUint8(5)} duration ${dv.getUint16(7, true)} s`); started = performance.now(); break;
    case 0x83: {
      snaps++;
      const phase = dv.getUint8(9);
      if (phase !== lastPhase) { console.log(`phase -> ${phase} at ${((performance.now() - t0) / 1000).toFixed(1)} s`); lastPhase = phase; }
      const teams = dv.getUint8(13); const o = 14 + teams * 25 + 4; const n = dv.getUint16(o, true); if (n > maxEntities) maxEntities = n;
      break;
    }
    case 0x84: { const n = dv.getUint8(1); for (let i = 0; i < n; i++) { const t = dv.getUint8(2 + i * 14); evCount.set(t, (evCount.get(t) ?? 0) + 1); } break; }
    case 0x86: rtts.push(performance.now() - dv.getFloat64(1, true)); break;
    case 0x85: {
      const counts = Array.from({ length: 7 }, (_, i) => dv.getUint16(18 + i * 2, true));
      console.log(`RESULTS reason ${dv.getUint8(1)} total ${dv.getInt32(2, true)} catch ${dv.getInt32(6, true)} toxic -${dv.getInt32(10, true)} throws ${dv.getUint16(14, true)} hits ${dv.getUint16(16, true)} counts ${counts} dist ${dv.getFloat32(32, true).toFixed(0)} m kg ${dv.getFloat32(36, true).toFixed(1)} dur ${dv.getFloat32(40, true).toFixed(1)} s`);
      const secs = (performance.now() - started) / 1000;
      rtts.sort((a, b) => a - b);
      const names = ["Countdown", "Go", "Stroke", "Sync", "NetThrow", "Catch", "Miss", "Hit", "ShieldOn", "ShieldOff", "GameOver", "Combo", "BotTakeover", "PowerOn", "PowerOff"];
      console.log("events:", [...evCount].map(([k, v]) => `${names[k]}=${v}`).join(" "));
      console.log(`snapshots ${snaps} (${(snaps / secs).toFixed(1)}/s) max entities ${maxEntities} down ${(bytes * 8 / 1000 / secs).toFixed(0)} kbps strokes sent ${strokes} rtt avg ${(rtts.reduce((a, b) => a + b, 0) / rtts.length).toFixed(1)} p95 ${rtts[Math.floor(rtts.length * 0.95)]?.toFixed(1)} ms`);
      ws.close(); process.exit(dv.getUint8(1) === 3 ? 1 : 0);
    }
  }
};
ws.onopen = () => {
  setInterval(() => send(msg(9, d => { d.setUint8(0, 0x20); d.setFloat64(1, performance.now(), true); })), 500);
  setInterval(() => send(msg(7, d => { d.setUint8(0, 0x21); d.setUint8(1, 60); d.setUint16(2, 40, true); d.setUint8(4, 1); d.setUint16(5, 0, true); })), 2000);
  setInterval(() => { if (lastPhase === 2) { strokes++; send(msg(7, d => { d.setUint8(0, 0x10); d.setUint8(1, 0); d.setUint8(2, 230); d.setFloat32(3, performance.now() / 1000, true); })); } }, 700);
};
ws.onerror = (e: Event) => { console.error("ws error", e); process.exit(1); };
setTimeout(() => { console.error("TIMEOUT"); process.exit(2); }, 200_000);
