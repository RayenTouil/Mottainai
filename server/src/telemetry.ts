import { DurableObject } from "cloudflare:workers";

const COLS = ["ts", "device", "netType", "edgeColo", "roomColo", "edgeRttAvg", "edgeRttP95", "edgeJitter", "roomRttAvg", "roomRttP95", "roomJitter", "downMbps", "upMbps", "downMB", "upMB", "note", "voiceE2eAvg", "voiceE2eP95", "voiceJitter", "voiceLossPct"];

/** Stores Network Lab runs in SQLite; exports CSV. */
export class TelemetryDO extends DurableObject {
  constructor(ctx: DurableObjectState, env: any) {
    super(ctx, env);
    ctx.storage.sql.exec(`CREATE TABLE IF NOT EXISTS runs (id INTEGER PRIMARY KEY AUTOINCREMENT, ${COLS.map(c => `${c} TEXT`).join(", ")})`);
    const have = new Set(ctx.storage.sql.exec(`PRAGMA table_info(runs)`).toArray().map((r: any) => r.name));
    for (const c of COLS) if (!have.has(c)) ctx.storage.sql.exec(`ALTER TABLE runs ADD COLUMN ${c} TEXT`); // v4 voice columns
  }

  async fetch(req: Request): Promise<Response> {
    const url = new URL(req.url);
    if (url.pathname.endsWith(".csv")) {
      const rows = this.ctx.storage.sql.exec(`SELECT ${COLS.join(",")} FROM runs ORDER BY id`).toArray();
      const esc = (v: unknown) => { const s = v == null ? "" : String(v); return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s; };
      const csv = [COLS.join(","), ...rows.map(r => COLS.map(c => esc((r as any)[c])).join(","))].join("\n") + "\n";
      return new Response(csv, { headers: { "content-type": "text/csv", "content-disposition": "attachment; filename=telemetry.csv", "cache-control": "no-store" } });
    }
    if (req.method !== "POST") return new Response("POST only", { status: 405 });
    let b: any;
    try { b = await req.json(); } catch { return Response.json({ ok: false, error: "bad json" }, { status: 400 }); }
    b.ts ??= new Date().toISOString();
    const vals = COLS.map(c => (b[c] == null ? null : String(b[c]).slice(0, 200)));
    this.ctx.storage.sql.exec(`INSERT INTO runs (${COLS.join(",")}) VALUES (${COLS.map(() => "?").join(",")})`, ...vals);
    return Response.json({ ok: true });
  }
}
