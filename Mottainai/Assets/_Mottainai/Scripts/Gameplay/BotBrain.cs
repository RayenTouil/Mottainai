using UnityEngine;

namespace Mottainai.Gameplay
{
    /// AI for any role not held by a human. Paddlers use a short-horizon planner that simulates
    /// candidate stroke patterns on a copy of the raft physics and picks the safest/most rewarding one.
    public class BotBrain
    {
        const byte L = 1, R = 2, B = 4; // left, right, back-paddle
        struct Policy { public byte first, rest; public Policy(byte f, byte r) { first = f; rest = r; } }
        static readonly Policy[] Policies =
        {
            new Policy(L | R, L | R), new Policy(0, 0), new Policy(L, L), new Policy(R, R), new Policy(L, L | R), new Policy(R, L | R),
            new Policy(L, 0), new Policy(R, 0), new Policy(L | R, 0), new Policy(L | R | B, 0), new Policy(L | R | B, L | R | B),
            new Policy(L, R), new Policy(R, L), new Policy(L | B, 0), new Policy(R | B, 0)
        };

        float nextAct, nextNet;
        readonly RaftSim probe = new RaftSim();
        const float Horizon = 3.2f, StepDt = 1f / 30f;

        public void Step(MatchSim m, TeamState team, System.Random rng)
        {
            var t = m.T;
            float now = m.Time;
            bool lb = team.IsBot(Role.LeftPaddler), rb = team.IsBot(Role.RightPaddler);

            if ((lb || rb) && now >= nextAct)
            {
                float interval = t.botStrokeInterval;
                float bestAll = float.MaxValue, bestFeasible = float.MaxValue;
                Policy pAll = Policies[1], pFeasible = Policies[1];
                for (int i = 0; i < Policies.Length; i++)
                {
                    var p = Policies[i];
                    float cost = Evaluate(m, team.raft, p, interval);
                    if (cost < bestAll) { bestAll = cost; pAll = p; }
                    bool feasible = (lb || ((p.first | p.rest) & L) == 0) && (rb || ((p.first | p.rest) & R) == 0);
                    if (feasible && cost < bestFeasible) { bestFeasible = cost; pFeasible = p; }
                }

                byte act = pFeasible.first;
                // cooperate with a human partner: sync with their stroke when the ideal plan wants both
                if (!(lb && rb) && (pAll.first & (L | R)) == (L | R))
                {
                    bool humanLeftJust = !lb && now - team.raft.LastStroke(true) < 0.15f;
                    bool humanRightJust = !rb && now - team.raft.LastStroke(false) < 0.15f;
                    if (humanLeftJust) act = (byte)(R | (pAll.first & B));
                    if (humanRightJust) act = (byte)(L | (pAll.first & B));
                }

                bool back = (act & B) != 0;
                bool did = false;
                if ((act & L) != 0 && lb) did |= m.ApplyStroke(team, Role.LeftPaddler, 0.9f, back);
                if ((act & R) != 0 && rb) did |= m.ApplyStroke(team, Role.RightPaddler, 0.9f, back);
                nextAct = now + (did ? interval : 0.08f);
            }

            if (team.IsBot(Role.Collector) && now >= nextNet) Collect(m, team, rng);
        }

        float Evaluate(MatchSim m, RaftSim raft, Policy p, float interval)
        {
            var t = m.T;
            probe.CopyFrom(raft);
            float startZ = probe.z, nextStroke = 0f;
            bool first = true;
            float hw = t.riverWidth * 0.5f;
            float cost = 0f;
            for (float tau = 0f; tau < Horizon; tau += StepDt)
            {
                if (tau >= nextStroke)
                {
                    byte a = first ? p.first : p.rest;
                    first = false;
                    bool back = (a & B) != 0;
                    if ((a & L) != 0) probe.Stroke(true, 0.9f, back, m.Time + tau, t);
                    if ((a & R) != 0) probe.Stroke(false, 0.9f, back, m.Time + tau, t);
                    nextStroke += interval;
                }
                probe.Step(StepDt, m.Seed, t);
                float d = Danger(m, probe.x, probe.z, m.Clock + tau, t.raftRadius + 0.45f);
                if (d > 0f) { cost += 1000f * (Horizon - tau + 0.5f) + d * 50f; break; }
            }
            cost += Mathf.Max(0f, Mathf.Abs(probe.x) - (hw - 3f)) * 25f;
            cost += Mathf.Abs(probe.heading) * 3f;
            cost -= (probe.z - startZ) * 0.6f;
            // gradient away from obstacles still ahead of the end position (escapes "brake forever" local minima)
            int a0 = m.LowerBound(probe.z);
            for (int i = a0; i < m.Count && m.E[i].z < probe.z + 16f; i++)
            {
                ref var e = ref m.E[i];
                if (!e.alive || e.kind != EntityKind.Obstacle || e.sub == (byte)ObstacleType.Shark) continue;
                float reach = e.radius + e.halfLen + t.raftRadius + 0.8f;
                float overlap = reach - Mathf.Abs(probe.x - e.x);
                if (overlap > 0f) cost += overlap * 18f * (1.1f - (e.z - probe.z) / 16f);
            }
            // reward floating waste near where we end up (collector range)
            int s = m.LowerBound(probe.z - 5f);
            for (int i = s; i < m.Count && m.E[i].z < probe.z + 6f; i++)
            {
                ref var e = ref m.E[i];
                if (!e.alive) continue;
                float dx = e.x - probe.x, dz = e.z - probe.z;
                float d2 = dx * dx + dz * dz;
                if (e.kind == EntityKind.Waste && e.sub != (byte)WasteType.Toxic && d2 < 30f) cost -= 0.6f;
                else if (e.kind == EntityKind.Shield && d2 < 6f) cost -= 8f;
            }
            return cost;
        }

        /// > 0 if a dangerous obstacle overlaps a circle at (x,z) at the given (future) clock.
        static float Danger(MatchSim m, float x, float z, float clock, float radius)
        {
            int s = m.LowerBound(z - 4.5f);
            for (int i = s; i < m.Count && m.E[i].z < z + 4.5f; i++)
            {
                if (!m.E[i].alive || m.E[i].kind != EntityKind.Obstacle) continue;
                var e = m.E[i];
                if (e.sub == (byte)ObstacleType.Shark) { MatchSim.SharkPose(ref e, clock, m.T); if (e.flags != 2) continue; }
                float d = CourseGenerator.CapsuleDistance(e, x, z);
                if (d < radius) return radius - d;
            }
            return 0f;
        }

        void Collect(MatchSim m, TeamState team, System.Random rng)
        {
            var t = m.T;
            var r = team.raft;
            int best = -1; float bestD = t.netRange * 0.9f;
            int s = m.LowerBound(r.z - 2f);
            for (int i = s; i < m.Count && m.E[i].z < r.z + t.netRange; i++)
            {
                ref var e = ref m.E[i];
                if (!e.alive || e.kind != EntityKind.Waste || e.sub == (byte)WasteType.Toxic) continue;
                float d = Mathf.Sqrt((e.x - r.x) * (e.x - r.x) + (e.z - r.z) * (e.z - r.z));
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best < 0) return;
            var w = m.E[best];
            if (rng.NextDouble() < t.botAccuracy) m.ApplyThrow(team, w.id, 0, 0, 0);
            else m.ApplyThrow(team, NetConst.NoEntity, w.x - r.x + (float)(rng.NextDouble() * 4 - 2), w.z - r.z, 0);
            nextNet = m.Time + t.netCooldown + (float)rng.NextDouble() * 0.7f;
        }
    }
}
