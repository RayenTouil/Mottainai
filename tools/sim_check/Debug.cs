using System;
using Mottainai;
using Mottainai.Gameplay;

static class DebugRun
{
    // dotnet sim.dll debug <seed> <fromSec>
    public static void Run(int seedIndex, float fromSec)
    {
        var t = new TuningData();
        var sim = new MatchSim(seedIndex * 7919, t, new Coop2MinMode(t));
        float nextPrint = fromSec;
        while (sim.Phase != MatchPhase.Finished)
        {
            sim.Step(1f / 60f);
            sim.Events.Clear();
            if (sim.Time >= nextPrint)
            {
                nextPrint += 2f;
                var r = sim.Teams[0].raft;
                var b = sim.Teams[0].brain;
                Console.WriteLine($"t={sim.Time:F1} x={r.x:F2} z={r.z:F2} h={r.heading * 57.3f:F1} vx={r.vx:F2} vz={r.vz:F2} fwd={r.ForwardSpeed:F2} act={b.DebugAct} costs=[{string.Join(" ", Array.ConvertAll(b.DebugCosts, c => c.ToString("F0")))}]");
            }
        }
    }
}
