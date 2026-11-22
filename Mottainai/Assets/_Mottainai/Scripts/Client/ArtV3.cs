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

        static Texture2D caustics;

        /// Tileable 512 px caustic lines baked once from Voronoi (F2 - F1 edges).
        public static Texture2D Caustics()
        {
            if (caustics != null) return caustics;
            const int S = 512, N = 8; // 8x8 jittered cells
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
                    float line = Mathf.Pow(Mathf.Clamp01(1f - e / 0.22f), 2.2f);
                    px[y * S + x] = (byte)(line * 255f);
                }
            caustics = new Texture2D(S, S, TextureFormat.R8, true, true) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, name = "Caustics" };
            caustics.SetPixelData(px, 0);
            caustics.Apply(true, true);
            return caustics;
        }
    }
}
