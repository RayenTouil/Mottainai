using System.Collections.Generic;
using Mottainai.Gameplay;
using Mottainai.Net;
using UnityEngine;

namespace Mottainai.Client
{
    /// Renders server snapshots with an adaptive interpolation buffer. Pure view: never simulates gameplay.
    public class WorldView : MonoBehaviour
    {
        const int MaxIds = 4096, Ring = 6;

        struct Sample { public uint tick; public float x, y, z, yaw; }

        class Slot
        {
            public readonly Sample[] s = new Sample[Ring];
            public int head, count;
            public byte code, flags;
            public uint lastTick;
            public GameObject go;
            public Transform fin, body;
            public bool active;
            public int listIndex;
        }

        readonly Slot[] slots = new Slot[MaxIds];
        readonly List<int> active = new List<int>(512);
        readonly Dictionary<byte, Stack<GameObject>> pool = new Dictionary<byte, Stack<GameObject>>();

        struct RaftSample { public uint tick; public float x, z, heading, shield; }
        readonly RaftSample[] raft = new RaftSample[16];
        int raftHead, raftCount;
        public Vector3 RaftPos { get; private set; }
        public float RaftHeading { get; private set; }
        public float ShieldLeft { get; private set; }
        public uint LatestTick { get; private set; }

        // clock sync
        readonly float[] offsets = new float[120];
        readonly float[] sortBuf = new float[120];
        int offHead, offCount, offSinceSort;
        double baseOffset, smoothOffset;
        bool clockInit;
        float delayTicks = 2f, jitterTicks;
        public float InterpMs => delayTicks / NetConst.TickRate * 1000f;
        public float JitterMs => jitterTicks / NetConst.TickRate * 1000f;
        public double RenderTick { get; private set; }

        public RaftView raftView;
        public Camera cam;
        Transform camT;
        Vector3 camVel;
        WaterView water;
        public FxSystem fx;
        public readonly Vector2[] flow = new Vector2[16];

        public static WorldView Create(ContentService content)
        {
            var go = new GameObject("World");
            var w = go.AddComponent<WorldView>();
            w.Build(content);
            return w;
        }

        void Build(ContentService content)
        {
            for (int i = 0; i < MaxIds; i++) slots[i] = new Slot();
            cam = Camera.main;
            if (cam == null)
            {
                var cgo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = cgo.AddComponent<Camera>();
                cgo.AddComponent<AudioListener>();
            }
            cam.transform.SetParent(transform, false);
            camT = cam.transform;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.62f, 0.84f, 0.95f);
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 140f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = cam.backgroundColor;
            RenderSettings.fogStartDistance = 55f;
            RenderSettings.fogEndDistance = 125f;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.6f, 0.65f);

            var lightGo = new GameObject("Sun");
            lightGo.transform.SetParent(transform, false);
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            var l = lightGo.AddComponent<Light>();
            l.type = LightType.Directional;
            l.intensity = 1.15f;
            l.color = new Color(1f, 0.97f, 0.9f);
            l.shadows = LightShadows.None;

            water = WaterView.Create(transform, content);
            raftView = RaftView.Create(transform, content);
            fx = new GameObject("Fx").AddComponent<FxSystem>();
            fx.transform.SetParent(transform, false);
        }

        public void ResetWorld()
        {
            for (int i = active.Count - 1; i >= 0; i--) Despawn(active[i]);
            active.Clear();
            raftCount = 0; raftHead = 0;
            offCount = 0; offHead = 0; clockInit = false;
            LatestTick = 0;
        }

        // ---------------- snapshot intake ----------------
        public void OnSnapshot(SnapshotFrame f)
        {
            double nowTicks = Time.unscaledTimeAsDouble * NetConst.TickRate;
            if (f.tick > LatestTick) LatestTick = f.tick;
            if (f.hasRaft)
            {
                float o = (float)(f.tick - nowTicks);
                offsets[offHead] = o;
                offHead = (offHead + 1) % offsets.Length;
                offCount = Mathf.Min(offCount + 1, offsets.Length);
                double mx = double.MinValue;
                for (int i = 0; i < offCount; i++) if (offsets[i] > mx) mx = offsets[i];
                baseOffset = mx;
                if (!clockInit) { smoothOffset = baseOffset; clockInit = true; }
                if (++offSinceSort >= 15)
                {
                    offSinceSort = 0;
                    System.Array.Copy(offsets, sortBuf, offCount);
                    System.Array.Sort(sortBuf, 0, offCount);
                    float p5 = sortBuf[Mathf.Clamp(Mathf.FloorToInt(offCount * 0.05f), 0, offCount - 1)];
                    jitterTicks = (float)(baseOffset - p5);
                }
                raft[raftHead] = new RaftSample { tick = f.tick, x = f.raft.x, z = f.raft.z, heading = f.raft.heading, shield = f.raft.shieldLeft };
                raftHead = (raftHead + 1) % raft.Length;
                raftCount = Mathf.Min(raftCount + 1, raft.Length);
                for (int i = 0; i < f.flowCount && i < flow.Length; i++) flow[i] = f.flow[i];
            }

            for (int i = 0; i < f.entityCount; i++)
            {
                ref var e = ref f.entities[i];
                if (e.id >= MaxIds) continue;
                var s = slots[e.id];
                if (!s.active || s.code != e.code) Spawn(e.id, e.code);
                s.s[s.head] = new Sample { tick = f.tick, x = e.x, y = e.y, z = e.z, yaw = e.yaw };
                s.head = (s.head + 1) % Ring;
                s.count = Mathf.Min(s.count + 1, Ring);
                s.flags = e.flags;
                s.lastTick = f.tick;
            }
        }

        void Spawn(int id, byte code)
        {
            var s = slots[id];
            if (s.active) Despawn(id);
            if (!pool.TryGetValue(code, out var stack)) pool[code] = stack = new Stack<GameObject>();
            s.go = stack.Count > 0 ? stack.Pop() : VisualFactory.Create(code);
            s.go.transform.SetParent(transform, false);
            s.go.SetActive(true);
            s.code = code;
            s.count = 0; s.head = 0;
            s.fin = s.go.transform.Find("fin");
            s.body = s.go.transform.Find("body");
            s.active = true;
            s.listIndex = active.Count;
            active.Add(id);
        }

        void Despawn(int id)
        {
            var s = slots[id];
            if (!s.active) return;
            s.active = false;
            s.go.SetActive(false);
            pool[s.code].Push(s.go);
            s.go = null;
            int last = active[active.Count - 1];
            active[s.listIndex] = last;
            slots[last].listIndex = s.listIndex;
            active.RemoveAt(active.Count - 1);
        }

        /// Immediately hide an entity (caught by net).
        public void Hide(ushort id) { if (id < MaxIds && slots[id].active) Despawn(id); }

        // ---------------- render ----------------
        void LateUpdate()
        {
            if (!clockInit) return;
            double nowTicks = Time.unscaledTimeAsDouble * NetConst.TickRate;
            double diff = baseOffset - smoothOffset;
            smoothOffset = System.Math.Abs(diff) > 12 ? baseOffset : smoothOffset + diff * Mathf.Min(1f, Time.unscaledDeltaTime * 2f);
            float target = Mathf.Max(1f, jitterTicks) + 0.35f;
            delayTicks = Mathf.Lerp(delayTicks, target, Mathf.Min(1f, Time.unscaledDeltaTime * 1.5f));
            RenderTick = nowTicks + smoothOffset - delayTicks;

            UpdateRaft();
            for (int k = active.Count - 1; k >= 0; k--)
            {
                int id = active[k];
                var s = slots[id];
                if (LatestTick > s.lastTick + 20) { Despawn(id); continue; }
                Interp(s, RenderTick, out var p, out float yaw);
                var t = s.go.transform;
                t.localPosition = p;
                t.localRotation = Quaternion.Euler(0f, yaw, 0f);
                if (EntityCode.Kind(s.code) == EntityKind.Obstacle && EntityCode.Sub(s.code) == (byte)ObstacleType.Shark)
                {
                    if (s.body != null) s.body.gameObject.SetActive(s.flags == 2);
                    if (s.fin != null) s.fin.gameObject.SetActive(s.flags >= 1);
                }
            }
            UpdateCamera();
            water.Follow(RaftPos, DirtPoints());
        }

        // provides waste positions to the water dirt tint without allocations
        readonly List<Vector4> dirt = new List<Vector4>(16);
        List<Vector4> DirtPoints()
        {
            dirt.Clear();
            for (int k = 0; k < active.Count && dirt.Count < 16; k++)
            {
                var s = slots[active[k]];
                if (EntityCode.Kind(s.code) != EntityKind.Waste) continue;
                var p = s.go.transform.localPosition;
                if (Mathf.Abs(p.z - RaftPos.z) > 35f) continue;
                dirt.Add(new Vector4(p.x, p.z, EntityCode.Sub(s.code) == (byte)WasteType.Toxic ? 4.5f : 3f, 1f));
            }
            return dirt;
        }

        static void Interp(Slot s, double rt, out Vector3 p, out float yaw)
        {
            // find newest sample <= rt and the one after
            int newest = (s.head - 1 + Ring) % Ring;
            Sample a = s.s[newest], b = a;
            bool found = false;
            for (int i = 0; i < s.count; i++)
            {
                int idx = (s.head - 1 - i + Ring * 2) % Ring;
                var smp = s.s[idx];
                if (smp.tick <= rt) { a = smp; found = true; break; }
                b = smp;
            }
            if (!found) a = b = s.s[(s.head - s.count + Ring * 2) % Ring];
            float t = b.tick > a.tick ? Mathf.Clamp01((float)((rt - a.tick) / (b.tick - a.tick))) : 0f;
            p = new Vector3(Mathf.Lerp(a.x, b.x, t), Mathf.Lerp(a.y, b.y, t), Mathf.Lerp(a.z, b.z, t));
            yaw = Mathf.LerpAngle(a.yaw, b.yaw, t);
        }

        void UpdateRaft()
        {
            if (raftCount == 0) return;
            int newest = (raftHead - 1 + raft.Length) % raft.Length;
            RaftSample a = raft[newest], b = a;
            bool found = false;
            for (int i = 0; i < raftCount; i++)
            {
                int idx = (raftHead - 1 - i + raft.Length * 2) % raft.Length;
                if (raft[idx].tick <= RenderTick) { a = raft[idx]; found = true; break; }
                b = raft[idx];
            }
            float t;
            if (!found) { a = b; t = 0f; }
            else if (b.tick > a.tick) t = Mathf.Clamp01((float)((RenderTick - a.tick) / (b.tick - a.tick)));
            else
            {
                // extrapolate briefly if starved (max 4 ticks)
                int prevIdx = (raftHead - 2 + raft.Length * 2) % raft.Length;
                var prev = raft[prevIdx];
                float ahead = Mathf.Min(4f, (float)(RenderTick - a.tick));
                if (raftCount > 1 && a.tick > prev.tick && ahead > 0f)
                {
                    float k = ahead / (a.tick - prev.tick);
                    RaftPos = new Vector3(a.x + (a.x - prev.x) * k, 0f, a.z + (a.z - prev.z) * k);
                    RaftHeading = a.heading;
                    ShieldLeft = a.shield;
                    raftView.SetPose(RaftPos, RaftHeading);
                    return;
                }
                t = 0f;
            }
            RaftPos = new Vector3(Mathf.Lerp(a.x, b.x, t), 0f, Mathf.Lerp(a.z, b.z, t));
            RaftHeading = Mathf.Lerp(a.heading, b.heading, t);
            ShieldLeft = Mathf.Lerp(a.shield, b.shield, t);
            raftView.SetPose(RaftPos, RaftHeading);
            raftView.SetShield(ShieldLeft, GameTuning.Current.shieldBlinkTime);
        }

        void UpdateCamera()
        {
            float h = RaftHeading * 0.35f;
            var fwd = new Vector3(Mathf.Sin(h), 0f, Mathf.Cos(h));
            var targetPos = RaftPos - fwd * 9f + Vector3.up * 6.3f;
            camT.position = Vector3.SmoothDamp(camT.position, targetPos, ref camVel, 0.18f);
            var look = RaftPos + fwd * 5f + Vector3.up * 0.5f;
            camT.rotation = Quaternion.Slerp(camT.rotation, Quaternion.LookRotation(look - camT.position), Mathf.Min(1f, Time.deltaTime * 8f));
        }

        public void SnapCamera()
        {
            camT.position = RaftPos + new Vector3(0, 6.3f, -9f);
            camT.rotation = Quaternion.LookRotation(RaftPos + new Vector3(0, 0.5f, 5f) - camT.position);
        }

        /// Screen-space generous picking of floating waste for the collector.
        public bool PickWaste(Vector2 screen, out ushort id, out Vector3 world)
        {
            id = NetConst.NoEntity; world = default;
            float best = float.MaxValue;
            float minPx = Screen.height * 0.07f;
            for (int k = 0; k < active.Count; k++)
            {
                var s = slots[active[k]];
                if (EntityCode.Kind(s.code) != EntityKind.Waste) continue;
                var p = s.go.transform.position;
                var sp = cam.WorldToScreenPoint(p + Vector3.up * 0.15f);
                if (sp.z <= 0f) continue;
                var edge = cam.WorldToScreenPoint(p + camT.right * WasteCatalog.Radius[EntityCode.Sub(s.code)] * GameTuning.Current.hitRadiusScale * 1.6f);
                float radius = Mathf.Max(minPx, Vector2.Distance(sp, edge));
                float d = Vector2.Distance(sp, screen);
                if (d < radius && d < best) { best = d; id = (ushort)active[k]; world = p; }
            }
            return id != NetConst.NoEntity;
        }

        public bool ScreenToWater(Vector2 screen, out Vector3 world)
        {
            var ray = cam.ScreenPointToRay(screen);
            world = default;
            if (Mathf.Abs(ray.direction.y) < 1e-4f) return false;
            float t = -ray.origin.y / ray.direction.y;
            if (t <= 0f) return false;
            world = ray.origin + ray.direction * t;
            return true;
        }

        public Vector3 CameraFlatForward { get { var f = camT.forward; f.y = 0; return f.sqrMagnitude > 0 ? f.normalized : Vector3.forward; } }
        public Vector3 CameraFlatRight { get { var r = camT.right; r.y = 0; return r.sqrMagnitude > 0 ? r.normalized : Vector3.right; } }

        public Vector3 EntityPosition(ushort id, Vector3 fallback) => id < MaxIds && slots[id].active ? slots[id].go.transform.position : fallback;
    }
}
