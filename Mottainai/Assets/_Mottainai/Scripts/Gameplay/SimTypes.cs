using UnityEngine;

namespace Mottainai.Gameplay
{
    public struct SimEntity
    {
        public ushort id;
        public EntityKind kind;
        public byte sub;
        public bool alive;
        public float x, z, baseX, y, yaw, phase, radius, halfLen;
        public byte flags; // shark: 0 submerged, 1 fin telegraph, 2 active

        public byte Code => EntityCode.Pack(kind, sub);
        public bool Dangerous => kind == EntityKind.Obstacle && (sub != (byte)ObstacleType.Shark || flags == 2);
    }

    public enum EvType : byte { Countdown, Go, Stroke, Sync, NetThrow, Catch, Miss, Hit, ShieldOn, ShieldOff, GameOver, Combo, BotTakeover }

    public struct MatchEvent
    {
        public EvType type;
        public byte team;
        public Role role;
        public ushort entity;
        public short value;
        public byte extra;
        public float x, z;
    }

    public class ResultsData
    {
        public EndReason reason;
        public int total, catchScore, toxicPenalty, throws, hits;
        public int[] counts = new int[NetConst.WasteTypeCount];
        public float distance, kg, duration;
        public float Accuracy => throws > 0 ? (float)hits / throws : 0f;
    }

    public static class FlowField
    {
        public static Vector2 Sample(int seed, float x, float z, TuningData t)
        {
            float hw = t.riverWidth * 0.5f;
            float edge = Mathf.Clamp01(Mathf.Abs(x) / hw);
            float s = (seed % 1000) * 0.01f;
            float vz = t.currentSpeed * (1f + 0.15f * Mathf.Sin(z * 0.031f + s)) * (1f - 0.35f * edge * edge);
            float vx = 0.35f * Mathf.Sin(z * 0.045f + s * 1.7f) * (1f - edge * edge);
            return new Vector2(vx, vz);
        }
    }
}
