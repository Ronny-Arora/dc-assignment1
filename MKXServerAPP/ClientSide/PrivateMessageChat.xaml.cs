using SharedContracts;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using ClientSide.Services; // DuplexServerProxy, ClientCallback

namespace ClientSide
{
    public partial class PrivateMessageChat : Window
    {
        private readonly DuplexServerProxy _proxy;
        private readonly ClientCallback _callback;
        private readonly string _me;
        private readonly string _other;
        private readonly LobbyRoomInfo _room;

        private readonly ObservableCollection<ChatMessage> _thread = new ObservableCollection<ChatMessage>();

        public PrivateMessageChat(string currentPlayer,
                                  string otherPlayer,
                                  LobbyRoomInfo currentRoom,
                                  DuplexServerProxy proxy,
                                  ClientCallback callback)
        {
            InitializeComponent();

            _me = currentPlayer;
            _other = otherPlayer;
            _room = currentRoom;
            _proxy = proxy;
            _callback = callback;

            privateMessageLabel.Content = "Private Messaging: " + _other;
            privateMessages.ItemsSource = _thread;

            Loaded += async (s, e) => await LoadInitialAsync();
            Unloaded += (s, e) => _callback.PrivateMessage -= OnPrivateMessage;

            // listen for future pushes while this window is open
            _callback.PrivateMessage += OnPrivateMessage;
        }

        // Pull existing history so the thread isn't empty when we open
        private async Task LoadInitialAsync()
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-12);
                var history = await _proxy.Channel.GetPrivateHistoryAsync(_me, _other, since);
                _thread.Clear();
                foreach (var m in history.OrderBy(m => m.TimestampUtc)) _thread.Add(m);
                pmStatusText.Text = $"Loaded {history.Count} messages";
            }
            catch (Exception ex)
            {
                pmStatusText.Text = "Load failed: " + ex.Message;
            }
        }

        // Append incoming messages pushed by the server
        private void OnPrivateMessage(ChatMessage msg)
        {
            if (msg == null || !msg.IsPrivate) return;
            if (!string.Equals(msg.RoomId, _room.RoomId, StringComparison.OrdinalIgnoreCase)) return;

            // Only append incoming messages: other -> me
            if (!string.Equals(msg.PrivateRecipient, _me, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(msg.Sender, _other, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.Invoke(() => _thread.Add(msg));
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string text = (messageInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            var newMessage = new ChatMessage
            {
                RoomId = _room.RoomId,
                Sender = _me,
                Body = text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = true,
                PrivateRecipient = _other
            };

            try
            {
                _proxy.Channel.SendPrivateMessage(newMessage); // IsOneWay push
                messageInput.Text = "";
                _thread.Add(newMessage); // optimistic add; server will also push back
            }
            catch (Exception ex)
            {
                pmStatusText.Text = "Send failed: " + ex.Message;
            }
        }
    }
}
