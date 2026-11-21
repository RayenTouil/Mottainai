namespace Mottainai
{
    public enum Role : byte { None = 0, LeftPaddler = 1, RightPaddler = 2, Collector = 3 }
    public enum EntityKind : byte { None = 0, Waste = 1, Obstacle = 2, Shield = 3 }
    public enum WasteType : byte { Plastic, Paper, Glass, Organic, Metal, Valuable, Toxic }
    public enum ObstacleType : byte { Rock, BigRock, Iceberg, Wreck, Log, Shark }
    public enum PowerUp : byte { Shield = 0, Magnet = 1, Turbo = 2 }
    public enum MatchPhase : byte { Lobby, Countdown, Running, Finished }
    public enum EndReason : byte { None, TimeUp, Crashed, Aborted }

    public static class NetConst
    {



        public const int TickRate = 60;
        public const float TickDt = 1f / TickRate;
        public const int WasteTypeCount = 7;
        public const int RoleCount = 3;
        public const ushort NoEntity = 0xFFFF;
    }

    public static class EntityCode
    {
        public static byte Pack(EntityKind k, byte sub) => (byte)(((byte)k << 4) | (sub & 0xF));
        public static EntityKind Kind(byte code) => (EntityKind)(code >> 4);
        public static byte Sub(byte code) => (byte)(code & 0xF);
    }

    public static class RoleInfo
    {
        public static string Name(Role r) => r switch
        {
            Role.LeftPaddler => "LEFT PADDLER",
            Role.RightPaddler => "RIGHT PADDLER",
            Role.Collector => "COLLECTOR",
            _ => "SPECTATOR"
        };

        public static string Letter(Role r) => r switch
        {
            Role.LeftPaddler => "L",
            Role.RightPaddler => "R",
            Role.Collector => "C",
            _ => "?"
        };

        public static string Hint(Role r) => r switch
        {
            Role.LeftPaddler => "Swipe DOWN to paddle (turns right). Sync with Right paddler for a BOOST!",
            Role.RightPaddler => "Swipe DOWN to paddle (turns left). Sync with Left paddler for a BOOST!",
            Role.Collector => "TAP floating waste to throw the net! Red toxic = penalty.",
            _ => ""
        };
    }
}
