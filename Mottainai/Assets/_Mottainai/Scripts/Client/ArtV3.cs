using UnityEngine;

namespace Mottainai.Client
{
    /// ART_DIRECTION_v3 palette tokens + quality tier (0 Low default, 1 Medium, 2 High). Visual only.
    public static class ArtV3
    {
        public static int Tier => PlayerPrefs.GetInt("artTier", 0);

        public static Color Hex(string h) { ColorUtility.TryParseHtmlString(h, out var c); return c; }

        public static readonly Color WaterDeep = Hex("#178CB8"), WaterShallow = Hex("#3FD3E3"), WaterHorizon = Hex("#8FE6F0");
        public static readonly Color Foam = Hex("#F4FBFF"), Caustic = Hex("#CFFBFF");
        public static readonly Color Grass = Hex("#7CC243"), GrassDark = Hex("#5FA834"), Cliff = Hex("#D9A464"), CliffShade = Hex("#B9844A");
        public static readonly Color RockTop = Hex("#8E959C"), RockSide = Hex("#6F767D");
        public static readonly Color PineA = Hex("#2F8F4E"), PineB = Hex("#3BA55C"), TreeRound = Hex("#57B847");
        public static readonly Color Wood = Hex("#A0703C"), Bark = Hex("#8B5A2B"), Roof = Hex("#D9653B"), Wall = Hex("#F3E6C8");
        public static readonly Color StoneFar = Hex("#C9C2B5"), HillsFar = Hex("#9CCB7A");
        public static readonly Color SkyTop = Hex("#6EC6FF"), SkyHorizon = Hex("#CFEFFF"), Fog = Hex("#BFE6F2");
        public static readonly Color RaftBody = Hex("#FFB020"), RaftBand = Hex("#3A3F47"), RaftFloor = Hex("#2E3338");
        public static readonly Color HazardOrange = Hex("#FF7A1A");
        public static readonly Color CrewBlue = Hex("#2F6BFF"), CapBlue = Hex("#1F4FCC"), CrewYellow = Hex("#FFD23F"), CapCoral = Hex("#FF6F59"), CrewGreen = Hex("#4CC94C"), Eyes = Hex("#1A1A1A");
        public static readonly Color UiPanel = new Color(0x1E / 255f, 0x2A / 255f, 0x34 / 255f, 0.8f), UiOutline = Hex("#0E161C");
        public static readonly Color UiPrimary = Hex("#4CC94C"), UiPrimaryShadow = Hex("#2E8A2E"), UiSecondary = Hex("#2F8BFF"), UiSecondaryShadow = Hex("#1E5FB3");
        public static readonly Color UiWarn = Hex("#FF7A1A"), NetBadge = Hex("#00C2A8");

        static Mesh gem;
        static Material blob;

        /// Unlit radial-gradient blob shadow (#0B3A4A @ 35%), 128 px.
        public static Material BlobMaterial()
        {
            if (blob != null) return blob;
            const int S = 128;
            var px = new Color32[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float dx = (x + 0.5f) / S * 2f - 1f, dy = (y + 0.5f) / S * 2f - 1f;
                    float a = Mathf.Clamp01(1f - Mathf.Sqrt(dx * dx + dy * dy));
                    px[y * S + x] = new Color32(255, 255, 255, (byte)(Mathf.SmoothStep(0f, 1f, a) * 255f));
                }
            var tex = new Texture2D(S, S, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, name = "BlobShadow" };
            tex.SetPixels32(px); tex.Apply(false, true);
            blob = new Material(Shader.Find("Mottainai/Transparent")) { name = "BlobShadow", renderQueue = 2990 };
            blob.SetColor("_BaseColor", new Color(0x0B / 255f, 0x3A / 255f, 0x4A / 255f, 0.35f));
            blob.SetTexture("_MainTex", tex);
            blob.SetFloat("_UseTex", 1f);
            return blob;
        }
        static readonly System.Collections.Generic.Dictionary<int, Mesh> rings = new System.Collections.Generic.Dictionary<int, Mesh>();

        /// Flat annulus in XZ facing up, outer radius 1, inner radius = innerRatio (32 segments).
        public static Mesh Ring(float innerRatio, int seg = 32)
        {
            int key = (int)(innerRatio * 1000) * 100 + seg;
            if (rings.TryGetValue(key, out var m)) return m;
            var v = new Vector3[seg * 2]; var uv = new Vector2[seg * 2]; var tri = new int[seg * 6];
            for (int i = 0; i < seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var d = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                v[i * 2] = d * innerRatio; v[i * 2 + 1] = d;
                uv[i * 2] = new Vector2(0, 0); uv[i * 2 + 1] = new Vector2(1, 0);
                int n = (i + 1) % seg;
                int k = i * 6;
                tri[k] = i * 2; tri[k + 1] = n * 2 + 1; tri[k + 2] = i * 2 + 1;
                tri[k + 3] = i * 2; tri[k + 4] = n * 2; tri[k + 5] = n * 2 + 1;
            }
            m = new Mesh { name = "Ring", vertices = v, uv = uv, triangles = tri };
            m.RecalculateNormals(); m.RecalculateBounds();
            rings[key] = m;
            return m;
        }

        /// Faceted gem (hexagonal bipyramid, 12 flat-shaded tris).
        public static Mesh Gem()
        {
            if (gem != null) return gem;
            var top = new Vector3(0, 0.55f, 0); var bot = new Vector3(0, -0.45f, 0);
            var verts = new System.Collections.Generic.List<Vector3>();
            for (int i = 0; i < 6; i++)
            {
                float a0 = i * Mathf.PI / 3f, a1 = (i + 1) * Mathf.PI / 3f;
                var p0 = new Vector3(Mathf.Cos(a0) * 0.5f, 0.12f, Mathf.Sin(a0) * 0.5f);
                var p1 = new Vector3(Mathf.Cos(a1) * 0.5f, 0.12f, Mathf.Sin(a1) * 0.5f);
                verts.Add(top); verts.Add(p1); verts.Add(p0);
                verts.Add(bot); verts.Add(p0); verts.Add(p1);
            }
            var tri = new int[verts.Count];
            for (int i = 0; i < tri.Length; i++) tri[i] = i;
            gem = new Mesh { name = "Gem", vertices = verts.ToArray(), triangles = tri };
            gem.RecalculateNormals(); gem.RecalculateBounds();
            return gem;
        }

        static Texture2D caustics;

        /// Tileable 512 px caustic lines baked once from Voronoi (F2 - F1 edges).
        public static Texture2D Caustics()
        {
            if (caustics != null) return caustics;
            const int S = 512, N = 4; // 4x4 jittered cells (low frequency)
            var rng = new System.Random(7);
            var pts = new Vector2[N * N];
            for (int i = 0; i < pts.Length; i++) pts[i] = new Vector2((i % N + (float)rng.NextDouble()) / N, (i / N + (float)rng.NextDouble()) / N);
            var px = new byte[S * S];
            for (int y = 0; y < S; y++)
                for (int x = 0; x < S; x++)
                {
                    float u = (x + 0.5f) / S, v = (y + 0.5f) / S;
                    int cx = (int)(u * N), cy = (int)(v * N);
                    float f1 = 9f, f2 = 9f;
                    for (int oy = -1; oy <= 1; oy++)
                        for (int ox = -1; ox <= 1; ox++)
                        {
                            int gx = cx + ox, gy = cy + oy;
                            int wx = (gx + N) % N, wy = (gy + N) % N;
                            var p = pts[wy * N + wx] + new Vector2(gx - wx, gy - wy) / N;
                            float d = (p - new Vector2(u, v)).sqrMagnitude;
                            if (d < f1) { f2 = f1; f1 = d; } else if (d < f2) f2 = d;
                        }
                    float e = (Mathf.Sqrt(f2) - Mathf.Sqrt(f1)) * N; // 0 on cell edges
                    float line = Mathf.Pow(Mathf.Clamp01(1f - e / 0.35f), 1.6f);
                    px[y * S + x] = (byte)(line * 255f);
                }
            caustics = new Texture2D(S, S, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Caustics" };
            caustics.SetPixelData(px, 0);
            caustics.Apply(true, true);
            return caustics;
        }
    }
}
