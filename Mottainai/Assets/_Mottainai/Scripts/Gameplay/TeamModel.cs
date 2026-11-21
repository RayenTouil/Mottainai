using System.Collections.Generic;
using UnityEngine;

namespace Mottainai.Gameplay
{
    public class ModeConfig
    {
        public string name = "Coop 2:00";
        public float duration = 120f;
        public int teams = 1;
        public int hitsToGameOver = 1;
        public bool raftCollisions;
    }

    public interface IGameMode
    {
        ModeConfig Config { get; }
        /// returns true if the team is eliminated
        bool OnObstacleHit(MatchSim sim, TeamState team);
        bool CheckEnd(MatchSim sim, out EndReason reason);
    }

    public class Coop2MinMode : IGameMode
    {
        public ModeConfig Config { get; }

        public Coop2MinMode(TuningData t)
        {
            Config = new ModeConfig { duration = t.matchDuration, teams = 1, hitsToGameOver = t.hitsToGameOver, raftCollisions = false };
        }

        public bool OnObstacleHit(MatchSim sim, TeamState team) => team.health.TakeHit(sim.Time);

        public bool CheckEnd(MatchSim sim, out EndReason reason)
        {
            foreach (var t in sim.Teams)
                if (t.health.Dead) { reason = EndReason.Crashed; return true; }
            if (sim.Time >= Config.duration) { reason = EndReason.TimeUp; return true; }
            reason = EndReason.None;
            return false;
        }
    }

    public class HealthComponent
    {
        public int max, hp;
        public float invulnUntil;
        public float invulnAfterHit = 1.5f;
        public bool Dead => hp <= 0;

        public HealthComponent(int max) { this.max = Mathf.Max(1, max); hp = this.max; }

        public bool TakeHit(float now)
        {
            if (Dead || now < invulnUntil) return false;
            hp--;
            invulnUntil = now + invulnAfterHit;
            return Dead;
        }
    }

    public class ScoreService
    {
        public int catchScore, throws, hits;
        public readonly int[] counts = new int[NetConst.WasteTypeCount];
        public float kg;
        readonly float[] chain = new float[16];
        int chainLen;
        bool comboArmed;

        public int AddCatch(WasteType type, float now, TuningData t, out bool combo)
        {
            combo = false;
            counts[(int)type]++;
            kg += WasteCatalog.Kg[(int)type];
            if (WasteCatalog.IsToxic(type)) { chainLen = 0; comboArmed = false; return 0; }

            int pts = t.wasteScores[(int)type];
            if (comboArmed)
            {
                combo = true;
                comboArmed = false;
                chainLen = 0;
                pts = Mathf.RoundToInt(pts * t.comboMultiplier);
            }
            else
            {
                int w = 0;
                for (int i = 0; i < chainLen; i++) if (now - chain[i] <= t.comboWindow) chain[w++] = chain[i];
                chainLen = w;
                if (chainLen < chain.Length) chain[chainLen++] = now;
                if (chainLen >= t.comboCount) { comboArmed = true; chainLen = 0; }
            }
            catchScore += pts;
            return pts;
        }

        public bool ComboArmed => comboArmed;
        public int ToxicPenalty(TuningData t) => counts[(int)WasteType.Toxic] * Mathf.Abs(t.wasteScores[(int)WasteType.Toxic]);
        public int Total(TuningData t) => catchScore - ToxicPenalty(t);
    }

    public class TeamState
    {
        public readonly int id;
        public readonly RaftSim raft = new RaftSim();
        public readonly HealthComponent health;
        public readonly ScoreService score = new ScoreService();
        public readonly bool[] bot = { false, true, true, true }; // indexed by Role
        public readonly float[] strokeReady = new float[4];
        public readonly BotBrain brain = new BotBrain();
        public float shieldUntil = -1f, netReadyAt, startZ;
        public bool shieldActive;
        public float magnetUntil = -1f, turboUntil = -1f;
        public bool magnetActive, turboActive;
        public readonly List<int> pendingNets = new List<int>();

        public TeamState(int id, int hits, float startX)
        {
            this.id = id;
            health = new HealthComponent(hits);
            raft.x = startX;
        }

        public bool IsBot(Role r) => bot[(int)r];
    }
}
