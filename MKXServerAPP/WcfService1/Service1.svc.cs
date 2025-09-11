using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.Serialization;
using System.ServiceModel;
using System.ServiceModel.Web;
using System.Text;
using System.Threading.Tasks;
using SharedContracts;

namespace WcfService1
{
    public class Service1 : LobbyServices
    {
        private static readonly ConcurrentDictionary<string, PlayerInfo> Players = new ConcurrentDictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, LobbyRoomInfo> Rooms = new ConcurrentDictionary<string, LobbyRoomInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, SharedFile> Files = new ConcurrentDictionary<string, SharedFile>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<ChatMessage>> RoomLogs = new ConcurrentDictionary<string, List<ChatMessage>>(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, List<ChatMessage>> DmLogs = new ConcurrentDictionary<string, List<ChatMessage>>(StringComparer.OrdinalIgnoreCase);

        private const int MaxRoomMessages = 1000;
        private const int MaxDmMessages = 1000;

        // Players
        public Task<PlayerInfo> RegisterPlayerAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
                throw Fault("Validation", "Username is required.");
            if (Players.ContainsKey(username))
                throw Fault("Conflict", "Username already logged in.");

            var p = new PlayerInfo { Username = username.Trim(), LoggedInAt = DateTime.UtcNow };
            Players[p.Username] = p;
            return Task.FromResult(p);
        }

        public Task<bool> HeartbeatAsync(string username)
        {
            if (!Players.TryGetValue(username, out var p))
                throw Fault("NotFound", "Player not found");

            p.LoggedInAt = DateTime.UtcNow;
            Players[p.Username] = p;
            return Task.FromResult(true);
        }

        // Rooms
        public Task<LobbyRoomInfo> CreateRoomAsync(string roomName, int capacity, bool isRanked)
        {
            if (string.IsNullOrWhiteSpace(roomName))
                throw Fault("Validation", "Room name is required");
            if (capacity < 1)
                throw Fault("Validation", "Capacity must be at least 1.");

            var room = new LobbyRoomInfo
            {
                RoomId = Guid.NewGuid().ToString("N"),
                RoomName = roomName.Trim(),
                Capacity = capacity,
                IsRanked = isRanked,
                Players = new List<string>()
            };
            Rooms[room.RoomId] = room;
            return Task.FromResult(room);
        }

        public Task<bool> JoinRoomAsync(string roomId, string username)
        {
            if (!Rooms.TryGetValue(roomId, out var room))
                throw Fault("NotFound", "Room not found.");
            if (!Players.ContainsKey(username))
                throw Fault("NotFound", "Player not found.");

            if (!room.Players.Contains(username, StringComparer.OrdinalIgnoreCase))
            {
                if (room.Players.Count >= room.Capacity)
                    throw Fault("Conflict", "Room is full.");

                room.Players.Add(username);
                Rooms[room.RoomId] = room; // write back updated room

                var joinMsg = new ChatMessage
                {
                    RoomId = roomId,
                    Sender = "[system]",
                    Body = $"{username} joined the room",
                    TimestampUtc = DateTime.UtcNow,
                    IsPrivate = false,
                };
                AppendRoom(roomId, joinMsg);
            }
            return Task.FromResult(true);


        }

        public Task<bool> LeaveRoomAsync(string roomId, string username)
        {
            if (!Rooms.TryGetValue(roomId, out var room))
                return Task.FromResult(false);

            room.Players = room.Players
                .Where(u => !u.Equals(username, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Rooms[room.RoomId] = room;
            var leaveMsg = new ChatMessage
            {
                RoomId = roomId,
                Sender = "[system]",
                Body = $"{username} left the room",
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = false,
            };
            AppendRoom(roomId, leaveMsg);

            return Task.FromResult(true);


        }

        public Task<List<LobbyRoomInfo>> ListRoomAsync()
        {
            var list = Rooms.Values.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase).ToList();
            return Task.FromResult(list);
        }

        public Task<List<SharedFile>> ListFilesInRoomAsync(string roomId)
        {
            var list = Files.Values.Where(x => x.RoomID.Equals(roomId)).ToList();
            return Task.FromResult(list);
        }

        // Chat
        public Task<bool> SendChatAsync(ChatMessage message)
        {
            if (message == null) throw Fault("Validation", "Message is required.");
            if (!Rooms.TryGetValue(message.RoomId, out var room)) throw Fault("NotFound", "Room not found.");
            if (string.IsNullOrWhiteSpace(message.Sender) || !Players.ContainsKey(message.Sender))
                throw Fault("Validation", "Valid Sender is required.");

            // Private message must target a user in the same room
            if (message.IsPrivate)
            {
                if (string.IsNullOrWhiteSpace(message.PrivateRecipient))
                    throw Fault("Validation", "PrivateRecipient required for private messages.");
                if (!room.Players.Contains(message.PrivateRecipient, StringComparer.OrdinalIgnoreCase))
                    throw Fault("Forbidden", "Recipient is not in the room.");

                AppendDm(message.Sender, message.PrivateRecipient, message);
            }

            else
            {
                AppendRoom(message.RoomId, message);
            }

            return Task.FromResult(true);
        }

        public Task<List<ChatMessage>> GetRoomHistoryAsync(string roomId, DateTime sinceUtc)
        {
            if (string.IsNullOrWhiteSpace(roomId)) throw Fault("Validation", "roomId required.");
            if (!Rooms.ContainsKey(roomId)) throw Fault("NotFound", "Room not found.");

            if (!RoomLogs.TryGetValue(roomId, out var list)) list = new List<ChatMessage>();
            var result = list
                .Where(m => m.TimestampUtc >= sinceUtc)
                .OrderBy(m => m.TimestampUtc)
                .ToList();
            return Task.FromResult(result);
        }

        public Task<List<ChatMessage>> GetPrivateHistoryAsync(string user1, string user2, DateTime sinceUtc)
        {
            if (string.IsNullOrWhiteSpace(user1) || string.IsNullOrWhiteSpace(user2))
                throw Fault("Validation", "Both users required.");

            var key = DmKey(user1, user2);
            if (!DmLogs.TryGetValue(key, out var list)) list = new List<ChatMessage>();
            var result = list
                .Where(m => m.TimestampUtc >= sinceUtc)
                .OrderBy(m => m.TimestampUtc)
                .ToList();
            return Task.FromResult(result);
        }

        // Files
        public Task<SharedFile> UploadFileAsync(SharedFile file)
        {
            if (file == null || string.IsNullOrWhiteSpace(file.FileName) || file.Content == null)
                throw Fault("Validation", "FileName and Content are required.");

            if (string.IsNullOrWhiteSpace(file.FieldID))
                file.FieldID = Guid.NewGuid().ToString("N");

            file.UploadedAtUtc = DateTime.UtcNow;
            file.SizeBytes = file.Content.LongLength;

            Files[file.FieldID] = file;
            return Task.FromResult(file);
        }

        public Task<SharedFile> DownloadFileAsync(string fileId)
        {
            if (string.IsNullOrWhiteSpace(fileId))
                throw Fault("Validation", "fileId is required.");
            if (!Files.TryGetValue(fileId, out var file))
                throw Fault("NotFound", "File not found.");
            return Task.FromResult(file);
        }

        // Summary
        public Task<LobbySummary> GetLobbySummaryAsync()
        {
            var summary = new LobbySummary
            {
                LobbyRooms = Rooms.Values.ToList(),
                OnlinePlayers = Players.Count
            };
            return Task.FromResult(summary);
        }

        // Helper to return typed faults
        private static FaultException<ApiFault> Fault(string code, string message, string details = null) =>
            new FaultException<ApiFault>(
                new ApiFault { Code = code, Message = message, Details = details },
                new FaultReason(message));

        // RoomId -> chronological message

        private static string DmKey(string a, string b)
        {
            var x = a?.Trim() ?? ""; var y = b?.Trim() ?? "";
            return string.Compare(x, y, StringComparison.OrdinalIgnoreCase) <= 0 ? $"{x}|{y}" : $"{y}|{x}";
        }

        private static void AppendRoom(string roomId, ChatMessage msg)
        {
            RoomLogs.AddOrUpdate(
                roomId,
                _ => new List<ChatMessage> { msg },
                (_, list) =>
                {
                    list.Add(msg);
                    Trim(list, MaxRoomMessages);
                    return list;
                });
        }

        private static void AppendDm(string from, string to, ChatMessage msg)
        {
            var key = DmKey(from, to);
            DmLogs.AddOrUpdate(
                key,
                _ => new List<ChatMessage> { msg },
                (_, list) =>
                {
                    list.Add(msg);
                    Trim(list, MaxDmMessages);
                    return list;
                });
        }

        private static void Trim(List<ChatMessage> list, int max)
        {
            if (list.Count > max)
            {
                list.RemoveRange(0, list.Count - max);
            }
        }
    }
}