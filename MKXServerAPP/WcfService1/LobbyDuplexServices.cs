// WcfService1/LobbyDuplexService.cs
using System;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using System.Threading.Tasks;
using SharedContracts;
using static WcfService1.InMemoryStore;

namespace WcfService1
{
    [ServiceBehavior(InstanceContextMode = InstanceContextMode.Single, ConcurrencyMode = ConcurrencyMode.Multiple)]
    public class LobbyDuplexService : ILobbyDuplexService
    {
        private ILobbyClientCallbacks GetCallback() =>
            OperationContext.Current.GetCallbackChannel<ILobbyClientCallbacks>();

        public Task<bool> LoginAsync(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
                throw new FaultException("Username required.");

            // Ensure uniqueness in shared store
            if (!Players.TryAdd(username, new PlayerInfo { Username = username, LoggedInAt = DateTime.UtcNow }))
                return Task.FromResult(false);

            ChannelUserMap.AddForCurrentChannel(username);
            // Register callback
            var ok = DuplexHub.Register(username, GetCallback());
            if (!ok) { Players.TryRemove(username, out _); return Task.FromResult(false); }

            DuplexHub.BroadcastPresence(username, isOnline: true);
            // push lobby snapshot on login
            var snap = new LobbySummary { LobbyRooms = Rooms.Values.ToList(), OnlinePlayers = Players.Count };
            DuplexHub.BroadcastLobbySummary(snap);

            return Task.FromResult(true);
        }

        public Task LogoutAsync()
        {
            var cb = GetCallback();
            // NOTE: Ideally, you map callback -> username at Register time and look it up here.
            var user = Players.Keys.FirstOrDefault(); // placeholder; wire to your mapping if added

            if (user != null)
            {
                Players.TryRemove(user, out _);
                DuplexHub.Unregister(user);
                DuplexHub.BroadcastPresence(user, isOnline: false);
                var snap = new LobbySummary { LobbyRooms = Rooms.Values.ToList(), OnlinePlayers = Players.Count };
                DuplexHub.BroadcastLobbySummary(snap);
            }
            return Task.CompletedTask;
        }

        public void Heartbeat()
        {
            // Optional: refresh player timestamp
        }

        public Task JoinLobbyAsync()
        {
            var user = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            DuplexHub.SubscribeLobby(user, on: true);
            // push a fresh snapshot right away to this caller
            var snap = new LobbySummary { LobbyRooms = Rooms.Values.ToList(), OnlinePlayers = Players.Count };
            GetCallback().OnLobbySummary(snap);
            return Task.CompletedTask;
        }

        public Task LeaveLobbyAsync()
        {
            var user = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            DuplexHub.SubscribeLobby(user, on: false);
            return Task.CompletedTask;
        }

        public Task SubscribeRoomAsync(string roomId)
        {
            var user = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            DuplexHub.SubscribeRoom(user, roomId, on: true);
            return Task.CompletedTask;
        }

        public Task UnsubscribeRoomAsync(string roomId)
        {
            var user = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            DuplexHub.SubscribeRoom(user, roomId, on: false);
            return Task.CompletedTask;
        }

        public Task<LobbyRoomInfo> CreateRoomAsync(string roomName, int capacity, bool isRanked)
        {
            if (string.IsNullOrWhiteSpace(roomName)) throw new FaultException("Room name required");
            if (capacity < 1) throw new FaultException("Capacity must be >= 1");

            var room = new LobbyRoomInfo
            {
                RoomId = Guid.NewGuid().ToString("N"),
                RoomName = roomName.Trim(),
                Capacity = capacity,
                IsRanked = isRanked,
                Players = new System.Collections.Generic.List<string>()
            };
            Rooms[room.RoomId] = room;

            DuplexHub.BroadcastRoomCreated(room);
            DuplexHub.BroadcastLobbySummary(new LobbySummary { LobbyRooms = Rooms.Values.ToList(), OnlinePlayers = Players.Count });

            return Task.FromResult(room);
        }

        public Task<bool> JoinRoomAsync(string roomId)
        {
            var username = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            if (!Rooms.TryGetValue(roomId, out var room)) throw new FaultException("Room not found.");

            if (!room.Players.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                if (room.Players.Count >= room.Capacity) throw new FaultException("Room is full.");
                room.Players.Add(username);
                Rooms[room.RoomId] = room;
                DuplexHub.BroadcastRoomUpdated(room);

                var joinMsg = new ChatMessage
                {
                    RoomId = roomId,
                    Sender = "[system]",
                    Body = $"{username} joined the room",
                    TimestampUtc = DateTime.UtcNow
                };
                AppendRoom(roomId, joinMsg);
                DuplexHub.BroadcastRoomMessage(joinMsg);
            }

            DuplexHub.SubscribeRoom(username, roomId, on: true);
            return Task.FromResult(true);
        }

        public Task<bool> LeaveRoomAsync(string roomId)
        {
            var username = ChannelUserMap.GetForCurrentChannel() ?? throw new FaultException("Not logged in");
            if (!Rooms.TryGetValue(roomId, out var room)) return Task.FromResult(false);

            room.Players = room.Players.Where(u => !u.Equals(username, StringComparison.OrdinalIgnoreCase)).ToList();
            Rooms[room.RoomId] = room;
            DuplexHub.BroadcastRoomUpdated(room);

            var leaveMsg = new ChatMessage
            {
                RoomId = roomId,
                Sender = "[system]",
                Body = $"{username} left the room",
                TimestampUtc = DateTime.UtcNow
            };
            AppendRoom(roomId, leaveMsg);
            DuplexHub.BroadcastRoomMessage(leaveMsg);

            DuplexHub.SubscribeRoom(username, roomId, on: false);
            return Task.FromResult(true);
        }

        public void SendRoomMessage(ChatMessage message)
        {
            if (message == null) throw new FaultException("Message required");
            if (!Rooms.ContainsKey(message.RoomId)) throw new FaultException("Room not found");

            message.TimestampUtc = DateTime.UtcNow;
            AppendRoom(message.RoomId, message);
            DuplexHub.BroadcastRoomMessage(message);
        }

        public void SendPrivateMessage(ChatMessage message)
        {
            if (message == null) throw new FaultException("Message required");
            if (string.IsNullOrWhiteSpace(message.PrivateRecipient))
                throw new FaultException("PrivateRecipient required");

            message.IsPrivate = true;
            message.TimestampUtc = DateTime.UtcNow;
            AppendDm(message.Sender, message.PrivateRecipient, message);
            DuplexHub.BroadcastPrivateMessage(message);
        }

        public Task<SharedFile> UploadFileAsync(SharedFile file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.FileName) || file.Content == null)
                throw new FaultException("FileName and Content required");
            if (string.IsNullOrWhiteSpace(file.FieldID))
                file.FieldID = Guid.NewGuid().ToString("N");

            file.UploadedAtUtc = DateTime.UtcNow;
            file.SizeBytes = file.Content.LongLength;
            Files[file.FieldID] = file;

            DuplexHub.BroadcastFileUploaded(file);
            return Task.FromResult(file);
        }

        // ---- shared append helpers (re-use store) ----
        private static void AppendRoom(string roomId, ChatMessage msg)
        {
            RoomLogs.AddOrUpdate(
                roomId,
                _ => new System.Collections.Generic.List<ChatMessage> { msg },
                (_, list) =>
                {
                    list.Add(msg);
                    Trim(list, MaxRoomMessages);
                    return list;
                });
        }

        private static void AppendDm(string a, string b, ChatMessage msg)
        {
            var key = DmKey(a, b);
            DmLogs.AddOrUpdate(
                key,
                _ => new System.Collections.Generic.List<ChatMessage> { msg },
                (_, list) =>
                {
                    list.Add(msg);
                    Trim(list, MaxDmMessages);
                    return list;
                });
        }

        private static string DmKey(string a, string b)
        {
            var x = a?.Trim() ?? "";
            var y = b?.Trim() ?? "";
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{x}|{y}" : $"{y}|{x}";
        }

        private static void Trim(System.Collections.Generic.List<ChatMessage> list, int max)
        {
            if (list.Count > max) list.RemoveRange(0, list.Count - max);
        }

        public Task<List<ChatMessage>> GetPrivateHistoryAsync(string user1, string user2, DateTime sinceUtc)
        {
            if (string.IsNullOrWhiteSpace(user1) || string.IsNullOrWhiteSpace(user2))
                throw new FaultException("Both users required.");

            var key = DmKey(user1, user2);
            if (!DmLogs.TryGetValue(key, out var list)) list = new List<ChatMessage>();
            var result = list
                .Where(m => m.TimestampUtc >= sinceUtc)
                .OrderBy(m => m.TimestampUtc)
                .ToList();

            return Task.FromResult(result);
        }
    }
}
