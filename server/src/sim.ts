// Authoritative gameplay simulation — port of Mottainai/Assets/_Mottainai/Scripts/Gameplay/*.cs
import tuningJson from "./tuning.json";

export type Tuning = typeof tuningJson;
export const TUNING: Tuning = tuningJson;

export const Role = { None: 0, Left: 1, Right: 2, Collector: 3 } as const;
export const Kind = { Waste: 1, Obstacle: 2, Power: 3 } as const;
export const Obs = { Rock: 0, BigRock: 1, Iceberg: 2, Wreck: 3, Log: 4, Shark: 5 } as const;
export const Power = { Shield: 0, Magnet: 1, Turbo: 2 } as const;
export const Phase = { Lobby: 0, Countdown: 1, Running: 2, Finished: 3 } as const;
export const End = { None: 0, TimeUp: 1, Crashed: 2, Aborted: 3 } as const;
export const Ev = { Countdown: 0, Go: 1, Stroke: 2, Sync: 3, NetThrow: 4, Catch: 5, Miss: 6, Hit: 7, ShieldOn: 8, ShieldOff: 9, GameOver: 10, Combo: 11, BotTakeover: 12, PowerOn: 13, PowerOff: 14 } as const;
export const NO_ENTITY = 0xffff;
const TOXIC = 6;

const WASTE_KG = [0.05, 0.12, 0.35, 0.15, 0.2, 1.5, 2.5];
const WASTE_R = [0.35, 0.4, 0.32, 0.3, 0.3, 0.45, 0.5];
const OBS_R = [1.0, 2.0, 1.7, 1.3, 0.45, 0.8];
const OBS_HL = [0, 0, 0.4, 2.2, 1.6, 0];
const DEG = Math.PI / 180;

const clamp = (v: number, a: number, b: number) => (v < a ? a : v > b ? b : v);
const clamp01 = (v: number) => clamp(v, 0, 1);
const lerp = (a: number, b: number, k: number) => a + (b - a) * k;
const repeat = (t: number, len: number) => t - Math.floor(t / len) * len;

/** mulberry32 */
export class Rng {
  s: number;
  constructor(seed: number) { this.s = seed >>> 0; }
  next() { let t = (this.s = (this.s + 0x6d2b79f5) >>> 0); t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61); return ((t ^ (t >>> 14)) >>> 0) / 4294967296; }
  range(a: number, b: number) { return a + this.next() * (b - a); }
  int(a: number, bExcl: number) { return a + Math.floor(this.next() * (bExcl - a)); }
}

export interface Entity {
  id: number; kind: number; sub: number; alive: boolean;
  x: number; z: number; baseX: number; y: number; yaw: number; phase: number; radius: number; halfLen: number; flags: number;
}
const dangerous = (e: Entity) => e.kind === Kind.Obstacle && (e.sub !== Obs.Shark || e.flags === 2);

export interface MatchEvent { type: number; team: number; role: number; entity: number; value: number; extra: number; x: number; z: number; }

export function flowSample(seed: number, x: number, z: number, t: Tuning): [number, number] {
  const hw = t.riverWidth * 0.5;
  const edge = clamp01(Math.abs(x) / hw);
  const s = (seed % 1000) * 0.01;
  const vz = t.currentSpeed * (1 + 0.15 * Math.sin(z * 0.031 + s)) * (1 - 0.35 * edge * edge);
  const vx = 0.35 * Math.sin(z * 0.045 + s * 1.7) * (1 - edge * edge);
  return [vx, vz];
}

export class RaftSim {
  x = 0; z = 0; heading = 0; vx = 0; vz = 0; yawRate = 0;
  lastLeft = -99; lastRight = -99; lastLeftBack = false; lastRightBack = false;
  worldVx = 0; worldVz = 0; maxSpeedBonus = 0;

  copyFrom(o: RaftSim) {
    this.x = o.x; this.z = o.z; this.heading = o.heading; this.vx = o.vx; this.vz = o.vz; this.yawRate = o.yawRate;
    this.lastLeft = o.lastLeft; this.lastRight = o.lastRight; this.lastLeftBack = o.lastLeftBack; this.lastRightBack = o.lastRightBack;
    this.maxSpeedBonus = o.maxSpeedBonus;
  }

  stroke(left: boolean, power: number, back: boolean, now: number, t: Tuning, boost = 1): boolean {
    const other = left ? this.lastRight : this.lastLeft;
    const otherBack = left ? this.lastRightBack : this.lastLeftBack;
    const sync = now - other <= t.syncWindow && otherBack === back;
    const imp = t.strokeImpulse * power * (back ? -t.backStrokeFactor : 1) * (sync ? t.syncBoost : 1) * boost;
    this.vx += Math.sin(this.heading) * imp;
    this.vz += Math.cos(this.heading) * imp;
    this.yawRate += t.yawKickDeg * DEG * power * (left ? 1 : -1) * (back ? -1 : 1);
    if (sync) this.yawRate *= 0.3;
    if (left) { this.lastLeft = now; this.lastLeftBack = back; } else { this.lastRight = now; this.lastRightBack = back; }
    if (sync) { this.lastLeft = -99; this.lastRight = -99; }
    return sync;
  }

  step(dt: number, seed: number, t: Tuning) {
    this.yawRate *= Math.exp(-t.yawDamping * dt);
    this.heading += this.yawRate * dt;
    const maxH = t.maxHeadingDeg * DEG;
    if (this.heading > maxH) { this.heading = maxH; if (this.yawRate > 0) this.yawRate = 0; }
    if (this.heading < -maxH) { this.heading = -maxH; if (this.yawRate < 0) this.yawRate = 0; }
    const fx = Math.sin(this.heading), fz = Math.cos(this.heading);
    const rx = fz, rz = -fx;
    let fwd = this.vx * fx + this.vz * fz;
    let lat = this.vx * rx + this.vz * rz;
    fwd *= Math.exp(-t.waterDrag * dt);
    lat *= Math.exp(-t.lateralDrag * dt);
    this.vx = fwd * fx + lat * rx;
    this.vz = fwd * fz + lat * rz;
    const [flx, flz] = flowSample(seed, this.x, this.z, t);
    let wx = this.vx + flx, wz = this.vz + flz;
    const sp = Math.sqrt(wx * wx + wz * wz);
    const vmax = t.maxSpeed + this.maxSpeedBonus;
    if (sp > vmax) { const k = vmax / sp; wx *= k; wz *= k; this.vx = wx - flx; this.vz = wz - flz; }
    this.worldVx = wx; this.worldVz = wz;
    this.x += wx * dt;
    this.z += wz * dt;
    const limit = t.riverWidth * 0.5 - t.raftRadius;
    if (this.x > limit) { this.x = limit; if (this.vx > 0) this.vx = -this.vx * 0.3; this.heading *= 0.9; }
    if (this.x < -limit) { this.x = -limit; if (this.vx < 0) this.vx = -this.vx * 0.3; this.heading *= 0.9; }
  }
}

export function capsuleDistance(o: Entity, x: number, z: number) {
  const dx = Math.max(0, Math.abs(x - o.x) - o.halfLen);
  const dz = z - o.z;
  return Math.sqrt(dx * dx + dz * dz) - o.radius;
}

function pickWeighted(rng: Rng, w: number[]) {
  let sum = 0;
  for (const v of w) sum += v;
  let r = rng.next() * sum;
  for (let i = 0; i < w.length; i++) { r -= w[i]; if (r <= 0) return i; }
  return w.length - 1;
}

/** Deterministic course from seed; entities sorted by z, id = index. */
export function generateCourse(seed: number, t: Tuning): { E: Entity[]; lanes: number[] } {
  const rng = new Rng(seed);
  const R = (a: number, b: number) => rng.range(a, b);
  const list: Entity[] = [];
  const hw = t.riverWidth * 0.5;
  const laneHalf = t.freeLaneFactor * t.raftWidth * 0.5;
  const end = t.courseLength + 120;
  const chunks = Math.ceil(end / t.chunkLength);
  const lanes = new Array<number>(chunks).fill(0);
  let lane = 0, nextObs = 30;
  let obstacles: Entity[] = [];
  const mk = (kind: number, sub: number, x: number, z: number, radius: number, halfLen = 0, phase = 0, yaw = 0): Entity =>
    ({ id: 0, kind, sub, alive: true, x, z, baseX: x, y: 0, yaw, phase, radius, halfLen, flags: 0 });

  for (let c = 0; c < chunks; c++) {
    const cz0 = c * t.chunkLength, cz1 = cz0 + t.chunkLength;
    lane = clamp(lane + R(-5, 5), -hw + laneHalf + 0.5, hw - laneHalf - 0.5);
    lanes[c] = lane;
    const statics: [number, number][] = [];
    while (nextObs < cz1) {
      const oz = nextObs;
      let type: number;
      do type = pickWeighted(rng, t.obstacleWeights); while (oz < t.sharkMinDistance && type === Obs.Shark);
      const r = OBS_R[type], hl = OBS_HL[type], ext = r + hl;
      let ox = 0, placed = false;
      for (let a = 0; a < 12 && !placed; a++) {
        ox = R(-hw + ext * 0.6, hw - ext * 0.6);
        if (type === Obs.Shark) { ox = clamp(ox, -hw + t.sharkAmplitude * 0.5, hw - t.sharkAmplitude * 0.5); placed = true; break; }
        placed = ox + ext < lane - laneHalf || ox - ext > lane + laneHalf;
      }
      if (placed) {
        const e = mk(Kind.Obstacle, type, ox, oz, r, hl, R(0, 10), R(0, 360));
        if (type === Obs.Log || type === Obs.Wreck) e.yaw = 0;
        obstacles.push(e);
        if (type !== Obs.Shark) statics.push([ox - ext, ox + ext]);
      }
      const time = oz / Math.max(1, t.expectedAvgSpeed);
      nextObs += lerp(t.obstacleSpacingStart, t.obstacleSpacingEnd, clamp01(time / t.obstacleRampTime)) * R(0.8, 1.2);
    }
    // validation: guarantee a free lane >= freeLaneFactor x raft width in every chunk
    statics.sort((a, b) => a[0] - b[0]);
    let cursor = -hw, free = false;
    for (const [a, b] of statics) { if (a - cursor >= laneHalf * 2) { free = true; break; } cursor = Math.max(cursor, b); }
    if (!free && hw - cursor < laneHalf * 2)
      obstacles = obstacles.filter(o => !(o.z >= cz0 && o.z < cz1 && o.sub !== Obs.Shark && o.x + o.radius + o.halfLen > lane - laneHalf && o.x - o.radius - o.halfLen < lane + laneHalf));
  }
  list.push(...obstacles);

  for (let z = 14; z < end; z += t.wasteClusterSpacing * R(0.75, 1.25)) {
    const n = rng.int(t.wasteClusterMin, t.wasteClusterMax + 1);
    let cx = R(-hw + 2, hw - 2), cz = z;
    if (rng.next() < t.wasteNearObstacleChance) {
      let best = -1, bestD = 12;
      for (let i = 0; i < obstacles.length; i++) {
        const d = Math.abs(obstacles[i].z - z);
        if (d < bestD && obstacles[i].sub !== Obs.Shark) { bestD = d; best = i; }
      }
      if (best >= 0) {
        const o = obstacles[best];
        const side = rng.next() < 0.5 ? -1 : 1;
        cx = clamp(o.x + side * (o.radius + o.halfLen + 1.4), -hw + 1, hw - 1);
        cz = o.z + R(-1.5, 1.5);
      }
    }
    for (let k = 0; k < n; k++) {
      const wt = pickWeighted(rng, t.wasteWeights);
      const wx = clamp(cx + R(-1.6, 1.6), -hw + 0.6, hw - 0.6);
      const wz = cz + R(-1.6, 1.6);
      if (obstacles.some(o => Math.abs(o.z - wz) <= 5 && capsuleDistance(o, wx, wz) < 0.6)) continue;
      list.push(mk(Kind.Waste, wt, wx, wz, WASTE_R[wt], 0, R(0, 6.28), R(0, 360)));
    }
  }

  const first = Math.max(t.shieldMinTime * t.expectedAvgSpeed, 100);
  for (let sz = first; sz < end; sz += t.shieldSpacing * R(0.85, 1.15)) {
    const c = clamp(Math.floor(sz / t.chunkLength), 0, chunks - 1);
    list.push(mk(Kind.Power, Power.Shield, lanes[c] + R(-2, 2), sz, 0.8, 0, R(0, 6)));
  }
  let k = 0;
  for (let pz = first + t.shieldSpacing * 0.5; pz < end; pz += t.shieldSpacing * R(0.85, 1.15), k++) {
    const c = clamp(Math.floor(pz / t.chunkLength), 0, chunks - 1);
    list.push(mk(Kind.Power, k % 2 === 0 ? Power.Magnet : Power.Turbo, lanes[c] + R(-2.5, 2.5), pz, 0.8, 0, R(0, 6)));
  }
  list.sort((a, b) => a.z - b.z);
  list.forEach((e, i) => (e.id = i));
  return { E: list, lanes };
}

export function sharkPose(e: Entity, clock: number, t: Tuning) {
  const omega = t.sharkSpeed / Math.max(0.1, t.sharkAmplitude);
  const circle = 2, attack = 4.5;
  const local = repeat(clock + e.phase, circle + t.sharkTelegraph + attack);
  e.flags = local < circle ? 0 : local < circle + t.sharkTelegraph ? 1 : 2;
  e.x = e.baseX + t.sharkAmplitude * Math.sin(omega * clock + e.phase);
  e.yaw = Math.cos(omega * clock + e.phase) >= 0 ? 90 : -90;
  e.y = e.flags === 2 ? 0.15 + 0.08 * Math.sin(clock * 6 + e.phase) : -0.35;
}

export class Score {
  catchScore = 0; throws = 0; hits = 0; kg = 0;
  counts = [0, 0, 0, 0, 0, 0, 0];
  chain: number[] = [];
  comboArmed = false;

  addCatch(type: number, now: number, t: Tuning): { pts: number; combo: boolean } {
    this.counts[type]++;
    this.kg += WASTE_KG[type];
    if (type === TOXIC) { this.chain = []; this.comboArmed = false; return { pts: 0, combo: false }; }
    let pts = t.wasteScores[type];
    let combo = false;
    if (this.comboArmed) { combo = true; this.comboArmed = false; this.chain = []; pts = Math.round(pts * t.comboMultiplier); }
    else {
      this.chain = this.chain.filter(c => now - c <= t.comboWindow);
      this.chain.push(now);
      if (this.chain.length >= t.comboCount) { this.comboArmed = true; this.chain = []; }
    }
    this.catchScore += pts;
    return { pts, combo };
  }
  toxicPenalty(t: Tuning) { return this.counts[TOXIC] * Math.abs(t.wasteScores[TOXIC]); }
  total(t: Tuning) { return this.catchScore - this.toxicPenalty(t); }
}

/** Game mode abstraction (Coop2Min now; Battle later: N teams, HP, raft collisions). */
export interface ModeConfig { name: string; duration: number; teams: number; hitsToGameOver: number; raftCollisions: boolean; }
export interface GameMode {
  config: ModeConfig;
  onObstacleHit(sim: MatchSim, team: Team): boolean; // true = eliminated
  checkEnd(sim: MatchSim): number; // End reason or End.None
}
export class Coop2MinMode implements GameMode {
  config: ModeConfig;
  constructor(t: Tuning) { this.config = { name: "Coop 2:00", duration: t.matchDuration, teams: 1, hitsToGameOver: t.hitsToGameOver, raftCollisions: false }; }
  onObstacleHit(sim: MatchSim, team: Team) { return team.takeHit(sim.time); }
  checkEnd(sim: MatchSim) {
    if (sim.teams.some(t => t.hp <= 0)) return End.Crashed;
    return sim.time >= this.config.duration ? End.TimeUp : End.None;
  }
}

export class Team {
  raft = new RaftSim();
  score = new Score();
  bot = [false, true, true, true];
  strokeReady = [0, 0, 0, 0];
  brain = new BotBrain();
  hp: number; invulnUntil = 0;
  shieldActive = false; shieldUntil = -1; magnetActive = false; magnetUntil = -1; turboActive = false; turboUntil = -1;
  netReadyAt = 0; startZ = 0;
  constructor(public id: number, hits: number, startX: number) { this.hp = Math.max(1, hits); this.raft.x = startX; }
  takeHit(now: number) { if (this.hp <= 0 || now < this.invulnUntil) return false; this.hp--; this.invulnUntil = now + 1.5; return this.hp <= 0; }
}

export interface Results { reason: number; total: number; catchScore: number; toxicPenalty: number; throws: number; hits: number; counts: number[]; distance: number; kg: number; duration: number; }

const HISTORY = 32; // ticks of entity/raft history for lag compensation (>= 300 ms at 60 Hz)

export class MatchSim {
  tickRate = 60;
  E: Entity[]; lanes: number[]; count: number;
  teams: Team[] = [];
  phase: number = Phase.Countdown;
  time = 0; clock = 0; countdownLeft: number; tick = 0;
  events: MatchEvent[] = [];
  results: Results | null = null;
  rng: Rng;
  histX: Float32Array; histZ: Float32Array; raftHX = new Float32Array(HISTORY); raftHZ = new Float32Array(HISTORY);
  lastCountdown = -1;
  nets: { team: number; land: number; x: number; z: number }[] = [];

  constructor(public seed: number, public T: Tuning, public mode: GameMode) {
    this.rng = new Rng(seed ^ 0x5bd1e995);
    const c = generateCourse(seed, T);
    this.E = c.E; this.lanes = c.lanes; this.count = c.E.length;
    this.histX = new Float32Array(HISTORY * this.count);
    this.histZ = new Float32Array(HISTORY * this.count);
    for (let i = 0; i < mode.config.teams; i++) this.teams.push(new Team(i, mode.config.hitsToGameOver, i * 4));
    this.countdownLeft = T.countdown;
    this.updateEntities();
    this.record();
  }

  emit(type: number, team = 0, role = 0, entity = NO_ENTITY, value = 0, extra = 0, x = 0, z = 0) {
    this.events.push({ type, team, role, entity, value: clamp(Math.round(value), -32768, 32767), extra, x, z });
  }

  step(dt: number) {
    this.tick++;
    this.clock += dt;
    if (this.phase === Phase.Countdown) {
      this.countdownLeft -= dt;
      const c = Math.ceil(this.countdownLeft);
      if (c !== this.lastCountdown && c > 0) { this.lastCountdown = c; this.emit(Ev.Countdown, 0, 0, NO_ENTITY, c); }
      if (this.countdownLeft <= 0) { this.phase = Phase.Running; this.emit(Ev.Go); }
    }
    this.updateEntities();
    if (this.phase === Phase.Running) {
      this.time += dt;
      const T = this.T;
      for (const team of this.teams) {
        team.brain.step(this, team);
        team.raft.step(dt, this.seed, T);
        if (team.shieldActive && this.time >= team.shieldUntil) { team.shieldActive = false; this.emit(Ev.ShieldOff, team.id); }
        if (team.magnetActive && this.time >= team.magnetUntil) { team.magnetActive = false; this.emit(Ev.PowerOff, team.id, 0, NO_ENTITY, 0, Power.Magnet); }
        if (team.turboActive && this.time >= team.turboUntil) { team.turboActive = false; this.emit(Ev.PowerOff, team.id, 0, NO_ENTITY, 0, Power.Turbo); }
        team.raft.maxSpeedBonus = team.turboActive ? T.turboMaxSpeedBonus : 0;
        this.collide(team);
      }
      this.resolveNets();
      const end = this.mode.checkEnd(this);
      if (end !== End.None) this.finish(end);
    }
    this.record();
  }

  abort() { if (this.phase !== Phase.Finished) this.finish(End.Aborted); }

  finish(reason: number) {
    this.phase = Phase.Finished;
    const team = this.teams[0], T = this.T;
    this.results = {
      reason, catchScore: team.score.catchScore, toxicPenalty: team.score.toxicPenalty(T), total: team.score.total(T),
      throws: team.score.throws, hits: team.score.hits, counts: [...team.score.counts], distance: team.raft.z - team.startZ, kg: team.score.kg, duration: this.time,
    };
    this.emit(Ev.GameOver, team.id, 0, NO_ENTITY, reason);
  }

  updateEntities() {
    const c = this.clock, T = this.T;
    for (const e of this.E) {
      if (!e.alive) continue;
      if (e.kind === Kind.Waste) { e.y = 0.06 * Math.sin(c * 2.2 + e.phase); e.yaw = e.phase * 57 + c * 25; }
      else if (e.kind === Kind.Power) { e.y = 0.5 + 0.15 * Math.sin(c * 3 + e.phase); e.yaw = c * 90; }
      else if (e.sub === Obs.Shark) sharkPose(e, c, T);
      else if (e.sub === Obs.Iceberg || e.sub === Obs.Wreck || e.sub === Obs.Log) e.y = 0.05 * Math.sin(c * 1.3 + e.phase);
    }
  }

  record() {
    const slot = (this.tick % HISTORY) * this.count;
    for (let i = 0; i < this.count; i++) { this.histX[slot + i] = this.E[i].x; this.histZ[slot + i] = this.E[i].z; }
    this.raftHX[this.tick % HISTORY] = this.teams[0].raft.x;
    this.raftHZ[this.tick % HISTORY] = this.teams[0].raft.z;
  }

  historicEntity(i: number, back: number): [number, number] {
    if (back <= 0) return [this.E[i].x, this.E[i].z];
    back = clamp(back, 0, Math.min(HISTORY - 1, this.tick));
    const slot = ((this.tick - back) % HISTORY) * this.count;
    return [this.histX[slot + i], this.histZ[slot + i]];
  }

  historicRaft(back: number): [number, number] {
    if (back <= 0) return [this.teams[0].raft.x, this.teams[0].raft.z];
    back = clamp(back, 0, Math.min(HISTORY - 1, this.tick));
    const s = (this.tick - back) % HISTORY;
    return [this.raftHX[s], this.raftHZ[s]];
  }

  lowerBound(z: number) {
    let lo = 0, hi = this.count;
    while (lo < hi) { const mid = (lo + hi) >> 1; if (this.E[mid].z < z) lo = mid + 1; else hi = mid; }
    return lo;
  }

  collide(team: Team) {
    const r = team.raft, T = this.T;
    for (let i = this.lowerBound(r.z - 4); i < this.count && this.E[i].z < r.z + 4; i++) {
      const e = this.E[i];
      if (!e.alive) continue;
      if (e.kind === Kind.Power) {
        const dx = e.x - r.x, dz = e.z - r.z, rr = T.raftRadius + e.radius + 0.4;
        if (dx * dx + dz * dz >= rr * rr) continue;
        e.alive = false;
        if (e.sub === Power.Magnet) { team.magnetActive = true; team.magnetUntil = this.time + T.magnetDuration; this.emit(Ev.PowerOn, team.id, 0, e.id, T.magnetDuration * 10, e.sub, e.x, e.z); }
        else if (e.sub === Power.Turbo) { team.turboActive = true; team.turboUntil = this.time + T.turboDuration; this.emit(Ev.PowerOn, team.id, 0, e.id, T.turboDuration * 10, e.sub, e.x, e.z); }
        else { team.shieldActive = true; team.shieldUntil = this.time + T.shieldDuration; this.emit(Ev.ShieldOn, team.id, 0, e.id, 0, 0, e.x, e.z); }
      } else if (e.kind === Kind.Obstacle && dangerous(e)) {
        if (capsuleDistance(e, r.x, r.z) >= T.raftRadius * 0.9) continue;
        if (team.shieldActive || this.time < team.invulnUntil) continue;
        const dead = this.mode.onObstacleHit(this, team);
        this.emit(Ev.Hit, team.id, 0, e.id, team.hp, 0, e.x, e.z);
        if (!dead) { r.vz = -2; r.vx = (r.x - e.x) * 1.5; }
      }
    }
  }

  applyStroke(team: Team, role: number, power: number, back: boolean): boolean {
    if (this.phase !== Phase.Running || (role !== Role.Left && role !== Role.Right)) return false;
    if (this.time < team.strokeReady[role]) return false;
    const T = this.T;
    team.strokeReady[role] = this.time + T.strokeCooldown;
    power = clamp(power, T.minStrokePower, T.maxStrokePower);
    const sync = team.raft.stroke(role === Role.Left, power, back, this.time, T, team.turboActive ? T.turboImpulseScale : 1);
    this.emit(Ev.Stroke, team.id, role, NO_ENTITY, power * 100, back ? 1 : 0);
    if (sync) this.emit(Ev.Sync, team.id, 0, NO_ENTITY, T.syncBoost * 100, 0, team.raft.x, team.raft.z);
    return true;
  }

  /** Human stroke: also notifies the bot partner (sync assist). */
  humanStroke(team: Team, role: number, power: number, back: boolean) {
    const ok = this.applyStroke(team, role, power, back);
    if (ok) team.brain.onHumanStroke(this, team, role, back);
    return ok;
  }

  /** rewindSec: lag compensation (RTT/2, capped by caller). */
  applyThrow(team: Team, target: number, dirX: number, dirZ: number, rewindSec: number): boolean {
    const T = this.T;
    if (this.phase !== Phase.Running || this.time < team.netReadyAt) return false;
    team.netReadyAt = this.time + T.netCooldown;
    team.score.throws++;
    const back = Math.round(rewindSec * this.tickRate);
    const [rx, rz] = this.historicRaft(back);
    const range = T.netRange;
    let lx: number, lz: number;
    const tgt = target < this.count ? this.E[target] : null;
    let hx = 0, hz = 0;
    if (tgt && tgt.alive && tgt.kind === Kind.Waste) [hx, hz] = this.historicEntity(target, back);
    if (tgt && tgt.alive && tgt.kind === Kind.Waste && Math.hypot(hx - rx, hz - rz) <= range * 1.15 + tgt.radius) { lx = hx; lz = hz; }
    else {
      let dx = dirX, dz = dirZ;
      if (dx * dx + dz * dz < 1e-4) { dx = Math.sin(team.raft.heading); dz = Math.cos(team.raft.heading); }
      const dl = Math.hypot(dx, dz); dx /= dl; dz /= dl;
      let best = -1, bestScore = Infinity;
      for (let i = this.lowerBound(rz - range); i < this.count && this.E[i].z < rz + range; i++) {
        const e = this.E[i];
        if (!e.alive || e.kind !== Kind.Waste) continue;
        const [ex, ez] = this.historicEntity(i, back);
        const px = ex - rx, pz = ez - rz, d = Math.hypot(px, pz);
        if (d > range || d < 0.01) continue;
        const cos = (px * dx + pz * dz) / d;
        if (cos < 0.9) continue; // ~25 deg cone
        const perp = d * Math.sqrt(Math.max(0, 1 - cos * cos));
        if (perp < bestScore) { bestScore = perp; best = i; }
      }
      if (best >= 0) [lx, lz] = this.historicEntity(best, back);
      else { lx = rx + dx * range * 0.75; lz = rz + dz * range * 0.75; }
    }
    const travel = Math.max(0.02, T.netTravel - rewindSec);
    this.nets.push({ team: team.id, land: this.time + travel, x: lx, z: lz });
    this.emit(Ev.NetThrow, team.id, Role.Collector, NO_ENTITY, travel * 1000, 0, lx, lz);
    return true;
  }

  resolveNets() {
    const T = this.T;
    for (let n = this.nets.length - 1; n >= 0; n--) {
      const net = this.nets[n];
      if (this.time < net.land) continue;
      this.nets.splice(n, 1);
      const team = this.teams[net.team];
      let caught = 0;
      const netR = T.netRadius * (team.magnetActive ? T.magnetRadiusScale : 1);
      for (let i = this.lowerBound(net.z - 4); i < this.count && this.E[i].z < net.z + 4; i++) {
        const e = this.E[i];
        if (!e.alive || e.kind !== Kind.Waste) continue;
        const rr = (netR + e.radius) * T.hitRadiusScale, dx = e.x - net.x, dz = e.z - net.z;
        if (dx * dx + dz * dz > rr * rr) continue;
        e.alive = false;
        caught++;
        const { pts, combo } = team.score.addCatch(e.sub, this.time, T);
        this.emit(Ev.Catch, team.id, Role.Collector, e.id, pts, e.sub | (combo ? 0x80 : 0), e.x, e.z);
        if (team.score.comboArmed) this.emit(Ev.Combo, team.id, 0, NO_ENTITY, T.comboMultiplier * 10);
      }
      if (caught > 0) team.score.hits++;
      else this.emit(Ev.Miss, team.id, Role.Collector, NO_ENTITY, 0, 0, net.x, net.z);
    }
  }

  setBot(team: number, role: number, isBot: boolean) {
    const t = this.teams[team];
    if (!t || role < 1 || role > 3 || t.bot[role] === isBot) return;
    t.bot[role] = isBot;
    if (isBot) this.emit(Ev.BotTakeover, team, role);
  }
}

// ---------------- bots ----------------
const L = 1, Rb = 2, B = 4;
const POLICIES: [number, number][] = [
  [L | Rb, L | Rb], [0, 0], [L, L], [Rb, Rb], [L, L | Rb], [Rb, L | Rb], [L, 0], [Rb, 0], [L | Rb, 0], [L | Rb | B, 0], [L | Rb | B, L | Rb | B],
  [L, Rb], [Rb, L], [L | B, 0], [Rb | B, 0],
];
const HORIZON = 3.2, STEP_DT = 1 / 30;

/** Paddler bot = short-horizon planner + "sync assist" (mirrors a human partner's stroke); collector bot = nearest safe waste. */
export class BotBrain {
  nextAct = 0; nextNet = 0; netAimAt = -1; netTarget = -1;
  progressCheckAt = 0; lastProgressZ = 0; stallLevel = 0;
  mirrorAt = -1; mirrorRole = 0; mirrorBack = false;
  probe = new RaftSim();

  step(m: MatchSim, team: Team) {
    const t = m.T, now = m.time;
    const lb = team.bot[Role.Left], rb = team.bot[Role.Right];
    if (now >= this.progressCheckAt) {
      this.stallLevel = team.raft.z - this.lastProgressZ < 3 && this.progressCheckAt > 0 ? Math.min(this.stallLevel + 1, 6) : 0;
      this.lastProgressZ = team.raft.z;
      this.progressCheckAt = now + 2;
    }
    // sync assist: mirror the human partner's stroke shortly after it
    if (this.mirrorAt >= 0 && now >= this.mirrorAt) {
      if (team.bot[this.mirrorRole]) m.applyStroke(team, this.mirrorRole, 0.9, this.mirrorBack);
      this.mirrorAt = -1;
      this.nextAct = Math.max(this.nextAct, now + t.botStrokeInterval * 0.6);
    }
    if ((lb || rb) && now >= this.nextAct && this.mirrorAt < 0) {
      const interval = t.botStrokeInterval;
      let bestFeasible = Infinity, act = 0;
      for (const [first, rest] of POLICIES) {
        let cost = this.evaluate(m, team.raft, first, rest, interval, team.turboActive ? t.turboImpulseScale : 1);
        if (this.stallLevel > 0 && ((first | rest) & B) !== 0) cost += this.stallLevel * 12;
        if (this.stallLevel > 0 && first === 0 && rest === 0) cost += this.stallLevel * 6;
        const feasible = (lb || ((first | rest) & L) === 0) && (rb || ((first | rest) & Rb) === 0);
        if (feasible && cost < bestFeasible) { bestFeasible = cost; act = first; }
      }
      const back = (act & B) !== 0;
      let did = false;
      if ((act & L) && lb) did = m.applyStroke(team, Role.Left, 0.9, back) || did;
      if ((act & Rb) && rb) did = m.applyStroke(team, Role.Right, 0.9, back) || did;
      this.nextAct = now + (did ? interval : 0.08);
    }
    if (team.bot[Role.Collector]) this.collect(m, team);
  }

  onHumanStroke(m: MatchSim, team: Team, role: number, back: boolean) {
    const other = role === Role.Left ? Role.Right : Role.Left;
    if (!team.bot[other] || m.rng.next() > m.T.botSyncChance) return;
    // skip the mirror if it would steer into danger
    const first = other === Role.Left ? L : Rb;
    if (this.evaluate(m, team.raft, first | (back ? B : 0), 0, m.T.botStrokeInterval, team.turboActive ? m.T.turboImpulseScale : 1) >= 1000) return;
    this.mirrorRole = other; this.mirrorBack = back;
    this.mirrorAt = m.time + 0.03 + m.rng.next() * 0.07; // within 0.1 s -> inside the 0.25 s sync window
  }

  evaluate(m: MatchSim, raft: RaftSim, first: number, rest: number, interval: number, boost: number): number {
    const t = m.T, p = this.probe;
    p.copyFrom(raft);
    const startZ = p.z, hw = t.riverWidth * 0.5;
    let next = 0, isFirst = true, cost = 0;
    for (let tau = 0; tau < HORIZON; tau += STEP_DT) {
      if (tau >= next) {
        const a = isFirst ? first : rest;
        isFirst = false;
        const back = (a & B) !== 0;
        if (a & L) p.stroke(true, 0.9, back, m.time + tau, t, boost);
        if (a & Rb) p.stroke(false, 0.9, back, m.time + tau, t, boost);
        next += interval;
      }
      p.step(STEP_DT, m.seed, t);
      const d = this.danger(m, p.x, p.z, m.clock + tau, t.raftRadius + 0.45);
      if (d > 0) { cost += 1000 * (HORIZON - tau + 0.5) + d * 50; break; }
    }
    cost += Math.max(0, Math.abs(p.x) - (hw - 3)) * 25;
    cost += Math.abs(p.heading) * 3;
    cost -= (p.z - startZ) * 0.6;
    for (let i = m.lowerBound(p.z); i < m.count && m.E[i].z < p.z + 16; i++) {
      const e = m.E[i];
      if (!e.alive || e.kind !== Kind.Obstacle || e.sub === Obs.Shark) continue;
      const overlap = e.radius + e.halfLen + t.raftRadius + 0.8 - Math.abs(p.x - e.x);
      if (overlap > 0) cost += overlap * 18 / (1 + this.stallLevel * 1.5) * (1.1 - (e.z - p.z) / 16);
    }
    for (let i = m.lowerBound(p.z - 5); i < m.count && m.E[i].z < p.z + 6; i++) {
      const e = m.E[i];
      if (!e.alive) continue;
      const dx = e.x - p.x, dz = e.z - p.z, d2 = dx * dx + dz * dz;
      if (e.kind === Kind.Waste && e.sub !== TOXIC && d2 < 30) cost -= 0.6;
      else if (e.kind === Kind.Power && d2 < 6) cost -= 8;
    }
    return cost;
  }

  shark: Entity = { id: 0, kind: 0, sub: 0, alive: true, x: 0, z: 0, baseX: 0, y: 0, yaw: 0, phase: 0, radius: 0, halfLen: 0, flags: 0 };
  danger(m: MatchSim, x: number, z: number, clock: number, radius: number) {
    for (let i = m.lowerBound(z - 4.5); i < m.count && m.E[i].z < z + 4.5; i++) {
      let e = m.E[i];
      if (!e.alive || e.kind !== Kind.Obstacle) continue;
      if (e.sub === Obs.Shark) { Object.assign(this.shark, e); sharkPose(this.shark, clock, m.T); if (this.shark.flags !== 2) continue; e = this.shark; }
      const d = capsuleDistance(e, x, z);
      if (d < radius) return radius - d;
    }
    return 0;
  }

  /** Nearest non-toxic waste in range; human-like reaction delay 0.3–0.6 s; botAccuracy hit chance. */
  collect(m: MatchSim, team: Team) {
    const t = m.T, r = team.raft, now = m.time;
    if (now < this.nextNet) return;
    if (this.netTarget >= 0) {
      if (now < this.netAimAt) return;
      const w = m.E[this.netTarget];
      this.netTarget = -1;
      if (w.alive && Math.hypot(w.x - r.x, w.z - r.z) < t.netRange) {
        if (m.rng.next() < t.botAccuracy) m.applyThrow(team, w.id, 0, 0, 0);
        else { const ang = (m.rng.next() < 0.5 ? -1 : 1) * (0.6 + m.rng.next() * 0.4), dx = w.x - r.x, dz = w.z - r.z; m.applyThrow(team, NO_ENTITY, dx * Math.cos(ang) - dz * Math.sin(ang), dx * Math.sin(ang) + dz * Math.cos(ang), 0); } // miss: 35-57 deg off target
      }
      this.nextNet = now + t.netCooldown;
      return;
    }
    let best = -1, bestD = t.netRange * 0.9;
    for (let i = m.lowerBound(r.z - 2); i < m.count && m.E[i].z < r.z + t.netRange; i++) {
      const e = m.E[i];
      if (!e.alive || e.kind !== Kind.Waste || e.sub === TOXIC) continue;
      const d = Math.hypot(e.x - r.x, e.z - r.z);
      if (d < bestD) { bestD = d; best = i; }
    }
    if (best >= 0) { this.netTarget = best; this.netAimAt = now + 0.3 + m.rng.next() * 0.3; }
  }
}
