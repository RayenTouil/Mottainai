using System.Collections.Generic;
using Mottainai.Gameplay;
using Unity.Netcode;
using UnityEngine;

namespace Mottainai.Net
{
    public struct NetEntity
    {
        public ushort id;
        public byte code, flags;
        public float x, y, z, yaw;
    }

    public class NetRaft
    {
        public float x, z, heading, vx, vz, yawRate, shieldLeft;
        public byte hp;
    }

    public class SnapshotFrame
    {
        public uint tick, seq;
        public float matchTime, timeLeft, originZ;
        public MatchPhase phase;
        public int score;
        public bool comboArmed;
        public byte chunk, chunkCount;
        public readonly NetRaft raft = new NetRaft();
        public bool hasRaft;
        public readonly Vector2[] flow = new Vector2[16];
        public int flowCount;
        public readonly NetEntity[] entities = new NetEntity[256];
        public int entityCount;
    }

    public class LobbyPlayer { public ulong clientId; public string name; public Role role; public bool ready, connected; }

    public class LobbyState
    {
        public string code = "", message = "";
        public MatchPhase phase;
        public ulong creator;
        public readonly List<LobbyPlayer> players = new List<LobbyPlayer>();
    }

    public class MatchStartInfo { public string code; public int seed; public Role role; public byte team; public float duration, countdown; }

    public static class Codec
    {
        public const int EntitiesPerChunk = 96;

        // ---------- snapshot (server) ----------
        static readonly List<int> scratch = new List<int>(512);

        /// Writes and sends all chunks of one world snapshot for a team window to a set of clients.
        public static void SendSnapshot(MatchSim sim, TeamState team, IReadOnlyList<ulong> clients, IList<uint> seqs)
        {
            var t = sim.T;
            var r = team.raft;
            scratch.Clear();
            int s = sim.LowerBound(r.z - t.snapshotBehind);
            for (int i = s; i < sim.Count && sim.E[i].z < r.z + t.snapshotAhead; i++)
                if (sim.E[i].alive) scratch.Add(i);

            int chunkCount = Mathf.Max(1, (scratch.Count + EntitiesPerChunk - 1) / EntitiesPerChunk);
            float originZ = r.z;
            for (int c = 0; c < chunkCount; c++)
            {
                int from = c * EntitiesPerChunk, to = Mathf.Min(scratch.Count, from + EntitiesPerChunk);
                for (int k = 0; k < clients.Count; k++)
                {
                    var w = NetBus.Begin(Msg.Snapshot, 1200, 2048);
                    w.WriteValueSafe(sim.Tick);
                    w.WriteValueSafe(seqs[k]);
                    seqs[k] = seqs[k] + 1;
                    w.WriteValueSafe(sim.Time);
                    w.WriteValueSafe((byte)c);
                    w.WriteValueSafe((byte)chunkCount);
                    w.WriteValueSafe(originZ);
                    if (c == 0) WriteHeaderBlock(ref w, sim, team);
                    w.WriteValueSafe((byte)(to - from));
                    for (int j = from; j < to; j++)
                    {
                        ref var e = ref sim.E[scratch[j]];
                        w.WriteValueSafe(e.id);
                        w.WriteValueSafe(e.Code);
                        w.WriteValueSafe(Q(e.x, 100f));
                        w.WriteValueSafe(Q(e.z - originZ, 100f));
                        w.WriteValueSafe((sbyte)Mathf.Clamp(Mathf.RoundToInt(e.y * 50f), -127, 127));
                        w.WriteValueSafe((byte)(Mathf.RoundToInt(Mathf.Repeat(e.yaw, 360f) / 360f * 256f) & 0xFF));
                        w.WriteValueSafe(e.flags);
                    }
                    NetBus.ToClient(clients[k], w, NetworkDelivery.UnreliableSequenced);
                    w.Dispose();
                }
            }
        }

        static void WriteHeaderBlock(ref FastBufferWriter w, MatchSim sim, TeamState team)
        {
            var t = sim.T;
            var r = team.raft;
            w.WriteValueSafe((byte)sim.Phase);
            float left = sim.Phase == MatchPhase.Countdown ? sim.CountdownLeft : Mathf.Max(0f, sim.Mode.Config.duration - sim.Time);
            w.WriteValueSafe((ushort)Mathf.Clamp(Mathf.RoundToInt(left * 100f), 0, 65535));
            w.WriteValueSafe(team.score.catchScore);
            w.WriteValueSafe((byte)(team.score.ComboArmed ? 1 : 0));
            w.WriteValueSafe(r.x);
            w.WriteValueSafe(r.z);
            w.WriteValueSafe(r.heading);
            w.WriteValueSafe(Q(r.worldVx, 100f));
            w.WriteValueSafe(Q(r.worldVz, 100f));
            w.WriteValueSafe(Q(r.yawRate, 1000f));
            w.WriteValueSafe((byte)Mathf.Max(0, team.health.hp));
            w.WriteValueSafe((byte)Mathf.Clamp(Mathf.RoundToInt((team.shieldActive ? team.shieldUntil - sim.Time : 0f) * 20f), 0, 255));
            w.WriteValueSafe((byte)16);
            for (int zi = 0; zi < 4; zi++)
                for (int xi = 0; xi < 4; xi++)
                {
                    var f = FlowField.Sample(sim.Seed, -9f + xi * 6f, r.z + 5f + zi * 10f, t);
                    w.WriteValueSafe((sbyte)Mathf.Clamp(Mathf.RoundToInt(f.x * 50f), -127, 127));
                    w.WriteValueSafe((sbyte)Mathf.Clamp(Mathf.RoundToInt((f.y - t.currentSpeed) * 50f), -127, 127));
                }
        }

        static short Q(float v, float scale) => (short)Mathf.Clamp(Mathf.RoundToInt(v * scale), short.MinValue, short.MaxValue);

        // ---------- snapshot (client) ----------
        public static void ReadSnapshot(FastBufferReader rd, SnapshotFrame f, TuningData t)
        {
            rd.ReadValueSafe(out f.tick);
            rd.ReadValueSafe(out f.seq);
            rd.ReadValueSafe(out f.matchTime);
            rd.ReadValueSafe(out f.chunk);
            rd.ReadValueSafe(out f.chunkCount);
            rd.ReadValueSafe(out f.originZ);
            f.hasRaft = f.chunk == 0;
            if (f.hasRaft)
            {
                rd.ReadValueSafe(out byte phase); f.phase = (MatchPhase)phase;
                rd.ReadValueSafe(out ushort left); f.timeLeft = left / 100f;
                rd.ReadValueSafe(out f.score);
                rd.ReadValueSafe(out byte combo); f.comboArmed = combo != 0;
                var r = f.raft;
                rd.ReadValueSafe(out r.x);
                rd.ReadValueSafe(out r.z);
                rd.ReadValueSafe(out r.heading);
                rd.ReadValueSafe(out short vx); r.vx = vx / 100f;
                rd.ReadValueSafe(out short vz); r.vz = vz / 100f;
                rd.ReadValueSafe(out short yr); r.yawRate = yr / 1000f;
                rd.ReadValueSafe(out r.hp);
                rd.ReadValueSafe(out byte sh); r.shieldLeft = sh / 20f;
                rd.ReadValueSafe(out byte n);
                f.flowCount = Mathf.Min(n, f.flow.Length);
                for (int i = 0; i < n; i++)
                {
                    rd.ReadValueSafe(out sbyte fx);
                    rd.ReadValueSafe(out sbyte fz);
                    if (i < f.flow.Length) f.flow[i] = new Vector2(fx / 50f, fz / 50f + t.currentSpeed);
                }
            }
            rd.ReadValueSafe(out byte count);
            f.entityCount = Mathf.Min(count, f.entities.Length);
            for (int i = 0; i < count; i++)
            {
                rd.ReadValueSafe(out ushort id);
                rd.ReadValueSafe(out byte code);
                rd.ReadValueSafe(out short x);
                rd.ReadValueSafe(out short z);
                rd.ReadValueSafe(out sbyte y);
                rd.ReadValueSafe(out byte yaw);
                rd.ReadValueSafe(out byte flags);
                if (i >= f.entities.Length) continue;
                f.entities[i] = new NetEntity
                {
                    id = id, code = code, flags = flags, x = x / 100f, z = f.originZ + z / 100f, y = y / 50f, yaw = yaw * (360f / 256f)
                };
            }
        }

        // ---------- events ----------
        public static void WriteEvents(ref FastBufferWriter w, List<MatchEvent> evs, int from, int count)
        {
            w.WriteValueSafe((byte)count);
            for (int i = from; i < from + count; i++)
            {
                var e = evs[i];
                w.WriteValueSafe((byte)e.type);
                w.WriteValueSafe(e.team);
                w.WriteValueSafe((byte)e.role);
                w.WriteValueSafe(e.entity);
                w.WriteValueSafe(e.value);
                w.WriteValueSafe(e.extra);
                w.WriteValueSafe(e.x);
                w.WriteValueSafe(e.z);
            }
        }

        public static void ReadEvents(FastBufferReader rd, List<MatchEvent> outList)
        {
            rd.ReadValueSafe(out byte n);
            for (int i = 0; i < n; i++)
            {
                var e = new MatchEvent();
                rd.ReadValueSafe(out byte type); e.type = (EvType)type;
                rd.ReadValueSafe(out e.team);
                rd.ReadValueSafe(out byte role); e.role = (Role)role;
                rd.ReadValueSafe(out e.entity);
                rd.ReadValueSafe(out e.value);
                rd.ReadValueSafe(out e.extra);
                rd.ReadValueSafe(out e.x);
                rd.ReadValueSafe(out e.z);
                outList.Add(e);
            }
        }

        // ---------- results ----------
        public static void WriteResults(ref FastBufferWriter w, ResultsData r)
        {
            w.WriteValueSafe((byte)r.reason);
            w.WriteValueSafe(r.total);
            w.WriteValueSafe(r.catchScore);
            w.WriteValueSafe(r.toxicPenalty);
            w.WriteValueSafe(r.throws);
            w.WriteValueSafe(r.hits);
            for (int i = 0; i < NetConst.WasteTypeCount; i++) w.WriteValueSafe(r.counts[i]);
            w.WriteValueSafe(r.distance);
            w.WriteValueSafe(r.kg);
            w.WriteValueSafe(r.duration);
        }

        public static ResultsData ReadResults(FastBufferReader rd)
        {
            var r = new ResultsData();
            rd.ReadValueSafe(out byte reason); r.reason = (EndReason)reason;
            rd.ReadValueSafe(out r.total);
            rd.ReadValueSafe(out r.catchScore);
            rd.ReadValueSafe(out r.toxicPenalty);
            rd.ReadValueSafe(out r.throws);
            rd.ReadValueSafe(out r.hits);
            for (int i = 0; i < NetConst.WasteTypeCount; i++) rd.ReadValueSafe(out r.counts[i]);
            rd.ReadValueSafe(out r.distance);
            rd.ReadValueSafe(out r.kg);
            rd.ReadValueSafe(out r.duration);
            return r;
        }

        // ---------- lobby ----------
        public static void WriteLobby(ref FastBufferWriter w, LobbyState s)
        {
            w.WriteValueSafe(s.code ?? "");
            w.WriteValueSafe(s.message ?? "");
            w.WriteValueSafe((byte)s.phase);
            w.WriteValueSafe(s.creator);
            w.WriteValueSafe((byte)s.players.Count);
            foreach (var p in s.players)
            {
                w.WriteValueSafe(p.clientId);
                w.WriteValueSafe(p.name ?? "");
                w.WriteValueSafe((byte)p.role);
                w.WriteValueSafe(p.ready);
                w.WriteValueSafe(p.connected);
            }
        }

        public static LobbyState ReadLobby(FastBufferReader rd)
        {
            var s = new LobbyState();
            rd.ReadValueSafe(out s.code);
            rd.ReadValueSafe(out s.message);
            rd.ReadValueSafe(out byte phase); s.phase = (MatchPhase)phase;
            rd.ReadValueSafe(out s.creator);
            rd.ReadValueSafe(out byte n);
            for (int i = 0; i < n; i++)
            {
                var p = new LobbyPlayer();
                rd.ReadValueSafe(out p.clientId);
                rd.ReadValueSafe(out p.name);
                rd.ReadValueSafe(out byte role); p.role = (Role)role;
                rd.ReadValueSafe(out p.ready);
                rd.ReadValueSafe(out p.connected);
                s.players.Add(p);
            }
            return s;
        }

        public static void WriteMatchStart(ref FastBufferWriter w, MatchStartInfo m)
        {
            w.WriteValueSafe(m.code ?? "");
            w.WriteValueSafe(m.seed);
            w.WriteValueSafe((byte)m.role);
            w.WriteValueSafe(m.team);
            w.WriteValueSafe(m.duration);
            w.WriteValueSafe(m.countdown);
        }

        public static MatchStartInfo ReadMatchStart(FastBufferReader rd)
        {
            var m = new MatchStartInfo();
            rd.ReadValueSafe(out m.code);
            rd.ReadValueSafe(out m.seed);
            rd.ReadValueSafe(out byte role); m.role = (Role)role;
            rd.ReadValueSafe(out m.team);
            rd.ReadValueSafe(out m.duration);
            rd.ReadValueSafe(out m.countdown);
            return m;
        }
    }
}
