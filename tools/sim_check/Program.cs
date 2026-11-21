using System;
using Mottainai;
using Mottainai.Gameplay;

// Headless all-bot matches on the real Gameplay code (no Unity editor needed).
static class Program
{
    static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "debug") { DebugRun.Run(int.Parse(args[1]), float.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture)); return 0; }
        int runs = args.Length > 0 ? int.Parse(args[0]) : 20;
        int tick = args.Length > 1 ? int.Parse(args[1]) : 60;
        var t = new TuningData();
        int crashes = 0, stuck = 0;
        double sumT = 0, sumScore = 0, sumDist = 0, sumPow = 0;
        for (int seed = 1; seed <= runs; seed++)
        {
            var sim = new MatchSim(seed * 7919, t, new Coop2MinMode(t)) { TickRate = tick };
            int hit = -1, pow = 0, mag = 0, tur = 0;
            while (sim.Phase != MatchPhase.Finished && sim.Tick < tick * 200)
            {
                sim.Step(1f / tick);
                foreach (var e in sim.Events)
                {
                    if (e.type == EvType.Hit) hit = e.entity;
                    if (e.type == EvType.ShieldOn) pow++;
                    if (e.type == EvType.PowerOn) { pow++; if (e.extra == (byte)PowerUp.Magnet) mag++; else tur++; }
                }
                sim.Events.Clear();
            }
            var r = sim.Results;
            if (r.reason == EndReason.Crashed) crashes++;
            if (r.distance < 250f) stuck++;
            sumT += r.duration; sumScore += r.total; sumDist += r.distance; sumPow += pow;
            string hitInfo = hit >= 0 ? $" hit {ObstacleCatalog.Names[sim.E[hit].sub]}@z{sim.E[hit].z:F0}" : "";
            Console.WriteLine($"seed {seed,2}: {r.reason,-8} t={r.duration,5:F1}s dist={r.distance,5:F0}m total={r.total,5} acc={r.Accuracy * 100,3:F0}% pickups={pow} (magnet {mag}, turbo {tur}){hitInfo}");
            if (r.distance < 250f)
            {
                var rf = sim.Teams[0].raft;
                var sb = new System.Text.StringBuilder($"   short run: raft x={rf.x:F1} z={rf.z:F1} heading={rf.heading * 57.3f:F0} | ");
                for (int i = 0; i < sim.Count; i++)
                    if (sim.E[i].kind == EntityKind.Obstacle && Math.Abs(sim.E[i].z - rf.z) < 14f)
                        sb.Append($"{ObstacleCatalog.Names[sim.E[i].sub]}(x{sim.E[i].x:F1} z{sim.E[i].z:F1} r{sim.E[i].radius + sim.E[i].halfLen:F1}) ");
                Console.WriteLine(sb.ToString());
            }
        }
        Console.WriteLine($"tick {tick} Hz | crashes {crashes}/{runs} | short runs (<250 m) {stuck} | avg time {sumT / runs:F1}s dist {sumDist / runs:F0}m score {sumScore / runs:F0} pickups {sumPow / runs:F1}");
        return crashes * 10 > runs ? 1 : 0;
    }
}
