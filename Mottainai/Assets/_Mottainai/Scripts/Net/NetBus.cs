using System;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;

namespace Mottainai.Net
{
    public enum Msg : byte
    {
        // client -> server
        Hello = 1, Input = 2, Stroke = 3, Throw = 4, Ping = 5, CreateRoom = 6, JoinRoom = 7, PickRole = 8,
        SetReady = 9, StartMatch = 10, LeaveRoom = 11, BackToLobby = 12,
        // server -> client
        Welcome = 32, Pong = 33, Lobby = 34, Error = 35, MatchStart = 36, Snapshot = 37, Events = 38, Results = 39
    }

    /// All gameplay traffic goes through NGO unnamed custom messages (1 byte type header).
    /// In host mode, local delivery is done in-process (no transport round trip).
    public static class NetBus
    {
        public static NetworkManager NM { get; private set; }
        public static UnityTransport Transport { get; private set; }
        public delegate void ServerHandler(ulong sender, Msg type, FastBufferReader reader);
        public delegate void ClientHandler(Msg type, FastBufferReader reader);
        public static ServerHandler OnServer;
        public static ClientHandler OnClient;

        public static long BytesSent, BytesRecv, MsgsSent, MsgsRecv;
        static bool hooked;

        public static NetworkManager EnsureManager()
        {
            if (NM != null) return NM;
            var go = new GameObject("NetworkManager");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.SetActive(false);
            NM = go.AddComponent<NetworkManager>();
            Transport = go.AddComponent<UnityTransport>();
            if (NM.NetworkConfig == null) NM.NetworkConfig = new NetworkConfig();
            NM.NetworkConfig.NetworkTransport = Transport;
            NM.NetworkConfig.TickRate = (uint)NetConst.TickRate;
            NM.NetworkConfig.EnableSceneManagement = false;
            NM.NetworkConfig.ConnectionApproval = false;
            NM.NetworkConfig.ForceSamePrefabs = false;
            NM.NetworkConfig.ClientConnectionBufferTimeout = 10;
            Transport.MaxPayloadSize = 16384;
            Transport.MaxPacketQueueSize = 512;
            Transport.HeartbeatTimeoutMS = 500;
            Transport.DisconnectTimeoutMS = 2500;
            Transport.ConnectTimeoutMS = 1000;
            Transport.MaxConnectAttempts = 10;
            go.SetActive(true);
            NM.OnServerStarted += Hook;
            NM.OnClientStarted += Hook;
            NM.OnServerStopped += _ => hooked = false;
            NM.OnClientStopped += _ => hooked = false;
            return NM;
        }

        static void Hook()
        {
            if (hooked || NM.CustomMessagingManager == null) return;
            hooked = true;
            NM.CustomMessagingManager.OnUnnamedMessage += Receive;
        }

        public static void Unhook() => hooked = false;

        public static bool IsServer => NM != null && NM.IsServer;
        public static bool IsHost => NM != null && NM.IsHost;
        public static bool IsConnectedClient => NM != null && NM.IsConnectedClient;

        static void Receive(ulong sender, FastBufferReader reader)
        {
            MsgsRecv++;
            BytesRecv += reader.Length + 28; // approx UDP/IP + UTP header
            reader.ReadValueSafe(out byte t);
            if (NM.IsServer) OnServer?.Invoke(sender, (Msg)t, reader);
            else OnClient?.Invoke((Msg)t, reader);
        }

        public static FastBufferWriter Begin(Msg type, int size = 128, int max = 8192)
        {
            var w = new FastBufferWriter(size, Allocator.Temp, max);
            w.WriteValueSafe((byte)type);
            return w;
        }

        public static void ToServer(FastBufferWriter w, NetworkDelivery d)
        {
            try
            {
                if (NM == null) return;
                if (NM.IsHost) { Local(w, true, NM.LocalClientId); return; }
                if (!NM.IsConnectedClient) return;
                MsgsSent++; BytesSent += w.Length + 28;
                NM.CustomMessagingManager.SendUnnamedMessage(NetworkManager.ServerClientId, w, d);
            }
            finally { w.Dispose(); }
        }

        /// Does not dispose: used for fan-out.
        public static void ToClient(ulong client, FastBufferWriter w, NetworkDelivery d)
        {
            if (NM == null || !NM.IsServer) return;
            if (NM.IsHost && client == NM.LocalClientId) { Local(w, false, client); return; }
            if (!NM.ConnectedClients.ContainsKey(client)) return;
            MsgsSent++; BytesSent += w.Length + 28;
            try { NM.CustomMessagingManager.SendUnnamedMessage(client, w, d); }
            catch (Exception e) { Debug.LogWarning("[NetBus] send failed: " + e.Message); }
        }

        public static long LocalBytesToClient;

        static void Local(FastBufferWriter w, bool toServer, ulong localId)
        {
            var r = new FastBufferReader(w, Allocator.Temp);
            try
            {
                r.ReadValueSafe(out byte t);
                if (toServer) OnServer?.Invoke(localId, (Msg)t, r);
                else
                {
                    MsgsRecv++;
                    BytesRecv += w.Length + 28;
                    OnClient?.Invoke((Msg)t, r);
                }
            }
            finally { r.Dispose(); }
        }
    }
}
