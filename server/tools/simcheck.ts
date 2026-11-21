// Headless all-bot matches on the TS sim (same check as tools/sim_check for C#).
import { Coop2MinMode, End, Ev, Kind, MatchSim, Obs, Phase, TUNING } from "../src/sim";
const runs = Number(process.argv[2] ?? 20);
let crashes = 0, sumScore = 0, sumDist = 0, sumAcc = 0, sumSharks = 0, maxStepMs = 0, totalSteps = 0, totalMs = 0;
for (let s = 1; s <= runs; s++) {
  const sim = new MatchSim(s * 7919, TUNING, new Coop2MinMode(TUNING));
  let hit = "";
  while (sim.phase !== Phase.Finished && sim.tick < 60 * 200) {
    const t0 = performance.now();
    sim.step(1 / 60);
    const ms = performance.now() - t0; totalMs += ms; totalSteps++; if (ms > maxStepMs) maxStepMs = ms;
    for (const e of sim.events) if (e.type === Ev.Hit) { const o = sim.E[e.entity]; hit = ` hit ${["Rock","BigRock","Iceberg","Wreck","Log","Shark"][o.sub]}@z${o.z.toFixed(0)}`; }
    sim.events.length = 0;
  }
  const r = sim.results!;
  const sharks = sim.E.filter(e => e.kind === Kind.Obstacle && e.sub === Obs.Shark).length;
  sumSharks += sharks;
  if (r.reason === End.Crashed) crashes++;
  sumScore += r.total; sumDist += r.distance; sumAcc += r.throws ? r.hits / r.throws : 0;
  console.log(`seed ${s}: ${r.reason === End.TimeUp ? "TimeUp " : "Crashed"} t=${r.duration.toFixed(1)}s dist=${r.distance.toFixed(0)}m total=${r.total} acc=${(r.hits / Math.max(1, r.throws) * 100).toFixed(0)}% entities=${sim.count}${hit}`);
}
console.log(`crashes ${crashes}/${runs} | avg dist ${(sumDist / runs).toFixed(0)} m score ${(sumScore / runs).toFixed(0)} acc ${(sumAcc / runs * 100).toFixed(0)}% sharks/course ${(sumSharks / runs).toFixed(1)} | step avg ${(totalMs / totalSteps * 1000).toFixed(0)} us max ${maxStepMs.toFixed(2)} ms`);
