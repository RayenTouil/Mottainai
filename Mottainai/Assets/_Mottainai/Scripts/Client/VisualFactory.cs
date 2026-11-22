using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using UnityEngine.Rendering;

namespace Mottainai.Client
{
    /// Builds visuals: CC0 model from Resources/Models if present, else colored primitives (no colliders).
    public static class VisualFactory
    {
        static Shader toon, trans;
        static readonly Dictionary<int, Material> litCache = new Dictionary<int, Material>();
        static readonly Dictionary<int, Material> transCache = new Dictionary<int, Material>();
        static Mesh cube, sphere, cylinder, capsule, quad, cone, triangle;

        public static Mesh Cube => cube ??= PrimMesh(PrimitiveType.Cube);
        public static Mesh Sphere => sphere ??= PrimMesh(PrimitiveType.Sphere);
        public static Mesh Cylinder => cylinder ??= PrimMesh(PrimitiveType.Cylinder);
        public static Mesh Capsule => capsule ??= PrimMesh(PrimitiveType.Capsule);
        public static Mesh Quad => quad ??= PrimMesh(PrimitiveType.Quad);
        public static Mesh Cone => cone ??= BuildCone(20);
        public static Mesh Triangle => triangle ??= BuildTriangle();

        static Mesh PrimMesh(PrimitiveType t)
        {
            var go = GameObject.CreatePrimitive(t);
            var m = go.GetComponent<MeshFilter>().sharedMesh;
            Object.Destroy(go);
            return m;
        }

        static Mesh BuildCone(int seg)
        {
            var v = new List<Vector3>(); var tris = new List<int>();
            v.Add(new Vector3(0, 1, 0)); v.Add(Vector3.zero);
            for (int i = 0; i <= seg; i++)
            {
                float a = i * Mathf.PI * 2 / seg;
                v.Add(new Vector3(Mathf.Cos(a) * 0.5f, 0, Mathf.Sin(a) * 0.5f));
            }
            for (int i = 0; i < seg; i++)
            {
                tris.Add(0); tris.Add(i + 3); tris.Add(i + 2);
                tris.Add(1); tris.Add(i + 2); tris.Add(i + 3);
            }
            var m = new Mesh { name = "Cone" };
            m.SetVertices(v); m.SetTriangles(tris, 0); m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static Mesh BuildTriangle()
        {
            var m = new Mesh { name = "Tri" };
            m.vertices = new[] { new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 0.87f, 0), new Vector3(-0.5f, 0, 0), new Vector3(0.5f, 0, 0), new Vector3(0, 0.87f, 0) };
            m.triangles = new[] { 0, 2, 1, 3, 4, 5 };
            m.RecalculateNormals(); m.RecalculateBounds();
            return m;
        }

        static int Key(Color c) => ((int)(c.r * 255) << 24) ^ ((int)(c.g * 255) << 16) ^ ((int)(c.b * 255) << 8) ^ (int)(c.a * 255);

        public static Material Lit(Color c, float emission = 0f)
        {
            int k = Key(c) ^ (int)(emission * 1000) * 7919;
            if (litCache.TryGetValue(k, out var m)) return m;
            toon ??= Shader.Find("Mottainai/Toon") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            m = new Material(toon) { enableInstancing = true, name = "Toon_" + k };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Emission")) m.SetFloat("_Emission", emission);
            litCache[k] = m;
            return m;
        }

        public static Material Transparent(Color c, float fresnel = 0f)
        {
            int k = Key(c) ^ (int)(fresnel * 100) * 31;
            if (transCache.TryGetValue(k, out var m)) return m;
            trans ??= Shader.Find("Mottainai/Transparent");
            m = new Material(trans) { enableInstancing = true, name = "Trans_" + k };
            m.SetColor("_BaseColor", c);
            m.SetFloat("_Fresnel", fresnel);
            transCache[k] = m;
            return m;
        }

        public static GameObject Part(Transform parent, Mesh mesh, Material mat, Vector3 pos, Vector3 scale, Vector3 euler = default, string name = "part")
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localScale = scale;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return go;
        }

        static readonly Dictionary<string, GameObject> prefabCache = new Dictionary<string, GameObject>();
        static readonly Dictionary<Material, Material> toonOf = new Dictionary<Material, Material>();
        static readonly Dictionary<string, AnimationClip> clipCache = new Dictionary<string, AnimationClip>();

        static GameObject LoadPrefab(string path)
        {
            if (!prefabCache.TryGetValue(path, out var p)) prefabCache[path] = p = Resources.Load<GameObject>("Models/" + path);
            return p;
        }

        /// Toon copy of an imported material (URP Lit, Standard or glTFast), keeping its color and texture.
        static Material ToonFrom(Material src)
        {
            if (src == null) return Lit(Color.white);
            if (toonOf.TryGetValue(src, out var m)) return m;
            Color c = src.HasProperty("_BaseColor") ? src.GetColor("_BaseColor")
                : src.HasProperty("baseColorFactor") ? src.GetColor("baseColorFactor")
                : src.HasProperty("_Color") ? src.GetColor("_Color") : Color.white;
            Texture tex = null;
            foreach (var p in new[] { "_BaseMap", "baseColorTexture", "_MainTex" })
                if (tex == null && src.HasProperty(p)) tex = src.GetTexture(p);
            if (tex == null) m = Lit(c);
            else
            {
                toon ??= Shader.Find("Mottainai/Toon") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
                m = new Material(toon) { enableInstancing = true, name = "Toon_" + src.name };
                m.SetColor("_BaseColor", c);
                m.SetTexture("_BaseMap", tex);
            }
            toonOf[src] = m;
            return m;
        }

        static AnimationClip LoopClip(string path)
        {
            if (clipCache.TryGetValue(path, out var clip)) return clip;
            foreach (var c in Resources.LoadAll<AnimationClip>("Models/" + path))
            {
                if (c.name.StartsWith("__preview__")) continue;
                if (clip == null || c.name.ToLowerInvariant().Contains("swim")) clip = c;
            }
            return clipCache[path] = clip;
        }

        /// CC0 model from Resources/Models/<path>: toon materials, no colliders/shadows, fitted so its largest side = targetSize.
        static GameObject TryModel(string path, Transform parent, float targetSize, bool center = false, bool animate = false)
        {
            var prefab = LoadPrefab(path);
            if (prefab == null) return null;
            var go = Object.Instantiate(prefab, parent, false);
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.Destroy(c);
            var rs = go.GetComponentsInChildren<Renderer>();
            foreach (var r in rs)
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = ToonFrom(mats[i]);
                r.sharedMaterials = mats;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            var anim = go.GetComponentInChildren<Animator>();
            var clip = animate && anim != null ? LoopClip(path) : null;
            if (clip != null) go.AddComponent<ModelLoop>().Play(anim, clip);
            else foreach (var a in go.GetComponentsInChildren<Animator>()) Object.Destroy(a);
            if (rs.Length > 0)
            {
                var b = WorldBounds(rs);
                float s = Mathf.Max(b.size.x, b.size.y, b.size.z);
                if (s > 0.001f) go.transform.localScale *= targetSize / s;
                if (center) go.transform.position += parent.position - WorldBounds(rs).center;
            }
            return go;
        }

        static Bounds WorldBounds(Renderer[] rs)
        {
            var b = rs[0].bounds;
            foreach (var r in rs) b.Encapsulate(r.bounds);
            return b;
        }

        /// Model centered in a new child pivot (fitted while unrotated, then placed), or null if the model is missing.
        public static Transform Model(string path, Transform parent, float size, Vector3 pos, Vector3 euler, string name = "model", bool animate = false, bool longAxisZ = false)
        {
            if (LoadPrefab(path) == null) return null;
            var pivot = new GameObject(name).transform;
            pivot.SetParent(parent, false);
            var go = TryModel(path, pivot, size, true, animate);
            var rot = Quaternion.Euler(euler);
            if (longAxisZ)
            {
                var rs = go.GetComponentsInChildren<Renderer>();
                if (rs.Length > 0) { var b = WorldBounds(rs); if (b.size.x > b.size.z) rot *= Quaternion.Euler(0, 90, 0); }
            }
            pivot.localPosition = pos;
            pivot.localRotation = rot;
            return pivot;
        }

        static readonly string[][] WasteModels =
        {
            new[] { "Waste/plastic_bottle" },                     // Plastic
            null,                                                 // Paper
            new[] { "Waste/glass_bottle" },                       // Glass
            null,                                                 // Organic
            new[] { "Waste/metal_can", "Waste/metal_can_broken" },  // Metal
            null,                                                 // Valuable
            null,                                                 // Toxic: v3 red barrel (procedural)
        };

        static GameObject TryWasteModel(WasteType t, Transform root, float size)
        {
            var opts = WasteModels[(int)t];
            if (opts != null)
            {
                int start = Random.Range(0, opts.Length);
                for (int i = 0; i < opts.Length; i++)
                {
                    var go = TryModel(opts[(start + i) % opts.Length], root, size);
                    if (go != null) return go;
                }
            }
            return TryModel("waste_" + t.ToString().ToLowerInvariant(), root, size);
        }

        /// Root object for an entity code; child 0 = body, optional child "fin" for sharks.
        public static GameObject Create(byte code)
        {
            var kind = EntityCode.Kind(code);
            byte sub = EntityCode.Sub(code);
            var root = new GameObject($"E_{kind}_{sub}").transform;
            switch (kind)
            {
                case EntityKind.Waste: BuildWaste(root, (WasteType)sub); break;
                case EntityKind.Obstacle: BuildObstacle(root, (ObstacleType)sub); break;
                case EntityKind.Shield: BuildPowerUp(root, (PowerUp)sub); break;
            }
            return root.gameObject;
        }

        /// Floating power-up bubble: Shield (cyan diamond), Magnet (magenta U-magnet), Turbo (orange chevrons).
        static void BuildPowerUp(Transform root, PowerUp p)
        {
            Color c = p == PowerUp.Magnet ? ArtV3.NetBadge : p == PowerUp.Turbo ? ArtV3.HazardOrange : ArtV3.Hex("#7FE9FF");
            Part(root, Sphere, Transparent(new Color(c.r, c.g, c.b, 0.33f), 2f), Vector3.zero, Vector3.one * 1.35f, default, "bubble");
            switch (p)
            {
                case PowerUp.Magnet:
                    Part(root, Cube, Lit(c, 0.7f), new Vector3(-0.18f, 0.02f, 0), new Vector3(0.12f, 0.42f, 0.12f));
                    Part(root, Cube, Lit(c, 0.7f), new Vector3(0.18f, 0.02f, 0), new Vector3(0.12f, 0.42f, 0.12f));
                    Part(root, Cube, Lit(c, 0.7f), new Vector3(0f, -0.17f, 0), new Vector3(0.48f, 0.12f, 0.12f));
                    Part(root, Cube, Lit(Color.white, 0.8f), new Vector3(-0.18f, 0.26f, 0), new Vector3(0.13f, 0.1f, 0.13f));
                    Part(root, Cube, Lit(Color.white, 0.8f), new Vector3(0.18f, 0.26f, 0), new Vector3(0.13f, 0.1f, 0.13f));
                    break;
                case PowerUp.Turbo:
                    Part(root, Triangle, Lit(c, 0.8f), new Vector3(0, 0.12f, 0), new Vector3(0.5f, 0.35f, 1f), new Vector3(0, 0, 180));
                    Part(root, Triangle, Lit(Color.white, 0.8f), new Vector3(0, -0.1f, 0), new Vector3(0.5f, 0.35f, 1f), new Vector3(0, 0, 180));
                    break;
                default:
                    if (Model("Pickups/first_aid_kit", root, 0.6f, Vector3.zero, Vector3.zero, "core") == null)
                        Part(root, Cube, Lit(c, 0.8f), Vector3.zero, new Vector3(0.45f, 0.55f, 0.12f), new Vector3(0, 0, 45), "core");
                    break;
            }
        }

        static void BuildWaste(Transform root, WasteType t)
        {
            // v3 §8: chunky silhouette x1.5 (visual only) on a thick category ring with glow; bob/drift/pulse via WasteFloat
            Color c = WasteCatalog.Colors[(int)t];
            var vis = new GameObject("vis").transform;
            vis.SetParent(root, false);
            var body = new GameObject("body3d").transform;
            body.SetParent(vis, false);
            body.localScale = Vector3.one * 1.5f;
            var fl = root.gameObject.AddComponent<WasteFloat>();
            fl.vis = vis;
            GameObject model = t == WasteType.Plastic || t == WasteType.Glass || t == WasteType.Metal ? TryWasteModel(t, body, 0.8f) : null;
            if (model == null)
            {
                switch (t)
                {
                    case WasteType.Plastic:
                        Part(body, Capsule, Lit(ArtV3.Hex("#BFE3FF"), 0.6f), new Vector3(0, 0.1f, 0), new Vector3(0.28f, 0.32f, 0.28f), new Vector3(0, 0, 90));
                        Part(body, Cylinder, Lit(Color.white), new Vector3(0.38f, 0.1f, 0), new Vector3(0.12f, 0.05f, 0.12f), new Vector3(0, 0, 90));
                        break;
                    case WasteType.Paper: // milk carton: white + blue panel
                        Part(body, Cube, Lit(Color.white), new Vector3(0, 0.16f, 0), new Vector3(0.3f, 0.36f, 0.3f));
                        Part(body, Cube, Lit(c), new Vector3(0, 0.14f, 0.152f), new Vector3(0.26f, 0.2f, 0.01f));
                        Part(body, Cube, Lit(c), new Vector3(0, 0.14f, -0.152f), new Vector3(0.26f, 0.2f, 0.01f));
                        Part(body, Triangle, Lit(Color.white), new Vector3(0, 0.4f, 0), new Vector3(0.3f, 0.14f, 0.3f));
                        fl.tilt = 5f;
                        break;
                    case WasteType.Glass:
                        Part(body, Cylinder, Lit(ArtV3.Hex("#3FBF5F"), 0.6f), new Vector3(0, 0.1f, 0), new Vector3(0.22f, 0.22f, 0.22f), new Vector3(0, 0, 90));
                        Part(body, Cylinder, Lit(ArtV3.Hex("#3FBF5F"), 0.6f), new Vector3(0.3f, 0.1f, 0), new Vector3(0.09f, 0.1f, 0.09f), new Vector3(0, 0, 90));
                        break;
                    case WasteType.Organic: // apple core
                        Part(body, Sphere, Lit(ArtV3.Hex("#E8C9A0")), new Vector3(0, 0.13f, 0), new Vector3(0.2f, 0.26f, 0.2f));
                        Part(body, Sphere, Lit(new Color(0.85f, 0.75f, 0.25f)), new Vector3(0, 0.27f, 0), new Vector3(0.26f, 0.1f, 0.26f));
                        Part(body, Sphere, Lit(new Color(0.85f, 0.75f, 0.25f)), new Vector3(0, 0.01f, 0), new Vector3(0.26f, 0.1f, 0.26f));
                        Part(body, Cylinder, Lit(c), new Vector3(0, 0.36f, 0), new Vector3(0.03f, 0.06f, 0.03f));
                        break;
                    case WasteType.Metal: // tin can with darker rims
                        Part(body, Cylinder, Lit(ArtV3.Hex("#B8C0C8"), 0.9f), new Vector3(0, 0.12f, 0), new Vector3(0.24f, 0.16f, 0.24f));
                        Part(body, Cylinder, Lit(ArtV3.RockSide, 0.9f), new Vector3(0, 0.28f, 0), new Vector3(0.25f, 0.01f, 0.25f));
                        Part(body, Cylinder, Lit(ArtV3.RockSide, 0.9f), new Vector3(0, -0.04f, 0), new Vector3(0.25f, 0.01f, 0.25f));
                        break;
                    case WasteType.Valuable: // faceted gem
                        Part(body, ArtV3.Gem(), Lit(c, 1.1f), new Vector3(0, 0.25f, 0), Vector3.one * 0.42f);
                        Part(body, ArtV3.Gem(), Lit(ArtV3.Hex("#C79BFF"), 1.3f), new Vector3(0.04f, 0.31f, 0.04f), Vector3.one * 0.2f);
                        fl.spin = 60f;
                        break;
                    case WasteType.Toxic: // red barrel, dark lid, black hazard symbol on a white plate
                        Part(body, Cylinder, Lit(c), new Vector3(0, 0.22f, 0), new Vector3(0.36f, 0.24f, 0.36f));
                        Part(body, Cylinder, Lit(new Color(0.12f, 0.12f, 0.13f)), new Vector3(0, 0.47f, 0), new Vector3(0.37f, 0.02f, 0.37f));
                        Part(body, Cylinder, Lit(new Color(0.12f, 0.12f, 0.13f)), new Vector3(0, 0.22f, 0), new Vector3(0.37f, 0.015f, 0.37f));
                        var sign = new GameObject("billboard").transform;
                        sign.SetParent(body, false);
                        sign.localPosition = new Vector3(0, 0.85f, 0);
                        Part(sign, Triangle, Lit(Color.white, 0.6f), Vector3.zero, Vector3.one * 0.42f);
                        Part(sign, Triangle, Lit(new Color(0.08f, 0.08f, 0.08f), 0.6f), new Vector3(0, -0.015f, -0.01f), Vector3.one * 0.28f);
                        break;
                }
            }
            else if (t == WasteType.Metal) fl.roll = 10f;
            if (t == WasteType.Metal && model == null) fl.roll = 10f;

            // ring: inner 0.55 / outer 0.75 x (object radius x 1.6) with object radius x1.5 visual scale
            float k = WasteCatalog.Radius[(int)t] * 1.5f * 1.6f;
            var ring = new GameObject("ring").transform;
            ring.SetParent(root, false);
            ring.localPosition = new Vector3(0, 0.14f, 0);
            ring.localScale = Vector3.one * 0.75f * k;
            Part(ring, ArtV3.Ring(0.6f, 32), Transparent(new Color(c.r, c.g, c.b, 0.35f * 0.6f)), new Vector3(0, -0.004f, 0), Vector3.one * 1.45f, default, "glow");
            if (t == WasteType.Organic || t == WasteType.Metal)
            {
                var o = t == WasteType.Organic ? ArtV3.Hex("#E8C9A0") : Color.white;
                Part(ring, ArtV3.Ring(0.69f, 32), Transparent(new Color(o.r, o.g, o.b, 0.95f)), new Vector3(0, -0.002f, 0), Vector3.one * 1.06f, default, "outline");
            }
            Part(ring, ArtV3.Ring(0.55f / 0.75f, 32), Transparent(new Color(c.r, c.g, c.b, 0.9f)), Vector3.zero, Vector3.one, default, "band");
            fl.ring = ring;
            if (t == WasteType.Toxic) fl.pulseHz = 2f;
        }

        static void BuildObstacle(Transform root, ObstacleType t)
        {
            float r = ObstacleCatalog.Radius[(int)t], hl = ObstacleCatalog.HalfLen[(int)t];
            if (t == ObstacleType.BigRock || t == ObstacleType.Iceberg || t == ObstacleType.Wreck) // v3 blob shadow, 1.2x footprint
                Part(root, Quad, ArtV3.BlobMaterial(), new Vector3(0, 0.13f, 0), new Vector3((r + hl) * 2.4f, r * 2.4f, 1f), new Vector3(90f, 90f, 0f), "blobShadow");
            if (t == ObstacleType.Shark && Model("Obstacles/shark", root, 2.4f, new Vector3(0, -0.05f, 0), Vector3.zero, "body", true, true) != null)
            {
                var sharkFin = Part(root, Cone, Lit(new Color(0.35f, 0.4f, 0.48f)), new Vector3(0, 0.25f, 0), new Vector3(0.12f, 0.75f, 0.6f), default, "fin");
                sharkFin.transform.SetAsLastSibling();
                return;
            }
            var model = TryModel("obstacle_" + t.ToString().ToLowerInvariant(), root, (r + hl) * 2f);
            if (model != null) { if (t == ObstacleType.Shark) model.name = "body"; return; }
            switch (t)
            {
                case ObstacleType.Rock:
                {
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Rock(v), Lit(ArtV3.RockTop), new Vector3(0, 0.05f, 0), new Vector3(2.1f * r, 1.5f * r, 2f * r), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Rock(v + 4), Lit(ArtV3.RockSide), new Vector3(0.55f * r, 0f, -0.3f * r), new Vector3(0.9f * r, 0.7f * r, 0.9f * r));
                    break;
                }
                case ObstacleType.BigRock:
                {
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Rock(v + 8), Lit(ArtV3.RockSide), new Vector3(0, 0.25f, 0), new Vector3(2.1f * r, 1.7f * r, 2f * r), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Blob(v), Lit(ArtV3.Grass), new Vector3(0.2f, 1.55f * r * 0.8f, 0.1f), new Vector3(1.6f, 0.55f, 1.4f));
                    Part(root, ProceduralMeshes.Rock(v + 12), Lit(ArtV3.RockTop), new Vector3(-0.8f * r, 0f, 0.5f * r), new Vector3(0.8f * r, 0.6f * r, 0.8f * r));
                    break;
                }
                case ObstacleType.Iceberg:
                {
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Iceberg(v), Lit(ArtV3.Hex("#E8F8FF"), 0.25f), new Vector3(0, 0.3f, 0), new Vector3(2.4f, 2.4f, 2.4f), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Iceberg(v + 4), Lit(ArtV3.Hex("#9FDDF2"), 0.3f), new Vector3(0.7f, 0.1f, 0.4f), new Vector3(1.2f, 1.3f, 1.2f));
                    break;
                }
                case ObstacleType.Wreck:
                    Part(root, Capsule, Lit(ArtV3.Wood), new Vector3(0, 0.2f, 0), new Vector3(2f * r, (hl + r), 1.6f * r), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(ArtV3.Bark), new Vector3(0.4f, 1.3f, 0), new Vector3(0.12f, 1.1f, 0.12f), new Vector3(0, 0, 12));
                    Part(root, Cube, Lit(new Color(0.85f, 0.85f, 0.75f)), new Vector3(0.6f, 1.6f, 0), new Vector3(0.05f, 0.8f, 0.7f), new Vector3(0, 0, 12));
                    break;
                case ObstacleType.Log:
                    Part(root, Cylinder, Lit(ArtV3.Bark), new Vector3(0, 0.05f, 0), new Vector3(2f * r, hl + r, 2f * r), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(ArtV3.Cliff), new Vector3(hl + r + 0.01f, 0.05f, 0), new Vector3(2f * r * 0.85f, 0.01f, 2f * r * 0.85f), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(ArtV3.Cliff), new Vector3(-hl - r - 0.01f, 0.05f, 0), new Vector3(2f * r * 0.85f, 0.01f, 2f * r * 0.85f), new Vector3(0, 0, 90));
                    break;
                case ObstacleType.Shark:
                    var body = new GameObject("body").transform;
                    body.SetParent(root, false);
                    Part(body, Capsule, Lit(ArtV3.Hex("#6F7D8C")), new Vector3(0, -0.05f, 0), new Vector3(0.9f, 1.1f, 0.9f), new Vector3(90, 0, 0));
                    Part(body, Cone, Lit(ArtV3.Hex("#6F7D8C")), new Vector3(0, -0.05f, -1.0f), new Vector3(0.8f, 0.7f, 0.3f), new Vector3(-90, 0, 0));
                    Part(body, Sphere, Lit(ArtV3.Hex("#EEF2F5")), new Vector3(0, -0.2f, 0.5f), new Vector3(0.6f, 0.2f, 0.4f));
                    Part(body, Sphere, Lit(ArtV3.Eyes, 1.2f), new Vector3(0.28f, 0.12f, 0.75f), Vector3.one * 0.1f);
                    Part(body, Sphere, Lit(ArtV3.Eyes, 1.2f), new Vector3(-0.28f, 0.12f, 0.75f), Vector3.one * 0.1f);
                    Part(body, Cube, Lit(Color.white, 0.4f), new Vector3(0, -0.12f, 0.95f), new Vector3(0.45f, 0.05f, 0.06f));
                    var fin = Part(root, Cone, Lit(ArtV3.Hex("#6F7D8C")), new Vector3(0, 0.25f, 0), new Vector3(0.12f, 0.75f, 0.6f), default, "fin");
                    fin.transform.SetAsLastSibling();
                    break;
            }
        }
    }
    /// Loops one clip on an imported model through a PlayableGraph (no AnimatorController asset needed).
    public class ModelLoop : MonoBehaviour
    {
        PlayableGraph graph;

        public void Play(Animator a, AnimationClip clip)
        {
            a.applyRootMotion = false;
            a.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            var p = AnimationPlayableUtilities.PlayClip(a, clip, out graph);
            p.SetTime(Random.value * clip.length);
        }

        void OnDestroy() { if (graph.IsValid()) graph.Destroy(); }
    }
}