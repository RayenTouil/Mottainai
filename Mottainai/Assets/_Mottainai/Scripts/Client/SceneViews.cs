using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Client
{
    public class RaftView : MonoBehaviour
    {
        Transform leftPaddle, rightPaddle, collectorArm, highlight;
        Transform[] crew = new Transform[4];
        GameObject bubble;
        MaterialPropertyBlock bubbleProps;
        MeshRenderer bubbleRenderer;
        float leftAnim = -1f, rightAnim = -1f, armAnim = -1f;
        bool leftBack, rightBack;
        float bob;

        public static RaftView Create(Transform parent, ContentService content)
        {
            var go = new GameObject("Raft");
            go.transform.SetParent(parent, false);
            var v = go.AddComponent<RaftView>();
            v.Build(content);
            return v;
        }

        void Build(ContentService content)
        {
            var t = transform;
            // v3 §6: yellow inflatable oval ring (12 x 24 segs), dark mid band, dark floor, orange life ring on the bow; matte
            VisualFactory.Part(t, ProceduralMeshes.Torus(0.85f, 0.35f, 1.35f, 24, 12), VisualFactory.Lit(ArtV3.RaftBody), new Vector3(0, 0.26f, 0), Vector3.one, default, "hull");
            VisualFactory.Part(t, ProceduralMeshes.Torus(1.18f, 0.045f, 1.252f, 24, 6), VisualFactory.Lit(ArtV3.RaftBand), new Vector3(0, 0.26f, 0), Vector3.one, default, "band");
            VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(ArtV3.RaftFloor), new Vector3(0, 0.08f, 0), new Vector3(1.75f, 0.04f, 2.35f), default, "floor");
            var lifeRing = new GameObject("lifeRing").transform;
            lifeRing.SetParent(t, false);
            lifeRing.localPosition = new Vector3(0, 0.58f, 1.42f);
            lifeRing.localRotation = Quaternion.Euler(-60f, 0f, 0f);
            VisualFactory.Part(lifeRing, ProceduralMeshes.Torus(0.2f, 0.07f, 1f, 16, 6), VisualFactory.Lit(ArtV3.HazardOrange), Vector3.zero, Vector3.one, default, "ring");
            for (int s = 0; s < 4; s++)
            {
                float a = s * 90f + 45f;
                VisualFactory.Part(lifeRing, VisualFactory.Cube, VisualFactory.Lit(Color.white), Quaternion.Euler(0, a, 0) * new Vector3(0.2f, 0f, 0f), new Vector3(0.05f, 0.15f, 0.15f), new Vector3(0, a, 0), "stripe");
            }
            // blob shadow 2.8 x 1.8 m (#0B3A4A @ 35%), 0.01 m above water, follows the raft bob
            VisualFactory.Part(t, VisualFactory.Quad, ArtV3.BlobMaterial(), new Vector3(0, 0.13f, 0), new Vector3(2.6f, 3.2f, 1f), new Vector3(90f, 0f, 0f), "blobShadow");

            crew[1] = Crew(t, new Vector3(-0.5f, 0.12f, 0.15f), ArtV3.CrewBlue, ArtV3.CapBlue, false, out leftPaddle, -1);
            crew[2] = Crew(t, new Vector3(0.5f, 0.12f, 0.15f), ArtV3.CrewYellow, ArtV3.CapCoral, true, out rightPaddle, 1);
            crew[3] = Crew(t, new Vector3(0f, 0.12f, -0.75f), ArtV3.CrewGreen, ArtV3.Hex("#2E8A2E"), false, out collectorArm, 0);
            for (int i = 1; i <= 3; i++) crewBase[i] = crew[i].localPosition;

            highlight = VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(new Color(1f, 0.85f, 0.2f), 1f), Vector3.zero, new Vector3(0.5f, 0.02f, 0.5f), default, "you").transform;
            highlight.gameObject.SetActive(false);

            magnetRing = new GameObject("magnetRing").transform;
            magnetRing.SetParent(t, false);
            var dashMat = VisualFactory.Lit(new Color32(0x00, 0xC2, 0xA8, 255), 1.2f);
            for (int d = 0; d < 14; d++)
            {
                float a = d * Mathf.PI * 2f / 14f;
                VisualFactory.Part(magnetRing, VisualFactory.Cube, dashMat, new Vector3(Mathf.Sin(a) * 1.75f, 0f, Mathf.Cos(a) * 1.75f), new Vector3(0.42f, 0.06f, 0.09f), new Vector3(0f, a * Mathf.Rad2Deg + 90f, 0f), "dash");
            }
            magnetRing.gameObject.SetActive(false);
            var jetMat = VisualFactory.Lit(new Color(1f, 0.55f, 0.1f), 1.6f);
            for (int j = 0; j < 2; j++)
            {
                jets[j] = VisualFactory.Part(t, VisualFactory.Cone, jetMat, new Vector3(j == 0 ? -0.55f : 0.55f, 0.3f, -1.55f), new Vector3(0.25f, 0.7f, 0.25f), new Vector3(-90f, 0f, 0f), "turboJet").transform;
                jets[j].gameObject.SetActive(false);
            }
            bubble = VisualFactory.Part(t, VisualFactory.Sphere, VisualFactory.Transparent(new Color(0.5f, 0.91f, 1f, 0.08f), 1.1f), new Vector3(0, 0.55f, 0), new Vector3(2.4f, 1.9f, 3.0f), default, "shield");
            bubbleRenderer = bubble.GetComponent<MeshRenderer>();
            bubbleProps = new MaterialPropertyBlock();
            bubble.SetActive(false);
        }

        Transform magnetRing;
        readonly Transform[] jets = new Transform[2];
        bool magnetOn, turboOn;

        public void SetPowers(bool magnet, bool turbo)
        {
            if (magnet != magnetOn) { magnetOn = magnet; magnetRing.gameObject.SetActive(magnet); }
            if (turbo != turboOn) { turboOn = turbo; foreach (var j in jets) j.gameObject.SetActive(turbo); }
        }

        MeshRenderer hullRenderer;
        bool skinApplied;

        /// Raft skin from the downloaded Content Pack.
        public void ApplyContent(ContentService content)
        {
            if (skinApplied || content == null || hullRenderer == null) return;
            var skin = ContentService.LoadRaw(content.RaftSkin, false);
            if (skin == null) return;
            var m = new Material(hullRenderer.sharedMaterial);
            m.SetTexture("_BaseMap", skin);
            m.SetTextureScale("_BaseMap", new Vector2(1f, 1f));
            m.SetColor("_BaseColor", Color.white);
            hullRenderer.sharedMaterial = m;
            skinApplied = true;
        }

        /// v3 original toy "bean": one-piece capsule body+head 0.9 m, stubby arms, dot eyes with a highlight, cap or helmet. No mouth/nose.
        Transform Crew(Transform parent, Vector3 pos, Color c, Color cap, bool helmet, out Transform tool, int side)
        {
            var root = new GameObject("crew").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            var body = VisualFactory.Lit(c);
            VisualFactory.Part(root, VisualFactory.Capsule, body, new Vector3(0, 0.45f, 0), new Vector3(0.46f, 0.45f, 0.46f), default, "bean");
            VisualFactory.Part(root, VisualFactory.Capsule, body, new Vector3(-0.26f, 0.42f, 0.04f), new Vector3(0.13f, 0.14f, 0.13f), new Vector3(0, 0, -35f), "armL");
            VisualFactory.Part(root, VisualFactory.Capsule, body, new Vector3(0.26f, 0.42f, 0.04f), new Vector3(0.13f, 0.14f, 0.13f), new Vector3(0, 0, 35f), "armR");
            var eye = VisualFactory.Lit(ArtV3.Eyes);
            var glint = VisualFactory.Lit(Color.white, 0.6f);
            for (int e = -1; e <= 1; e += 2)
            {
                VisualFactory.Part(root, VisualFactory.Sphere, eye, new Vector3(e * 0.085f, 0.72f, 0.205f), new Vector3(0.07f, 0.09f, 0.04f));
                VisualFactory.Part(root, VisualFactory.Sphere, glint, new Vector3(e * 0.085f + 0.015f, 0.74f, 0.225f), Vector3.one * 0.022f);
            }
            var capMat = VisualFactory.Lit(cap);
            if (helmet)
                VisualFactory.Part(root, VisualFactory.Sphere, capMat, new Vector3(0, 0.83f, -0.01f), new Vector3(0.5f, 0.3f, 0.5f), default, "helmet");
            else
            {
                VisualFactory.Part(root, VisualFactory.Sphere, capMat, new Vector3(0, 0.84f, -0.02f), new Vector3(0.48f, 0.26f, 0.48f), default, "cap");
                VisualFactory.Part(root, VisualFactory.Cylinder, capMat, new Vector3(0, 0.82f, 0.22f), new Vector3(0.3f, 0.012f, 0.22f), default, "brim");
            }
            tool = new GameObject("tool").transform;
            tool.SetParent(root, false);
            tool.localPosition = new Vector3(side * 0.25f, 0.45f, 0);
            if (side != 0)
            {
                // CC0 paddle (blade down, see ModelImportRules) over the same span as the primitive shaft + blade
                if (VisualFactory.Model("Raft/paddle", tool, 1.34f, new Vector3(side * 0.4f, -0.3f, 0), new Vector3(0, 0, side * 40f), "paddle") == null)
                {
                    VisualFactory.Part(tool, VisualFactory.Cylinder, VisualFactory.Lit(ArtV3.Wood), new Vector3(side * 0.35f, -0.25f, 0), new Vector3(0.06f, 0.6f, 0.06f), new Vector3(0, 0, side * 40f));
                    VisualFactory.Part(tool, VisualFactory.Cube, VisualFactory.Lit(ArtV3.Wood), new Vector3(side * 0.72f, -0.68f, 0), new Vector3(0.05f, 0.35f, 0.22f), new Vector3(0, 0, side * 40f));
                }
            }
            else
            {
                VisualFactory.Part(tool, VisualFactory.Cylinder, VisualFactory.Lit(ArtV3.Wood), new Vector3(0, 0.2f, 0.25f), new Vector3(0.05f, 0.45f, 0.05f), new Vector3(60, 0, 0));
                VisualFactory.Part(tool, ProceduralMeshes.Torus(0.22f, 0.025f, 1f, 16, 5), VisualFactory.Lit(ArtV3.Hex("#DDE3E8")), new Vector3(0, 0.45f, 0.62f), Vector3.one, new Vector3(-30, 0, 0), "rim");
                VisualFactory.Part(tool, VisualFactory.Sphere, VisualFactory.Transparent(new Color(1f, 1f, 1f, 0.5f)), new Vector3(0, 0.4f, 0.65f), new Vector3(0.42f, 0.18f, 0.42f), new Vector3(-30, 0, 0), "mesh");
            }
            return root;
        }

        public void SetLocalRole(Role r)
        {
            int i = (int)r;
            if (i >= 1 && i <= 3)
            {
                highlight.gameObject.SetActive(true);
                highlight.SetParent(crew[i], false);
                highlight.localPosition = new Vector3(0, 1.22f, 0);
            }
            else highlight.gameObject.SetActive(false);
        }

        public void Stroke(Role side, bool back)
        {
            if (side == Role.LeftPaddler) { leftAnim = 0f; leftBack = back; }
            else if (side == Role.RightPaddler) { rightAnim = 0f; rightBack = back; }
        }

        public void Throw() => armAnim = 0f;

        public Vector3 PaddleTip(bool left) => transform.TransformPoint(new Vector3(left ? -1.5f : 1.5f, 0f, -0.35f));
        public Vector3 CollectorHand => transform.TransformPoint(new Vector3(0, 1.2f, 1.1f));

        public void SetPose(Vector3 pos, float heading)
        {
            bob += Time.deltaTime;
            transform.localPosition = pos + Vector3.up * (0.04f * Mathf.Sin(bob * 2.1f));
            transform.localRotation = Quaternion.Euler(1.5f * Mathf.Sin(bob * 1.7f), heading * Mathf.Rad2Deg, 2f * Mathf.Sin(bob * 1.3f));
        }

        public void SetShield(float left, float blinkTime)
        {
            bool on = left > 0.01f;
            if (on && left < blinkTime) on = Mathf.Repeat(Time.time * 6f, 1f) > 0.4f;
            if (bubble.activeSelf != on) bubble.SetActive(on);
            if (on)
            {
                bubbleProps.SetColor("_BaseColor", new Color(0.5f, 0.91f, 1f, 0.08f));
                bubbleRenderer.SetPropertyBlock(bubbleProps);
            }
        }

        static float StrokeCurve(float t) => t < 0.15f ? t / 0.15f : Mathf.Max(0f, 1f - (t - 0.15f) / 0.3f);

        readonly Vector3[] crewBase = new Vector3[4];
        float idleT, cheerT;

        /// Whole crew jumps for joy (end of match).
        public void Cheer() => cheerT = 2.6f;

        void Update()
        {
            float dt = Time.deltaTime;
            idleT += dt;
            cheerT = Mathf.Max(0f, cheerT - dt);
            for (int i = 1; i <= 3; i++)
            {
                var c = crew[i];
                if (c == null) continue;
                float ph = i * 1.7f;
                float jump = cheerT > 0f ? Mathf.Abs(Mathf.Sin((idleT + ph) * 7f)) * 0.35f * Mathf.Clamp01(cheerT) : 0f;
                float lean = i == 1 && leftAnim >= 0f ? StrokeCurve(leftAnim) : i == 2 && rightAnim >= 0f ? StrokeCurve(rightAnim) : i == 3 && armAnim >= 0f ? StrokeCurve(armAnim) * 0.6f : 0f;
                c.localPosition = crewBase[i] + Vector3.up * (Mathf.Sin(idleT * 9.42f + ph) * 0.03f + jump);
                c.localRotation = Quaternion.Euler(lean * 14f, 0f, Mathf.Sin(idleT * 1.3f + ph) * 3f);
                c.localScale = new Vector3(1f, 1f - 0.05f * Mathf.Clamp01(lean), 1f);
            }
            if (magnetOn)
            {
                magnetRing.localRotation = Quaternion.Euler(0f, idleT * 120f, 0f);
                magnetRing.localPosition = new Vector3(0f, 0.35f + Mathf.Sin(idleT * 4f) * 0.08f, 0f);
            }
            if (turboOn)
                for (int j = 0; j < 2; j++)
                    jets[j].localScale = new Vector3(0.25f, 0.55f + Mathf.PerlinNoise(idleT * 18f, j * 3.1f) * 0.5f, 0.25f);
            Animate(leftPaddle, ref leftAnim, leftBack, 1f);
            Animate(rightPaddle, ref rightAnim, rightBack, 1f);
            if (armAnim >= 0f)
            {
                armAnim += Time.deltaTime;
                collectorArm.localRotation = Quaternion.Euler(-70f * StrokeCurve(armAnim), 0, 0);
                if (armAnim > 0.5f) armAnim = -1f;
            }
            else collectorArm.localRotation = Quaternion.Euler(Mathf.Sin(idleT * 1.4f) * 10f, Mathf.Sin(idleT * 0.9f) * 10f, 0f);
        }

        void Animate(Transform paddle, ref float t, bool back, float dir)
        {
            if (t < 0f) return;
            t += Time.deltaTime;
            float a = StrokeCurve(t) * 55f * (back ? -1f : 1f) * dir;
            paddle.localRotation = Quaternion.Euler(a, 0, 0);
            if (t > 0.5f) { t = -1f; paddle.localRotation = Quaternion.identity; }
        }
    }

    public class WaterView : MonoBehaviour
    {
        Transform waterT, bankL, bankR, sandL, sandR;
        Material mat;
        readonly List<Transform> trees = new List<Transform>();
        const int TreeCount = 36;
        const float TreeSpacing = 8f;

        public static WaterView Create(Transform parent, ContentService content)
        {
            var go = new GameObject("Water");
            go.transform.SetParent(parent, false);
            var w = go.AddComponent<WaterView>();
            w.Build(content);
            return w;
        }

        void Build(ContentService content)
        {
            var t = GameTuning.Current;
            var shader = Shader.Find("Mottainai/Water");
            mat = shader != null ? new Material(shader) : VisualFactory.Lit(new Color(0.2f, 0.55f, 0.7f));
            if (shader != null)
            {
                mat.SetFloat("_Flow", 0.4f);
                mat.SetFloat("_HalfWidth", t.riverWidth * 0.5f);
                mat.SetFloat("_Tier", ArtV3.Tier);
                mat.SetTexture("_Caustics", ArtV3.Caustics());
            }
            ApplyContent(content);
            float hw = t.riverWidth * 0.5f;
            var wgo = new GameObject("WaterMesh");
            wgo.transform.SetParent(transform, false);
            wgo.AddComponent<MeshFilter>().sharedMesh = Grid(hw * 2f + 6f, 150f, 16, 150); // 4.8k tris, 1 vertex per metre
            var mr = wgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterT = wgo.transform;

            var grass = VisualFactory.Lit(ArtV3.Grass);
            var sand = VisualFactory.Lit(ArtV3.Cliff);
            bankL = VisualFactory.Part(transform, VisualFactory.Cube, grass, new Vector3(-hw - 36f, 0.4f, 0), new Vector3(70f, 1.2f, 190f)).transform;
            bankR = VisualFactory.Part(transform, VisualFactory.Cube, grass, new Vector3(hw + 36f, 0.4f, 0), new Vector3(70f, 1.2f, 190f)).transform;
            sandL = VisualFactory.Part(transform, VisualFactory.Cube, sand, new Vector3(-hw - 0.6f, 0.1f, 0), new Vector3(1.6f, 0.5f, 170f)).transform;
            sandR = VisualFactory.Part(transform, VisualFactory.Cube, sand, new Vector3(hw + 0.6f, 0.1f, 0), new Vector3(1.6f, 0.5f, 170f)).transform;

            var trunk = VisualFactory.Lit(ArtV3.Bark);
            var leaf = new[] { VisualFactory.Lit(ArtV3.PineA), VisualFactory.Lit(ArtV3.TreeRound), VisualFactory.Lit(ArtV3.PineB) };
            for (int i = 0; i < TreeCount; i++)
            {
                var tr = new GameObject("tree").transform;
                tr.SetParent(transform, false);
                VisualFactory.Part(tr, VisualFactory.Cylinder, trunk, new Vector3(0, 1.2f, 0), new Vector3(0.35f, 1.2f, 0.35f));
                if (i % 3 == 0) VisualFactory.Part(tr, ProceduralMeshes.Pine(i % 4), leaf[i % 3], Vector3.zero, Vector3.one, new Vector3(0, i * 37, 0));
                else
                {
                    VisualFactory.Part(tr, ProceduralMeshes.Blob(i % 5), leaf[i % 3], new Vector3(0, 3f, 0), new Vector3(2.8f, 2.4f, 2.8f), new Vector3(0, i * 53, 0));
                    VisualFactory.Part(tr, ProceduralMeshes.Blob(i % 5 + 5), leaf[(i + 1) % 3], new Vector3(0.7f, 2.4f, 0.4f), new Vector3(1.6f, 1.4f, 1.6f));
                }
                if (i % 4 == 1) VisualFactory.Part(tr, ProceduralMeshes.Rock(i % 6 + 20), VisualFactory.Lit(ArtV3.RockTop), new Vector3(1.6f, 0.1f, 0.8f), new Vector3(1.2f, 0.8f, 1.1f));
                trees.Add(tr);
            }
        }

        bool mapsApplied;

        /// High-res normal maps from the downloaded Content Pack.
        public void ApplyContent(ContentService content)
        {
            if (mapsApplied || content == null || !mat.HasProperty("_NormalA")) return;
            var na = ContentService.LoadRaw(content.WaterNormalA, true);
            var nb = ContentService.LoadRaw(content.WaterNormalB, true);
            if (na == null || nb == null) return;
            mat.SetTexture("_NormalA", na); mat.SetTexture("_NormalB", nb); mat.SetFloat("_HasMaps", 1f);
            mapsApplied = true;
        }

        static Mesh Grid(float w, float l, int nx, int nz)
        {
            var v = new Vector3[(nx + 1) * (nz + 1)];
            var tris = new int[nx * nz * 6];
            for (int z = 0; z <= nz; z++)
                for (int x = 0; x <= nx; x++)
                    v[z * (nx + 1) + x] = new Vector3(-w * 0.5f + w * x / nx, 0, -40f + l * z / nz);
            int k = 0;
            for (int z = 0; z < nz; z++)
                for (int x = 0; x < nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    tris[k++] = i; tris[k++] = i + nx + 1; tris[k++] = i + 1;
                    tris[k++] = i + 1; tris[k++] = i + nx + 1; tris[k++] = i + nx + 2;
                }
            var m = new Mesh { name = "WaterGrid", indexFormat = UnityEngine.Rendering.IndexFormat.UInt32 };
            m.vertices = v; m.triangles = tris;
            m.RecalculateNormals();
            m.bounds = new Bounds(new Vector3(0, 0, l * 0.5f - 40f), new Vector3(w, 4f, l));
            return m;
        }

        public void Follow(Vector3 raft)
        {
            float z = Mathf.Floor(raft.z / 2f) * 2f;
            waterT.localPosition = new Vector3(0, 0, z);
            bankL.localPosition = new Vector3(bankL.localPosition.x, 0.4f, z + 40f);
            bankR.localPosition = new Vector3(bankR.localPosition.x, 0.4f, z + 40f);
            sandL.localPosition = new Vector3(sandL.localPosition.x, 0.1f, z + 40f);
            sandR.localPosition = new Vector3(sandR.localPosition.x, 0.1f, z + 40f);

            float hw = GameTuning.Current.riverWidth * 0.5f;
            int baseCell = Mathf.FloorToInt((raft.z - 20f) / TreeSpacing);
            for (int c = baseCell; c < baseCell + TreeCount; c++)
            {
                int idx = ((c % TreeCount) + TreeCount) % TreeCount;
                uint h = (uint)(c * 2654435761u);
                float side = (c & 1) == 0 ? -1f : 1f;
                float off = 2.8f + (h % 1000) / 1000f * 6f;
                float s = 0.8f + ((h >> 10) % 100) / 100f * 0.6f;
                var tr = trees[idx];
                tr.localPosition = new Vector3(side * (hw + off), 0.9f, c * TreeSpacing + ((h >> 20) % 100) / 100f * 4f);
                tr.localScale = Vector3.one * s;
            }
        }
    }

    /// Tiny pooled particle + net VFX system (no ParticleSystem assets needed).
    public class FxSystem : MonoBehaviour
    {
        class P { public Transform t; public MeshRenderer r; public Vector3 v; public float life, max, size; public bool on; }
        readonly List<P> ps = new List<P>(220);
        int cursor;

        class Net { public Transform t; public MeshRenderer r; public Vector3 from, to; public float time, dur; public bool on; }
        readonly List<Net> nets = new List<Net>(6);
        MaterialPropertyBlock mpb;

        void Awake()
        {
            mpb = new MaterialPropertyBlock();
            var white = VisualFactory.Lit(Color.white, 0.6f);
            for (int i = 0; i < 220; i++)
            {
                var go = VisualFactory.Part(transform, VisualFactory.Sphere, white, Vector3.zero, Vector3.one * 0.1f, default, "p");
                go.SetActive(false);
                ps.Add(new P { t = go.transform, r = go.GetComponent<MeshRenderer>() });
            }
            for (int i = 0; i < 6; i++)
            {
                var root = new GameObject("net").transform;
                root.SetParent(transform, false);
                var disc = VisualFactory.Part(root, VisualFactory.Cylinder, VisualFactory.Transparent(new Color(1f, 1f, 1f, 0.55f), 1f), Vector3.zero, new Vector3(1f, 0.02f, 1f), default, "disc");
                VisualFactory.Part(root, VisualFactory.Cylinder, VisualFactory.Lit(new Color(0.95f, 0.95f, 0.85f)), Vector3.zero, new Vector3(1.05f, 0.03f, 1.05f), default, "rim").SetActive(false);
                root.gameObject.SetActive(false);
                nets.Add(new Net { t = root, r = disc.GetComponent<MeshRenderer>() });
            }
        }

        public void Burst(Vector3 pos, Color c, int count, float speed, float size = 0.12f, float up = 3f)
        {
            var mat = VisualFactory.Lit(c, 0.5f);
            for (int i = 0; i < count; i++)
            {
                var p = ps[cursor];
                cursor = (cursor + 1) % ps.Count;
                p.on = true;
                p.t.gameObject.SetActive(true);
                p.r.sharedMaterial = mat;
                p.t.position = pos;
                var d = Random.insideUnitSphere; d.y = Mathf.Abs(d.y);
                p.v = d * speed + Vector3.up * up;
                p.life = 0f; p.max = Random.Range(0.4f, 0.8f); p.size = size * Random.Range(0.7f, 1.3f);
            }
        }

        public void Splash(Vector3 pos, bool big) => Burst(pos, new Color(0.85f, 0.95f, 1f), big ? 22 : 10, big ? 2.5f : 1.5f, big ? 0.16f : 0.11f, big ? 3.5f : 2.5f);

        public void ThrowNet(Vector3 from, Vector3 to, float dur)
        {
            foreach (var n in nets)
            {
                if (n.on) continue;
                n.on = true; n.from = from; n.to = to; n.time = 0f; n.dur = Mathf.Max(0.1f, dur);
                n.t.gameObject.SetActive(true);
                return;
            }
        }

        void Update()
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < ps.Count; i++)
            {
                var p = ps[i];
                if (!p.on) continue;
                p.life += dt;
                if (p.life >= p.max) { p.on = false; p.t.gameObject.SetActive(false); continue; }
                p.v += Vector3.down * 9f * dt;
                p.t.position += p.v * dt;
                p.t.localScale = Vector3.one * (p.size * (1f - p.life / p.max));
            }
            float netR = GameTuning.Current.netRadius * 2f;
            foreach (var n in nets)
            {
                if (!n.on) continue;
                n.time += dt;
                float k = n.time / n.dur;
                if (k <= 1f)
                {
                    var p = Vector3.Lerp(n.from, n.to, k) + Vector3.up * (Mathf.Sin(k * Mathf.PI) * 2f);
                    n.t.position = p;
                    float s = Mathf.Lerp(0.3f, netR, k);
                    n.t.localScale = new Vector3(s, 1f, s);
                    mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.6f));
                }
                else
                {
                    float f = (n.time - n.dur) / 0.35f;
                    n.t.position = n.to + Vector3.up * 0.1f;
                    n.t.localScale = new Vector3(netR * (1f + f * 0.2f), 1f, netR * (1f + f * 0.2f));
                    mpb.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.6f * (1f - f)));
                    if (f >= 1f) { n.on = false; n.t.gameObject.SetActive(false); }
                }
                n.r.SetPropertyBlock(mpb);
            }
        }
    }
}
