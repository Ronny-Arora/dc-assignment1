// WcfService1/DuplexHub.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.ServiceModel;
using SharedContracts;

namespace WcfService1
{
    internal static class DuplexHub
    {
        private class ClientCtx
        {
            public string User = "";
            public ILobbyClientCallbacks Callback = default;
            public HashSet<string> Rooms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public bool LobbySubscribed;
        }

        // username -> ctx
        private static readonly ConcurrentDictionary<string, ClientCtx> _clients =
            new ConcurrentDictionary<string, ClientCtx>(StringComparer.OrdinalIgnoreCase);

        public static bool Register(string username, ILobbyClientCallbacks cb)
        {
            var comm = (ICommunicationObject)cb;
            var ctx = new ClientCtx { User = username, Callback = cb };
            if (!_clients.TryAdd(username, ctx)) return false;

            void remove(object _, EventArgs __) => Unregister(username);
            comm.Faulted += remove;
            comm.Closed += remove;

            return true;
        }

        public static void Unregister(string username)
        {
            _clients.TryRemove(username, out _);
        }

        public static void SubscribeLobby(string username, bool on = true)
        {
            if (_clients.TryGetValue(username, out var ctx)) ctx.LobbySubscribed = on;
        }

        public static void SubscribeRoom(string username, string roomId, bool on = true)
        {
            if (_clients.TryGetValue(username, out var ctx))
            {
                if (on) ctx.Rooms.Add(roomId);
                else ctx.Rooms.Remove(roomId);
            }
        }

        // ----- Broadcast helpers -----

        public static void BroadcastLobbySummary(LobbySummary snap)
        {
            foreach (var c in _clients.Values.Where(c => c.LobbySubscribed))
                SafeInvoke(c, cb => cb.OnLobbySummary(snap));
        }

        public static void BroadcastRoomCreated(LobbyRoomInfo room)
        {
            foreach (var c in _clients.Values.Where(c => c.LobbySubscribed))
                SafeInvoke(c, cb => cb.OnRoomCreated(room));
        }

        public static void BroadcastRoomUpdated(LobbyRoomInfo room)
        {
            foreach (var c in _clients.Values.Where(c => c.Rooms.Contains(room.RoomId) || c.LobbySubscribed))
                SafeInvoke(c, cb => cb.OnRoomUpdated(room));
        }

        public static void BroadcastPresence(string username, bool isOnline)
        {
            foreach (var c in _clients.Values.Where(c => c.LobbySubscribed))
                SafeInvoke(c, cb => cb.OnPresenceChanged(username, isOnline));
        }

        public static void BroadcastRoomMessage(ChatMessage msg)
        {
            foreach (var c in _clients.Values.Where(c => c.Rooms.Contains(msg.RoomId)))
                SafeInvoke(c, cb => cb.OnRoomMessage(msg));
        }

        public static void BroadcastPrivateMessage(ChatMessage msg)
        {
            // Deliver to sender and recipient if they are connected
            if (!string.IsNullOrWhiteSpace(msg.Sender) &&
                _clients.TryGetValue(msg.Sender, out var s))
                SafeInvoke(s, cb => cb.OnPrivateMessage(msg));

            if (!string.IsNullOrWhiteSpace(msg.PrivateRecipient) &&
                _clients.TryGetValue(msg.PrivateRecipient, out var r))
                SafeInvoke(r, cb => cb.OnPrivateMessage(msg));
        }

        public static void BroadcastFileUploaded(SharedFile file)
        {
            foreach (var c in _clients.Values.Where(c => c.Rooms.Contains(file.RoomID)))
                SafeInvoke(c, cb => cb.OnFileUploaded(file));
        }

        private static void SafeInvoke(ClientCtx ctx, Action<ILobbyClientCallbacks> call)
        {
            try { call(ctx.Callback); }
            catch { Unregister(ctx.User); }
        }
    }
}
