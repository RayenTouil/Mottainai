using UnityEngine;

namespace Mottainai.Gameplay
{
    /// Simple AI filling any role not held by a human.
    public class BotBrain
    {
        float nextLeft, nextRight, nextNet, retargetAt;
        public float targetX;

        public void Step(MatchSim m, TeamState team, System.Random rng)
        {
            var t = m.T;
            var r = team.raft;
            float now = m.Time;
            bool lb = team.IsBot(Role.LeftPaddler), rb = team.IsBot(Role.RightPaddler), cb = team.IsBot(Role.Collector);

            if (lb || rb)
            {
                if (now >= retargetAt) { targetX = ChooseLane(m, r); retargetAt = now + 0.25f; }
                float desired = Mathf.Clamp(Mathf.Atan2(targetX - r.x, 10f), -0.6f, 0.6f);
                float err = desired - (r.heading + r.yawRate * 0.35f);
                bool wantThrust = r.ForwardSpeed < 3.2f;
                const float band = 0.07f;
                float interval = t.botStrokeInterval;

                if (lb && now >= nextLeft)
                {
                    bool partnerJustStroked = !rb && now - r.LastStroke(false) < 0.12f;
                    if (err > band) { m.ApplyStroke(team, Role.LeftPaddler, Mathf.Clamp(0.5f + err, 0.4f, 1f), false); nextLeft = now + interval; }
                    else if (Mathf.Abs(err) <= band * 2f && (partnerJustStroked || (rb && wantThrust && now >= nextRight)))
                    {
                        m.ApplyStroke(team, Role.LeftPaddler, 0.85f, false);
                        nextLeft = now + interval * 1.3f;
                        if (rb) { m.ApplyStroke(team, Role.RightPaddler, 0.85f, false); nextRight = now + interval * 1.3f; }
                    }
                }
                if (rb && now >= nextRight)
                {
                    bool partnerJustStroked = !lb && now - r.LastStroke(true) < 0.12f;
                    if (err < -band) { m.ApplyStroke(team, Role.RightPaddler, Mathf.Clamp(0.5f - err, 0.4f, 1f), false); nextRight = now + interval; }
                    else if (Mathf.Abs(err) <= band * 2f && partnerJustStroked)
                    {
                        m.ApplyStroke(team, Role.RightPaddler, 0.85f, false);
                        nextRight = now + interval * 1.3f;
                    }
                }
            }

            if (cb && now >= nextNet)
            {
                int best = -1; float bestD = t.netRange * 0.9f;
                int s = m.LowerBound(r.z - 2f);
                for (int i = s; i < m.Count && m.E[i].z < r.z + t.netRange; i++)
                {
                    ref var e = ref m.E[i];
                    if (!e.alive || e.kind != EntityKind.Waste || e.sub == (byte)WasteType.Toxic) continue;
                    float d = Mathf.Sqrt((e.x - r.x) * (e.x - r.x) + (e.z - r.z) * (e.z - r.z));
                    if (d < bestD) { bestD = d; best = i; }
                }
                if (best >= 0)
                {
                    var e = m.E[best];
                    if (rng.NextDouble() < t.botAccuracy) m.ApplyThrow(team, e.id, 0, 0, 0);
                    else m.ApplyThrow(team, NetConst.NoEntity, e.x - r.x + (float)(rng.NextDouble() * 4 - 2), e.z - r.z, 0);
                    nextNet = now + t.netCooldown + (float)rng.NextDouble() * 0.7f;
                }
            }
        }

        static float ChooseLane(MatchSim m, RaftSim r)
        {
            var t = m.T;
            float hw = t.riverWidth * 0.5f - t.raftRadius - 0.3f;
            float bestX = r.x, bestCost = float.MaxValue;
            int s = m.LowerBound(r.z + 0.5f);
            for (float x = -hw; x <= hw; x += 0.75f)
            {
                float cost = Mathf.Abs(x - r.x) * 0.12f;
                for (int i = s; i < m.Count && m.E[i].z < r.z + 30f; i++)
                {
                    ref var e = ref m.E[i];
                    if (!e.alive) continue;
                    float ahead = e.z - r.z;
                    if (e.kind == EntityKind.Obstacle)
                    {
                        bool shark = e.sub == (byte)ObstacleType.Shark;
                        float reach = e.radius + e.halfLen + t.raftRadius + (shark ? 2.5f : 0.9f);
                        float ex = shark ? e.x : e.x;
                        if (Mathf.Abs(x - ex) < reach) cost += 120f * (1.2f - ahead / 30f);
                    }
                    else if (e.kind == EntityKind.Waste && ahead > 2f && ahead < 16f && Mathf.Abs(x - e.x) < 3f && e.sub != (byte)WasteType.Toxic)
                        cost -= 1.5f;
                    else if (e.kind == EntityKind.Shield && ahead < 20f && Mathf.Abs(x - e.x) < 2f)
                        cost -= 6f;
                }
                if (cost < bestCost) { bestCost = cost; bestX = x; }
            }
            return bestX;
        }
    }
}
