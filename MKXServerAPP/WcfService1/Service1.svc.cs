using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            return Task.FromResult(true);
        }

        public Task<List<LobbyRoomInfo>> ListRoomAsync()
        {
            var list = Rooms.Values.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase).ToList();
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
            }

            // For Part A we don’t need persistence here; just accept the message.
            return Task.FromResult(true);
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
    }
}