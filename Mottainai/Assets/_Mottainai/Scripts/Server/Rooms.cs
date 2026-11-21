using System.Collections.Generic;
using System.Linq;
using Mottainai.Gameplay;
using Mottainai.Net;
using Unity.Netcode;
using UnityEngine;

namespace Mottainai.Server
{
    public class Session
    {
        public const ulong NoClient = ulong.MaxValue;
        public string token, name;
        public ulong clientId = NoClient;
        public Room room;
        public Role role;
        public bool ready;
        public uint snapSeq;
        public float interpMs, lastSeen, disconnectedAt;
        public bool Connected => clientId != NoClient;
    }

    public class Room
    {
        public readonly string code;
        public readonly List<Session> members = new List<Session>();
        public ulong creator;
        public MatchSim sim;
        public float emptySince = -1f;
        bool resultsSent;
        readonly List<ulong> sendClients = new List<ulong>(8);
        readonly List<uint> sendSeqs = new List<uint>(8);
        readonly List<Session> sendSessions = new List<Session>(8);

        public Room(string code) { this.code = code; }

        public MatchPhase Phase => sim == null ? MatchPhase.Lobby : sim.Phase;
        public bool Running => sim != null && (sim.Phase == MatchPhase.Countdown || sim.Phase == MatchPhase.Running);

        public Session Holder(Role r) => members.FirstOrDefault(m => m.role == r && r != Role.None);

        public Role FirstFreeRole()
        {
            foreach (var r in new[] { Role.LeftPaddler, Role.RightPaddler, Role.Collector })
                if (Holder(r) == null) return r;
            return Role.None;
        }

        public LobbyState BuildLobby(string message = "")
        {
            var s = new LobbyState { code = code, phase = Phase, creator = creator, message = message };
            foreach (var m in members)
                s.players.Add(new LobbyPlayer { clientId = m.clientId, name = m.name, role = m.role, ready = m.ready, connected = m.Connected });
            return s;
        }

        public void BroadcastLobby(string message = "")
        {
            var state = BuildLobby(message);
            foreach (var m in members)
            {
                if (!m.Connected) continue;
                var w = NetBus.Begin(Msg.Lobby, 256, 4096);
                Codec.WriteLobby(ref w, state);
                NetBus.ToClient(m.clientId, w, NetworkDelivery.ReliableSequenced);
                w.Dispose();
            }
        }

        public void StartMatch(SessionLog log)
        {
            foreach (var m in members)
                if (m.role == Role.None && m.Connected) m.role = FirstFreeRole();
            var t = GameTuning.Current;
            int seed = Random.Range(1, int.MaxValue);
            sim = new MatchSim(seed, t, new Coop2MinMode(t));
            foreach (Role r in new[] { Role.LeftPaddler, Role.RightPaddler, Role.Collector })
            {
                var h = Holder(r);
                sim.Teams[0].bot[(int)r] = h == null || !h.Connected;
            }
            resultsSent = false;
            foreach (var m in members) SendMatchStart(m);
            BroadcastLobby("Match started");
            log?.Event("match_start", code, $"seed={seed} humans={members.Count(m => m.Connected && m.role != Role.None)}");
        }

        public void SendMatchStart(Session m)
        {
            if (!m.Connected || sim == null) return;
            var info = new MatchStartInfo { code = code, seed = sim.Seed, role = m.role, team = 0, duration = sim.Mode.Config.duration, countdown = sim.CountdownLeft };
            var w = NetBus.Begin(Msg.MatchStart, 64);
            Codec.WriteMatchStart(ref w, info);
            NetBus.ToClient(m.clientId, w, NetworkDelivery.ReliableSequenced);
            w.Dispose();
        }

        public void OnMemberLost(Session s)
        {
            if (Running && s.role != Role.None) sim.SetBot(0, s.role, true);
            BroadcastLobby($"{s.name} disconnected - bot took over");
        }

        public void OnMemberBack(Session s)
        {
            if (Running && s.role != Role.None)
            {
                sim.SetBot(0, s.role, false);
                SendMatchStart(s);
            }
            BroadcastLobby($"{s.name} is back");
        }

        public void Tick(float dt, SessionLog log)
        {
            if (sim == null) return;
            if (sim.Phase == MatchPhase.Finished && resultsSent) return;
            sim.Step(dt);

            sendClients.Clear(); sendSeqs.Clear(); sendSessions.Clear();
            foreach (var m in members)
                if (m.Connected) { sendClients.Add(m.clientId); sendSeqs.Add(m.snapSeq); sendSessions.Add(m); }

            if (sim.Events.Count > 0)
            {
                for (int from = 0; from < sim.Events.Count; from += 200)
                {
                    int n = Mathf.Min(200, sim.Events.Count - from);
                    var w = NetBus.Begin(Msg.Events, 64 + n * 16, 8192);
                    w.WriteValueSafe(sim.Tick);
                    Codec.WriteEvents(ref w, sim.Events, from, n);
                    foreach (var c in sendClients) NetBus.ToClient(c, w, NetworkDelivery.ReliableSequenced);
                    w.Dispose();
                }
                sim.Events.Clear();
            }

            if (sendClients.Count > 0) Codec.SendSnapshot(sim, sim.Teams[0], sendClients, sendSeqs);
            for (int i = 0; i < sendSessions.Count; i++) sendSessions[i].snapSeq = sendSeqs[i];

            if (sim.Phase == MatchPhase.Finished && !resultsSent)
            {
                resultsSent = true;
                var w = NetBus.Begin(Msg.Results, 128);
                Codec.WriteResults(ref w, sim.Results);
                foreach (var c in sendClients) NetBus.ToClient(c, w, NetworkDelivery.ReliableSequenced);
                w.Dispose();
                foreach (var m in members) m.ready = false;
                BroadcastLobby("Match over");
                var r = sim.Results;
                log?.Event("match_end", code, $"reason={r.reason} total={r.total} toxic={r.toxicPenalty} acc={r.Accuracy:F2} dist={r.distance:F0}");
            }
        }
    }

    public class RoomManager
    {
        public readonly Dictionary<string, Room> rooms = new Dictionary<string, Room>();
        public readonly Dictionary<string, Session> byToken = new Dictionary<string, Session>();
        public readonly Dictionary<ulong, Session> byClient = new Dictionary<ulong, Session>();
        readonly object tokenLock = new object();
        readonly SessionLog log;
        const string Letters = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public RoomManager(SessionLog log) { this.log = log; }

        // called from HTTP thread
        public Session Login(string name, string token)
        {
            lock (tokenLock)
            {
                if (!string.IsNullOrEmpty(token) && byToken.TryGetValue(token, out var existing))
                {
                    existing.name = string.IsNullOrEmpty(name) ? existing.name : name;
                    return existing;
                }
                var s = new Session { token = System.Guid.NewGuid().ToString("N").Substring(0, 16), name = Sanitize(name) };
                byToken[s.token] = s;
                return s;
            }
        }

        static string Sanitize(string n)
        {
            n = string.IsNullOrWhiteSpace(n) ? "Player" : n.Trim();
            return n.Length > 14 ? n.Substring(0, 14) : n;
        }

        public string RoomsJson()
        {
            var list = new RoomList { rooms = rooms.Values.Select(r => new RoomInfo { code = r.code, players = r.members.Count(m => m.Connected), phase = r.Phase.ToString() }).ToArray() };
            return JsonUtility.ToJson(list);
        }

        string NewCode()
        {
            for (int tries = 0; tries < 1000; tries++)
            {
                var c = new char[4];
                for (int i = 0; i < 4; i++) c[i] = Letters[Random.Range(0, Letters.Length)];
                var code = new string(c);
                if (!rooms.ContainsKey(code)) return code;
            }
            return "ZZZZ";
        }

        public void Hello(ulong clientId, string token, string name)
        {
            Session s;
            lock (tokenLock)
            {
                if (string.IsNullOrEmpty(token) || !byToken.TryGetValue(token, out s))
                {
                    s = new Session { token = string.IsNullOrEmpty(token) ? System.Guid.NewGuid().ToString("N").Substring(0, 16) : token, name = Sanitize(name) };
                    byToken[s.token] = s;
                }
            }
            if (s.Connected && s.clientId != clientId) byClient.Remove(s.clientId);
            s.clientId = clientId;
            if (!string.IsNullOrEmpty(name)) s.name = Sanitize(name);
            s.lastSeen = Time.realtimeSinceStartup;
            byClient[clientId] = s;

            var w = NetBus.Begin(Msg.Welcome, 128);
            w.WriteValueSafe(clientId);
            w.WriteValueSafe(s.name);
            w.WriteValueSafe(s.token);
            NetBus.ToClient(clientId, w, NetworkDelivery.ReliableSequenced);
            w.Dispose();
            log?.Event("hello", s.room?.code ?? "", $"{s.name} client={clientId}");

            if (s.room != null && rooms.ContainsKey(s.room.code)) s.room.OnMemberBack(s);
            else s.room = null;
        }

        public void Disconnected(ulong clientId)
        {
            if (!byClient.TryGetValue(clientId, out var s)) return;
            byClient.Remove(clientId);
            s.clientId = Session.NoClient;
            s.disconnectedAt = Time.realtimeSinceStartup;
            log?.Event("disconnect", s.room?.code ?? "", s.name);
            s.room?.OnMemberLost(s);
        }

        public Session Get(ulong clientId) => byClient.TryGetValue(clientId, out var s) ? s : null;

        void Error(Session s, string text)
        {
            var w = NetBus.Begin(Msg.Error, 128);
            w.WriteValueSafe(text);
            NetBus.ToClient(s.clientId, w, NetworkDelivery.ReliableSequenced);
            w.Dispose();
        }

        public void CreateRoom(Session s)
        {
            Leave(s);
            var room = new Room(NewCode()) { creator = s.clientId };
            rooms[room.code] = room;
            room.members.Add(s);
            s.room = room;
            s.role = room.FirstFreeRole();
            s.ready = false;
            log?.Event("room_create", room.code, s.name);
            room.BroadcastLobby($"Room {room.code} created");
        }

        public void JoinRoom(Session s, string code)
        {
            code = (code ?? "").Trim().ToUpperInvariant();
            if (!rooms.TryGetValue(code, out var room)) { Error(s, $"Room {code} not found"); return; }
            if (s.room == room) { room.BroadcastLobby(); if (room.Running) room.SendMatchStart(s); return; }
            Leave(s);
            if (room.members.Count(m => m.Connected) >= 3 && room.FirstFreeRole() == Role.None)
            {
                // allow replacing a disconnected member's slot
                var ghost = room.members.FirstOrDefault(m => !m.Connected);
                if (ghost == null) { Error(s, "Room is full (3 players)"); return; }
                room.members.Remove(ghost);
                ghost.room = null;
                s.role = ghost.role;
            }
            else s.role = room.FirstFreeRole();
            room.members.Add(s);
            s.room = room;
            s.ready = false;
            if (room.Running && s.role != Role.None)
            {
                room.sim.SetBot(0, s.role, false);
                room.SendMatchStart(s);
            }
            log?.Event("room_join", room.code, s.name);
            room.BroadcastLobby($"{s.name} joined");
        }

        public void Leave(Session s)
        {
            var room = s.room;
            if (room == null) return;
            if (room.Running && s.role != Role.None) room.sim.SetBot(0, s.role, true);
            room.members.Remove(s);
            s.room = null;
            s.role = Role.None;
            s.ready = false;
            if (room.members.Count == 0) { room.sim?.Abort(); rooms.Remove(room.code); }
            else
            {
                if (room.creator == s.clientId) room.creator = room.members[0].clientId;
                room.BroadcastLobby($"{s.name} left");
            }
        }

        public void PickRole(Session s, Role r)
        {
            var room = s.room;
            if (room == null) return;
            if (room.Running) { Error(s, "Match in progress"); return; }
            var holder = room.Holder(r);
            if (r != Role.None && holder != null && holder != s)
            {
                if (holder.Connected) { Error(s, $"{RoleInfo.Name(r)} is taken by {holder.name}"); return; }
                holder.role = Role.None;
            }
            s.role = r;
            room.BroadcastLobby();
        }

        public void SetReady(Session s, bool ready)
        {
            if (s.room == null) return;
            s.ready = ready;
            s.room.BroadcastLobby();
        }

        public void StartMatch(Session s)
        {
            var room = s.room;
            if (room == null) return;
            if (room.Running) { room.SendMatchStart(s); return; }
            room.StartMatch(log);
        }

        public void BackToLobby(Session s)
        {
            var room = s.room;
            if (room == null || room.Running) return;
            room.sim = null;
            room.BroadcastLobby();
        }

        public void Tick(float dt)
        {
            float now = Time.realtimeSinceStartup;
            List<string> dead = null;
            foreach (var room in rooms.Values)
            {
                room.Tick(dt, log);
                bool anyConnected = room.members.Any(m => m.Connected);
                if (anyConnected) room.emptySince = -1f;
                else if (room.emptySince < 0f) room.emptySince = now;
                else if (now - room.emptySince > 120f) (dead ??= new List<string>()).Add(room.code);
            }
            if (dead != null)
                foreach (var code in dead)
                {
                    foreach (var m in rooms[code].members) m.room = null;
                    rooms.Remove(code);
                    log?.Event("room_closed", code, "empty");
                }
        }
    }
}
