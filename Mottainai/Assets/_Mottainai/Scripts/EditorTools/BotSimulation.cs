using System.Text;
using Mottainai.Gameplay;
using UnityEditor;
using UnityEngine;

namespace Mottainai.EditorTools
{
    /// Headless all-bot matches to tune AI/course. Batchmode: -executeMethod Mottainai.EditorTools.BotSimulation.Run
    public static class BotSimulation
    {
        [MenuItem("Mottainai/Debug/Simulate 20 bot matches")]
        public static void Run()
        {
            var t = new TuningData();
            var sb = new StringBuilder("[BotSim]\n");
            int crashes = 0; float sumTime = 0, sumScore = 0, sumDist = 0;
            const int runs = 20;
            for (int seed = 1; seed <= runs; seed++)
            {
                var sim = new MatchSim(seed * 7919, t, new Coop2MinMode(t));
                int hitEntity = -1;
                while (sim.Phase != MatchPhase.Finished && sim.Tick < 60 * 200)
                {
                    sim.Step(1f / 60f);
                    foreach (var e in sim.Events) if (e.type == EvType.Hit) hitEntity = e.entity;
                    sim.Events.Clear();
                }
                var r = sim.Results;
                if (r.reason == EndReason.Crashed) crashes++;
                sumTime += r.duration; sumScore += r.total; sumDist += r.distance;
                string hit = hitEntity >= 0 ? $"{ObstacleCatalog.Names[sim.E[hitEntity].sub]}@z{sim.E[hitEntity].z:F0}" : "";
                sb.AppendLine($"seed {seed,2}: {r.reason,-8} t={r.duration,5:F1}s dist={r.distance,5:F0}m total={r.total,4} acc={r.Accuracy * 100,3:F0}% {hit}");
                if (r.distance < 250f)
                {
                    var raft = sim.Teams[0].raft;
                    sb.Append($"   stuck raft x={raft.x:F1} z={raft.z:F1} h={raft.heading * Mathf.Rad2Deg:F0} | ");
                    for (int i = 0; i < sim.Count; i++)
                        if (sim.E[i].kind == EntityKind.Obstacle && Mathf.Abs(sim.E[i].z - raft.z) < 14f)
                            sb.Append($"{ObstacleCatalog.Names[sim.E[i].sub]}(x{sim.E[i].x:F1},z{sim.E[i].z:F1},r{sim.E[i].radius + sim.E[i].halfLen:F1}) ");
                    sb.AppendLine();
                }
            }
            sb.AppendLine($"crashes {crashes}/{runs}  avg time {sumTime / runs:F1}s  avg dist {sumDist / runs:F0}m  avg score {sumScore / runs:F0}");
            Debug.Log(sb.ToString());
        }
    }
}
