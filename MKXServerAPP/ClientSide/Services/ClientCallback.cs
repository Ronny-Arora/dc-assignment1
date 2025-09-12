using System;
using System.Windows;
using SharedContracts;

namespace ClientSide.Services
{
    public class ClientCallback : ILobbyClientCallbacks
    {
        public event Action<LobbySummary> LobbySummaryReceived;
        public event Action<LobbyRoomInfo> RoomCreated;
        public event Action<LobbyRoomInfo> RoomUpdated;
        public event Action<string, bool> PresenceChanged;
        public event Action<ChatMessage> RoomMessage;
        public event Action<ChatMessage> PrivateMessage;
        public event Action<SharedFile> FileUploaded;

        private readonly Action<Action> _ui = a => Application.Current.Dispatcher.Invoke(a);

        public void OnLobbySummary(LobbySummary summary) { _ui(() => LobbySummaryReceived?.Invoke(summary)); }
        public void OnRoomCreated(LobbyRoomInfo room) { _ui(() => RoomCreated?.Invoke(room)); }
        public void OnRoomUpdated(LobbyRoomInfo room) { _ui(() => RoomUpdated?.Invoke(room)); }
        public void OnPresenceChanged(string u, bool on) { _ui(() => PresenceChanged?.Invoke(u, on)); }
        public void OnRoomMessage(ChatMessage msg) { _ui(() => RoomMessage?.Invoke(msg)); }
        public void OnPrivateMessage(ChatMessage msg) { _ui(() => PrivateMessage?.Invoke(msg)); }
        public void OnFileUploaded(SharedFile file) { _ui(() => FileUploaded?.Invoke(file)); }
    }
}
