using SharedContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for LobbyRoomChat.xaml
    /// </summary>
    public partial class LobbyRoomChat : UserControl
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;
        private LobbyRoomInfo _roomInfo;                 // will be refreshed periodically
        private readonly ContentControl _generalLobby;

        public ObservableCollection<string> Players { get; } = new ObservableCollection<string>();

        public ObservableCollection<ChatMessage> Messages { get; } = new ObservableCollection<ChatMessage>();
        // public ObservableCollection<SharedFileDto> Files { get; } = new(); // if/when you add file polling

        private CancellationTokenSource _pollCts;

        // Track last-seen message time (UTC) so we only fetch new
        private DateTime _lastSeenUtc;

        public LobbyRoomChat(PlayerInfo currentPlayer, LobbyServices connection,
                             LobbyRoomInfo currentLobbyRoom, ContentControl generalLobby)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _roomInfo = currentLobbyRoom;
            _generalLobby = generalLobby;

            // Bind labels/collections
            lobbyRoomLabel.Text = $"Lobby: {_roomInfo.RoomName}";
            listOfPlayers.ItemsSource = Players;
            lobbyMessages.ItemsSource = Messages;
            // filesList.ItemsSource = Files;

            // Seed from current snapshot
            ReplacePlayers(_roomInfo.Players);
            _lastSeenUtc = DateTime.UtcNow.AddMinutes(-5); // start window (or last year if you prefer)

            // Load initial history once (optional)
            _ = LoadInitialMessagesAsync();

            // Start/stop polling with the control’s lifetime
            Loaded += (_, __) => StartPolling();
            Unloaded += (_, __) => StopPolling();
        }

        // --------- Initial one-shot loads ---------

        private async Task LoadInitialMessagesAsync()
        {
            try
            {
                var since = DateTime.UtcNow.AddHours(-12); // initial history window
                var history = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, since);
                await Dispatcher.InvokeAsync(() =>
                {
                    Messages.Clear();
                    foreach (var m in history.OrderBy(m => m.TimestampUtc)) Messages.Add(m);
                    if (Messages.Count > 0)
                        if (Messages.Count > 0)
                            _lastSeenUtc = Messages[Messages.Count - 1].TimestampUtc.AddTicks(1);
                });
            }
            catch (Exception ex)
            {
                ShowStatus($"History load failed: {ex.Message}");
            }
        }

        // --------- Polling loop ---------

        private void StartPolling()
        {
            _pollCts?.Cancel();
            _pollCts = new CancellationTokenSource();
            _ = PollLoopAsync(_pollCts.Token);
        }

        private void StopPolling() => _pollCts?.Cancel();

        private async Task PollLoopAsync(CancellationToken ct)
        {
            var baseDelay = TimeSpan.FromMilliseconds(1200);
            var maxDelay = TimeSpan.FromSeconds(8);
            var delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1) Fetch NEW messages since lastSeen
                    var newMsgs = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, _lastSeenUtc);

                    // 2) Refresh room to get latest players list
                    //    (we reuse ListRoomAsync and find this room)
                    var allRooms = await _proxy.ListRoomAsync();
                    var refreshed = allRooms.FirstOrDefault(r => r.RoomId == _roomInfo.RoomId);
                    if (refreshed != null)
                        _roomInfo = refreshed; // keep up-to-date snapshot

                    // 3) (Optional) fetch new files if/when you expose an API:
                    // var newFiles = await _proxy.GetFilesAsync(_roomInfo.RoomId, _lastFileId);

                    // 4) Apply to UI
                    await Dispatcher.InvokeAsync(() =>
                    {
                        // append messages in order & bump _lastSeenUtc
                        foreach (var m in newMsgs.OrderBy(m => m.TimestampUtc))
                        {
                            // ignore private msgs (room feed only)
                            if (!m.IsPrivate) Messages.Add(m);
                            if (m.TimestampUtc >= _lastSeenUtc)
                                _lastSeenUtc = m.TimestampUtc.AddTicks(1);
                        }

                        // replace players list in-place
                        if (_roomInfo?.Players != null)
                            ReplacePlayers(_roomInfo.Players);

                        // status heartbeat (optional)
                        statusText.Text = $"Last refresh: {DateTime.Now:T} • Players: {Players.Count} • Msgs: {Messages.Count}";
                    }, DispatcherPriority.Background, ct);

                    delay = baseDelay; // success -> reset
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Surface error, back off, keep looping
                    await Dispatcher.InvokeAsync(() =>
                        statusText.Text = $"Polling error: {ex.Message}");

                    var next = Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(next);
                }

                var jitter = TimeSpan.FromMilliseconds(new Random().Next(0, 300));
                await Task.Delay(delay + jitter, ct);
            }
        }

        // --------- UI helpers ---------

        private void ReplacePlayers(IEnumerable<string> names)
        {
            Players.Clear();
            if (names == null) return;
            foreach (var n in names) Players.Add(n);
        }

        private void ShowStatus(string msg)
        {
            if (statusText != null) statusText.Text = msg ?? "";
            System.Diagnostics.Debug.WriteLine(msg);
        }

        // --------- Buttons / actions ---------

        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _proxy.LeaveRoomAsync(_roomInfo.RoomId, _player.Username);
            }
            catch { /* best effort */ }
            finally
            {
                StopPolling();
                _generalLobby.Content = null;
            }
        }

        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            var text = messageInput.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text)) return;

            var msg = new ChatMessage
            {
                RoomId = _roomInfo.RoomId,
                Sender = _player.Username,
                Body = text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = false
            };

            try
            {
                await _proxy.SendChatAsync(msg);
                messageInput.Text = "";

                // Optional optimistic append; polling will also fetch it soon.
                Messages.Add(msg);
                if (msg.TimestampUtc >= _lastSeenUtc)
                    _lastSeenUtc = msg.TimestampUtc.AddTicks(1);
            }
            catch (Exception ex)
            {
                ShowStatus($"Send failed: {ex.Message}");
            }
        }

        private void MessagePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            var otherPlayer = listOfPlayers.SelectedItem as string;
            if (string.IsNullOrWhiteSpace(otherPlayer))
            {
                ShowStatus("Select a player first.");
                return;
            }
            var pm = new PrivateMessageChat(_player.Username, otherPlayer, _roomInfo, _proxy);
            pm.Show();
        }

        private async void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {
            // TODO: wire your file upload here if you have UploadFileAsync in the service.
            // After upload succeeds, the polling loop (when you add file polling) will show it.
            await Task.CompletedTask;
        }
    }
}
