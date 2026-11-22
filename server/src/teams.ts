import { DurableObject } from "cloudflare:workers";

/** Teams + leaderboard (one global SQLite Durable Object). Identity = the device token (no password, default guest).
 *  Scores are credited only by RoomDO (server-authoritative), once per match id (matches.id PRIMARY KEY). */
export const TEAM_MAX = 3;
const PENDING_MS = 10 * 60_000;
const CODE_ABC = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

export interface TeamView {
  id: number; name: string; joinCode: string; totalScore: number; matches: number; bestScore: number; rank: number;
  members: { name: string; me: boolean }[];
}
export type ApiResult = { ok: true; team: TeamView | null } | { ok: false; status: number; error: string };
export interface BoardRow { rank: number; name: string; totalScore: number; matches: number; bestScore: number; members: string; me: boolean; }
export interface ReportResult { counted: boolean; teamId: number; teamName: string; points: number; rank: number; totalScore: number; canSave: boolean; reason: string; }

/** 3-16 letters / digits / spaces, trimmed, inner spaces collapsed. */
export function cleanTeamName(raw: unknown): string | null {
  if (typeof raw !== "string") return null;
  const n = raw.trim().replace(/\s+/g, " ");
  return /^[\p{L}\p{N} ]{3,16}$/u.test(n) ? n : null;
}
export const validToken = (t: unknown): t is string => typeof t === "string" && /^[A-Za-z0-9_-]{8,64}$/.test(t);
const err = (status: number, error: string): ApiResult => ({ ok: false, status, error });

interface TeamRow { id: number; name: string; joinCode: string; createdAt: number; totalScore: number; matches: number; bestScore: number; }

export class TeamsDO extends DurableObject {
  sql: SqlStorage;

  constructor(ctx: DurableObjectState, env: any) {
    super(ctx, env);
    this.sql = ctx.storage.sql;
    const ddl = [
      `CREATE TABLE IF NOT EXISTS players (token TEXT PRIMARY KEY, name TEXT NOT NULL, teamId INTEGER)`,
      `CREATE TABLE IF NOT EXISTS teams (id INTEGER PRIMARY KEY AUTOINCREMENT, name TEXT NOT NULL UNIQUE COLLATE NOCASE, nameKey TEXT NOT NULL UNIQUE,
         joinCode TEXT NOT NULL UNIQUE, createdAt INTEGER NOT NULL, totalScore INTEGER NOT NULL DEFAULT 0, matches INTEGER NOT NULL DEFAULT 0, bestScore INTEGER NOT NULL DEFAULT 0)`,
      `CREATE TABLE IF NOT EXISTS members (token TEXT PRIMARY KEY, teamId INTEGER NOT NULL, joinedAt INTEGER NOT NULL)`, // PK token = one team per player
      `CREATE INDEX IF NOT EXISTS members_team ON members(teamId)`,
      `CREATE TABLE IF NOT EXISTS matches (id TEXT PRIMARY KEY, teamId INTEGER NOT NULL, score INTEGER NOT NULL, ts INTEGER NOT NULL, roomCode TEXT NOT NULL)`, // PK id = counted once
      `CREATE TABLE IF NOT EXISTS pending (roomCode TEXT PRIMARY KEY, matchId TEXT NOT NULL, score INTEGER NOT NULL, tokens TEXT NOT NULL, ts INTEGER NOT NULL)`,
      `CREATE INDEX IF NOT EXISTS teams_rank ON teams(totalScore DESC, bestScore DESC, createdAt ASC, id ASC)`,
    ];
    for (const s of ddl) this.sql.exec(s);
  }

  // ---------------- helpers ----------------
  one<T>(q: string, ...b: unknown[]): T | null { const r = this.sql.exec(q, ...b).toArray(); return (r[0] as T) ?? null; }
  teamById(id: number) { return this.one<TeamRow>(`SELECT * FROM teams WHERE id = ?`, id); }
  memberTeam(token: string) { return this.one<{ teamId: number }>(`SELECT teamId FROM members WHERE token = ?`, token)?.teamId ?? 0; }
  memberCount(teamId: number) { return this.one<{ n: number }>(`SELECT COUNT(*) AS n FROM members WHERE teamId = ?`, teamId)!.n; }
  upsertPlayer(token: string, name: string, teamId: number | null) {
    this.sql.exec(`INSERT INTO players (token, name, teamId) VALUES (?, ?, ?) ON CONFLICT(token) DO UPDATE SET name = CASE WHEN excluded.name <> '' THEN excluded.name ELSE players.name END, teamId = excluded.teamId`,
      token, (name || "").slice(0, 16), teamId);
  }
  rankOf(t: TeamRow) {
    return 1 + this.one<{ n: number }>(`SELECT COUNT(*) AS n FROM teams WHERE totalScore > ?1 OR (totalScore = ?1 AND (bestScore > ?2 OR (bestScore = ?2 AND (createdAt < ?3 OR (createdAt = ?3 AND id < ?4)))))`,
      t.totalScore, t.bestScore, t.createdAt, t.id)!.n;
  }
  view(t: TeamRow, token: string): TeamView {
    const members = this.sql.exec(`SELECT m.token AS token, COALESCE(p.name, '?') AS name FROM members m LEFT JOIN players p ON p.token = m.token WHERE m.teamId = ? ORDER BY m.joinedAt`, t.id)
      .toArray().map((r: any) => ({ name: r.name as string, me: r.token === token }));
    return { id: t.id, name: t.name, joinCode: t.joinCode, totalScore: t.totalScore, matches: t.matches, bestScore: t.bestScore, rank: this.rankOf(t), members };
  }
  newCode() {
    for (;;) {
      const b = crypto.getRandomValues(new Uint8Array(6));
      const c = [...b].map(x => CODE_ABC[x % CODE_ABC.length]).join("");
      if (!this.one(`SELECT 1 FROM teams WHERE joinCode = ?`, c)) return c;
    }
  }
  removeMember(token: string) {
    const tid = this.memberTeam(token);
    if (!tid) return;
    this.sql.exec(`DELETE FROM members WHERE token = ?`, token);
    this.sql.exec(`UPDATE players SET teamId = NULL WHERE token = ?`, token);
    if (this.memberCount(tid) === 0) { // a team has 1..3 members: the last one out deletes it (its name becomes free)
      this.sql.exec(`DELETE FROM teams WHERE id = ?`, tid);
      this.sql.exec(`DELETE FROM matches WHERE teamId = ?`, tid);
    }
  }

  // ---------------- public API (via index.ts) ----------------
  teamOf(token: string): { teamId: number; teamName: string } | null {
    const tid = this.memberTeam(token);
    const t = tid ? this.teamById(tid) : null;
    return t ? { teamId: t.id, teamName: t.name } : null;
  }

  me(token: string): ApiResult {
    if (!validToken(token)) return err(400, "bad token");
    const tid = this.memberTeam(token);
    const t = tid ? this.teamById(tid) : null;
    return { ok: true, team: t ? this.view(t, token) : null };
  }

  create(token: string, playerName: string, rawName: unknown): ApiResult {
    if (!validToken(token)) return err(400, "bad token");
    const name = cleanTeamName(rawName);
    if (!name) return err(400, "Team name: 3-16 letters, digits or spaces");
    const cur = this.memberTeam(token);
    if (cur) return err(409, `You are already in team ${this.teamById(cur)?.name ?? ""} - leave it first`);
    if (this.one(`SELECT 1 FROM teams WHERE nameKey = ?`, name.toLowerCase())) return err(409, "Team name already taken");
    const now = Date.now();
    let id = 0;
    this.ctx.storage.transactionSync(() => {
      id = this.one<{ id: number }>(`INSERT INTO teams (name, nameKey, joinCode, createdAt) VALUES (?, ?, ?, ?) RETURNING id`, name, name.toLowerCase(), this.newCode(), now)!.id;
      this.sql.exec(`INSERT INTO members (token, teamId, joinedAt) VALUES (?, ?, ?)`, token, id, now);
      this.upsertPlayer(token, playerName, id);
    });
    return { ok: true, team: this.view(this.teamById(id)!, token) };
  }

  /** Join by code (HTTP) or by id (room invite). force = leave the current team first (invite accepted). */
  join(token: string, playerName: string, codeOrId: string | number, force = false): ApiResult {
    if (!validToken(token)) return err(400, "bad token");
    const t = typeof codeOrId === "number" ? this.teamById(codeOrId)
      : this.one<TeamRow>(`SELECT * FROM teams WHERE joinCode = ?`, String(codeOrId).trim().toUpperCase());
    if (!t) return err(404, "No team with this code");
    const cur = this.memberTeam(token);
    if (cur === t.id) { this.upsertPlayer(token, playerName, t.id); return { ok: true, team: this.view(t, token) }; }
    if (cur && !force) return err(409, `You are already in team ${this.teamById(cur)?.name ?? ""} - leave it first`);
    if (this.memberCount(t.id) >= TEAM_MAX) return err(409, `Team is full (${TEAM_MAX} players)`); // checked before leaving the old team
    this.ctx.storage.transactionSync(() => {
      if (cur) this.removeMember(token);
      this.sql.exec(`INSERT INTO members (token, teamId, joinedAt) VALUES (?, ?, ?)`, token, t.id, Date.now());
      this.upsertPlayer(token, playerName, t.id);
    });
    return { ok: true, team: this.view(this.teamById(t.id)!, token) };
  }

  leave(token: string): ApiResult {
    if (!validToken(token)) return err(400, "bad token");
    this.ctx.storage.transactionSync(() => this.removeMember(token));
    return { ok: true, team: null };
  }

  leaderboard(limit: number, token: string): { ok: true; rows: BoardRow[]; me: BoardRow | null } {
    const n = Math.max(1, Math.min(100, Math.floor(limit) || 20));
    const myTid = validToken(token) ? this.memberTeam(token) : 0;
    const q = `SELECT t.*, (SELECT group_concat(COALESCE(p.name, '?'), ', ') FROM members m LEFT JOIN players p ON p.token = m.token WHERE m.teamId = t.id) AS memberNames FROM teams t`;
    const toRow = (r: any, rank: number): BoardRow => ({ rank, name: r.name, totalScore: r.totalScore, matches: r.matches, bestScore: r.bestScore, members: r.memberNames ?? "", me: r.id === myTid });
    const rows = this.sql.exec(`${q} ORDER BY t.totalScore DESC, t.bestScore DESC, t.createdAt ASC, t.id ASC LIMIT ?`, n).toArray().map((r, i) => toRow(r, i + 1));
    let me: BoardRow | null = rows.find(r => r.me) ?? null;
    if (!me && myTid) { const r = this.one<any>(`${q} WHERE t.id = ?`, myTid); if (r) me = toRow(r, this.rankOf(r)); }
    return { ok: true, rows, me };
  }

  // ---------------- server-authoritative scoring (RoomDO only, never exposed by index.ts) ----------------
  credit(teamId: number, matchId: string, score: number, roomCode: string): boolean {
    if (this.one(`SELECT 1 FROM matches WHERE id = ?`, matchId)) return false; // already counted (reconnect, double report, double claim)
    this.ctx.storage.transactionSync(() => {
      this.sql.exec(`INSERT INTO matches (id, teamId, score, ts, roomCode) VALUES (?, ?, ?, ?, ?)`, matchId, teamId, score, Date.now(), roomCode);
      this.sql.exec(`UPDATE teams SET totalScore = totalScore + ?1, bestScore = CASE WHEN matches = 0 THEN ?1 ELSE MAX(bestScore, ?1) END, matches = matches + 1 WHERE id = ?2`, score, teamId);
    });
    return true;
  }

  outcome(teamId: number, counted: boolean, points: number, reason: string): ReportResult {
    const t = this.teamById(teamId)!;
    return { counted, teamId, teamName: t.name, points, rank: this.rankOf(t), totalScore: t.totalScore, canSave: false, reason };
  }

  /** Match end (never called for aborted matches). Counts when every human who played belongs to the same team;
   *  otherwise keeps the result 10 min for "save this crew as a team". */
  report(m: { matchId: string; roomCode: string; score: number; tokens: string[] }): ReportResult {
    const guest: ReportResult = { counted: false, teamId: 0, teamName: "", points: 0, rank: 0, totalScore: 0, canSave: false, reason: "no humans" };
    const tokens = [...new Set(m.tokens.filter(validToken))];
    if (!tokens.length) return guest;
    const done = this.one<{ teamId: number; score: number }>(`SELECT teamId, score FROM matches WHERE id = ?`, m.matchId);
    if (done && this.teamById(done.teamId)) return this.outcome(done.teamId, true, done.score, "already counted");
    const tids = new Set(tokens.map(t => this.memberTeam(t)));
    const tid = tids.size === 1 ? [...tids][0] : 0;
    if (tid) {
      this.credit(tid, m.matchId, Math.round(m.score), m.roomCode);
      return this.outcome(tid, true, Math.round(m.score), "team");
    }
    this.sql.exec(`INSERT INTO pending (roomCode, matchId, score, tokens, ts) VALUES (?, ?, ?, ?, ?) ON CONFLICT(roomCode) DO UPDATE SET matchId = excluded.matchId, score = excluded.score, tokens = excluded.tokens, ts = excluded.ts`,
      m.roomCode, m.matchId, Math.round(m.score), JSON.stringify(tokens), Date.now());
    return { ...guest, canSave: true, reason: "guests" };
  }

  /** "Save this crew as a team": the room's last guest result counts once every player of that match is in the team. */
  claim(roomCode: string, teamId: number): ReportResult {
    const no = (reason: string): ReportResult => ({ counted: false, teamId, teamName: this.teamById(teamId)?.name ?? "", points: 0, rank: 0, totalScore: 0, canSave: false, reason });
    const p = this.one<{ matchId: string; score: number; tokens: string; ts: number }>(`SELECT * FROM pending WHERE roomCode = ?`, roomCode);
    if (!p) return no("nothing to claim");
    if (Date.now() - p.ts > PENDING_MS) { this.sql.exec(`DELETE FROM pending WHERE roomCode = ?`, roomCode); return no("expired"); }
    const tokens: string[] = JSON.parse(p.tokens);
    if (!tokens.every(t => this.memberTeam(t) === teamId)) return { ...no("waiting for the crew"), canSave: true };
    const fresh = this.credit(teamId, p.matchId, p.score, roomCode);
    this.sql.exec(`DELETE FROM pending WHERE roomCode = ?`, roomCode);
    return this.outcome(teamId, true, p.score, fresh ? "saved crew" : "already counted");
  }
}
