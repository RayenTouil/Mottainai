// S0 smoke test: WELCOME, PONG RTT, snapshot rate.
const base = (process.argv[2] ?? "https://mottainai.mottainai-server.workers.dev").replace(/^http/, "ws");
const ws = new WebSocket(`${base}/ws/room/DEMO?token=smoke${Date.now()}&name=Smoke&role=1`);
ws.binaryType = "arraybuffer";
let snaps = 0, bytes = 0; const rtts: number[] = [];
ws.onmessage = (ev) => {
  const dv = new DataView(ev.data as ArrayBuffer); const id = dv.getUint8(0); bytes += dv.byteLength;
  if (id === 0x80) { const n1 = dv.getUint8(4); const code = new TextDecoder().decode(new Uint8Array(ev.data, 5, n1)); const n2 = dv.getUint8(5 + n1); console.log("WELCOME v" + dv.getUint16(1, true), "player", dv.getUint8(3), "room", code, "roomColo", new TextDecoder().decode(new Uint8Array(ev.data, 6 + n1, n2))); }
  else if (id === 0x86) rtts.push(performance.now() - dv.getFloat64(1, true));
  else if (id === 0x83) snaps++;
};
ws.onopen = () => { const iv = setInterval(() => { const b = new DataView(new ArrayBuffer(9)); b.setUint8(0, 0x20); b.setFloat64(1, performance.now(), true); ws.send(b.buffer); }, 500); setTimeout(() => { clearInterval(iv); rtts.sort((a, b) => a - b); console.log(`snapshots/s ${(snaps / 5).toFixed(1)} down ${(bytes * 8 / 5000).toFixed(0)} kbps rtt avg ${(rtts.reduce((a, b) => a + b, 0) / rtts.length).toFixed(1)} ms p95 ${rtts[Math.floor(rtts.length * 0.95)]?.toFixed(1)} n=${rtts.length}`); ws.close(); process.exit(0); }, 5000); };
ws.onerror = (e) => { console.error("ws error", e); process.exit(1); };
