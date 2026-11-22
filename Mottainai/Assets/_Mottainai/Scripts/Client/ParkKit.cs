using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Client
{
    /// Stylized low-poly park kit (ART_DIRECTION §1/Phase 4): recycling bins, benches, lamp posts, fences, banner poles,
    /// wooden recycling signs, round cartoon trees, stone arch bridge, lighthouse island + sailboat backdrop.
    /// Props face +Z (rotate toward the river when placed). Built once, pooled by Scenery.
    public static class ParkKit
    {
        public static readonly Color Wood = new Color(0.74f, 0.51f, 0.29f), WoodDark = new Color(0.55f, 0.36f, 0.2f);
        public static readonly Color Metal = new Color(0.22f, 0.28f, 0.3f), Stone = new Color(0.78f, 0.7f, 0.6f);
        public static readonly Color BinBlue = new Color(0.16f, 0.45f, 0.86f), BinGreen = new Color(0.22f, 0.66f, 0.3f);
        public static readonly Color Leaf = new Color(0.34f, 0.64f, 0.26f), LeafLight = new Color(0.46f, 0.75f, 0.31f), Bark = new Color(0.5f, 0.33f, 0.2f);

        static Mesh recycle, arch;
        static readonly Dictionary<int, Material> farCache = new Dictionary<int, Material>();

        static GameObject P(Transform t, Mesh m, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default, string name = "p")
            => VisualFactory.Part(t, m, mat, pos, scale, euler, name);
        static Material L(Color c, float e = 0f) => VisualFactory.Lit(c, e);
        static Transform Root(string name) => new GameObject(name).transform;

        /// Toon material with little fog (distant landmarks stay readable).
        public static Material Far(Color c, float fog = 0.3f)
        {
            int k = c.GetHashCode() ^ (int)(fog * 100);
            if (farCache.TryGetValue(k, out var m)) return m;
            m = new Material(VisualFactory.Lit(c)) { name = "Far_" + k };
            m.SetFloat("_FogMul", fog);
            return farCache[k] = m;
        }

        /// White recycling symbol: three chasing arrows on a triangle, flat in XY, double-sided, ~1 unit.
        public static Mesh RecycleIcon()
        {
            if (recycle != null) return recycle;
            var v = new List<Vector3>(); var t = new List<int>();
            Vector3 Pt(int k) { float a = Mathf.PI / 2f + k * 2f * Mathf.PI / 3f; return new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * 0.5f; }
            void Tri(Vector3 a, Vector3 b, Vector3 c) { int i = v.Count; v.Add(a); v.Add(b); v.Add(c); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 1); }
            for (int k = 0; k < 3; k++)
            {
                Vector3 a = Pt(k), b = Pt((k + 1) % 3);
                var dir = (b - a).normalized; var nr = new Vector3(-dir.y, dir.x, 0f);
                Vector3 s = Vector3.Lerp(a, b, 0.14f), e = Vector3.Lerp(a, b, 0.6f), tip = Vector3.Lerp(a, b, 0.88f);
                const float w = 0.075f;
                Tri(s - nr * w, s + nr * w, e + nr * w); Tri(s - nr * w, e + nr * w, e - nr * w);
                Tri(e - nr * w * 2.4f, e + nr * w * 2.4f, tip);
            }
            recycle = new Mesh { name = "RecycleIcon" };
            recycle.SetVertices(v); recycle.SetTriangles(t, 0); recycle.RecalculateNormals(); recycle.RecalculateBounds();
            return recycle;
        }

        static void Icon(Transform t, Vector3 pos, float size, float yRot = 0f) =>
            P(t, RecycleIcon(), L(Color.white, 0.35f), pos, Vector3.one * size, new Vector3(0f, yRot, 0f), "recycle");

        public static Transform Bin(Color c)
        {
            var t = Root("bin");
            P(t, VisualFactory.Cube, VisualFactory.Gloss(c, 0.5f), new Vector3(0, 0.45f, 0), new Vector3(0.62f, 0.9f, 0.56f));
            P(t, VisualFactory.Cube, L(new Color(0.2f, 0.22f, 0.24f)), new Vector3(0, 0.93f, -0.02f), new Vector3(0.68f, 0.08f, 0.62f));
            Icon(t, new Vector3(0, 0.52f, 0.285f), 0.36f);
            return t;
        }

        public static Transform BinPair()
        {
            var t = Root("bins");
            Bin(BinBlue).SetParent(t, false);
            var g = Bin(BinGreen); g.SetParent(t, false); g.localPosition = new Vector3(0.75f, 0, 0);
            return t;
        }

        public static Transform Bench()
        {
            var t = Root("bench");
            var w = L(Wood); var m = L(Metal);
            P(t, VisualFactory.Cube, w, new Vector3(0, 0.45f, 0), new Vector3(1.6f, 0.08f, 0.46f));
            P(t, VisualFactory.Cube, w, new Vector3(0, 0.78f, -0.21f), new Vector3(1.6f, 0.3f, 0.06f), new Vector3(-12f, 0, 0));
            foreach (float x in new[] { -0.68f, 0.68f })
            {
                P(t, VisualFactory.Cube, m, new Vector3(x, 0.22f, 0.15f), new Vector3(0.07f, 0.45f, 0.07f));
                P(t, VisualFactory.Cube, m, new Vector3(x, 0.4f, -0.2f), new Vector3(0.07f, 0.8f, 0.07f));
            }
            return t;
        }

        public static Transform Lamp()
        {
            var t = Root("lamp");
            P(t, VisualFactory.Cylinder, L(Metal), new Vector3(0, 1.6f, 0), new Vector3(0.12f, 1.6f, 0.12f));
            P(t, VisualFactory.Cylinder, L(Metal), new Vector3(0, 0.1f, 0), new Vector3(0.3f, 0.1f, 0.3f));
            P(t, VisualFactory.Sphere, L(new Color(1f, 0.93f, 0.7f), 0.9f), new Vector3(0, 3.35f, 0), Vector3.one * 0.42f);
            P(t, VisualFactory.Cone, L(Metal), new Vector3(0, 3.48f, 0), new Vector3(0.62f, 0.3f, 0.62f));
            return t;
        }

        public static Transform Fence(float len = 3f)
        {
            var t = Root("fence");
            var w = L(WoodDark);
            P(t, VisualFactory.Cube, w, new Vector3(-len * 0.5f, 0.45f, 0), new Vector3(0.14f, 0.9f, 0.14f));
            P(t, VisualFactory.Cube, w, new Vector3(len * 0.5f, 0.45f, 0), new Vector3(0.14f, 0.9f, 0.14f));
            P(t, VisualFactory.Cube, L(Wood), new Vector3(0, 0.72f, 0), new Vector3(len, 0.09f, 0.07f));
            P(t, VisualFactory.Cube, L(Wood), new Vector3(0, 0.4f, 0), new Vector3(len, 0.09f, 0.07f));
            return t;
        }

        public static Transform BannerPole()
        {
            var t = Root("banner");
            P(t, VisualFactory.Cylinder, L(WoodDark), new Vector3(0, 1.9f, 0), new Vector3(0.12f, 1.9f, 0.12f));
            P(t, VisualFactory.Sphere, L(WoodDark), new Vector3(0, 3.82f, 0), Vector3.one * 0.18f);
            P(t, VisualFactory.Cylinder, L(WoodDark), new Vector3(0.45f, 3.55f, 0), new Vector3(0.06f, 0.48f, 0.06f), new Vector3(0, 0, 90f));
            P(t, VisualFactory.Cube, L(DesignTokens.Current.primary), new Vector3(0.48f, 2.75f, 0), new Vector3(0.82f, 1.5f, 0.04f), default, "flag");
            Icon(t, new Vector3(0.48f, 2.8f, 0.025f), 0.5f);
            Icon(t, new Vector3(0.48f, 2.8f, -0.025f), 0.5f, 180f);
            return t;
        }

        public static Transform Sign()
        {
            var t = Root("sign");
            var w = L(WoodDark);
            P(t, VisualFactory.Cube, w, new Vector3(-0.6f, 0.65f, 0), new Vector3(0.12f, 1.3f, 0.12f));
            P(t, VisualFactory.Cube, w, new Vector3(0.6f, 0.65f, 0), new Vector3(0.12f, 1.3f, 0.12f));
            P(t, VisualFactory.Cube, L(Wood), new Vector3(0, 1.1f, 0), new Vector3(1.5f, 0.9f, 0.09f));
            P(t, VisualFactory.Cube, L(WoodDark), new Vector3(0, 1.1f, 0.03f), new Vector3(1.6f, 1.0f, 0.05f));
            Icon(t, new Vector3(0, 1.1f, 0.065f), 0.62f);
            return t;
        }

        /// Round cartoon tree: brown trunk + 2-3 soft spheres (variant 0..2: round, double, tall oval).
        public static Transform RoundTree(int variant)
        {
            var t = Root("roundTree");
            var leaf = L(variant == 1 ? LeafLight : Leaf); var leaf2 = L(variant == 1 ? Leaf : LeafLight);
            P(t, VisualFactory.Cylinder, L(Bark), new Vector3(0, 1.1f, 0), new Vector3(0.36f, 1.1f, 0.36f));
            switch (variant)
            {
                case 0:
                    P(t, VisualFactory.Sphere, leaf, new Vector3(0, 3.1f, 0), new Vector3(3.2f, 2.8f, 3.2f));
                    P(t, VisualFactory.Sphere, leaf2, new Vector3(0.9f, 2.6f, 0.4f), new Vector3(1.9f, 1.7f, 1.9f));
                    break;
                case 1:
                    P(t, VisualFactory.Sphere, leaf, new Vector3(-0.6f, 2.9f, 0), new Vector3(2.6f, 2.4f, 2.6f));
                    P(t, VisualFactory.Sphere, leaf2, new Vector3(0.8f, 3.3f, 0.2f), new Vector3(2.5f, 2.3f, 2.5f));
                    P(t, VisualFactory.Sphere, leaf, new Vector3(0.1f, 4.1f, -0.2f), new Vector3(2.0f, 1.8f, 2.0f));
                    break;
                default:
                    P(t, VisualFactory.Sphere, leaf, new Vector3(0, 3.5f, 0), new Vector3(2.3f, 3.6f, 2.3f));
                    P(t, VisualFactory.Sphere, leaf2, new Vector3(0.3f, 2.6f, 0.5f), new Vector3(1.5f, 1.4f, 1.5f));
                    break;
            }
            return t;
        }

        /// Half-elliptic stone arch band extruded along Z (inner semi-axes a,b; thickness th; width w).
        static Mesh Arch(float a, float b, float th, float w)
        {
            if (arch != null) return arch;
            var v = new List<Vector3>(); var t = new List<int>();
            const int seg = 24;
            void Q(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3) { int i = v.Count; v.Add(p0); v.Add(p1); v.Add(p2); v.Add(p3); t.Add(i); t.Add(i + 1); t.Add(i + 2); t.Add(i); t.Add(i + 2); t.Add(i + 3); }
            for (int s = 0; s < seg; s++)
            {
                float a0 = Mathf.PI * s / seg, a1 = Mathf.PI * (s + 1) / seg;
                Vector3 i0 = new Vector3(Mathf.Cos(a0) * a, Mathf.Sin(a0) * b, 0), i1 = new Vector3(Mathf.Cos(a1) * a, Mathf.Sin(a1) * b, 0);
                Vector3 o0 = new Vector3(Mathf.Cos(a0) * (a + th), Mathf.Sin(a0) * (b + th), 0), o1 = new Vector3(Mathf.Cos(a1) * (a + th), Mathf.Sin(a1) * (b + th), 0);
                var f = new Vector3(0, 0, w * 0.5f);
                Q(i0 - f, i1 - f, i1 + f, i0 + f);           // underside (visible from the river)
                Q(o0 + f, o1 + f, o1 - f, o0 - f);           // top
                Q(i0 + f, i1 + f, o1 + f, o0 + f);           // front face
                Q(o0 - f, o1 - f, i1 - f, i0 - f);           // back face
            }
            arch = new Mesh { name = "Arch" };
            arch.SetVertices(v); arch.SetTriangles(t, 0); arch.RecalculateNormals(); arch.RecalculateBounds();
            return arch;
        }

        /// Stone arch footbridge over the river (scenery only, no collision): arch, abutments, deck, wooden railings, banners.
        public static Transform Bridge(float riverHalfWidth)
        {
            var t = Root("bridge");
            float a = riverHalfWidth + 1.2f, b = 7.4f, deckY = b + 1.1f;
            var stone = L(Stone); var stoneDark = L(new Color(0.66f, 0.58f, 0.5f));
            P(t, Arch(a, b, 1.0f, 3.2f), stone, Vector3.zero, Vector3.one, default, "arch");
            foreach (float s in new[] { -1f, 1f })
                P(t, VisualFactory.Cube, stoneDark, new Vector3(s * (a + 2.2f), deckY * 0.5f - 0.2f, 0), new Vector3(3.2f, deckY + 0.4f, 3.6f), default, "abutment");
            P(t, VisualFactory.Cube, L(Wood), new Vector3(0, deckY, 0), new Vector3(2f * a + 7f, 0.3f, 3.2f), default, "deck");
            var post = L(WoodDark); var rail = L(Wood);
            foreach (float z in new[] { -1.5f, 1.5f })
            {
                for (float x = -a - 3f; x <= a + 3.01f; x += 2.5f) P(t, VisualFactory.Cube, post, new Vector3(x, deckY + 0.6f, z), new Vector3(0.14f, 1.0f, 0.14f));
                P(t, VisualFactory.Cube, rail, new Vector3(0, deckY + 1.05f, z), new Vector3(2f * a + 6.4f, 0.1f, 0.08f));
            }
            foreach (float x in new[] { -a * 0.45f, a * 0.45f })
            {
                P(t, VisualFactory.Cube, L(DesignTokens.Current.primary), new Vector3(x, deckY - 0.9f, -1.65f), new Vector3(1.4f, 1.6f, 0.05f), default, "banner");
                Icon(t, new Vector3(x, deckY - 0.85f, -1.69f), 0.75f, 180f);
            }
            return t;
        }

        /// Rocky island with a red/white lighthouse (distant backdrop; low fog).
        public static Transform LighthouseIsland()
        {
            var t = Root("lighthouseIsland");
            P(t, ProceduralMeshes.Rock(7), Far(new Color(0.6f, 0.53f, 0.47f)), new Vector3(0, -1f, 0), new Vector3(26f, 7f, 16f));
            P(t, ProceduralMeshes.Blob(3), Far(new Color(0.42f, 0.66f, 0.33f)), new Vector3(-3f, 2.8f, 1f), new Vector3(14f, 3f, 9f));
            var red = Far(new Color(0.88f, 0.2f, 0.18f)); var white = Far(new Color(0.97f, 0.96f, 0.93f));
            for (int i = 0; i < 4; i++) P(t, VisualFactory.Cylinder, i % 2 == 0 ? red : white, new Vector3(3f, 4.6f + i * 2.2f, 0), new Vector3(2.4f - i * 0.18f, 1.1f, 2.4f - i * 0.18f));
            P(t, VisualFactory.Cylinder, Far(new Color(0.2f, 0.22f, 0.25f)), new Vector3(3f, 13.6f, 0), new Vector3(2.2f, 0.2f, 2.2f));
            P(t, VisualFactory.Sphere, Far(new Color(1f, 0.95f, 0.6f), 0.15f), new Vector3(3f, 14.4f, 0), Vector3.one * 1.3f);
            P(t, VisualFactory.Cone, red, new Vector3(3f, 15f, 0), new Vector3(2.1f, 1.4f, 2.1f));
            return t;
        }

        public static Transform Sailboat()
        {
            var t = Root("sailboat");
            P(t, VisualFactory.Capsule, Far(new Color(0.96f, 0.96f, 0.94f)), new Vector3(0, 0.2f, 0), new Vector3(1.2f, 2.2f, 1.0f), new Vector3(90f, 0, 0));
            P(t, VisualFactory.Cylinder, Far(new Color(0.45f, 0.32f, 0.22f)), new Vector3(0, 3f, 0), new Vector3(0.12f, 2.8f, 0.12f));
            P(t, VisualFactory.Triangle, Far(Color.white), new Vector3(0.05f, 1f, -0.2f), new Vector3(3.2f, 5.6f, 1f), new Vector3(0, 90f, 0));
            return t;
        }
    }
}
