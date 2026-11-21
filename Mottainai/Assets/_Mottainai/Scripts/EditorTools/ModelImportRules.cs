using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Mottainai.EditorTools
{
    /// Import settings for Assets/_Mottainai/Resources/Models/**: file scale, Generic rig, animations only where listed (looped),
    /// no cameras/lights/colliders. Size is normalized at runtime by VisualFactory, toon material applied there too.
    public class ModelImportRules : AssetPostprocessor
    {
        const string ModelsRoot = "Assets/_Mottainai/Resources/Models/";
        static readonly HashSet<string> Animated = new HashSet<string> { "shark" };

        public override uint GetVersion() => 1;

        bool InModels => assetPath.Replace('\\', '/').StartsWith(ModelsRoot);
        string Name => Path.GetFileNameWithoutExtension(assetPath).ToLowerInvariant();

        void OnPreprocessModel()
        {
            if (!InModels || !(assetImporter is ModelImporter mi)) return;
            bool anim = Animated.Contains(Name);
            mi.globalScale = 1f;
            mi.useFileScale = true;
            mi.importCameras = false;
            mi.importLights = false;
            mi.importVisibility = false;
            mi.addCollider = false;
            mi.isReadable = false;
            mi.generateSecondaryUV = false;
            mi.animationType = ModelImporterAnimationType.Generic;
            mi.avatarSetup = anim ? ModelImporterAvatarSetup.CreateFromThisModel : ModelImporterAvatarSetup.NoAvatar;
            mi.importAnimation = anim;
            mi.optimizeGameObjects = false;
        }

        void OnPreprocessAnimation()
        {
            if (!InModels || !Animated.Contains(Name) || !(assetImporter is ModelImporter mi)) return;
            var clips = mi.defaultClipAnimations;
            foreach (var c in clips) { c.loopTime = true; c.loopPose = true; }
            mi.clipAnimations = clips;
        }

        /// Paddle: long axis on Y, blade (wider half) pointing down. Raft: long axis on Z.
        void OnPostprocessModel(GameObject root)
        {
            if (!InModels) return;
            if (Name == "paddle") Align(root, Vector3.down, true);
            else if (Name == "raft") Align(root, Vector3.forward, false);
        }

        static void Align(GameObject root, Vector3 target, bool wideEnd)
        {
            var pts = new List<Vector3>();
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>())
            {
                if (mf.sharedMesh == null) continue;
                var m = mf.transform.localToWorldMatrix;
                foreach (var v in mf.sharedMesh.vertices) pts.Add(m.MultiplyPoint3x4(v));
            }
            if (pts.Count == 0) return;
            var b = new Bounds(pts[0], Vector3.zero);
            foreach (var p in pts) b.Encapsulate(p);
            int ax = b.size.x >= b.size.y && b.size.x >= b.size.z ? 0 : b.size.y >= b.size.z ? 1 : 2;
            var dir = Vector3.zero; dir[ax] = 1f;
            if (wideEnd && WidthOfHalf(pts, ax, b.center[ax], false) > WidthOfHalf(pts, ax, b.center[ax], true)) dir = -dir;
            var q = Quaternion.FromToRotation(dir, target);
            root.transform.rotation = q * root.transform.rotation;
            root.transform.position = q * root.transform.position;
        }

        static float WidthOfHalf(List<Vector3> pts, int ax, float mid, bool high)
        {
            bool any = false;
            var b = new Bounds();
            foreach (var p in pts)
            {
                if ((p[ax] >= mid) != high) continue;
                if (!any) { b = new Bounds(p, Vector3.zero); any = true; } else b.Encapsulate(p);
            }
            if (!any) return 0f;
            var s = b.size; s[ax] = 0f;
            return Mathf.Max(s.x, s.y, s.z);
        }
    }
}
