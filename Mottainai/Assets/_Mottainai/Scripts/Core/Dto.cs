using System;

namespace Mottainai
{
    [Serializable] public class LoginRequest { public string name; public string token; public string device; }
    [Serializable] public class RoomInfo { public string code; public int players; public string phase; }
    [Serializable] public class LoginResponse { public string token; public string name; public string serverName; public int gamePort; public RoomInfo[] rooms; }
    [Serializable] public class RoomList { public RoomInfo[] rooms; }
    [Serializable] public class ContentFile { public string name; public long size; public string hash; }
    [Serializable] public class ContentManifest { public string version; public long totalBytes; public ContentFile[] files; }
    /// Relative to the API host:port the client typed (server never knows its public playit address).
    [Serializable] public class ServerJson { public string login = "/login", rooms = "/rooms", content = "/content/", telemetry = "/telemetry", labDownload = "/lab/download", labResult = "/lab/result"; }

    [Serializable]
    public class TelemetryDto
    {
        public string token, name, device, net, slice, room;
        public float rttAvg, rttP95, jitter, fps, dlKbps, ulKbps, loss, interpMs, pps;
    }

    [Serializable]
    public class LabResultDto
    {
        public string name, device, net, slice, time;
        public float rttAvg, rttMin, rttP95, rttMax, jitter, loss, downMbps, downMB, downSec;
        public int sent, received;
    }
}
