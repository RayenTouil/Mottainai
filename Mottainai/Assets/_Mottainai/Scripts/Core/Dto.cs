using System;

namespace Mottainai
{
    [Serializable] public class ServerConfig { public string baseUrl; }
    [Serializable] public class LoginRequest { public string name; }
    [Serializable] public class LoginResponse { public string token; public string name; }
    [Serializable] public class RoomCreated { public string code; }
    [Serializable] public class HealthResponse { public bool ok; public int protocolVersion; public string colo; }
    [Serializable] public class ContentFile { public string name; public long size; public string hash; }
    [Serializable] public class ContentManifest { public string version; public long totalBytes; public ContentFile[] files; }

    /// One Network Lab run (POST /api/telemetry; columns of /api/telemetry.csv).
    [Serializable]
    public class LabResultDto
    {
        public string ts, device, netType, edgeColo, roomColo, note;
        public float edgeRttAvg, edgeRttP95, edgeJitter, roomRttAvg, roomRttP95, roomJitter, downMbps, upMbps, downMB, upMB;
        public int edgeN, roomN;
    }
}
