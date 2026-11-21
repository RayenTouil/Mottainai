using System;

namespace Mottainai
{
    public class LaunchArgs
    {
        public bool server, headless, host, log = true;
        public ushort port = NetConst.GamePort, http = NetConst.HttpPort;
        public int tick = NetConst.TickRate;

        public static LaunchArgs Parse(string[] a)
        {
            var r = new LaunchArgs();
            for (int i = 0; i < a.Length; i++)
            {
                string s = a[i].ToLowerInvariant();
                string next = i + 1 < a.Length ? a[i + 1] : null;
                switch (s)
                {
                    case "-server": r.server = true; break;
                    case "-headless": case "-nographics": r.headless = true; break;
                    case "-log": r.log = true; break;
                    case "-nolog": r.log = false; break;
                    case "-port": if (ushort.TryParse(next, out var p)) { r.port = p; i++; } break;
                    case "-http": if (ushort.TryParse(next, out var h)) { r.http = h; i++; } break;
                    case "-tick": if (int.TryParse(next, out var t)) { r.tick = t; i++; } break;
                }
            }
            return r;
        }
    }

    /// Server assembly registers itself here (it is excluded from mobile builds).
    public static class AppHooks
    {
        public static Func<LaunchArgs, bool> StartServer;
        public static Func<string> ServerStatus;
        public static bool ServerAvailable => StartServer != null;
        public static bool ServerRunning;
        public static bool HostMode;
    }
}
