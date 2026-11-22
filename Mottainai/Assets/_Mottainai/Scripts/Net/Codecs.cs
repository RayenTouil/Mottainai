using System;
using System.Collections.Generic;
using System.Text;
using Mottainai.Gameplay;
using UnityEngine;

namespace Mottainai.Net
{
    /// Message ids — mirror of PROTOCOL.md / server/src/protocol.ts.
    public static class Msg
    {
        public const int ProtocolVersion = 3;
        // client -> server
        public const byte Role = 0x01, Ready = 0x02, Start = 0x03, Leave = 0x04, ContinueBot = 0x05, TeamCreate = 0x06, TeamAnswer = 0x07, Stroke = 0x10, BackPaddle = 0x11, Net = 0x12, Ping = 0x20, Stats = 0x21;
        // server -> client
        public const byte Welcome = 0x80, Lobby = 0x81, MatchStart = 0x82, Snapshot = 0x83, Events = 0x84, Results = 0x85, Pong = 0x86, Notice = 0x87, Pause = 0x8A, Resume = 0x8B, TeamResult = 0x8C, TeamInvite = 0x8D;
    }

    public struct NetEntity
    {
        public ushort id;
        public byte code, flags;
        public float x, y, z, yaw;
    }

    public class NetRaft
    {
        public float x, z, heading, vx, vz, yawRate, shieldLeft, magnetLeft, turboLeft;
        public byte hp, flags;
        public int score;
    }

    public class SnapshotFrame
    {
        public uint tick;
        public float matchTime, timeLeft, originZ;
        public MatchPhase phase;
        public int score, snapshotHz;
        public bool comboArmed;
        public readonly bool hasRaft = true; // full snapshots always carry the raft
        public readonly NetRaft raft = new NetRaft();
        public readonly NetRaft[] teams = { new NetRaft(), new NetRaft(), new NetRaft(), new NetRaft() };
        public int teamCount;
        public readonly Vector2[] flow = new Vector2[16];
        public int flowCount;
        public NetEntity[] entities = new NetEntity[512];
        public int entityCount;
    }

    public class LobbyPlayer { public ulong clientId; public string name; public Role role; public bool ready, connected; public string team = ""; }

    /// v3 TEAM_RESULT: did the finished match count for a team (server-authoritative), and can this guest crew still save it.
    public class TeamResultInfo { public bool counted, canSave; public string teamName; public int points, rank; }
    /// v3 TEAM_INVITE: the host invites me to a team (currentTeam = the team I would leave).
    public class TeamInviteInfo { public string teamName, fromName, currentTeam; }

    public class LobbyState
    {
        public string code = "", message = "";
        public MatchPhase phase;
        public ulong creator;
        public byte hostId; // v3: START/REMATCH only from the host
        public readonly List<LobbyPlayer> players = new List<LobbyPlayer>();
        public readonly byte[] roleOwner = { 0xFF, 0xFF, 0xFF };
        public bool IsBot(Role r) => r >= Role.LeftPaddler && r <= Role.Collector && roleOwner[(int)r - 1] == 0xFF;
    }

    /// v3 PAUSE: the server froze the match while human players are missing (grace in seconds per player).
    public class PauseInfo
    {
        public struct Missing { public byte id; public string name; public Role role; public int graceS; }
        public byte reason;
        public readonly List<Missing> missing = new List<Missing>();
    }

    public class MatchStartInfo { public string code; public int seed; public Role role; public byte team, tickRate = 60; public float duration, countdown; }

    public class WelcomeInfo { public int protocolVersion; public byte playerId; public string roomCode, roomColo; public uint serverTick; public byte tickRate, snapshotHz; }

    /// Little-endian reader over a byte[] (no allocations except strings).
    public struct ByteReader
    {
        readonly byte[] b;
        int p;
        readonly int end;
        public ByteReader(byte[] buf, int offset, int length) { b = buf; p = offset; end = offset + length; }
        public int Left => end - p;
        void Need(int n) { if (p + n > end) throw new IndexOutOfRangeException("short frame"); }
        public byte U8() { Need(1); return b[p++]; }
        public sbyte I8() { Need(1); return (sbyte)b[p++]; }
        public ushort U16() { Need(2); var v = (ushort)(b[p] | b[p + 1] << 8); p += 2; return v; }
        public short I16() => (short)U16();
        public uint U32() { Need(4); var v = (uint)(b[p] | b[p + 1] << 8 | b[p + 2] << 16 | b[p + 3] << 24); p += 4; return v; }
        public int I32() => (int)U32();
        public float F32() { Need(4); var v = BitConverter.ToSingle(b, p); p += 4; return v; }
        public double F64() { Need(8); var v = BitConverter.ToDouble(b, p); p += 8; return v; }
        public string Str() { int n = U8(); Need(n); var s = Encoding.UTF8.GetString(b, p, n); p += n; return s; }
    }

    /// Little-endian writer into a reusable buffer.
    public class ByteWriter
    {
        public byte[] buf = new byte[64];
        public int len;
        public ByteWriter Begin(byte id) { len = 0; return U8(id); }
        void Need(int n) { if (len + n > buf.Length) Array.Resize(ref buf, Math.Max(buf.Length * 2, len + n)); }
        public ByteWriter U8(int v) { Need(1); buf[len++] = (byte)v; return this; }
        public ByteWriter Str(string s)
        {
            var b = Encoding.UTF8.GetBytes(s ?? "");
            int n = Math.Min(b.Length, 255);
            U8(n); Need(n); Buffer.BlockCopy(b, 0, buf, len, n); len += n;
            return this;
        }
        public ByteWriter U16(int v) { Need(2); buf[len++] = (byte)v; buf[len++] = (byte)(v >> 8); return this; }
        public ByteWriter I16(int v) => U16((ushort)(short)Mathf.Clamp(v, short.MinValue, short.MaxValue));
        public ByteWriter F32(float v) { Need(4); var x = BitConverter.GetBytes(v); Buffer.BlockCopy(x, 0, buf, len, 4); len += 4; return this; }
        public ByteWriter F64(double v) { Need(8); var x = BitConverter.GetBytes(v); Buffer.BlockCopy(x, 0, buf, len, 8); len += 8; return this; }
        public byte[] ToArray() { var a = new byte[len]; Buffer.BlockCopy(buf, 0, a, 0, len); return a; }
    }

    public static class Codec
    {
        public static WelcomeInfo ReadWelcome(ref ByteReader r) => new WelcomeInfo
        {
            protocolVersion = r.U16(), playerId = r.U8(), roomCode = r.Str(), roomColo = r.Str(), serverTick = r.U32(), tickRate = r.U8(), snapshotHz = r.U8()
        };

        public static void ReadSnapshot(ref ByteReader r, SnapshotFrame f, int myTeam, int seed, TuningData t)
        {
            f.tick = r.U32();
            f.matchTime = r.F32();
            f.phase = (MatchPhase)r.U8();
            f.timeLeft = r.U16() / 100f;
            f.snapshotHz = r.U8();
            f.teamCount = r.U8();
            for (int k = 0; k < f.teamCount; k++)
            {
                var tr = k < f.teams.Length ? f.teams[k] : new NetRaft();
                tr.score = r.I32(); tr.flags = r.U8(); tr.hp = r.U8();
                tr.shieldLeft = r.U8() / 20f; tr.magnetLeft = r.U8() / 20f; tr.turboLeft = r.U8() / 20f;
                tr.x = r.F32(); tr.z = r.F32();
                tr.heading = r.I16() / 1000f; tr.vx = r.I16() / 100f; tr.vz = r.I16() / 100f; tr.yawRate = r.I16() / 1000f;
            }
            var me = f.teams[Mathf.Clamp(myTeam, 0, Mathf.Max(0, Mathf.Min(f.teamCount, f.teams.Length) - 1))];
            var raft = f.raft;
            raft.x = me.x; raft.z = me.z; raft.heading = me.heading; raft.vx = me.vx; raft.vz = me.vz; raft.yawRate = me.yawRate;
            raft.hp = me.hp; raft.flags = me.flags; raft.score = me.score;
            raft.shieldLeft = (me.flags & 2) != 0 ? me.shieldLeft : 0f; raft.magnetLeft = me.magnetLeft; raft.turboLeft = me.turboLeft;
            f.score = me.score;
            f.comboArmed = (me.flags & 1) != 0;

            // flow field is deterministic from the seed: computed locally instead of sent
            f.flowCount = 16;
            for (int zi = 0; zi < 4; zi++)
                for (int xi = 0; xi < 4; xi++)
                    f.flow[zi * 4 + xi] = FlowField.Sample(seed, -9f + xi * 6f, raft.z + 5f + zi * 10f, t);

            f.originZ = r.F32();
            int n = r.U16();
            if (f.entities.Length < n) f.entities = new NetEntity[n];
            f.entityCount = n;
            for (int i = 0; i < n; i++)
            {
                f.entities[i] = new NetEntity
                {
                    id = r.U16(), code = r.U8(), x = r.I16() / 100f, z = 0f, y = 0f, yaw = 0f, flags = 0
                };
                ref var e = ref f.entities[i];
                e.z = f.originZ + r.I16() / 100f;
                e.y = r.I8() / 50f;
                e.yaw = r.U8() * (360f / 256f);
                e.flags = r.U8();
            }
        }

        public static void ReadEvents(ref ByteReader r, List<MatchEvent> outList)
        {
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var e = new MatchEvent
                {
                    type = (EvType)r.U8(), team = r.U8(), role = (Role)r.U8(), entity = r.U16(), value = r.I16(), extra = r.U8(), x = r.I16() / 100f
                };
                e.z = r.F32();
                outList.Add(e);
            }
        }

        public static ResultsData ReadResults(ref ByteReader r)
        {
            var d = new ResultsData { reason = (EndReason)r.U8(), total = r.I32(), catchScore = r.I32(), toxicPenalty = r.I32(), throws = r.U16(), hits = r.U16() };
            for (int i = 0; i < NetConst.WasteTypeCount; i++) d.counts[i] = r.U16();
            d.distance = r.F32(); d.kg = r.F32(); d.duration = r.F32();
            return d;
        }

        public static LobbyState ReadLobby(ref ByteReader r, string code)
        {
            var s = new LobbyState { code = code, phase = (MatchPhase)r.U8() };
            int n = r.U8();
            for (int i = 0; i < n; i++)
            {
                var p = new LobbyPlayer { clientId = r.U8(), name = r.Str(), role = (Role)r.U8() };
                byte fl = r.U8();
                p.ready = (fl & 1) != 0; p.connected = (fl & 2) != 0;
                s.players.Add(p);
            }
            for (int i = 0; i < 3; i++) s.roleOwner[i] = r.U8();
            if (r.Left > 0) s.hostId = r.U8();
            for (int i = 0; i < s.players.Count && r.Left > 0; i++) s.players[i].team = r.Str(); // v3 teams ("" = guest)
            return s;
        }

        public static PauseInfo ReadPause(ref ByteReader r)
        {
            var p = new PauseInfo { reason = r.U8() };
            int n = r.U8();
            for (int i = 0; i < n; i++) p.missing.Add(new PauseInfo.Missing { id = r.U8(), name = r.Str(), role = (Role)r.U8(), graceS = r.U8() });
            return p;
        }

        public static MatchStartInfo ReadMatchStart(ref ByteReader r, string code) => new MatchStartInfo
        {
            code = code, seed = (int)r.U32(), role = (Role)r.U8(), team = r.U8(), duration = r.U16(), countdown = r.U16() / 1000f, tickRate = r.U8()
        };
    }
}
