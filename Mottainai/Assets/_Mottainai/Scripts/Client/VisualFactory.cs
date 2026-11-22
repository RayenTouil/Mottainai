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

        public static Material Lit(Color c, float emission = 0f) => Lit(c, emission, 0f);

        public static Material Gloss(Color c, float gloss = 1f) => Lit(c, 0f, gloss);

        public static Material Lit(Color c, float emission, float gloss)
        {
            int k = Key(c) ^ (int)(emission * 1000) * 7919 ^ (int)(gloss * 100) * 104729;
            if (litCache.TryGetValue(k, out var m)) return m;
            toon ??= Shader.Find("Mottainai/Toon") ?? Shader.Find("Universal Render Pipeline/Simple Lit");
            m = new Material(toon) { enableInstancing = true, name = "Toon_" + k };
            m.SetColor("_BaseColor", c);
            if (m.HasProperty("_Emission")) m.SetFloat("_Emission", emission);
            if (m.HasProperty("_Gloss")) m.SetFloat("_Gloss", gloss);
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
            new[] { "Waste/toxic_propane_tank", "Waste/battery" }, // Toxic
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
            Color c = p == PowerUp.Magnet ? new Color(1f, 0.3f, 0.85f) : p == PowerUp.Turbo ? new Color(1f, 0.6f, 0.15f) : new Color(0.3f, 0.85f, 1f);
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
            Color c = WasteCatalog.Colors[(int)t];
            var model = TryWasteModel(t, root, 0.8f);
            if (model != null && t == WasteType.Toxic)
                Part(root, Triangle, Lit(new Color(1f, 0.85f, 0f), 0.6f), new Vector3(0, 1.0f, 0), Vector3.one * 0.45f, default, "billboard");
            if (model == null)
            {
                switch (t)
                {
                    case WasteType.Plastic:
                        Part(root, Capsule, Lit(c), new Vector3(0, 0.1f, 0), new Vector3(0.28f, 0.32f, 0.28f), new Vector3(0, 0, 90));
                        Part(root, Cylinder, Lit(Color.white), new Vector3(0.38f, 0.1f, 0), new Vector3(0.12f, 0.05f, 0.12f), new Vector3(0, 0, 90));
                        break;
                    case WasteType.Paper:
                        Part(root, Cube, Lit(c), new Vector3(0, 0.12f, 0), new Vector3(0.5f, 0.3f, 0.4f), new Vector3(0, 15, 0));
                        Part(root, Cube, Lit(new Color(0.75f, 0.6f, 0.4f)), new Vector3(0, 0.28f, 0), new Vector3(0.52f, 0.03f, 0.12f));
                        break;
                    case WasteType.Glass:
                        Part(root, Cylinder, Lit(c, 0.25f), new Vector3(0, 0.1f, 0), new Vector3(0.22f, 0.22f, 0.22f), new Vector3(0, 0, 90));
                        Part(root, Cylinder, Lit(c, 0.25f), new Vector3(0.3f, 0.1f, 0), new Vector3(0.09f, 0.1f, 0.09f), new Vector3(0, 0, 90));
                        break;
                    case WasteType.Organic:
                        Part(root, Capsule, Lit(c), new Vector3(0, 0.08f, 0), new Vector3(0.18f, 0.3f, 0.18f), new Vector3(0, 0, 70));
                        Part(root, Sphere, Lit(new Color(0.95f, 0.85f, 0.3f)), new Vector3(0.15f, 0.12f, 0), new Vector3(0.2f, 0.15f, 0.2f));
                        break;
                    case WasteType.Metal:
                        Part(root, Cylinder, Lit(c), new Vector3(0, 0.12f, 0), new Vector3(0.24f, 0.16f, 0.24f));
                        Part(root, Cylinder, Lit(new Color(0.85f, 0.2f, 0.2f)), new Vector3(0, 0.12f, 0), new Vector3(0.25f, 0.06f, 0.25f));
                        break;
                    case WasteType.Valuable:
                        Part(root, Cube, Lit(new Color(0.55f, 0.33f, 0.15f)), new Vector3(0, 0.15f, 0), new Vector3(0.55f, 0.32f, 0.38f));
                        Part(root, Cube, Lit(c, 1.2f), new Vector3(0, 0.33f, 0), new Vector3(0.58f, 0.08f, 0.4f));
                        Part(root, Sphere, Transparent(new Color(1f, 0.9f, 0.4f, 0.25f), 1.5f), new Vector3(0, 0.25f, 0), Vector3.one * 0.95f, default, "sparkle");
                        break;
                    case WasteType.Toxic:
                        Part(root, Cylinder, Lit(c), new Vector3(0, 0.25f, 0), new Vector3(0.5f, 0.35f, 0.5f));
                        Part(root, Cylinder, Lit(new Color(0.1f, 0.1f, 0.1f)), new Vector3(0, 0.25f, 0), new Vector3(0.52f, 0.04f, 0.52f));
                        // hazard sign billboard
                        Part(root, Triangle, Lit(new Color(1f, 0.85f, 0f), 0.6f), new Vector3(0, 0.75f, 0), Vector3.one * 0.45f, default, "billboard");
                        break;
                }
            }
            // type-colored ring so kids can read the bin color at a glance
            Part(root, Cylinder, Transparent(new Color(c.r, c.g, c.b, 0.45f)), new Vector3(0, -0.02f, 0), new Vector3(1.0f, 0.01f, 1.0f), default, "ring");
        }

        /// Red-and-white river buoy (ART_DIRECTION §3: visual variant of Rock).
        static void Buoy(Transform root)
        {
            var red = Gloss(new Color(0.9f, 0.18f, 0.16f), 0.6f); var white = Gloss(new Color(0.97f, 0.96f, 0.94f), 0.6f);
            Part(root, Sphere, red, new Vector3(0, 0.15f, 0), new Vector3(1.3f, 0.7f, 1.3f));
            Part(root, Cylinder, white, new Vector3(0, 0.6f, 0), new Vector3(0.8f, 0.28f, 0.8f));
            Part(root, Cylinder, red, new Vector3(0, 1.05f, 0), new Vector3(0.62f, 0.2f, 0.62f));
            Part(root, Cone, white, new Vector3(0, 1.25f, 0), new Vector3(0.55f, 0.55f, 0.55f));
            Part(root, Sphere, Lit(new Color(1f, 0.9f, 0.4f), 1.2f), new Vector3(0, 1.85f, 0), Vector3.one * 0.18f);
        }

        static void BuildObstacle(Transform root, ObstacleType t)
        {
            float r = ObstacleCatalog.Radius[(int)t], hl = ObstacleCatalog.HalfLen[(int)t];
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
                    if (Random.value < 0.3f) { Buoy(root); break; } // visual variant, same collider
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Rock(v), Lit(new Color(0.6f, 0.57f, 0.53f)), new Vector3(0, 0.05f, 0), new Vector3(2.1f * r, 1.5f * r, 2f * r), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Rock(v + 4), Lit(new Color(0.52f, 0.5f, 0.48f)), new Vector3(0.55f * r, 0f, -0.3f * r), new Vector3(0.9f * r, 0.7f * r, 0.9f * r));
                    Part(root, ProceduralMeshes.Blob(v + 2), Lit(new Color(0.42f, 0.7f, 0.3f)), new Vector3(0.05f, 0.95f * r, 0f), new Vector3(1.3f * r, 0.32f * r, 1.2f * r)); // grassy cap
                    Part(root, ProceduralMeshes.Blob(v + 6), Lit(new Color(0.52f, 0.78f, 0.34f)), new Vector3(-0.3f * r, 1.05f * r, 0.25f * r), new Vector3(0.6f * r, 0.3f * r, 0.6f * r));
                    break;
                }
                case ObstacleType.BigRock:
                {
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Rock(v + 8), Lit(new Color(0.44f, 0.43f, 0.46f)), new Vector3(0, 0.25f, 0), new Vector3(2.1f * r, 1.7f * r, 2f * r), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Blob(v), Lit(new Color(0.33f, 0.55f, 0.28f)), new Vector3(0.2f, 1.55f * r * 0.8f, 0.1f), new Vector3(1.6f, 0.55f, 1.4f));
                    Part(root, ProceduralMeshes.Rock(v + 12), Lit(new Color(0.5f, 0.49f, 0.5f)), new Vector3(-0.8f * r, 0f, 0.5f * r), new Vector3(0.8f * r, 0.6f * r, 0.8f * r));
                    break;
                }
                case ObstacleType.Iceberg:
                {
                    int v = Random.Range(0, 4);
                    Part(root, ProceduralMeshes.Iceberg(v), Lit(new Color(0.9f, 0.97f, 1f), 0.25f), new Vector3(0, 0.3f, 0), new Vector3(2.4f, 2.4f, 2.4f), new Vector3(0, Random.Range(0, 360), 0));
                    Part(root, ProceduralMeshes.Iceberg(v + 4), Lit(new Color(0.7f, 0.88f, 1f), 0.3f), new Vector3(0.7f, 0.1f, 0.4f), new Vector3(1.2f, 1.3f, 1.2f));
                    break;
                }
                case ObstacleType.Wreck:
                    Part(root, Capsule, Lit(new Color(0.45f, 0.28f, 0.15f)), new Vector3(0, 0.2f, 0), new Vector3(2f * r, (hl + r), 1.6f * r), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(new Color(0.35f, 0.22f, 0.12f)), new Vector3(0.4f, 1.3f, 0), new Vector3(0.12f, 1.1f, 0.12f), new Vector3(0, 0, 12));
                    Part(root, Cube, Lit(new Color(0.85f, 0.85f, 0.75f)), new Vector3(0.6f, 1.6f, 0), new Vector3(0.05f, 0.8f, 0.7f), new Vector3(0, 0, 12));
                    break;
                case ObstacleType.Log:
                    Part(root, Cylinder, Lit(new Color(0.55f, 0.36f, 0.19f)), new Vector3(0, 0.05f, 0), new Vector3(2f * r, hl + r, 2f * r), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(new Color(0.9f, 0.75f, 0.52f)), new Vector3(hl + r + 0.01f, 0.05f, 0), new Vector3(1.85f * r, 0.02f, 1.85f * r), new Vector3(0, 0, 90)); // cut ends
                    Part(root, Cylinder, Lit(new Color(0.9f, 0.75f, 0.52f)), new Vector3(-hl - r - 0.01f, 0.05f, 0), new Vector3(1.85f * r, 0.02f, 1.85f * r), new Vector3(0, 0, 90));
                    Part(root, Cylinder, Lit(new Color(0.48f, 0.31f, 0.16f)), new Vector3(0.4f, 0.35f, 0.1f), new Vector3(0.16f, 0.35f, 0.16f), new Vector3(20f, 0, -30f)); // branch stub
                    break;
                case ObstacleType.Shark:
                    var body = new GameObject("body").transform;
                    body.SetParent(root, false);
                    Part(body, Capsule, Lit(new Color(0.45f, 0.5f, 0.58f)), new Vector3(0, -0.05f, 0), new Vector3(0.9f, 1.1f, 0.9f), new Vector3(90, 0, 0));
                    Part(body, Cone, Lit(new Color(0.45f, 0.5f, 0.58f)), new Vector3(0, -0.05f, -1.0f), new Vector3(0.8f, 0.7f, 0.3f), new Vector3(-90, 0, 0));
                    Part(body, Sphere, Lit(Color.white), new Vector3(0, -0.2f, 0.5f), new Vector3(0.6f, 0.2f, 0.4f));
                    Part(body, Sphere, Lit(new Color(1f, 0.15f, 0.1f), 1.2f), new Vector3(0.28f, 0.12f, 0.75f), Vector3.one * 0.1f);
                    Part(body, Sphere, Lit(new Color(1f, 0.15f, 0.1f), 1.2f), new Vector3(-0.28f, 0.12f, 0.75f), Vector3.one * 0.1f);
                    Part(body, Cube, Lit(Color.white, 0.4f), new Vector3(0, -0.12f, 0.95f), new Vector3(0.45f, 0.05f, 0.06f));
                    var fin = Part(root, Cone, Lit(new Color(0.35f, 0.4f, 0.48f)), new Vector3(0, 0.25f, 0), new Vector3(0.12f, 0.75f, 0.6f), default, "fin");
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