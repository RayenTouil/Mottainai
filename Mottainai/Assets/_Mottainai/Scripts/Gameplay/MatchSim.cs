using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Gameplay
{
    /// Authoritative match simulation (server only drives it). No Unity scene objects.
    public class MatchSim
    {
        public const int HistoryTicks = 32;
        public int TickRate = NetConst.TickRate;

        public TuningData T;
        public IGameMode Mode;
        public int Seed;
        public SimEntity[] E;
        public int Count;
        public readonly List<TeamState> Teams = new List<TeamState>();
        public MatchPhase Phase = MatchPhase.Lobby;
        public float Time, Clock, CountdownLeft;
        public uint Tick;
        public EndReason Reason;
        public readonly List<MatchEvent> Events = new List<MatchEvent>(64);
        public ResultsData Results;
        public float[] LaneCenters;

        float[] histX, histZ;
        readonly float[] raftHistX = new float[HistoryTicks], raftHistZ = new float[HistoryTicks];
        readonly System.Random rng;
        int lastCountdownShown = -1;

        struct PendingNet { public int team; public float landTime, x, z; }
        readonly List<PendingNet> nets = new List<PendingNet>();

        public MatchSim(int seed, TuningData t, IGameMode mode)
        {
            Seed = seed; T = t; Mode = mode;
            rng = new System.Random(seed ^ 0x5bd1e995);
            E = CourseGenerator.Generate(seed, t, out LaneCenters);
            Count = E.Length;
            histX = new float[HistoryTicks * Count];
            histZ = new float[HistoryTicks * Count];
            for (int i = 0; i < mode.Config.teams; i++)
            {
                var team = new TeamState(i, mode.Config.hitsToGameOver, i * 4f);
                Teams.Add(team);
            }
            Phase = MatchPhase.Countdown;
            CountdownLeft = t.countdown;
            UpdateEntities();
            RecordHistory();
        }

        void Emit(EvType type, int team = 0, Role role = Role.None, ushort entity = NetConst.NoEntity, int value = 0, byte extra = 0, float x = 0, float z = 0)
        {
            Events.Add(new MatchEvent { type = type, team = (byte)team, role = role, entity = entity, value = (short)Mathf.Clamp(value, short.MinValue, short.MaxValue), extra = extra, x = x, z = z });
        }

        public void Step(float dt)
        {
            Tick++;
            Clock += dt;
            if (Phase == MatchPhase.Countdown)
            {
                CountdownLeft -= dt;
                int c = Mathf.CeilToInt(CountdownLeft);
                if (c != lastCountdownShown && c > 0) { lastCountdownShown = c; Emit(EvType.Countdown, value: c); }
                if (CountdownLeft <= 0f) { Phase = MatchPhase.Running; Emit(EvType.Go); }
            }

            UpdateEntities();

            if (Phase == MatchPhase.Running)
            {
                Time += dt;
                foreach (var team in Teams)
                {
                    team.brain.Step(this, team, rng);
                    team.raft.Step(dt, Seed, T);
                    if (team.shieldActive && Time >= team.shieldUntil) { team.shieldActive = false; Emit(EvType.ShieldOff, team.id); }
                    if (team.magnetActive && Time >= team.magnetUntil) { team.magnetActive = false; Emit(EvType.PowerOff, team.id, extra: (byte)PowerUp.Magnet); }
                    if (team.turboActive && Time >= team.turboUntil) { team.turboActive = false; Emit(EvType.PowerOff, team.id, extra: (byte)PowerUp.Turbo); }
                    team.raft.maxSpeedBonus = team.turboActive ? T.turboMaxSpeedBonus : 0f;
                    Collide(team);
                }
                ResolveNets();
                if (Mode.CheckEnd(this, out var reason)) Finish(reason);
            }
            RecordHistory();
        }

        public void Abort() { if (Phase != MatchPhase.Finished) Finish(EndReason.Aborted); }

        void Finish(EndReason reason)
        {
            Phase = MatchPhase.Finished;
            Reason = reason;
            var team = Teams[0];
            Results = new ResultsData
            {
                reason = reason, catchScore = team.score.catchScore, toxicPenalty = team.score.ToxicPenalty(T),
                total = team.score.Total(T), throws = team.score.throws, hits = team.score.hits,
                distance = team.raft.z - team.startZ, kg = team.score.kg, duration = Time
            };
            System.Array.Copy(team.score.counts, Results.counts, Results.counts.Length);
            Emit(EvType.GameOver, team.id, value: (int)reason);
        }

        void UpdateEntities()
        {
            float c = Clock;
            for (int i = 0; i < Count; i++)
            {
                ref var e = ref E[i];
                if (!e.alive) continue;
                switch (e.kind)
                {
                    case EntityKind.Waste:
                        e.y = 0.06f * Mathf.Sin(c * 2.2f + e.phase);
                        e.yaw = e.phase * 57f + c * 25f;
                        break;
                    case EntityKind.Shield:
                        e.y = 0.5f + 0.15f * Mathf.Sin(c * 3f + e.phase);
                        e.yaw = c * 90f;
                        break;
                    case EntityKind.Obstacle:
                        if (e.sub == (byte)ObstacleType.Shark) SharkPose(ref e, c, T);
                        else if (e.sub == (byte)ObstacleType.Iceberg || e.sub == (byte)ObstacleType.Wreck || e.sub == (byte)ObstacleType.Log)
                        {
                            e.y = 0.05f * Mathf.Sin(c * 1.3f + e.phase);
                        }
                        break;
                }
            }
        }

        /// Deterministic shark patrol: submerged 3 s, fin telegraph, then active (dangerous) 5 s.
        public static void SharkPose(ref SimEntity e, float clock, TuningData T)
        {
            float omega = T.sharkSpeed / Mathf.Max(0.1f, T.sharkAmplitude);
            float cyc = 3f + T.sharkTelegraph + 5f;
            float local = Mathf.Repeat(clock + e.phase, cyc);
            e.flags = (byte)(local < 3f ? 0 : local < 3f + T.sharkTelegraph ? 1 : 2);
            e.x = e.baseX + T.sharkAmplitude * Mathf.Sin(omega * clock + e.phase);
            e.yaw = Mathf.Cos(omega * clock + e.phase) >= 0f ? 90f : -90f;
            e.y = e.flags == 2 ? 0f : -0.4f;
        }

        void RecordHistory()
        {
            int slot = (int)(Tick % HistoryTicks) * Count;
            for (int i = 0; i < Count; i++) { histX[slot + i] = E[i].x; histZ[slot + i] = E[i].z; }
            raftHistX[Tick % HistoryTicks] = Teams[0].raft.x;
            raftHistZ[Tick % HistoryTicks] = Teams[0].raft.z;
        }

        public Vector2 HistoricEntity(int i, int ticksBack)
        {
            ticksBack = Mathf.Clamp(ticksBack, 0, (int)Mathf.Min(HistoryTicks - 1, Tick));
            int slot = (int)((Tick - (uint)ticksBack) % HistoryTicks) * Count;
            return new Vector2(histX[slot + i], histZ[slot + i]);
        }

        public Vector2 HistoricRaft(int ticksBack)
        {
            ticksBack = Mathf.Clamp(ticksBack, 0, (int)Mathf.Min(HistoryTicks - 1, Tick));
            uint s = (Tick - (uint)ticksBack) % HistoryTicks;
            return new Vector2(raftHistX[s], raftHistZ[s]);
        }

        public int LowerBound(float z)
        {
            int lo = 0, hi = Count;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (E[mid].z < z) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        void Collide(TeamState team)
        {
            var r = team.raft;
            int start = LowerBound(r.z - 4f);
            for (int i = start; i < Count && E[i].z < r.z + 4f; i++)
            {
                ref var e = ref E[i];
                if (!e.alive) continue;
                if (e.kind == EntityKind.Shield)
                {
                    float dx = e.x - r.x, dz = e.z - r.z;
                    if (dx * dx + dz * dz < (T.raftRadius + e.radius + 0.4f) * (T.raftRadius + e.radius + 0.4f))
                    {
                        e.alive = false;
                        switch ((PowerUp)e.sub)
                        {
                            case PowerUp.Magnet:
                                team.magnetActive = true;
                                team.magnetUntil = Time + T.magnetDuration;
                                Emit(EvType.PowerOn, team.id, entity: e.id, value: Mathf.RoundToInt(T.magnetDuration * 10f), extra: e.sub, x: e.x, z: e.z);
                                break;
                            case PowerUp.Turbo:
                                team.turboActive = true;
                                team.turboUntil = Time + T.turboDuration;
                                Emit(EvType.PowerOn, team.id, entity: e.id, value: Mathf.RoundToInt(T.turboDuration * 10f), extra: e.sub, x: e.x, z: e.z);
                                break;
                            default:
                                team.shieldActive = true;
                                team.shieldUntil = Time + T.shieldDuration;
                                Emit(EvType.ShieldOn, team.id, entity: e.id, x: e.x, z: e.z);
                                break;
                        }
                    }
                }
                else if (e.kind == EntityKind.Obstacle && e.Dangerous)
                {
                    if (CourseGenerator.CapsuleDistance(e, r.x, r.z) < T.raftRadius * 0.9f)
                    {
                        if (team.shieldActive) continue;
                        if (Time < team.health.invulnUntil) continue;
                        bool dead = Mode.OnObstacleHit(this, team);
                        Emit(EvType.Hit, team.id, entity: e.id, value: team.health.hp, x: e.x, z: e.z);
                        if (!dead) { r.vz = -2f; r.vx = (r.x - e.x) * 1.5f; }
                    }
                }
            }
        }

        public bool ApplyStroke(TeamState team, Role role, float power, bool back)
        {
            if (Phase != MatchPhase.Running || (role != Role.LeftPaddler && role != Role.RightPaddler)) return false;
            if (Time < team.strokeReady[(int)role]) return false;
            team.strokeReady[(int)role] = Time + T.strokeCooldown;
            power = Mathf.Clamp(power, T.minStrokePower, T.maxStrokePower);
            bool sync = team.raft.Stroke(role == Role.LeftPaddler, power, back, Time, T, team.turboActive ? T.turboImpulseScale : 1f);
            Emit(EvType.Stroke, team.id, role, value: Mathf.RoundToInt(power * 100), extra: (byte)(back ? 1 : 0));
            if (sync) Emit(EvType.Sync, team.id, value: Mathf.RoundToInt(T.syncBoost * 100), x: team.raft.x, z: team.raft.z);
            return true;
        }

        /// rewindSec: lag compensation (one-way latency + client interpolation, capped by caller)
        public bool ApplyThrow(TeamState team, ushort target, float dirX, float dirZ, float rewindSec)
        {
            if (Phase != MatchPhase.Running || Time < team.netReadyAt) return false;
            team.netReadyAt = Time + T.netCooldown;
            team.score.throws++;
            int back = Mathf.RoundToInt(rewindSec * TickRate);
            Vector2 raftPos = HistoricRaft(back);
            Vector2 land;
            float range = T.netRange;

            if (target < Count && E[target].alive && E[target].kind == EntityKind.Waste &&
                (HistoricEntity(target, back) - raftPos).magnitude <= range * 1.15f + E[target].radius)
            {
                land = HistoricEntity(target, back);
            }
            else
            {
                var dir = new Vector2(dirX, dirZ);
                if (dir.sqrMagnitude < 1e-4f) dir = new Vector2(Mathf.Sin(team.raft.heading), Mathf.Cos(team.raft.heading));
                dir.Normalize();
                int best = -1; float bestScore = float.MaxValue;
                int s = LowerBound(raftPos.y - range);
                for (int i = s; i < Count && E[i].z < raftPos.y + range; i++)
                {
                    if (!E[i].alive || E[i].kind != EntityKind.Waste) continue;
                    var p = HistoricEntity(i, back) - raftPos;
                    float d = p.magnitude;
                    if (d > range || d < 0.01f) continue;
                    float cos = Vector2.Dot(p / d, dir);
                    if (cos < 0.9f) continue; // ~25 deg cone
                    float perp = d * Mathf.Sqrt(Mathf.Max(0f, 1f - cos * cos));
                    if (perp < bestScore) { bestScore = perp; best = i; }
                }
                land = best >= 0 ? HistoricEntity(best, back) : raftPos + dir * (range * 0.75f);
            }
            float travel = Mathf.Max(0.02f, T.netTravel - rewindSec);
            nets.Add(new PendingNet { team = team.id, landTime = Time + travel, x = land.x, z = land.y });
            Emit(EvType.NetThrow, team.id, Role.Collector, value: Mathf.RoundToInt(travel * 1000), x: land.x, z: land.y);
            return true;
        }

        void ResolveNets()
        {
            for (int n = nets.Count - 1; n >= 0; n--)
            {
                var net = nets[n];
                if (Time < net.landTime) continue;
                nets.RemoveAt(n);
                var team = Teams[net.team];
                int caught = 0;
                float netR = T.netRadius * (team.magnetActive ? T.magnetRadiusScale : 1f);
                int s = LowerBound(net.z - 4f);
                for (int i = s; i < Count && E[i].z < net.z + 4f; i++)
                {
                    ref var e = ref E[i];
                    if (!e.alive || e.kind != EntityKind.Waste) continue;
                    float rr = (netR + e.radius) * T.hitRadiusScale;
                    float dx = e.x - net.x, dz = e.z - net.z;
                    if (dx * dx + dz * dz > rr * rr) continue;
                    e.alive = false;
                    caught++;
                    int pts = team.score.AddCatch((WasteType)e.sub, Time, T, out bool combo);
                    Emit(EvType.Catch, team.id, Role.Collector, e.id, pts, (byte)(e.sub | (combo ? 0x80 : 0)), e.x, e.z);
                    if (team.score.ComboArmed) Emit(EvType.Combo, team.id, value: Mathf.RoundToInt(T.comboMultiplier * 10));
                }
                if (caught > 0) team.score.hits++;
                else Emit(EvType.Miss, team.id, Role.Collector, x: net.x, z: net.z);
            }
        }

        public void SetBot(int team, Role role, bool isBot)
        {
            if (team < 0 || team >= Teams.Count || role == Role.None) return;
            if (Teams[team].bot[(int)role] == isBot) return;
            Teams[team].bot[(int)role] = isBot;
            if (isBot) Emit(EvType.BotTakeover, team, role);
        }
    }
}
