import { PROTOCOL_VERSION } from "./protocol";
export { RoomDO } from "./room";
export { TelemetryDO } from "./telemetry";

export interface Env {
  ROOM: DurableObjectNamespace;
  TELEMETRY: DurableObjectNamespace;
  ASSETS: Fetcher;
}

const JSON_HDR = { "content-type": "application/json", "cache-control": "no-store", "access-control-allow-origin": "*" };
const json = (o: unknown, status = 200) => new Response(JSON.stringify(o), { status, headers: JSON_HDR });

function randomToken() {
  const b = new Uint8Array(16);
  crypto.getRandomValues(b);
  return [...b].map(x => x.toString(16).padStart(2, "0")).join("");
}

function randomCode() {
  const A = "ABCDEFGHJKLMNPQRSTUVWXYZ";
  const b = new Uint8Array(4);
  for (;;) {
    crypto.getRandomValues(b);
    const c = [...b].map(x => A[x % A.length]).join("");
    if (c !== "DEMO" && !c.startsWith("LAB")) return c;
  }
}

export default {
  async fetch(req: Request, env: Env): Promise<Response> {
    const url = new URL(req.url);
    const p = url.pathname;
    const colo = (req as any).cf?.colo ?? "?";

    if (p === "/api/health") return json({ ok: true, protocolVersion: PROTOCOL_VERSION, colo });

    if (p === "/api/login" && req.method === "POST") {
      let name = "Player";
      try { const b: any = await req.json(); if (typeof b?.name === "string" && b.name.trim()) name = b.name.trim().slice(0, 16); } catch {}
      return json({ token: randomToken(), name });
    }

    if (p === "/api/rooms" && req.method === "POST") return json({ code: randomCode() });

    const m = p.match(/^\/ws\/room\/([A-Za-z0-9]{3,8})$/);
    if (m) {
      if (req.headers.get("Upgrade")?.toLowerCase() !== "websocket") return json({ error: "expected websocket" }, 426);
      const code = m[1].toUpperCase();
      const stub = env.ROOM.get(env.ROOM.idFromName(code));
      const fwd = new URL(req.url);
      fwd.searchParams.set("code", code);
      fwd.searchParams.set("edge", colo);
      return stub.fetch(new Request(fwd.toString(), req));
    }

    if (p === "/api/lab/ping") return json({ t: Date.now(), colo });

    if (p === "/api/lab/upload" && req.method === "POST") {
      const t0 = Date.now();
      let bytes = 0;
      if (req.body) {
        const rd = req.body.getReader();
        for (;;) { const { done, value } = await rd.read(); if (done) break; bytes += value.byteLength; }
      }
      return json({ bytes, ms: Date.now() - t0, colo });
    }

    if (p === "/api/telemetry" || p === "/api/telemetry.csv") {
      const stub = env.TELEMETRY.get(env.TELEMETRY.idFromName("global"));
      return stub.fetch(req);
    }

    if (p.startsWith("/api/")) return json({ error: "not found" }, 404);
    return env.ASSETS.fetch(req);
  },
} satisfies ExportedHandler<Env>;
