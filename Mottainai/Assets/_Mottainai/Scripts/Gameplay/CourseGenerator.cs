using System;
using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Gameplay
{
    /// Deterministic course from seed. Entities are returned sorted by Z.
    public static class CourseGenerator
    {
        struct Interval { public float a, b; }

        public static SimEntity[] Generate(int seed, TuningData t, out float[] laneCenters)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var list = new List<SimEntity>(600);
            float hw = t.riverWidth * 0.5f;
            float laneHalf = t.freeLaneFactor * t.raftWidth * 0.5f;
            float end = t.courseLength + 120f;
            int chunks = Mathf.CeilToInt(end / t.chunkLength);
            laneCenters = new float[chunks];

            float lane = 0f;
            float nextObs = 30f;
            var obstacles = new List<SimEntity>();
            var chunkStatic = new List<Interval>();

            for (int c = 0; c < chunks; c++)
            {
                float cz0 = c * t.chunkLength, cz1 = cz0 + t.chunkLength;
                lane = Mathf.Clamp(lane + R(-5f, 5f), -hw + laneHalf + 0.5f, hw - laneHalf - 0.5f);
                laneCenters[c] = lane;
                chunkStatic.Clear();

                while (nextObs < cz1)
                {
                    float oz = nextObs;
                    var type = PickObstacle(rng, oz >= t.sharkMinDistance);
                    float r = ObstacleCatalog.Radius[(int)type], hl = ObstacleCatalog.HalfLen[(int)type];
                    float ext = r + hl;
                    float ox = 0f;
                    bool placed = false;
                    for (int attempt = 0; attempt < 12 && !placed; attempt++)
                    {
                        ox = R(-hw + ext * 0.6f, hw - ext * 0.6f);
                        if (type == ObstacleType.Shark) { ox = Mathf.Clamp(ox, -hw + t.sharkAmplitude * 0.5f, hw - t.sharkAmplitude * 0.5f); placed = true; break; }
                        placed = ox + ext < lane - laneHalf || ox - ext > lane + laneHalf;
                    }
                    if (placed)
                    {
                        var e = new SimEntity
                        {
                            kind = EntityKind.Obstacle, sub = (byte)type, alive = true, x = ox, baseX = ox, z = oz,
                            radius = r, halfLen = hl, phase = R(0f, 10f), yaw = R(0f, 360f)
                        };
                        if (type == ObstacleType.Log || type == ObstacleType.Wreck) e.yaw = 0f;
                        obstacles.Add(e);
                        if (type != ObstacleType.Shark) chunkStatic.Add(new Interval { a = ox - ext, b = ox + ext });
                    }
                    float time = oz / Mathf.Max(1f, t.expectedAvgSpeed);
                    float spacing = Mathf.Lerp(t.obstacleSpacingStart, t.obstacleSpacingEnd, Mathf.Clamp01(time / t.obstacleRampTime));
                    nextObs += spacing * R(0.8f, 1.2f);
                }

                if (!HasFreeLane(chunkStatic, -hw, hw, laneHalf * 2f))
                {
                    // validation fallback: drop static obstacles overlapping the planned lane in this chunk
                    obstacles.RemoveAll(o => o.z >= cz0 && o.z < cz1 && o.sub != (byte)ObstacleType.Shark &&
                                             o.x + o.radius + o.halfLen > lane - laneHalf && o.x - o.radius - o.halfLen < lane + laneHalf);
                }
            }
            list.AddRange(obstacles);

            // Waste clusters
            var weights = t.wasteWeights;
            for (float z = 14f; z < end; z += t.wasteClusterSpacing * R(0.75f, 1.25f))
            {
                int n = rng.Next(t.wasteClusterMin, t.wasteClusterMax + 1);
                float cx = R(-hw + 2f, hw - 2f), cz = z;
                if (rng.NextDouble() < t.wasteNearObstacleChance)
                {
                    int best = -1; float bestD = 12f;
                    for (int i = 0; i < obstacles.Count; i++)
                    {
                        float d = Mathf.Abs(obstacles[i].z - z);
                        if (d < bestD && obstacles[i].sub != (byte)ObstacleType.Shark) { bestD = d; best = i; }
                    }
                    if (best >= 0)
                    {
                        var o = obstacles[best];
                        float side = rng.NextDouble() < 0.5 ? -1f : 1f;
                        cx = Mathf.Clamp(o.x + side * (o.radius + o.halfLen + 1.4f), -hw + 1f, hw - 1f);
                        cz = o.z + R(-1.5f, 1.5f);
                    }
                }
                for (int k = 0; k < n; k++)
                {
                    var wt = (WasteType)PickWeighted(rng, weights);
                    float wx = Mathf.Clamp(cx + R(-1.6f, 1.6f), -hw + 0.6f, hw - 0.6f);
                    float wz = cz + R(-1.6f, 1.6f);
                    if (Overlaps(obstacles, wx, wz, 0.6f)) continue;
                    list.Add(new SimEntity
                    {
                        kind = EntityKind.Waste, sub = (byte)wt, alive = true, x = wx, baseX = wx, z = wz,
                        radius = WasteCatalog.Radius[(int)wt], phase = R(0f, 6.28f), yaw = R(0f, 360f)
                    });
                }
            }

            // Shields
            float sz = Mathf.Max(t.shieldMinTime * t.expectedAvgSpeed, 100f);
            while (sz < end)
            {
                int c = Mathf.Clamp((int)(sz / t.chunkLength), 0, chunks - 1);
                float sx = laneCenters[c] + R(-2f, 2f);
                list.Add(new SimEntity { kind = EntityKind.Shield, alive = true, x = sx, baseX = sx, z = sz, radius = 0.8f, phase = R(0f, 6f) });
                sz += t.shieldSpacing * R(0.85f, 1.15f);
            }

            list.Sort((a, b) => a.z.CompareTo(b.z));
            var arr = list.ToArray();
            for (int i = 0; i < arr.Length; i++) arr[i].id = (ushort)i;
            return arr;
        }

        static bool Overlaps(List<SimEntity> obs, float x, float z, float pad)
        {
            for (int i = 0; i < obs.Count; i++)
            {
                var o = obs[i];
                if (Mathf.Abs(o.z - z) > 5f) continue;
                if (CapsuleDistance(o, x, z) < pad) return true;
            }
            return false;
        }

        public static float CapsuleDistance(in SimEntity o, float x, float z)
        {
            float dx = Mathf.Max(0f, Mathf.Abs(x - o.x) - o.halfLen);
            float dz = z - o.z;
            return Mathf.Sqrt(dx * dx + dz * dz) - o.radius;
        }

        static bool HasFreeLane(List<Interval> ivs, float min, float max, float width)
        {
            ivs.Sort((a, b) => a.a.CompareTo(b.a));
            float cursor = min;
            foreach (var iv in ivs)
            {
                if (iv.a - cursor >= width) return true;
                cursor = Mathf.Max(cursor, iv.b);
            }
            return max - cursor >= width;
        }

        static ObstacleType PickObstacle(System.Random rng, bool allowShark)
        {
            var w = ObstacleCatalog.Weights;
            ObstacleType t;
            do t = (ObstacleType)PickWeighted(rng, w); while (!allowShark && t == ObstacleType.Shark);
            return t;
        }

        static int PickWeighted(System.Random rng, float[] w)
        {
            float sum = 0f;
            for (int i = 0; i < w.Length; i++) sum += w[i];
            float r = (float)rng.NextDouble() * sum;
            for (int i = 0; i < w.Length; i++) { r -= w[i]; if (r <= 0f) return i; }
            return w.Length - 1;
        }
    }
}
