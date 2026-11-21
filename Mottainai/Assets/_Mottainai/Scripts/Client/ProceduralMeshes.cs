using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Client
{
    /// Stylized low-poly meshes generated at runtime (flat-shaded, no assets needed).
    public static class ProceduralMeshes
    {
        static readonly Dictionary<string, Mesh> cache = new Dictionary<string, Mesh>();

        static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = key;
            cache[key] = m;
            return m;
        }

        /// Faceted rock: displaced icosphere. variant picks a different shape.
        public static Mesh Rock(int variant) => Cached("rock" + variant, () => Faceted(Icosphere(1), variant * 97 + 13, 0.28f, new Vector3(1f, 0.7f, 0.95f)));

        /// Faceted iceberg: taller, sharper.
        public static Mesh Iceberg(int variant) => Cached("ice" + variant, () => Faceted(Icosphere(1), variant * 53 + 7, 0.35f, new Vector3(1f, 1.15f, 1f), sharpTop: true));

        /// Inflatable tube (torus), radius R major, r minor, ellipse scaled on Z by zScale.
        public static Mesh Torus(float R, float r, float zScale, int seg = 40, int side = 14) => Cached($"torus{R}{r}{zScale}", () =>
        {
            var v = new List<Vector3>(); var n = new List<Vector3>(); var uv = new List<Vector2>(); var tri = new List<int>();
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2f / seg;
                var c = new Vector3(Mathf.Cos(a) * R, 0f, Mathf.Sin(a) * R * zScale);
                var outDir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a) * zScale).normalized;
                for (int j = 0; j <= side; j++)
                {
                    float b = j * Mathf.PI * 2f / side;
                    var d = outDir * Mathf.Cos(b) + Vector3.up * Mathf.Sin(b);
                    v.Add(c + d * r); n.Add(d); uv.Add(new Vector2((float)i / seg * 6f, (float)j / side));
                }
            }
            for (int i = 0; i < seg; i++)
                for (int j = 0; j < side; j++)
                {
                    int a0 = i * (side + 1) + j, a1 = a0 + side + 1;
                    tri.Add(a0); tri.Add(a0 + 1); tri.Add(a1);
                    tri.Add(a1); tri.Add(a0 + 1); tri.Add(a1 + 1);
                }
            var m = new Mesh();
            m.SetVertices(v); m.SetNormals(n); m.SetUVs(0, uv); m.SetTriangles(tri, 0); m.RecalculateBounds();
            return m;
        });

        /// Low-poly cone stack (pine) or blob (round tree).
        public static Mesh Pine(int variant) => Cached("pine" + variant, () =>
        {
            var parts = new List<CombineInstance>();
            var cone = VisualFactory.Cone;
            for (int k = 0; k < 3; k++)
            {
                float s = 2.2f - k * 0.55f + variant * 0.1f;
                parts.Add(new CombineInstance { mesh = cone, transform = Matrix4x4.TRS(new Vector3(0, 1.6f + k * 1.0f, 0), Quaternion.Euler(0, k * 40f, 0), new Vector3(s, 1.6f, s)) });
            }
            var m = new Mesh();
            m.CombineMeshes(parts.ToArray(), true, true);
            return FlatShade(m);
        });

        public static Mesh Blob(int variant) => Cached("blob" + variant, () => Faceted(Icosphere(1), variant * 31 + 5, 0.18f, Vector3.one));

        // ---------------- helpers ----------------
        static Mesh Icosphere(int subdiv)
        {
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized * 0.5f;
            var faces = new List<int>
            {
                0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1
            };
            var mid = new Dictionary<long, int>();
            for (int s = 0; s < subdiv; s++)
            {
                var nf = new List<int>();
                for (int i = 0; i < faces.Count; i += 3)
                {
                    int a = faces[i], b = faces[i + 1], c = faces[i + 2];
                    int ab = Mid(verts, mid, a, b), bc = Mid(verts, mid, b, c), ca = Mid(verts, mid, c, a);
                    nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                faces = nf;
            }
            var m = new Mesh();
            m.SetVertices(verts); m.SetTriangles(faces, 0);
            return m;
        }

        static int Mid(List<Vector3> v, Dictionary<long, int> cache, int a, int b)
        {
            long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
            if (cache.TryGetValue(key, out int i)) return i;
            v.Add(((v[a] + v[b]) * 0.5f).normalized * 0.5f);
            cache[key] = v.Count - 1;
            return v.Count - 1;
        }

        static Mesh Faceted(Mesh src, int seed, float amount, Vector3 scale, bool sharpTop = false)
        {
            var rng = new System.Random(seed);
            var verts = src.vertices;
            for (int i = 0; i < verts.Length; i++)
            {
                var p = verts[i];
                float d = 1f + ((float)rng.NextDouble() - 0.5f) * amount * 2f;
                if (sharpTop && p.y > 0.2f) d *= 1f + (p.y - 0.2f) * 0.8f;
                p *= d;
                p = Vector3.Scale(p, scale);
                if (p.y < -0.12f) p.y = -0.12f - (p.y + 0.12f) * 0.3f; // flatter, submerged base
                verts[i] = p;
            }
            src.vertices = verts;
            return FlatShade(src);
        }

        static Mesh FlatShade(Mesh m)
        {
            var v = m.vertices; var t = m.triangles;
            var nv = new Vector3[t.Length]; var nt = new int[t.Length];
            for (int i = 0; i < t.Length; i++) { nv[i] = v[t[i]]; nt[i] = i; }
            var r = new Mesh { indexFormat = nv.Length > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16 };
            r.vertices = nv; r.triangles = nt;
            r.RecalculateNormals(); r.RecalculateBounds();
            return r;
        }
    }
}
