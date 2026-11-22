using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Client
{
    /// Recycled urban-park riverbanks around the raft (purely visual, deterministic per z-cell):
    /// warm stone edges, reeds, trees (imported elm/pine/palm + round cartoon variants, never the same twice in a row),
    /// path-side props (recycling bins, benches, lamps, banners, signs, fences), stone arch bridges, distant hills,
    /// seagulls and a lighthouse island + sailboat on the sea horizon.
    public class Scenery : MonoBehaviour
    {
        const int ReedCount = 32; const float ReedSpacing = 5f;
        const int HillCount = 8; const float HillSpacing = 34f;
        const int TreeCount = 46; const float TreeSpacing = 6f;
        const int PropCount = 34; const float PropSpacing = 7f;
        const float EdgePeriod = 48f, BankTop = 1.0f;
        static readonly float[] BridgeZ = { 190f, 440f };

        readonly Transform[] reeds = new Transform[ReedCount];
        readonly Transform[] shoreRocks = new Transform[ReedCount];
        readonly Transform[] hills = new Transform[HillCount];
        readonly Transform[][] trees = new Transform[TreeCount][];
        readonly Transform[] treeRoots = new Transform[TreeCount];
        readonly Transform[][] props = new Transform[PropCount][];
        readonly Transform[] propRoots = new Transform[PropCount];
        readonly List<Transform> edges = new List<Transform>();
        readonly Transform[] bridges = new Transform[2];
        Transform island, boat;

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
            float hw = GameTuning.Current.riverWidth * 0.5f;
            var reedA = ReedClump(1); var reedB = ReedClump(2);
            var matA = VisualFactory.Lit(new Color(0.36f, 0.66f, 0.27f));
            var matB = VisualFactory.Lit(new Color(0.56f, 0.72f, 0.3f));
            var rockMat = VisualFactory.Lit(new Color(0.7f, 0.64f, 0.56f));
            for (int i = 0; i < ReedCount; i++)
            {
                reeds[i] = VisualFactory.Part(transform, i % 2 == 0 ? reedA : reedB, i % 2 == 0 ? matA : matB, Vector3.zero, Vector3.one, default, "reeds").transform;
                shoreRocks[i] = VisualFactory.Part(transform, ProceduralMeshes.Rock(30 + i % 5), rockMat, Vector3.zero, new Vector3(0.9f, 0.55f, 0.8f), default, "shoreRock").transform;
            }

            var hillMats = new[] { VisualFactory.Lit(new Color(0.42f, 0.66f, 0.42f)), VisualFactory.Lit(new Color(0.38f, 0.58f, 0.5f)), VisualFactory.Lit(new Color(0.5f, 0.7f, 0.4f)) };
            for (int i = 0; i < HillCount; i++)
                hills[i] = VisualFactory.Part(transform, ProceduralMeshes.Blob(40 + i % 6), hillMats[i % 3], Vector3.zero, Vector3.one, default, "hill").transform;

            // warm stone edge along both waterlines: 3 combined meshes (one per stone tint) per side, 3 copies leapfrogging
            var stoneMats = new[] { VisualFactory.Lit(new Color(0.8f, 0.71f, 0.6f)), VisualFactory.Lit(new Color(0.7f, 0.62f, 0.53f)), VisualFactory.Lit(new Color(0.62f, 0.58f, 0.53f)) };
            for (int side = -1; side <= 1; side += 2)
                for (int copy = 0; copy < 3; copy++)
                    for (int m = 0; m < 3; m++)
                        edges.Add(VisualFactory.Part(transform, EdgeMesh(side, m), stoneMats[m], Vector3.zero, Vector3.one, default, "stoneEdge").transform);

            // trees: every pooled slot holds all variants (0 elm, 1-3 round cartoon, 4 pine, 5 palm near the sea)
            for (int i = 0; i < TreeCount; i++)
            {
                var root = new GameObject("tree").transform;
                root.SetParent(transform, false);
                var v = new Transform[6];
                v[0] = VisualFactory.Model("Scenery/elm_tree", root, 7.5f, Vector3.zero, Vector3.zero, "elm");
                if (v[0] != null) Ground(v[0]); else v[0] = ParkKit.RoundTree(1);
                v[1] = ParkKit.RoundTree(0);
                v[2] = ParkKit.RoundTree(1);
                v[3] = ParkKit.RoundTree(2);
                v[4] = VisualFactory.Model("Scenery/pine_tree", root, 6.5f, Vector3.zero, Vector3.zero, "pine");
                if (v[4] != null) Ground(v[4]); else v[4] = PineFallback(i);
                v[5] = VisualFactory.Model("Scenery/palm_tree", root, 7f, Vector3.zero, Vector3.zero, "palm");
                if (v[5] != null) Ground(v[5]); else v[5] = ParkKit.RoundTree(2);
                for (int k = 0; k < v.Length; k++) { v[k].SetParent(root, false); v[k].gameObject.SetActive(false); }
                trees[i] = v; treeRoots[i] = root;
            }

            // path-side props
            for (int i = 0; i < PropCount; i++)
            {
                var root = new GameObject("prop").transform;
                root.SetParent(transform, false);
                var v = new[] { ParkKit.BinPair(), ParkKit.Bench(), ParkKit.Lamp(), ParkKit.BannerPole(), ParkKit.Sign(), ParkKit.Fence(3.2f) };
                foreach (var p in v) { p.SetParent(root, false); p.gameObject.SetActive(false); }
                props[i] = v; propRoots[i] = root;
            }

            for (int b = 0; b < bridges.Length; b++) { bridges[b] = ParkKit.Bridge(hw); bridges[b].SetParent(transform, false); }
            island = ParkKit.LighthouseIsland(); island.SetParent(transform, false);
            boat = ParkKit.Sailboat(); boat.SetParent(transform, false);

            // seagulls: white body, grey-tipped wings, yellow beak
            var gull = VisualFactory.Lit(new Color(0.97f, 0.97f, 0.96f));
            var tip = VisualFactory.Lit(new Color(0.55f, 0.6f, 0.65f));
            for (int i = 0; i < 6; i++)
            {
                var t = new GameObject("gull").transform;
                t.SetParent(transform, false);
                VisualFactory.Part(t, VisualFactory.Capsule, gull, Vector3.zero, new Vector3(0.22f, 0.3f, 0.22f), new Vector3(90f, 0, 0));
                VisualFactory.Part(t, VisualFactory.Sphere, VisualFactory.Lit(new Color(1f, 0.75f, 0.2f)), new Vector3(0, 0.02f, 0.33f), new Vector3(0.07f, 0.05f, 0.12f));
                var wl = new GameObject("wingL").transform; wl.SetParent(t, false); wl.localPosition = new Vector3(-0.08f, 0.02f, 0f);
                var wr = new GameObject("wingR").transform; wr.SetParent(t, false); wr.localPosition = new Vector3(0.08f, 0.02f, 0f);
                VisualFactory.Part(wl, VisualFactory.Cube, gull, new Vector3(-0.38f, 0f, 0f), new Vector3(0.72f, 0.03f, 0.26f));
                VisualFactory.Part(wl, VisualFactory.Cube, tip, new Vector3(-0.8f, 0f, -0.02f), new Vector3(0.18f, 0.03f, 0.2f));
                VisualFactory.Part(wr, VisualFactory.Cube, gull, new Vector3(0.38f, 0f, 0f), new Vector3(0.72f, 0.03f, 0.26f));
                VisualFactory.Part(wr, VisualFactory.Cube, tip, new Vector3(0.8f, 0f, -0.02f), new Vector3(0.18f, 0.03f, 0.2f));
                t.localScale = Vector3.one * 1.5f;
                int row = (i + 1) / 2;
                float sideOff = i == 0 ? 0f : (i % 2 == 0 ? 1f : -1f);
                birds.Add(new Bird { t = t, wingL = wl, wingR = wr, offset = new Vector3(-row * 1.8f, row * 0.3f, sideOff * row * 2f), phase = i * 0.7f });
            }
            flock = new Vector3(-70f, 14f, 55f);
        }

        /// Imported models are centered on their bounds: lift so the trunk base sits on the ground.
        static void Ground(Transform pivot)
        {
            var rs = pivot.GetComponentsInChildren<Renderer>();
            if (rs.Length == 0) return;
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            pivot.localPosition += Vector3.up * (pivot.position.y - b.min.y);
        }

        static Transform PineFallback(int i)
        {
            var t = new GameObject("pine").transform;
            VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(ParkKit.Bark), new Vector3(0, 0.8f, 0), new Vector3(0.3f, 0.8f, 0.3f));
            VisualFactory.Part(t, ProceduralMeshes.Pine(i % 4), VisualFactory.Lit(new Color(0.2f, 0.5f, 0.3f)), Vector3.zero, Vector3.one, new Vector3(0, i * 37, 0));
            return t;
        }

        /// Irregular stones along one waterline over EdgePeriod metres, for stone tint m (0..2).
        static Mesh EdgeMesh(int side, int m)
        {
            float hw = GameTuning.Current.riverWidth * 0.5f;
            var rng = new System.Random(side * 31 + m * 7 + 5);
            var parts = new List<CombineInstance>();
            int i = 0;
            for (float z = 0f; z < EdgePeriod; z += 0.85f, i++)
            {
                if (i % 3 != m) continue;
                float s = 0.55f + (float)rng.NextDouble() * 0.55f;
                var pos = new Vector3(side * (hw + 0.35f + (float)rng.NextDouble() * 0.6f), 0.15f + (float)rng.NextDouble() * 0.35f, z + (float)rng.NextDouble() * 0.4f);
                var rot = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                parts.Add(new CombineInstance { mesh = ProceduralMeshes.Rock(50 + (i % 6)), transform = Matrix4x4.TRS(pos, rot, new Vector3(s, s * 0.75f, s)) });
            }
            var mesh = new Mesh { name = $"Edge{side}_{m}", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            mesh.RecalculateBounds();
            return mesh;
        }

        static Mesh ReedClump(int seed)
        {
            var rng = new System.Random(seed);
            var parts = new List<CombineInstance>();
            for (int k = 0; k < 9; k++)
            {
                float h = 0.8f + (float)rng.NextDouble() * 1.0f;
                var pos = new Vector3(((float)rng.NextDouble() - 0.5f) * 0.9f, -0.05f, ((float)rng.NextDouble() - 0.5f) * 0.9f);
                var rot = Quaternion.Euler(((float)rng.NextDouble() - 0.5f) * 22f, (float)rng.NextDouble() * 360f, ((float)rng.NextDouble() - 0.5f) * 22f);
                parts.Add(new CombineInstance { mesh = VisualFactory.Cone, transform = Matrix4x4.TRS(pos, rot, new Vector3(0.1f, h, 0.1f)) });
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

        static int TreeVariant(int c, float z)
        {
            float L = GameTuning.Current.courseLength;
            if (z > L - 150f && (Hash(c * 5 + 1) % 3) == 0) return 5; // palms where the river meets the sea
            int v = (int)(Hash(c * 13 + 7) % 5);
            int prev = (int)(Hash((c - 2) * 13 + 7) % 5); // same side, previous tree
            return v == prev ? (v + 1) % 5 : v;
        }

        public void Follow(Vector3 raft)
        {
            float hw = GameTuning.Current.riverWidth * 0.5f;
            float seaZ = GameTuning.Current.courseLength - 45f;

            int rc = Mathf.FloorToInt((raft.z - 20f) / ReedSpacing);
            for (int c = rc; c < rc + ReedCount; c++)
            {
                int idx = ((c % ReedCount) + ReedCount) % ReedCount;
                uint h = Hash(c);
                float side = (h & 1) == 0 ? -1f : 1f;
                float x = side * (hw + 0.05f + ((h >> 3) % 100) / 100f * 0.5f);
                float z = c * ReedSpacing + ((h >> 9) % 100) / 100f * 3f;
                bool land = z < seaZ;
                bool reed = land && ((h >> 15) % 10) < 8;
                Show(reeds[idx], reed);
                if (reed) { reeds[idx].localPosition = new Vector3(x, 0f, z); reeds[idx].localRotation = Quaternion.Euler(0f, (h >> 7) % 360, 0f); }
                bool rock = land && ((h >> 19) % 10) < 3;
                Show(shoreRocks[idx], rock);
                if (rock) shoreRocks[idx].localPosition = new Vector3(x - side * 0.6f, -0.05f, z + 1.3f);
            }

            // stone edges: 3 copies per side leapfrog every EdgePeriod, hidden where the river opens to the sea
            int ec = Mathf.FloorToInt((raft.z - 20f) / EdgePeriod);
            for (int k = 0; k < edges.Count; k++)
            {
                int copy = (k / 3) % 3;
                int cell = ec + ((copy - ec) % 3 + 3) % 3;
                float z0 = cell * EdgePeriod;
                edges[k].localPosition = new Vector3(0f, 0f, z0);
                Show(edges[k], z0 < seaZ);
            }

            int hc = Mathf.FloorToInt((raft.z - 30f) / HillSpacing);
            for (int c = hc; c < hc + HillCount; c++)
            {
                int idx = ((c % HillCount) + HillCount) % HillCount;
                uint h = Hash(c * 7 + 3);
                float side = (c & 1) == 0 ? -1f : 1f;
                var t = hills[idx];
                t.localPosition = new Vector3(side * (50f + (h % 22)), -3f, c * HillSpacing + (h >> 8) % 12);
                t.localScale = new Vector3(30f + (h >> 4) % 18, 10f + (h >> 12) % 10, 30f + (h >> 16) % 18);
                t.localRotation = Quaternion.Euler(0f, (h >> 20) % 360, 0f);
                Show(t, c * HillSpacing < seaZ + 40f);
            }

            int tc = Mathf.FloorToInt((raft.z - 20f) / TreeSpacing);
            for (int c = tc; c < tc + TreeCount; c++)
            {
                int idx = ((c % TreeCount) + TreeCount) % TreeCount;
                uint h = Hash(c * 3 + 11);
                float side = (c & 1) == 0 ? -1f : 1f;
                float z = c * TreeSpacing + ((h >> 20) % 100) / 100f * 3f;
                bool near = (h % 10) < 2;
                float off = near ? 2.0f + ((h >> 4) % 100) / 100f * 0.4f : 8.5f + ((h >> 4) % 100) / 100f * 11f;
                var root = treeRoots[idx];
                bool land = z < seaZ + (near ? 0f : 25f);
                Show(root, land);
                if (!land) continue;
                int v = TreeVariant(c, z);
                var vs = trees[idx];
                for (int k = 0; k < vs.Length; k++) Show(vs[k], k == v);
                root.localPosition = new Vector3(side * (hw + off), BankTop, z);
                root.localRotation = Quaternion.Euler(0f, (h >> 9) % 360, 0f);
                root.localScale = Vector3.one * (0.85f + ((h >> 14) % 100) / 100f * 0.4f);
            }

            int pc = Mathf.FloorToInt((raft.z - 20f) / PropSpacing);
            for (int c = pc; c < pc + PropCount; c++)
            {
                int idx = ((c % PropCount) + PropCount) % PropCount;
                uint h = Hash(c * 17 + 29);
                float side = (h & 1) == 0 ? -1f : 1f;
                float z = c * PropSpacing + ((h >> 5) % 100) / 100f * 2f;
                int kind = (int)((h >> 8) % 9); // 0 bins, 1 bench, 2 lamp, 3 banner, 4 sign, 5 fence; 6-8 = lamp/fence/bins again
                if (kind == 6) kind = 2; else if (kind == 7) kind = 5; else if (kind == 8) kind = 0;
                bool on = z < seaZ && z > 6f;
                var root = propRoots[idx];
                Show(root, on);
                if (!on) continue;
                var vs = props[idx];
                for (int k = 0; k < vs.Length; k++) Show(vs[k], k == kind);
                float x = side * (hw + (kind == 5 ? 1.55f : 2.4f));
                root.localPosition = new Vector3(x, BankTop, z);
                // props face the river; fences run along the bank
                root.localRotation = kind == 5 ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.Euler(0f, side < 0f ? 90f : -90f, 0f);
            }

            for (int b = 0; b < bridges.Length; b++)
            {
                float bz = BridgeZ[b];
                bool on = bz < seaZ && Mathf.Abs(bz - raft.z) < 180f;
                Show(bridges[b], on);
                if (on) bridges[b].localPosition = new Vector3(0f, 0f, bz);
            }

            // lighthouse island + sailboat ride the horizon, then hold still near the end so the raft approaches them
            float L = GameTuning.Current.courseLength;
            float iz = Mathf.Max(raft.z + 150f, L + 75f);
            island.localPosition = new Vector3(34f, 0f, iz);
            boat.localPosition = new Vector3(-24f + Mathf.Sin(Time.time * 0.05f) * 6f, Mathf.Sin(Time.time * 0.9f) * 0.15f, iz - 14f);
            boat.localRotation = Quaternion.Euler(Mathf.Sin(Time.time * 0.7f) * 3f, 75f, Mathf.Sin(Time.time * 0.9f) * 4f);

            // seagulls: loose V crossing the sky ahead of the raft
            float dt = Time.deltaTime;
            flock.x += flockDir * 5f * dt;
            if (Mathf.Abs(flock.x) > 75f)
            {
                flockDir = -flockDir;
                flock.x = -flockDir * 75f;
                flock.y = 11f + Random.Range(0f, 6f);
                flock.z = Random.Range(40f, 80f);
            }
            float tt = Time.time;
            foreach (var b in birds)
            {
                var off = new Vector3(b.offset.x * flockDir, b.offset.y, b.offset.z);
                b.t.localPosition = new Vector3(flock.x, flock.y, raft.z + flock.z) + off + Vector3.up * Mathf.Sin(tt * 1.1f + b.phase) * 0.4f;
                b.t.localRotation = Quaternion.Euler(0f, flockDir > 0f ? 90f : -90f, Mathf.Sin(tt * 0.8f + b.phase) * 8f);
                float flap = Mathf.Sin(tt * 6.5f + b.phase) * 30f + 12f;
                b.wingL.localRotation = Quaternion.Euler(0f, 0f, -flap);
                b.wingR.localRotation = Quaternion.Euler(0f, 0f, flap);
            }
        }
    }
}
