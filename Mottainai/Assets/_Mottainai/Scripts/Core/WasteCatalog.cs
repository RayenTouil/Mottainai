using UnityEngine;

namespace Mottainai
{
    public static class WasteCatalog
    {
        public static readonly string[] Names = { "Plastic", "Paper", "Glass", "Organic", "Metal", "Valuable", "Toxic" };
        public static readonly Color32[] Colors =
        {
            new Color32(0xF2, 0xC2, 0x00, 255), new Color32(0x1E, 0x6F, 0xD9, 255), new Color32(0x2E, 0x9E, 0x44, 255),
            new Color32(0x7A, 0x4A, 0x21, 255), new Color32(0x8C, 0x95, 0x9F, 255), new Color32(0x9B, 0x4D, 0xFF, 255), // v3: valuable = purple gem
            new Color32(0xD6, 0x28, 0x28, 255)
        };
        public static readonly float[] Kg = { 0.05f, 0.12f, 0.35f, 0.15f, 0.2f, 1.5f, 2.5f };
        public static readonly float[] Radius = { 0.35f, 0.4f, 0.32f, 0.3f, 0.3f, 0.45f, 0.5f };
        public static bool IsToxic(WasteType t) => t == WasteType.Toxic;
    }

    public static class ObstacleCatalog
    {
        // collision: capsule along X with radius + half length
        public static readonly float[] Radius = { 1.0f, 2.0f, 1.7f, 1.3f, 0.45f, 0.8f };
        public static readonly float[] HalfLen = { 0f, 0f, 0.4f, 2.2f, 1.6f, 0f };
        public static readonly string[] Names = { "Rock", "Big Rock", "Iceberg", "Old Boat", "Log", "Shark" };
        public static readonly float[] Weights = { 30, 14, 14, 12, 20, 10 };
    }
}
