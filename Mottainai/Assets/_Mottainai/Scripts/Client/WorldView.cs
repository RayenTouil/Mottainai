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
        public int TickRate = NetConst.TickRate;
        public int SnapshotHz = 60; // adaptive delay = max(2 snapshot intervals, jitter p95)

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
        public float InterpMs => delayTicks / TickRate * 1000f;
        public float JitterMs => jitterTicks / TickRate * 1000f;
        public float LatePct { get; private set; }
        int lateFrames, allFrames;
        public double RenderTick { get; private set; }

        public RaftView raftView;
        public Camera cam;
        Transform camT;
        Vector3 camVel;
        WaterView water;
        Scenery scenery;
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
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            SceneLook.Apply(transform, cam, out sky, out skyMat);
            postFx = PostFx.Create(transform, cam);
            Quality.Changed += ApplyQuality;
            ApplyQuality();
            highlight = VisualFactory.Part(transform, VisualFactory.Cylinder, VisualFactory.Transparent(new Color(1f, 0.95f, 0.3f, 0.55f), 1.5f), Vector3.zero, new Vector3(1.4f, 0.02f, 1.4f), default, "AimHighlight").transform;
            highlight.gameObject.SetActive(false);

            water = WaterView.Create(transform, content);
            scenery = Scenery.Create(transform);
            raftView = RaftView.Create(transform, content);
            fx = new GameObject("Fx").AddComponent<FxSystem>();
            fx.transform.SetParent(transform, false);
        }

        public void ResetWorld()
        {
            FlushFlyers();
            for (int i = active.Count - 1; i >= 0; i--) Despawn(active[i]);
            active.Clear();
            raftCount = 0; raftHead = 0;
            offCount = 0; offHead = 0; clockInit = false;
            LatestTick = 0;
            srvTick = 0; lastPredTick = 0; havePred = false; srvRunning = false; localStrokes.Clear(); predErr = Vector3.zero; predErrH = 0f;
        }

        // ---------------- snapshot intake ----------------
        public void OnSnapshot(SnapshotFrame f)
        {
            double nowTicks = Time.unscaledTimeAsDouble * TickRate;
            if (f.tick > LatestTick) LatestTick = f.tick;
            if (f.hasRaft && Time.unscaledDeltaTime < 0.1f) // ignore frame hitches (loading) in the jitter estimate
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
            }
            if (f.hasRaft && f.tick >= srvTick)
            {
                srvTick = f.tick;
                srv = f.raft.x; srvZ = f.raft.z; srvH = f.raft.heading; srvVx = f.raft.vx; srvVz = f.raft.vz; srvYaw = f.raft.yawRate;
                srvRecv = Time.unscaledTimeAsDouble;
                srvRunning = f.phase == MatchPhase.Running;
                havePred = true;
            }
            if (f.hasRaft)
            {
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

        /// Caught waste / picked shields fly into the raft (arc + shrink + spin) before returning to the pool.
        public void Collect(ushort id)
        {
            if (id >= MaxIds || !slots[id].active) return;
            var s = slots[id];
            var go = s.go;
            s.active = false;
            s.go = null;
            int last = active[active.Count - 1];
            active[s.listIndex] = last;
            slots[last].listIndex = s.listIndex;
            active.RemoveAt(active.Count - 1);
            flyers.Add(new Flyer { go = go, code = s.code, from = go.transform.position, scale = go.transform.localScale });
        }

        class Flyer { public GameObject go; public byte code; public Vector3 from, scale; public float t; }
        readonly List<Flyer> flyers = new List<Flyer>(16);

        void UpdateFlyers(float dt)
        {
            if (flyers.Count == 0) return;
            var target = raftView.transform.position + Vector3.up * 0.7f;
            for (int i = flyers.Count - 1; i >= 0; i--)
            {
                var f = flyers[i];
                f.t += dt / 0.45f;
                var tr = f.go.transform;
                if (f.t >= 1f)
                {
                    tr.localScale = f.scale;
                    f.go.SetActive(false);
                    pool[f.code].Push(f.go);
                    flyers.RemoveAt(i);
                    continue;
                }
                float k = f.t;
                var mid = (f.from + target) * 0.5f + Vector3.up * 2.2f;
                tr.position = Vector3.Lerp(Vector3.Lerp(f.from, mid, k), Vector3.Lerp(mid, target, k), k);
                tr.localScale = f.scale * Mathf.Lerp(1.15f, 0.25f, k * k);
                tr.Rotate(0f, 720f * dt, 360f * dt, Space.Self);
            }
        }

        void FlushFlyers()
        {
            foreach (var f in flyers) { f.go.transform.localScale = f.scale; f.go.SetActive(false); pool[f.code].Push(f.go); }
            flyers.Clear();
        }

        /// Immediately hide an entity (caught by net).
        public void ApplyContent(ContentService c) { water.ApplyContent(c); raftView.ApplyContent(c); }

        public void Hide(ushort id) { if (id < MaxIds && slots[id].active) Despawn(id); }

        // ---------------- render ----------------
        void LateUpdate()
        {
            if (Attract) { UpdateAttract(); return; }
            if (!clockInit) return;
            double nowTicks = Time.unscaledTimeAsDouble * TickRate;
            double diff = baseOffset - smoothOffset;
            smoothOffset = System.Math.Abs(diff) > 12 ? baseOffset : smoothOffset + diff * Mathf.Min(1f, Time.unscaledDeltaTime * 2f);
            // buffer = one snapshot interval + p95 lateness (>= 2 intervals); starved frames extrapolate instead of freezing
            float interval = (float)TickRate / Mathf.Max(1, SnapshotHz);
            float target = Mathf.Min(Mathf.Max(2f * interval, interval + jitterTicks) + 0.25f, 0.25f * TickRate);
            delayTicks = Mathf.Lerp(delayTicks, target, Mathf.Min(1f, Time.unscaledDeltaTime * (target > delayTicks ? 4f : 0.8f)));
            RenderTick = nowTicks + smoothOffset - delayTicks;
            lateFrames += RenderTick > LatestTick ? 1 : 0; allFrames++;
            if (allFrames >= 120) { LatePct = 100f * lateFrames / allFrames; lateFrames = allFrames = 0; }

            UpdateRaft();
            if (PredictOwnRaft && havePred && srvRunning) ApplyPrediction();
            else PredictionLeadMs = 0f;
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
                    if (s.body != null && s.body.gameObject.activeSelf != (s.flags == 2)) s.body.gameObject.SetActive(s.flags == 2);
                    if (s.fin != null && !s.fin.gameObject.activeSelf) s.fin.gameObject.SetActive(true); // fin always visible
                    if (s.flags == 1 && Random.value < Time.deltaTime * 7f) fx.Splash(p + Vector3.up * 0.3f, false); // attack warning
                    if (s.flags == 2 && Random.value < Time.deltaTime * 4f) fx.Splash(p, false);
                }
            }
            UpdateFlyers(Time.deltaTime);
            UpdateCamera();
            UpdateWaterFx();
            water.Follow(RaftPos);
            scenery.Follow(RaftPos);
        }

        // ---------------- own-raft prediction (hides RTT for paddlers) ----------------
        public bool PredictOwnRaft = true;
        public int Seed;
        public float RttMs, RttMinMs;
        public float PredictionLeadMs { get; private set; }
        uint srvTick;
        float srv, srvZ, srvH, srvVx, srvVz, srvYaw;
        double srvRecv;
        bool havePred, srvRunning;
        readonly RaftSim pred = new RaftSim();
        struct LocalStroke { public double t; public bool left, back; public float power, boost; public uint ackTick; }
        float magnetUntil, turboUntil;

        /// Power-up timers (local clock) for raft effects + Turbo-aware prediction.
        public void SetPowers(float magnetUntilTime, float turboUntilTime) { magnetUntil = magnetUntilTime; turboUntil = turboUntilTime; }
        bool TurboOn => Time.time < turboUntil;
        readonly List<LocalStroke> localStrokes = new List<LocalStroke>(16);
        Vector3 predErr, lastDisplayed;
        float predErrH, lastDisplayedH;
        uint lastPredTick;

        /// Register a stroke sent by this client so the raft reacts this frame instead of after a round trip.
        public void PredictStroke(bool left, float power, bool back)
        {
            localStrokes.Add(new LocalStroke { t = Time.unscaledTimeAsDouble, left = left, back = back, power = power, boost = TurboOn ? GameTuning.Current.turboImpulseScale : 1f });
            if (localStrokes.Count > 12) localStrokes.RemoveAt(0);
        }

        /// Server echoed one of our strokes (EVENTS arrive in order with snapshots): every snapshot after LatestTick contains it.
        public void AckLocalStroke()
        {
            for (int i = 0; i < localStrokes.Count; i++)
            {
                if (localStrokes[i].ackTick != 0) continue;
                var s = localStrokes[i];
                s.ackTick = System.Math.Max(1u, LatestTick);
                localStrokes[i] = s;
                return;
            }
        }

        void ApplyPrediction()
        {
            var t = GameTuning.Current;
            double now = Time.unscaledTimeAsDouble;
            // Server "now" on the tick timeline: best-arrival clock line + min one-way delay. A late snapshot no longer
            // shifts the time base (old code used its arrival time), so network jitter does not wobble the raft.
            double serverNowTicks = now * TickRate + smoothOffset + Mathf.Clamp(RttMinMs * 0.0005f, 0f, 0.15f) * TickRate;
            float lead = Mathf.Clamp((float)((serverNowTicks - srvTick) / TickRate), 0f, 0.6f);
            PredictionLeadMs = lead * 1000f;
            pred.SetFromServer(srv, srvZ, srvH, srvVx, srvVz, srvYaw, Seed, t);
            pred.maxSpeedBonus = TurboOn ? t.turboMaxSpeedBonus : 0f;
            double t0 = now - lead; // local-clock time matching the server state
            double rejectAfter = 0.25 + Mathf.Clamp(RttMs, 20f, 400f) * 0.002; // never echoed -> rejected by the server
            for (int i = localStrokes.Count - 1; i >= 0; i--)
            {
                var s = localStrokes[i];
                if ((s.ackTick != 0 && srvTick > s.ackTick) || (s.ackTick == 0 && now - s.t > rejectAfter) || now - s.t > 1.5) localStrokes.RemoveAt(i);
            }
            const float step = 1f / 60f;
            double simT = t0;
            int next = 0;
            while (simT < now - 1e-4)
            {
                float dt = (float)System.Math.Min(step, now - simT);
                for (; next < localStrokes.Count; next++)
                {
                    var s = localStrokes[next];
                    if (s.t > simT + dt) break;
                    pred.Stroke(s.left, s.power, s.back, (float)s.t, t, s.boost);
                }
                pred.Step(dt, Seed, t);
                simT += dt;
            }
            for (; next < localStrokes.Count; next++)
            {
                var s = localStrokes[next];
                pred.Stroke(s.left, s.power, s.back, (float)s.t, t, s.boost);
            }
            var p = new Vector3(pred.x, 0f, pred.z);
            float dtf = Time.unscaledDeltaTime;
            // smooth corrections when a new authoritative state arrives (no visible snapping)
            if (srvTick != lastPredTick && lastPredTick != 0) { predErr = lastDisplayed - p; predErrH = lastDisplayedH - pred.heading; }
            lastPredTick = srvTick;
            float k = Mathf.Exp(-12f * dtf);
            predErr *= k; predErrH *= k;
            if (predErr.sqrMagnitude > 9f) predErr = Vector3.zero; // teleport-size error: snap
            RaftPos = p + predErr;
            RaftHeading = pred.heading + predErrH;
            lastDisplayed = RaftPos; lastDisplayedH = RaftHeading;
            raftView.SetPose(RaftPos, RaftHeading);
        }

        // ---------------- water effects map (foam rings, waste dirt, raft wake) ----------------
        WaterFxMap fxMap;
        const int TrailLen = 24;
        readonly Vector2[] trail = new Vector2[TrailLen];
        readonly float[] trailT = new float[TrailLen], trailS = new float[TrailLen];
        int trailHead;
        float trailTimer, fxTimer;

        void UpdateWaterFx()
        {
            float now = Time.time;
            var stern = new Vector2(RaftPos.x, RaftPos.z) - new Vector2(Mathf.Sin(RaftHeading), Mathf.Cos(RaftHeading)) * 1.45f;
            float strength = Attract ? 0.55f : Mathf.Clamp01(0.3f + Mathf.Max(0f, smoothSpeed - 2f) * 0.22f);
            trailTimer -= Time.deltaTime;
            if (trailTimer <= 0f)
            {
                trailTimer = 0.07f;
                trail[trailHead] = stern;
                trailT[trailHead] = now;
                trailS[trailHead] = strength;
                trailHead = (trailHead + 1) % TrailLen;
            }
            fxTimer -= Time.unscaledDeltaTime;
            if (fxTimer > 0f) return;
            fxTimer = 1f / 30f;
            fxMap ??= new WaterFxMap();
            fxMap.Begin(RaftPos.z);
            for (int k = 0; k < active.Count; k++)
            {
                var s = slots[active[k]];
                var p = s.go.transform.localPosition;
                if (p.z < RaftPos.z - 16f || p.z > RaftPos.z + 68f) continue;
                var kind = EntityCode.Kind(s.code);
                int sub = EntityCode.Sub(s.code);
                if (kind == EntityKind.Obstacle)
                {
                    bool shark = sub == (byte)ObstacleType.Shark;
                    float r = ObstacleCatalog.Radius[sub] + ObstacleCatalog.HalfLen[sub] * 0.7f;
                    fxMap.Ring(p.x, p.z, shark && s.flags != 2 ? 0.35f : r, shark ? (s.flags == 2 ? 0.85f : 0.45f) : 1f);
                    if (!shark || s.flags == 2)
                        fxMap.Shadow(p.x + ShadowOff.x * r, p.z + ShadowOff.y * r, ObstacleCatalog.Radius[sub] + ObstacleCatalog.HalfLen[sub] + 0.4f, ObstacleCatalog.Radius[sub] + 0.5f, 0.8f);
                }
                else if (kind == EntityKind.Waste)
                {
                    fxMap.Disc(p.x, p.z, sub == (byte)WasteType.Toxic ? 4.5f : 3f, 1f, 1);
                    fxMap.Shadow(p.x + ShadowOff.x * 0.3f, p.z + ShadowOff.y * 0.3f, 0.5f, 0.45f, 0.6f);
                }
            }
            // raft blob shadow, offset away from the key light
            var fw = new Vector2(Mathf.Sin(RaftHeading), Mathf.Cos(RaftHeading));
            float rs = 0.65f;
            fxMap.Shadow(RaftPos.x + ShadowOff.x * rs, RaftPos.z + ShadowOff.y * rs, 1.55f + Mathf.Abs(fw.x) * 0.4f, 1.55f + Mathf.Abs(fw.y) * 0.4f, 0.95f);
            // wake: chronological segments from the oldest trail point up to the current stern
            Vector2 prev = default;
            float prevAge = 0f, prevS = 0f;
            bool havePrev = false;
            for (int i = 0; i < TrailLen; i++)
            {
                int idx = (trailHead + i) % TrailLen;
                if (trailT[idx] <= 0f) continue;
                float age = (now - trailT[idx]) / 1.7f;
                if (age >= 1f) continue;
                if (havePrev) fxMap.Wake(prev, trail[idx], WakeWidth(prevAge), WakeWidth(age), (1f - prevAge) * prevS, (1f - age) * trailS[idx]);
                prev = trail[idx]; prevAge = age; prevS = trailS[idx]; havePrev = true;
            }
            if (havePrev) fxMap.Wake(prev, stern, WakeWidth(prevAge), WakeWidth(0f), (1f - prevAge) * prevS, strength);
            fxMap.End();
        }

        static float WakeWidth(float age) => 0.75f + age * 2.2f;
        static readonly Vector2 ShadowOff = new Vector2(-SceneLook.ToLight.x, -SceneLook.ToLight.z) / SceneLook.ToLight.y;

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
            if (found && b.tick == a.tick && s.count > 1 && rt > a.tick)
            {
                // starved (late snapshot): extrapolate from the two newest samples, max 6 ticks, instead of freezing
                var pr = s.s[(s.head - 2 + Ring * 2) % Ring];
                if (a.tick > pr.tick)
                {
                    float k = Mathf.Min(6f, (float)(rt - a.tick)) / (a.tick - pr.tick);
                    float dx = a.x - pr.x, dz = a.z - pr.z, dyaw = Mathf.DeltaAngle(pr.yaw, a.yaw);
                    if (dx * dx + dz * dz > 0.25f) k = 0f; // discontinuity (state change): hold
                    p = new Vector3(a.x + dx * k, a.y, a.z + dz * k);
                    yaw = Mathf.Abs(dyaw) < 20f ? a.yaw + dyaw * k : a.yaw;
                    return;
                }
            }
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
            // raft ~32 deg below the camera (lower-center third), horizon ~20% from the top (reference framing)
            var targetPos = RaftPos - fwd * CamBack + Vector3.up * CamUp;
            camT.position = Vector3.SmoothDamp(camT.position, targetPos, ref camVel, 0.18f);
            var look = RaftPos + fwd * CamAhead + Vector3.up * 0.9f;
            camT.rotation = Quaternion.Slerp(camT.rotation, Quaternion.LookRotation(look - camT.position), Mathf.Min(1f, Time.deltaTime * 8f));
            // speed-reactive FOV + punch on sync boost, roll with yaw
            float dt = Mathf.Max(1e-4f, Time.deltaTime);
            float speed = (RaftPos - lastRaftPos).magnitude / dt;
            lastRaftPos = RaftPos;
            smoothSpeed = Mathf.Lerp(smoothSpeed, Mathf.Min(speed, 12f), dt * 3f);
            fovKick = Mathf.Max(0f, fovKick - dt * 12f);
            cam.fieldOfView = Mathf.Lerp(cam.fieldOfView, 52f + smoothSpeed * 1.1f + fovKick, dt * 6f);
            if (shake > 0f) { camT.position += Random.insideUnitSphere * shake * 0.35f; shake = Mathf.Max(0f, shake - dt * 2.5f); }
            if (sky != null) sky.position = camT.position;
            postFx.ShieldLeft = ShieldLeft;
            raftView.SetPowers(Time.time < magnetUntil, TurboOn);
            UpdateHighlight();
            UpdateWakeSpray(dt);
        }

        const float CamBack = 8.5f, CamUp = 5.6f, CamAhead = 7f;
        Vector3 lastRaftPos;
        float smoothSpeed, fovKick, shake, sprayTimer, attractT;
        Transform sky, highlight;
        Material skyMat;

        void ApplyQuality()
        {
            bool hi = Quality.High;
            if (skyMat != null) skyMat.SetFloat("_Rays", hi ? 1f : 0f);
            Shader.SetGlobalFloat("_MottainaiHigh", hi ? 1f : 0f);
            postFx.SetEnabled(hi);
        }

        void OnDestroy() => Quality.Changed -= ApplyQuality;
        public PostFx postFx;
        bool attract = true;
        /// Menu fly-over mode. Entering it clears the previous match (entities, flyers, prediction).
        public bool Attract
        {
            get => attract;
            set { if (value && !attract) ResetWorld(); attract = value; }
        }
        public bool AimAssist;
        public ushort HighlightId { get; private set; } = NetConst.NoEntity;

        public void SyncKick() { fovKick = 7f; postFx.Sync(); }
        public void HitShake() { shake = 1f; postFx.Hit(); }

        // menus: slow river fly-over with the crew idling on the raft
        void UpdateAttract()
        {
            if (highlight.gameObject.activeSelf) highlight.gameObject.SetActive(false);
            raftView.SetShield(0f, 1f);
            postFx.ShieldLeft = 0f;
            raftView.SetPowers(false, false);
            attractT += Time.deltaTime;
            RaftPos += new Vector3(0f, 0f, GameTuning.Current.currentSpeed * Time.deltaTime);
            RaftPos = new Vector3(Mathf.Sin(attractT * 0.25f) * 3f, 0f, RaftPos.z);
            RaftHeading = Mathf.Cos(attractT * 0.25f) * 0.2f;
            raftView.SetPose(RaftPos, RaftHeading);
            float a = attractT * 0.12f;
            var orbit = new Vector3(Mathf.Sin(a) * 11f, 4.5f + Mathf.Sin(attractT * 0.3f), Mathf.Cos(a) * 11f);
            camT.position = Vector3.Lerp(camT.position, RaftPos + orbit, Time.deltaTime * 2f);
            camT.rotation = Quaternion.LookRotation(RaftPos + Vector3.up * 0.8f - camT.position);
            cam.fieldOfView = 50f;
            if (sky != null) sky.position = camT.position;
            UpdateWaterFx();
            water.Follow(RaftPos);
            scenery.Follow(RaftPos);
            UpdateWakeSpray(Time.deltaTime);
        }

        // collector aim assist: pulsing ring under the best target in net range
        void UpdateHighlight()
        {
            HighlightId = NetConst.NoEntity;
            if (AimAssist)
            {
                float best = GameTuning.Current.netRange;
                for (int k = 0; k < active.Count; k++)
                {
                    var s = slots[active[k]];
                    if (EntityCode.Kind(s.code) != EntityKind.Waste || EntityCode.Sub(s.code) == (byte)WasteType.Toxic) continue;
                    var p = s.go.transform.localPosition;
                    if (p.z < RaftPos.z - 1f) continue;
                    float d = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(RaftPos.x, RaftPos.z));
                    if (d < best) { best = d; HighlightId = (ushort)active[k]; }
                }
            }
            bool on = HighlightId != NetConst.NoEntity;
            highlight.gameObject.SetActive(on);
            if (on)
            {
                var p = slots[HighlightId].go.transform.localPosition;
                float pulse = 1.2f + Mathf.Sin(Time.time * 8f) * 0.18f;
                highlight.localPosition = new Vector3(p.x, 0.06f, p.z);
                highlight.localScale = new Vector3(pulse, 0.02f, pulse);
            }
        }

        // white spray at the stern proportional to speed
        void UpdateWakeSpray(float dt)
        {
            sprayTimer -= dt;
            if (sprayTimer > 0f) return;
            float sp = Attract ? 2.5f : smoothSpeed;
            sprayTimer = Mathf.Lerp(0.25f, 0.05f, Mathf.InverseLerp(1f, 7f, sp)) * (TurboOn && !Attract ? 0.4f : 1f);
            var back = new Vector3(Mathf.Sin(RaftHeading), 0f, Mathf.Cos(RaftHeading));
            var side = new Vector3(back.z, 0f, -back.x);
            var pos = RaftPos - back * 1.6f + side * Random.Range(-0.9f, 0.9f) + Vector3.up * 0.15f;
            fx.Burst(pos, new Color(0.9f, 0.97f, 1f), 2, 0.6f, 0.09f, 0.8f + sp * 0.15f);
        }

        public void SnapCamera()
        {
            lastRaftPos = RaftPos;
            smoothSpeed = 0f;
            camT.position = RaftPos + new Vector3(0, CamUp, -CamBack);
            camT.rotation = Quaternion.LookRotation(RaftPos + new Vector3(0, 0.9f, CamAhead) - camT.position);
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
