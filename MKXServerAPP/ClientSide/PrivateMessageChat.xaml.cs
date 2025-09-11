using SharedContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for PrivateMessageChat.xaml
    /// </summary>
    public partial class PrivateMessageChat : Window
    {
        private readonly LobbyServices _proxy;
        private readonly string _currentPlayer;
        private readonly string _otherPlayer;
        private readonly LobbyRoomInfo _room;

        private ObservableCollection<ChatMessage> _thread = new ObservableCollection<ChatMessage>();
        private CancellationTokenSource _pollCts;
        private readonly Random _rng = new Random();
        private DateTime _lastSeenUtc;

        public PrivateMessageChat(string currentPlayer, string otherPlayer,
            LobbyRoomInfo currentRoom, LobbyServices connection)
        {
            InitializeComponent();

            _proxy = connection;
            _currentPlayer = currentPlayer;
            _otherPlayer = otherPlayer;
            _room = currentRoom;

            privateMessageLabel.Content = "Private Messaging: " + _otherPlayer;

            privateMessages.ItemsSource = _thread;

            _lastSeenUtc = DateTime.UtcNow.AddMinutes(-10);

            Loaded += delegate { StartPolling(); };
            Unloaded += delegate { StopPolling(); };

            _ = LoadInitialAsync();
        }

        private async Task LoadInitialAsync()
        {
            try
            {
                DateTime since = DateTime.UtcNow.AddHours(-12);
                List<ChatMessage> history = await _proxy.GetPrivateHistoryAsync(_currentPlayer, _otherPlayer, since);
                _thread.Clear();
                foreach (ChatMessage m in history.OrderBy(m => m.TimestampUtc)) _thread.Add(m);
                if (_thread.Count > 0) _lastSeenUtc = _thread[_thread.Count - 1].TimestampUtc.AddTicks(1);
            }
            catch (Exception ex)
            {
                pmStatusText.Text = "Load failed: " + ex.Message;
            }
        }

        // ---------- Polling ----------
        private void StartPolling()
        {
            StopPolling();
            _pollCts = new CancellationTokenSource();
            _ = PollLoopAsync(_pollCts.Token);
        }

        private void StopPolling()
        {
            try { if (_pollCts != null) _pollCts.Cancel(); }
            catch { }
            finally { if (_pollCts != null) _pollCts.Dispose(); _pollCts = null; }
        }

        private async Task PollLoopAsync(CancellationToken ct)
        {
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(1000);
            TimeSpan maxDelay = TimeSpan.FromSeconds(8);
            TimeSpan delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    List<ChatMessage> newPms = await _proxy.GetPrivateHistoryAsync(_currentPlayer, _otherPlayer, _lastSeenUtc);

                    foreach (ChatMessage pm in newPms.OrderBy(m => m.TimestampUtc))
                    {
                        _thread.Add(pm);
                        if (pm.TimestampUtc >= _lastSeenUtc)
                            _lastSeenUtc = pm.TimestampUtc.AddTicks(1);
                    }

                    pmStatusText.Text = string.Format("Last refresh: {0:T} • Messages: {1}", DateTime.Now, _thread.Count);
                    delay = baseDelay;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    pmStatusText.Text = "Polling error: " + ex.Message;
                    double next = Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(next);
                }

                TimeSpan jitter = TimeSpan.FromMilliseconds(_rng.Next(0, 300));
                try { await Task.Delay(delay + jitter, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ---------- Send ----------
        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string text = (messageInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatMessage newMessage = new ChatMessage
            {
                RoomId = _room.RoomId,
                Sender = _currentPlayer,
                Body = text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = true,
                PrivateRecipient = _otherPlayer
            };

            try
            {
                await _proxy.SendChatAsync(newMessage);
                messageInput.Text = "";

                _thread.Add(newMessage);
                if (newMessage.TimestampUtc >= _lastSeenUtc)
                    _lastSeenUtc = newMessage.TimestampUtc.AddTicks(1);
            }
            catch (Exception ex)
            {
                pmStatusText.Text = "Send failed: " + ex.Message;
            }
        }
    }
}
