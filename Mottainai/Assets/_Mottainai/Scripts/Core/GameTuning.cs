using System;
using UnityEngine;

namespace Mottainai
{
    [Serializable]
    public class TuningData
    {
        [Header("River / raft")]
        public float riverWidth = 24f, raftWidth = 2.4f, raftRadius = 1.3f, currentSpeed = 2.5f;
        [Header("Paddling")]
        public float strokeImpulse = 1.6f, backStrokeFactor = 0.6f, yawKickDeg = 28f, yawDamping = 3f;
        public float maxSpeed = 7f, waterDrag = 0.6f, lateralDrag = 2.0f, syncWindow = 0.25f, syncBoost = 1.3f;
        public float strokeCooldown = 0.18f, minStrokePower = 0.4f, maxStrokePower = 1f, maxHeadingDeg = 75f;
        [Header("Net")]
        public float netTravel = 0.25f, netRange = 7f, netCooldown = 0.6f, netRadius = 1.0f, hitRadiusScale = 1.2f;
        [Header("Match")]
        public float matchDuration = 120f, countdown = 3f;
        public int hitsToGameOver = 1;
        [Header("Course")]
        public float chunkLength = 40f, courseLength = 650f, expectedAvgSpeed = 5.4f;
        public float obstacleSpacingStart = 25f, obstacleSpacingEnd = 12f, obstacleRampTime = 105f, freeLaneFactor = 3f;
        public float wasteClusterSpacing = 7f, wasteNearObstacleChance = 0.3f;
        public int wasteClusterMin = 3, wasteClusterMax = 5;
        public float shieldSpacing = 150f, shieldMinTime = 20f, shieldDuration = 6f, shieldBlinkTime = 1.5f;
        public float magnetDuration = 8f, magnetRadiusScale = 1.8f, turboDuration = 6f, turboImpulseScale = 1.35f, turboMaxSpeedBonus = 1.5f;
        public float sharkSpeed = 3.5f, sharkAmplitude = 6f, sharkTelegraph = 1.5f, sharkMinDistance = 60f;
        // Rock, BigRock, Iceberg, Wreck, Log, Shark
        public float[] obstacleWeights = { 30, 14, 14, 12, 20, 24 };
        [Header("Score")]
        public float[] wasteWeights = { 30, 15, 15, 12, 13, 3, 12 };
        public int[] wasteScores = { 10, 10, 15, 5, 20, 100, -30 };
        public int comboCount = 3;
        public float comboWindow = 3f, comboMultiplier = 1.5f;
        [Header("Net sim")]
        public float snapshotAhead = 60f, snapshotBehind = 15f, lagCompCapMs = 150f;
        public float botStrokeInterval = 0.5f, botAccuracy = 0.85f;
        [Header("Results")]
        public int[] starScores = { 250, 700, 1300 };
    }

    [CreateAssetMenu(menuName = "Mottainai/Game Tuning")]
    public class GameTuning : ScriptableObject
    {
        public TuningData data = new TuningData();

        static TuningData _current;
        public static TuningData Current
        {
            get
            {
                if (_current == null)
                {
                    var so = Resources.Load<GameTuning>("Tuning/GameTuning");
                    _current = so != null ? JsonUtility.FromJson<TuningData>(JsonUtility.ToJson(so.data)) : new TuningData();
                }
                return _current;
            }
        }

        public static void ApplyJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return;
            try { JsonUtility.FromJsonOverwrite(json, Current); }
            catch (Exception e) { Debug.LogWarning("[Tuning] bad JSON override: " + e.Message); }
        }

        public static string ToJson() => JsonUtility.ToJson(Current, true);
    }
}
