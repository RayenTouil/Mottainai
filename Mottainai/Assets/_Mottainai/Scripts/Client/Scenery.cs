using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Client
{
    /// Recycled river-side decor around the raft: reeds + shore rocks, recycling-bin stations,
    /// distant faceted hills and a small flock of birds. Purely visual, deterministic per z-cell.
    public class Scenery : MonoBehaviour
    {
        const int ReedCount = 32; const float ReedSpacing = 5f;
        const int HillCount = 8; const float HillSpacing = 34f;
        const int BinGroups = 4; const float BinSpacing = 55f;

        readonly Transform[] reeds = new Transform[ReedCount];
        readonly Transform[] shoreRocks = new Transform[ReedCount];
        readonly Transform[] hills = new Transform[HillCount];
        readonly Transform[] bins = new Transform[BinGroups];

        class Bird { public Transform t, wingL, wingR; public Vector3 offset; public float phase; }
        readonly List<Bird> birds = new List<Bird>();
        Vector3 flock;
        float flockDir = 1f;

        public static Scenery Create(Transform parent)
        {
            var go = new GameObject("Scenery");
            go.transform.SetParent(parent, false);
            var s = go.AddComponent<Scenery>();
            s.Build();
            return s;
        }

        void Build()
        {
            var reedA = ReedClump(1); var reedB = ReedClump(2);
            var matA = VisualFactory.Lit(new Color(0.33f, 0.6f, 0.24f));
            var matB = VisualFactory.Lit(new Color(0.55f, 0.66f, 0.26f));
            var rockMat = VisualFactory.Lit(new Color(0.56f, 0.54f, 0.5f));
            for (int i = 0; i < ReedCount; i++)
            {
                reeds[i] = VisualFactory.Part(transform, i % 2 == 0 ? reedA : reedB, i % 2 == 0 ? matA : matB, Vector3.zero, Vector3.one, default, "reeds").transform;
                shoreRocks[i] = VisualFactory.Part(transform, ProceduralMeshes.Rock(30 + i % 5), rockMat, Vector3.zero, new Vector3(0.9f, 0.55f, 0.8f), default, "shoreRock").transform;
            }

            var hillMats = new[] { VisualFactory.Lit(new Color(0.4f, 0.58f, 0.46f)), VisualFactory.Lit(new Color(0.36f, 0.5f, 0.52f)), VisualFactory.Lit(new Color(0.46f, 0.6f, 0.4f)) };
            for (int i = 0; i < HillCount; i++)
                hills[i] = VisualFactory.Part(transform, ProceduralMeshes.Rock(40 + i % 6), hillMats[i % 3], Vector3.zero, Vector3.one, default, "hill").transform;

            // recycling stations: one bin per waste stream, bin colors match the game's waste colors
            var binColors = new[] { WasteCatalog.Colors[(int)WasteType.Plastic], WasteCatalog.Colors[(int)WasteType.Paper], WasteCatalog.Colors[(int)WasteType.Glass], WasteCatalog.Colors[(int)WasteType.Organic] };
            var lidMat = VisualFactory.Lit(new Color(0.18f, 0.2f, 0.22f));
            var signMat = VisualFactory.Lit(new Color(0.95f, 0.95f, 0.9f), 0.2f);
            for (int g = 0; g < BinGroups; g++)
            {
                var root = new GameObject("binStation").transform;
                root.SetParent(transform, false);
                for (int b = 0; b < 4; b++)
                {
                    float x = (b - 1.5f) * 0.85f;
                    VisualFactory.Part(root, VisualFactory.Cube, VisualFactory.Lit(binColors[b]), new Vector3(x, 0.45f, 0), new Vector3(0.7f, 0.9f, 0.7f));
                    VisualFactory.Part(root, VisualFactory.Cube, lidMat, new Vector3(x, 0.93f, -0.02f), new Vector3(0.76f, 0.08f, 0.76f));
                    VisualFactory.Part(root, VisualFactory.Cube, signMat, new Vector3(x, 0.55f, 0.36f), new Vector3(0.3f, 0.3f, 0.02f));
                }
                bins[g] = root;
            }

            var birdMat = VisualFactory.Lit(new Color(0.15f, 0.17f, 0.2f));
            for (int i = 0; i < 5; i++)
            {
                var t = new GameObject("bird").transform;
                t.SetParent(transform, false);
                VisualFactory.Part(t, VisualFactory.Sphere, birdMat, Vector3.zero, new Vector3(0.18f, 0.14f, 0.36f));
                var wl = new GameObject("wingL").transform; wl.SetParent(t, false); wl.localPosition = new Vector3(-0.06f, 0f, 0f);
                var wr = new GameObject("wingR").transform; wr.SetParent(t, false); wr.localPosition = new Vector3(0.06f, 0f, 0f);
                VisualFactory.Part(wl, VisualFactory.Cube, birdMat, new Vector3(-0.32f, 0f, 0f), new Vector3(0.62f, 0.02f, 0.22f));
                VisualFactory.Part(wr, VisualFactory.Cube, birdMat, new Vector3(0.32f, 0f, 0f), new Vector3(0.62f, 0.02f, 0.22f));
                int row = (i + 1) / 2;
                float side = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
                birds.Add(new Bird { t = t, wingL = wl, wingR = wr, offset = new Vector3(-row * 1.3f, row * 0.2f, side * row * 1.4f), phase = i * 0.7f });
            }
            flock = new Vector3(-70f, 16f, 60f);
        }

        static Mesh ReedClump(int seed)
        {
            var rng = new System.Random(seed);
            var parts = new List<CombineInstance>();
            for (int k = 0; k < 7; k++)
            {
                float h = 0.8f + (float)rng.NextDouble() * 0.9f;
                var pos = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, -0.05f, ((float)rng.NextDouble() - 0.5f) * 0.9f);
                var rot = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 18f, (float)rng.NextDouble() * 360f, ((float)rng.NextDouble() - 0.5f) * 18f);
                parts.Add(new CombineInstance { mesh = VisualFactory.Cone, transform = Matrix4x4.TRS(pos, rot, new Vector3(0.09f, h, 0.09f)) });
            }
            var m = new Mesh { name = "ReedClump" + seed };
            m.CombineMeshes(parts.ToArray(), true, true);
            m.RecalculateBounds();
            return m;
        }

        static uint Hash(int c)
        {
            uint h = (uint)c * 2654435761u;
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return h;
        }

        static void Show(Transform t, bool on) { if (t.gameObject.activeSelf != on) t.gameObject.SetActive(on); }

        public void Follow(Vector3 raft)
        {
            float hw = GameTuning.Current.riverWidth * 0.5f;

            int rc = Mathf.FloorToInt((raft.z - 20f) / ReedSpacing);
            for (int c = rc; c < rc + ReedCount; c++)
            {
                int idx = ((c % ReedCount) + ReedCount) % ReedCount;
                uint h = Hash(c);
                float side = (h & 1) == 0 ? -1f : 1f;
                float x = side * (hw + 0.15f + ((h >> 3) % 100) / 100f * 0.7f);
                float z = c * ReedSpacing + ((h >> 9) % 100) / 100f * 3f;
                bool reed = ((h >> 15) % 10) < 7;
                Show(reeds[idx], reed);
                if (reed) { reeds[idx].localPosition = new Vector3(x, 0f, z); reeds[idx].localRotation = Quaternion.Euler(0f, (h >> 7) % 360, 0f); }
                bool rock = ((h >> 19) % 10) < 3;
                Show(shoreRocks[idx], rock);
                if (rock) shoreRocks[idx].localPosition = new Vector3(x - side * 0.5f, -0.05f, z + 1.3f);
            }

            int hc = Mathf.FloorToInt((raft.z - 30f) / HillSpacing);
            for (int c = hc; c < hc + HillCount; c++)
            {
                int idx = ((c % HillCount) + HillCount) % HillCount;
                uint h = Hash(c * 7 + 3);
                float side = (c & 1) == 0 ? -1f : 1f;
                var t = hills[idx];
                t.localPosition = new Vector3(side * (46f + (h % 22)), -2.5f, c * HillSpacing + (h >> 8) % 12);
                t.localScale = new Vector3(34f + (h >> 4) % 18, 12f + (h >> 12) % 12, 34f + (h >> 16) % 18);
                t.localRotation = Quaternion.Euler(0f, (h >> 20) % 360, 0f);
            }

            int bc = Mathf.FloorToInt((raft.z - 20f) / BinSpacing);
            for (int c = bc; c < bc + BinGroups; c++)
            {
                int idx = ((c % BinGroups) + BinGroups) % BinGroups;
                float side = (c & 1) == 0 ? -1f : 1f;
                bins[idx].localPosition = new Vector3(side * (hw + 3.4f), 0.95f, c * BinSpacing + 12f);
                bins[idx].localRotation = Quaternion.Euler(0f, side < 0f ? 90f : -90f, 0f);
            }

            // birds: V formation crossing the sky ahead of the raft
            float dt = Time.deltaTime;
            flock.x += flockDir * 6f * dt;
            if (Mathf.Abs(flock.x) > 75f)
            {
                flockDir = -flockDir;
                flock.x = -flockDir * 75f;
                flock.y = 13f + Random.Range(0f, 6f);
                flock.z = Random.Range(45f, 85f);
            }
            float tt = Time.time;
            foreach (var b in birds)
            {
                var off = new Vector3(b.offset.x * flockDir, b.offset.y, b.offset.z);
                b.t.localPosition = new Vector3(flock.x, flock.y, raft.z + flock.z) + off + Vector3.up * Mathf.Sin(tt * 1.3f + b.phase) * 0.3f;
                b.t.localRotation = Quaternion.Euler(0f, flockDir > 0f ? 90f : -90f, 0f);
                float flap = Mathf.Sin(tt * 9f + b.phase) * 38f;
                b.wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                b.wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
            }
        }
    }
}
