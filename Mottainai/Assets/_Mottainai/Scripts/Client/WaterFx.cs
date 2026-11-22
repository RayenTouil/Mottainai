using UnityEngine;

namespace Mottainai.Client
{
    /// Tileable noise baked once at startup (global _MottainaiNoise): R/G = fbm (clouds, foam), B = sparse sparkles.
    /// Replaces per-pixel procedural noise in the sky and water shaders (much cheaper on mobile GPUs).
    public static class NoiseTexture
    {
        static Texture2D tex;
        static readonly int Id = Shader.PropertyToID("_MottainaiNoise");

        public static Texture2D Ensure()
        {
            if (tex != null) return tex;
            const int S = 256;
            var r = Fbm(S, 11, new[] { 8, 16, 32, 64 });
            var g = Fbm(S, 23, new[] { 4, 8, 16, 32 });
            var rng = new System.Random(5);
            var px = new Color32[S * S];
            for (int i = 0; i < px.Length; i++)
            {
                byte b = (byte)(rng.NextDouble() < 0.035 ? 200 + rng.Next(56) : rng.Next(110));
                px[i] = new Color32((byte)(r[i] * 255f), (byte)(g[i] * 255f), b, 255);
            }
            tex = new Texture2D(S, S, TextureFormat.RGBA32, true, true)
            {
                wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Bilinear, anisoLevel = 2, name = "MottainaiNoise"
            };
            tex.SetPixels32(px);
            tex.Apply(true, true);
            Shader.SetGlobalTexture(Id, tex);
            return tex;
        }

        static float[] Fbm(int size, int seed, int[] cells)
        {
            var v = new float[size * size];
            var rng = new System.Random(seed);
            float amp = 0.5f;
            foreach (int c in cells)
            {
                var lat = new float[c * c];
                for (int i = 0; i < lat.Length; i++) lat[i] = (float)rng.NextDouble();
                float step = (float)c / size;
                for (int y = 0; y < size; y++)
                {
                    float fy = y * step; int y0 = (int)fy; float ty = fy - y0; ty = ty * ty * (3f - 2f * ty);
                    int y1 = (y0 + 1) % c;
                    for (int x = 0; x < size; x++)
                    {
                        float fx = x * step; int x0 = (int)fx; float tx = fx - x0; tx = tx * tx * (3f - 2f * tx);
                        int x1 = (x0 + 1) % c;
                        float a = lat[y0 * c + x0] + (lat[y0 * c + x1] - lat[y0 * c + x0]) * tx;
                        float b = lat[y1 * c + x0] + (lat[y1 * c + x1] - lat[y1 * c + x0]) * tx;
                        v[y * size + x] += (a + (b - a) * ty) * amp;
                    }
                }
                amp *= 0.5f;
            }
            float mn = float.MaxValue, mx = float.MinValue;
            for (int i = 0; i < v.Length; i++) { if (v[i] < mn) mn = v[i]; if (v[i] > mx) mx = v[i]; }
            float inv = mx > mn ? 1f / (mx - mn) : 1f;
            for (int i = 0; i < v.Length; i++) v[i] = (v[i] - mn) * inv;
            return v;
        }
    }

    /// CPU-rasterized water effects map (global _MottainaiFx, R = obstacle foam, G = waste dirt, B = raft wake, A = blob shadows)
    /// covering a window around the raft. One texture fetch per water pixel instead of dozens of loop iterations.
    public class WaterFxMap
    {
        public const int W = 128, H = 256;
        public const float SpanX = 32f, SpanZ = 80f;
        const float TX = SpanX / W, TZ = SpanZ / H;
        static readonly int TexId = Shader.PropertyToID("_MottainaiFx"), RectId = Shader.PropertyToID("_MottainaiFxRect");

        readonly Texture2D tex;
        readonly byte[] pix = new byte[W * H * 4];
        float x0, z0;

        public WaterFxMap()
        {
            tex = new Texture2D(W, H, TextureFormat.RGBA32, false, true)
            {
                wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear, name = "MottainaiWaterFx"
            };
            tex.SetPixelData(pix, 0);
            tex.Apply(false, false);
            Shader.SetGlobalTexture(TexId, tex);
            Shader.SetGlobalVector(RectId, new Vector4(-SpanX * 0.5f, -10000f, 1f / SpanX, 1f / SpanZ));
        }

        public void Begin(float raftZ)
        {
            x0 = -SpanX * 0.5f;
            z0 = Mathf.Floor((raftZ - 14f) / TZ) * TZ; // texel-snapped: no shimmering while moving
            System.Array.Clear(pix, 0, pix.Length);
        }

        public void End()
        {
            tex.SetPixelData(pix, 0);
            tex.Apply(false, false);
            Shader.SetGlobalVector(RectId, new Vector4(x0, z0, 1f / SpanX, 1f / SpanZ));
        }

        /// Soft disc. ch: 0 foam, 1 dirt, 2 wake.
        public void Disc(float cx, float cz, float radius, float strength, int ch)
        {
            Bounds(cx, cz, radius, out int ix0, out int ix1, out int iz0, out int iz1);
            float inv = 1f / Mathf.Max(0.01f, radius);
            for (int iz = iz0; iz <= iz1; iz++)
            {
                float dz = z0 + (iz + 0.5f) * TZ - cz;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float dx = x0 + (ix + 0.5f) * TX - cx;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    Put(ix, iz, ch, (1f - d * inv) * strength);
                }
            }
        }

        /// Soft blob shadow (channel A), ellipse rx x rz.
        public void Shadow(float cx, float cz, float rx, float rz, float strength)
        {
            Bounds(cx, cz, Mathf.Max(rx, rz), out int ix0, out int ix1, out int iz0, out int iz1);
            float ix = 1f / Mathf.Max(0.01f, rx), iz = 1f / Mathf.Max(0.01f, rz);
            for (int z = iz0; z <= iz1; z++)
            {
                float dz = (z0 + (z + 0.5f) * TZ - cz) * iz;
                for (int x = ix0; x <= ix1; x++)
                {
                    float dx = (x0 + (x + 0.5f) * TX - cx) * ix;
                    float d = 1f - Mathf.Sqrt(dx * dx + dz * dz);
                    Put(x, z, 3, d * d * (3f - 2f * d) * strength);
                }
            }
        }

        /// Foam ring hugging an obstacle edge with a soft outer halo.
        public void Ring(float cx, float cz, float r, float strength)
        {
            Bounds(cx, cz, r + 2.2f, out int ix0, out int ix1, out int iz0, out int iz1);
            for (int iz = iz0; iz <= iz1; iz++)
            {
                float dz = z0 + (iz + 0.5f) * TZ - cz;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float dx = x0 + (ix + 0.5f) * TX - cx;
                    float dd = Mathf.Sqrt(dx * dx + dz * dz) - r;
                    float v = Mathf.Max(1f - Mathf.Abs(dd) / 0.55f, 0f) + Mathf.Max(1f - Mathf.Max(dd, 0f) / 2.2f, 0f) * 0.35f;
                    Put(ix, iz, 0, v * strength);
                }
            }
        }

        /// Wake segment: soft core + brighter V edges (Kelvin-wake look), width/strength interpolated along a->b.
        public void Wake(Vector2 a, Vector2 b, float wa, float wb, float sa, float sb)
        {
            float wmax = Mathf.Max(wa, wb);
            float minX = Mathf.Min(a.x, b.x) - wmax, maxX = Mathf.Max(a.x, b.x) + wmax;
            float minZ = Mathf.Min(a.y, b.y) - wmax, maxZ = Mathf.Max(a.y, b.y) + wmax;
            int ix0 = Mathf.Max(0, Mathf.FloorToInt((minX - x0) / TX)), ix1 = Mathf.Min(W - 1, Mathf.CeilToInt((maxX - x0) / TX));
            int iz0 = Mathf.Max(0, Mathf.FloorToInt((minZ - z0) / TZ)), iz1 = Mathf.Min(H - 1, Mathf.CeilToInt((maxZ - z0) / TZ));
            var ab = b - a;
            float len2 = Mathf.Max(1e-5f, ab.sqrMagnitude);
            for (int iz = iz0; iz <= iz1; iz++)
            {
                float pz = z0 + (iz + 0.5f) * TZ;
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    float px = x0 + (ix + 0.5f) * TX;
                    float t = Mathf.Clamp01(((px - a.x) * ab.x + (pz - a.y) * ab.y) / len2);
                    float qx = a.x + ab.x * t - px, qz = a.y + ab.y * t - pz;
                    float d = Mathf.Sqrt(qx * qx + qz * qz);
                    float w = wa + (wb - wa) * t, s = sa + (sb - sa) * t;
                    float core = Mathf.Max(1f - d / w, 0f) * 0.55f;
                    float edge = Mathf.Max(1f - Mathf.Abs(d - w * 0.8f) / (w * 0.22f + 0.05f), 0f);
                    Put(ix, iz, 2, Mathf.Max(core, edge) * s);
                }
            }
        }

        void Bounds(float cx, float cz, float ext, out int ix0, out int ix1, out int iz0, out int iz1)
        {
            ix0 = Mathf.Max(0, Mathf.FloorToInt((cx - ext - x0) / TX));
            ix1 = Mathf.Min(W - 1, Mathf.CeilToInt((cx + ext - x0) / TX));
            iz0 = Mathf.Max(0, Mathf.FloorToInt((cz - ext - z0) / TZ));
            iz1 = Mathf.Min(H - 1, Mathf.CeilToInt((cz + ext - z0) / TZ));
        }

        void Put(int ix, int iz, int ch, float v)
        {
            if (v <= 0f) return;
            int o = (iz * W + ix) * 4 + ch;
            byte b = v >= 1f ? (byte)255 : (byte)(v * 255f);
            if (b > pix[o]) pix[o] = b;
        }
    }
}
