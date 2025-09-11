using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ServiceModel;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

using SharedContracts;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for GeneralLobby.xaml
    /// </summary>
    public partial class GeneralLobby : Window
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;

        private LobbyRoomInfo _currentRoom;

        public ObservableCollection<LobbyRoomInfo> Lobbies { get; } = new ObservableCollection<LobbyRoomInfo>();

        // polling
        private CancellationTokenSource _lobbyPollCts;

        public GeneralLobby(PlayerInfo currentPlayer, LobbyServices connection)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;

            DataContext = this;

            Loaded += (_, __) => StartLobbyPolling();
            Unloaded += (_, __) => StopLobbyPolling();
            Closed += (_, __) => StopLobbyPolling();
        }

        // ---------- POLLING ----------

        private void StartLobbyPolling()
        {
            _lobbyPollCts?.Cancel();
            _lobbyPollCts = new CancellationTokenSource();
            _ = PollLobbyLoopAsync(_lobbyPollCts.Token);
        }

        private void StopLobbyPolling() => _lobbyPollCts?.Cancel();

        private async Task PollLobbyLoopAsync(CancellationToken ct)
        {
            var baseDelay = TimeSpan.FromMilliseconds(1500);
            var maxDelay = TimeSpan.FromSeconds(10);
            var delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var list = await SafeListRoomsAsync(ct);

                    // update UI on the dispatcher
                    await Dispatcher.InvokeAsync(() =>
                    {
                        Lobbies.Clear();
                        foreach (var r in list) Lobbies.Add(r);
                        ShowStatus(""); // clear any previous transient status
                    }, DispatcherPriority.Background, ct);

                    delay = baseDelay; // success -> reset delay
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // transient or unknown; surface and backoff
                    await Dispatcher.InvokeAsync(() =>
                        ShowStatus($"Lobby refresh problem: {ex.Message}"));

                    var nextMs = Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(nextMs);
                }

                // With the following code:
                var random = new Random();
                var jitter = TimeSpan.FromMilliseconds(random.Next(0, 400));
                await Task.Delay(delay + jitter, ct);
            }
        }

        private async Task<List<LobbyRoomInfo>> SafeListRoomsAsync(CancellationToken ct)
        {
            // Wrap WCF exceptions to keep PollLobbyLoop concise
            try
            {
                return await _proxy.ListRoomAsync();
            }
            catch (FaultException fe)
            {
                throw new Exception($"Server error: {fe.Message}", fe);
            }
            catch (CommunicationException ce)
            {
                throw new Exception("Connection issue to server.", ce);
            }
            catch (TimeoutException te)
            {
                throw new Exception("Server timed out.", te);
            }
        }

        // ---------- UI COMMANDS / EVENTS ----------

        private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            var selectedRoom = listOfLobbies?.SelectedItem as LobbyRoomInfo;
            if (selectedRoom == null)
            {
                ShowStatus("Please select a lobby to join.");
                return;
            }

            try
            {
                // leave previous room if any
                if (_currentRoom != null)
                {
                    await _proxy.LeaveRoomAsync(_currentRoom.RoomId, _player.Username);
                    _currentRoom = null;
                }

                // join the selected room
                await _proxy.JoinRoomAsync(selectedRoom.RoomId, _player.Username);
                _currentRoom = selectedRoom;

                // ⛔ removed the test joins that were adding Bob1/Bob2/Bob3
                // Those were causing the “pre-existing users” issue.

                // navigate to room chat
                lobbyChatContent.Content = new LobbyRoomChat(_player, _proxy, selectedRoom, lobbyChatContent);

                ShowStatus($"Joined room: {selectedRoom.RoomName}");
            }
            catch (FaultException fe)
            {
                ShowStatus($"Server error joining room: {fe.Message}");
            }
            catch (CommunicationException)
            {
                ShowStatus("Network/connection problem while joining the room.");
            }
            catch (TimeoutException)
            {
                ShowStatus("Join room request timed out.");
            }
            catch (Exception ex)
            {
                ShowStatus($"Unexpected error: {ex.Message}");
            }
        }

        private async void LogOutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRoom != null)
                {
                    await _proxy.LeaveRoomAsync(_currentRoom.RoomId, _player.Username);
                    _currentRoom = null;
                }
            }
            catch { /* best-effort on logout */ }

            // back to login
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void CreateRoomPopup_Click(object sender, RoutedEventArgs e)
        {
            createRoomPopup.IsOpen = true;
        }

        private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            var roomName = roomNameInput.Text?.Trim();

            if (string.IsNullOrWhiteSpace(roomName))
            {
                ShowStatus("Room name is required.");
                return;
            }

            if (!int.TryParse(roomCapacityInput.Text, out var roomCapacity) || roomCapacity <= 0)
            {
                ShowStatus("Room capacity must be a positive integer.");
                return;
            }

            var roomIsRanked = isRankedInput.IsChecked == true;

            try
            {
                await _proxy.CreateRoomAsync(roomName, roomCapacity, roomIsRanked);

                // force a refresh immediately instead of waiting for next poll tick
                await RefreshLobbiesNowAsync();

                // clear inputs and close popup
                roomNameInput.Text = "";
                roomCapacityInput.Text = "";
                isRankedInput.IsChecked = false;
                createRoomPopup.IsOpen = false;

                ShowStatus("Room created.");
            }
            catch (FaultException fe)
            {
                ShowStatus($"Server error creating room: {fe.Message}");
            }
            catch (CommunicationException)
            {
                ShowStatus("Network/connection problem while creating room.");
            }
            catch (TimeoutException)
            {
                ShowStatus("Create room request timed out.");
            }
            catch (Exception ex)
            {
                ShowStatus($"Unexpected error: {ex.Message}");
            }
        }

        // ---------- HELPERS ----------

        private async Task RefreshLobbiesNowAsync()
        {
            try
            {
                var list = await _proxy.ListRoomAsync();
                Lobbies.Clear();
                foreach (var r in list) Lobbies.Add(r);
            }
            catch { /* non-fatal; polling will catch up */ }
        }

        private void ShowStatus(string message)
        {
            // Hook this up to a TextBlock named "StatusText" if you want visual feedback.
            // Example XAML: <TextBlock x:Name="StatusText" Margin="0,4,0,0" Foreground="DarkRed"/>
            if (StatusText != null) StatusText.Text = message ?? "";
            Console.WriteLine(message);
        }
    }
}
