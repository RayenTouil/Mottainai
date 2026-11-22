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
            var tk = DesignTokens.Current;
            // brown inflatable ring raft (thick glossy rubber tube), rope line, floor, small recycling badge on the stern
            if (VisualFactory.Model("Raft/raft", t, 3.4f, new Vector3(0, 0.22f, 0), Vector3.zero, "hull", false, true) == null)
            {
                hullRenderer = VisualFactory.Part(t, ProceduralMeshes.Torus(0.98f, 0.34f, 1.32f, 44, 16), VisualFactory.Gloss(new Color(0.42f, 0.27f, 0.18f), 0.9f), new Vector3(0, 0.27f, 0), Vector3.one, default, "hull").GetComponent<MeshRenderer>();
                ApplyContent(content);
                VisualFactory.Part(t, ProceduralMeshes.Torus(0.98f, 0.045f, 1.32f, 44, 6), VisualFactory.Lit(new Color(0.93f, 0.88f, 0.74f)), new Vector3(0, 0.6f, 0), Vector3.one, default, "rope");
                VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(new Color(0.3f, 0.2f, 0.14f)), new Vector3(0, 0.12f, 0), new Vector3(1.9f, 0.05f, 2.6f), default, "floor");
            }
            VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(tk.primary, 0.15f), new Vector3(0, 0.33f, -1.64f), new Vector3(0.36f, 0.015f, 0.36f), new Vector3(90f, 0f, 0f), "badge");
            VisualFactory.Part(t, VisualFactory.Triangle, VisualFactory.Lit(Color.white, 0.3f), new Vector3(0, 0.27f, -1.665f), Vector3.one * 0.2f, new Vector3(0f, 180f, 0f), "badgeIcon");

            crew[1] = Crew(t, new Vector3(-0.7f, 0.33f, -0.38f), tk.capLeft, out leftPaddle, -1);
            crew[2] = Crew(t, new Vector3(0.7f, 0.33f, -0.38f), tk.capRight, out rightPaddle, 1);
            crew[3] = Crew(t, new Vector3(0f, 0.33f, 0.8f), tk.capCollector, out collectorArm, 0);
            for (int i = 1; i <= 3; i++) crewBase[i] = crew[i].localPosition;

            highlight = VisualFactory.Part(t, VisualFactory.Cylinder, VisualFactory.Lit(new Color(1f, 0.85f, 0.2f), 1f), Vector3.zero, new Vector3(0.5f, 0.02f, 0.5f), default, "you").transform;
            highlight.gameObject.SetActive(false);

            magnetRing = VisualFactory.Part(t, ProceduralMeshes.Torus(1.75f, 0.05f, 1.2f, 40, 6), VisualFactory.Lit(new Color(1f, 0.3f, 0.85f), 1.2f), new Vector3(0, 0.35f, 0), Vector3.one, default, "magnetRing").transform;
            magnetRing.gameObject.SetActive(false);
            var jetMat = VisualFactory.Lit(new Color(1f, 0.55f, 0.1f), 1.6f);
            for (int j = 0; j < 2; j++)
            {
                jets[j] = VisualFactory.Part(t, VisualFactory.Cone, jetMat, new Vector3(j == 0 ? -0.55f : 0.55f, 0.3f, -1.55f), new Vector3(0.25f, 0.7f, 0.25f), new Vector3(-90f, 0f, 0f), "turboJet").transform;
                jets[j].gameObject.SetActive(false);
            }
            bubble = VisualFactory.Part(t, VisualFactory.Sphere, VisualFactory.Transparent(new Color(0.35f, 0.85f, 1f, 0.22f), 2.5f), new Vector3(0, 0.6f, 0), new Vector3(3.6f, 2.6f, 4.4f), default, "shield");
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

        /// Toy crew member: rounded cylinder body, orange life vest with straps, round head, role-colored cap + brim, dot eyes.
        Transform Crew(Transform parent, Vector3 pos, Color cap, out Transform tool, int side)
        {
            var tk = DesignTokens.Current;
            var root = new GameObject("crew").transform;
            root.SetParent(parent, false);
            root.localPosition = pos;
            var shirt = VisualFactory.Lit(Color.Lerp(cap, Color.white, 0.18f));
            var skin = VisualFactory.Lit(new Color(1f, 0.83f, 0.66f));
            var capM = VisualFactory.Gloss(cap, 0.5f);
            var strap = VisualFactory.Lit(new Color(0.96f, 0.95f, 0.9f));
            var dark = VisualFactory.Lit(new Color(0.12f, 0.1f, 0.1f));
            VisualFactory.Part(root, VisualFactory.Capsule, shirt, new Vector3(0, 0.33f, 0), new Vector3(0.44f, 0.33f, 0.44f), default, "body");
            VisualFactory.Part(root, VisualFactory.Cylinder, VisualFactory.Gloss(tk.vest, 0.35f), new Vector3(0, 0.41f, 0), new Vector3(0.5f, 0.18f, 0.5f), default, "vest");
            for (int sx = -1; sx <= 1; sx += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                    VisualFactory.Part(root, VisualFactory.Cube, strap, new Vector3(sx * 0.1f, 0.42f, sz * 0.24f), new Vector3(0.05f, 0.3f, 0.03f), default, "strap");
            VisualFactory.Part(root, VisualFactory.Sphere, skin, new Vector3(0, 0.83f, 0), Vector3.one * 0.36f, default, "head");
            VisualFactory.Part(root, VisualFactory.Sphere, capM, new Vector3(0, 0.91f, -0.01f), new Vector3(0.385f, 0.25f, 0.385f), default, "cap");
            VisualFactory.Part(root, VisualFactory.Cylinder, capM, new Vector3(0, 0.9f, 0.17f), new Vector3(0.27f, 0.012f, 0.22f), default, "brim");
            VisualFactory.Part(root, VisualFactory.Sphere, dark, new Vector3(0.075f, 0.84f, 0.165f), Vector3.one * 0.05f, default, "eye");
            VisualFactory.Part(root, VisualFactory.Sphere, dark, new Vector3(-0.075f, 0.84f, 0.165f), Vector3.one * 0.05f, default, "eye");
            tool = new GameObject("tool").transform;
            tool.SetParent(root, false);
            tool.localPosition = new Vector3(side * 0.25f, 0.47f, 0.05f);
            if (side != 0)
            {
                // wooden shaft, dark blade, hands on the grip
                VisualFactory.Part(tool, VisualFactory.Cylinder, VisualFactory.Lit(new Color(0.76f, 0.54f, 0.32f)), new Vector3(side * 0.36f, -0.26f, 0), new Vector3(0.055f, 0.62f, 0.055f), new Vector3(0, 0, side * 40f), "shaft");
                VisualFactory.Part(tool, VisualFactory.Capsule, VisualFactory.Gloss(new Color(0.27f, 0.18f, 0.12f), 0.3f), new Vector3(side * 0.76f, -0.72f, 0), new Vector3(0.07f, 0.26f, 0.22f), new Vector3(0, 0, side * 40f), "blade");
                VisualFactory.Part(tool, VisualFactory.Sphere, skin, new Vector3(side * 0.06f, 0.04f, 0), Vector3.one * 0.12f, default, "hand");
                VisualFactory.Part(tool, VisualFactory.Sphere, skin, new Vector3(side * 0.42f, -0.32f, 0), Vector3.one * 0.11f, default, "hand");
            }
            else
            {
                // landing net: pole, yellow hoop, translucent mesh
                VisualFactory.Part(tool, VisualFactory.Cylinder, VisualFactory.Lit(new Color(0.76f, 0.54f, 0.32f)), new Vector3(0, 0.2f, 0.25f), new Vector3(0.05f, 0.45f, 0.05f), new Vector3(60, 0, 0), "pole");
                VisualFactory.Part(tool, ProceduralMeshes.Torus(0.21f, 0.025f, 1f, 24, 6), VisualFactory.Lit(tk.reward), new Vector3(0, 0.45f, 0.64f), Vector3.one, default, "hoop");
                VisualFactory.Part(tool, VisualFactory.Sphere, VisualFactory.Transparent(new Color(1f, 1f, 1f, 0.45f)), new Vector3(0, 0.43f, 0.64f), new Vector3(0.4f, 0.1f, 0.4f), default, "mesh");
                VisualFactory.Part(tool, VisualFactory.Sphere, skin, new Vector3(0, 0.02f, 0.05f), Vector3.one * 0.12f, default, "hand");
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
                highlight.localPosition = new Vector3(0, 1.08f, 0);
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
                bubbleProps.SetColor("_BaseColor", new Color(0.35f, 0.85f, 1f, 0.18f + 0.08f * Mathf.Sin(Time.time * 5f)));
                bubbleRenderer.SetPropertyBlock(bubbleProps);
            }
        }

        static float StrokeCurve(float t) => t < 0.15f ? t / 0.15f : Mathf.Max(0f, 1f - (t - 0.15f) / 0.3f);

        readonly Vector3[] crewBase = new Vector3[4];
        float idleT, cheerT;

        /// Whole crew jumps for joy (end of match).
        public void Cheer() => cheerT = 2.6f;

        const float HopTime = 0.42f, FlinchTime = 0.6f;
        readonly float[] hopT = new float[4];
        float flinchT, trailAt;
        public FxSystem fx;

        /// Small cheer hop (catch, sync).
        public void Hop(Role r) { int i = (int)r; if (i >= 1 && i <= 3) hopT[i] = HopTime; }
        public void HopAll() { for (int i = 1; i <= 3; i++) hopT[i] = HopTime * (0.85f + 0.1f * i); }
        /// Crew ducks and squashes (obstacle hit / near miss).
        public void Flinch() => flinchT = FlinchTime;

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
                float hop = hopT[i] > 0f ? Mathf.Sin((1f - hopT[i] / HopTime) * Mathf.PI) * 0.24f : 0f;
                hopT[i] = Mathf.Max(0f, hopT[i] - dt);
                float fl = flinchT > 0f ? Mathf.Sin((1f - flinchT / FlinchTime) * Mathf.PI) : 0f;
                c.localPosition = crewBase[i] + Vector3.up * (Mathf.Sin(idleT * 2.2f + ph) * 0.025f + jump + hop);
                c.localRotation = Quaternion.Euler(Mathf.Sin(idleT * 1.6f + ph) * 3f + lean * 18f - fl * 24f, 0f, Mathf.Sin(idleT * 1.3f + ph) * 4f + hop * 30f * (i == 2 ? -1f : 1f));
                c.localScale = new Vector3(1f + fl * 0.12f, 1f - fl * 0.16f + hop * 0.3f, 1f + fl * 0.12f);
            }
            flinchT = Mathf.Max(0f, flinchT - dt);
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
        }

        void Animate(Transform paddle, ref float t, bool back, float dir)
        {
            if (t < 0f) return;
            t += Time.deltaTime;
            // water trail while the blade pulls through the water
            if (fx != null && t > 0.04f && t < 0.34f && Time.time >= trailAt)
            {
                trailAt = Time.time + 0.035f;
                fx.Burst(PaddleTip(paddle == leftPaddle) + Vector3.up * 0.05f, new Color(0.9f, 0.97f, 1f), 1, 0.9f, 0.07f, 1.3f);
            }
            float a = StrokeCurve(t) * 55f * (back ? -1f : 1f) * dir;
            paddle.localRotation = Quaternion.Euler(a, 0, 0);
            if (t > 0.5f) { t = -1f; paddle.localRotation = Quaternion.identity; }
        }
    }

    public class WaterView : MonoBehaviour
    {
        Transform waterT, bankL, bankR, sandL, sandR;
        Material mat;

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
                mat.SetFloat("_Flow", t.currentSpeed);
                mat.SetFloat("_HalfWidth", t.riverWidth * 0.5f);
            }
            ApplyContent(content);
            float hw = t.riverWidth * 0.5f;
            var wgo = new GameObject("WaterMesh");
            wgo.transform.SetParent(transform, false);
            wgo.AddComponent<MeshFilter>().sharedMesh = Grid(hw * 2f + 6f, 150f, 48, 100);
            var mr = wgo.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            waterT = wgo.transform;

            // park ground: grass patches, winding footpath, flowers, earth bank walls (Mottainai/Ground)
            var groundShader = Shader.Find("Mottainai/Ground");
            var grass = groundShader != null ? new Material(groundShader) : VisualFactory.Lit(new Color(0.47f, 0.76f, 0.33f));
            if (groundShader != null) grass.SetFloat("_PathX", hw + 5.5f);
            var sand = VisualFactory.Lit(new Color(0.6f, 0.47f, 0.34f));
            bankL = VisualFactory.Part(transform, VisualFactory.Cube, grass, new Vector3(-hw - 36f, 0.4f, 0), new Vector3(70f, 1.2f, 190f)).transform;
            bankR = VisualFactory.Part(transform, VisualFactory.Cube, grass, new Vector3(hw + 36f, 0.4f, 0), new Vector3(70f, 1.2f, 190f)).transform;
            sandL = VisualFactory.Part(transform, VisualFactory.Cube, sand, new Vector3(-hw - 0.6f, 0.1f, 0), new Vector3(1.6f, 0.5f, 170f)).transform;
            sandR = VisualFactory.Part(transform, VisualFactory.Cube, sand, new Vector3(hw + 0.6f, 0.1f, 0), new Vector3(1.6f, 0.5f, 170f)).transform;

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

        /// 0 = river between banks, 1 = open sea (last part of the course).
        public static float SeaOpen(float z)
        {
            float L = GameTuning.Current.courseLength;
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(L - 90f, L - 10f, z));
        }

        public void Follow(Vector3 raft)
        {
            float z = Mathf.Floor(raft.z / 2f) * 2f;
            float hw = GameTuning.Current.riverWidth * 0.5f;
            float open = SeaOpen(raft.z + 40f);
            waterT.localPosition = new Vector3(0, 0, z);
            waterT.localScale = new Vector3(1f + open * 7f, 1f, 1f);
            float push = open * 70f;
            bankL.localPosition = new Vector3(-hw - 36f - push, 0.4f, z + 40f);
            bankR.localPosition = new Vector3(hw + 36f + push, 0.4f, z + 40f);
            sandL.localPosition = new Vector3(-hw - 0.6f - push, 0.1f, z + 40f);
            sandR.localPosition = new Vector3(hw + 0.6f + push, 0.1f, z + 40f);
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
