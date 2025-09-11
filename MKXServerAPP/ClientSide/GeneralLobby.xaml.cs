using System;
using System.Collections.Generic;
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

        private List<LobbyRoomInfo> _lobbies = new List<LobbyRoomInfo>();
        private LobbyRoomInfo _currentRoom;

        private CancellationTokenSource _lobbyPollCts;
        private readonly Random _rng = new Random();

        public GeneralLobby(PlayerInfo currentPlayer, LobbyServices connection)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _currentRoom = null;

            // start polling when window is ready / stop when closing
            Loaded += delegate { StartLobbyPolling(); };
            Unloaded += delegate { StopLobbyPolling(); };
            Closed += delegate { StopLobbyPolling(); };

            // initial load
            _ = UpdateLobbyRooms();
        }

        // ---------- Polling ----------

        private void StartLobbyPolling()
        {
            StopLobbyPolling();
            _lobbyPollCts = new CancellationTokenSource();
            _ = PollLobbyLoopAsync(_lobbyPollCts.Token);
        }

        private void StopLobbyPolling()
        {
            try { if (_lobbyPollCts != null) _lobbyPollCts.Cancel(); }
            catch { }
            finally { if (_lobbyPollCts != null) _lobbyPollCts.Dispose(); _lobbyPollCts = null; }
        }

        private async Task PollLobbyLoopAsync(CancellationToken ct)
        {
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(1500);
            TimeSpan maxDelay = TimeSpan.FromSeconds(10);
            TimeSpan delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    await UpdateLobbyRooms();

                    delay = baseDelay; // reset after success
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    ShowStatus("Lobby refresh problem: " + ex.Message);
                    double nextMs = Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(nextMs);
                }

                TimeSpan jitter = TimeSpan.FromMilliseconds(_rng.Next(0, 400));
                try { await Task.Delay(delay + jitter, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ---------- Data ops ----------

        public async Task UpdateLobbyRooms()
        {
            try
            {
                _lobbies = await ListRooms();

                // UI update
                await Dispatcher.InvokeAsync(delegate
                {
                    listOfLobbies.ItemsSource = null;
                    listOfLobbies.ItemsSource = _lobbies;
                    StatusText.Text = ""; // clear
                }, DispatcherPriority.Background);
            }
            catch (FaultException fe)
            {
                ShowStatus("Server error: " + fe.Message);
            }
            catch (CommunicationException)
            {
                ShowStatus("Connection issue to server.");
            }
            catch (TimeoutException)
            {
                ShowStatus("Server timed out.");
            }
        }

        public async Task<List<LobbyRoomInfo>> ListRooms()
        {
            return await _proxy.ListRoomAsync();
        }

        // ---------- UI events ----------

        private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            LobbyRoomInfo selectedRoom = listOfLobbies.SelectedItem as LobbyRoomInfo;

            if (selectedRoom == null)
            {
                ShowStatus("Please select a lobby to join.");
                return;
            }

            try
            {
                if (_currentRoom != null)
                {
                    await _proxy.LeaveRoomAsync(_currentRoom.RoomId, _player.Username);
                    _currentRoom = null;
                }

                await _proxy.JoinRoomAsync(selectedRoom.RoomId, _player.Username);
                _currentRoom = selectedRoom;

                lobbyChatContent.Content = new LobbyRoomChat(_player, _proxy, selectedRoom, lobbyChatContent);

                ShowStatus("Joined room: " + selectedRoom.RoomName);
            }
            catch (Exception ex)
            {
                ShowStatus("Failed to join room: " + ex.Message);
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
            catch { /* best-effort */ }

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
            string roomName = (roomNameInput.Text ?? "").Trim();

            if (string.IsNullOrEmpty(roomName))
            {
                ShowStatus("Room name is required.");
                return;
            }

            int roomCapacity;
            if (!int.TryParse(roomCapacityInput.Text, out roomCapacity) || roomCapacity <= 0)
            {
                ShowStatus("Room capacity must be a positive integer.");
                return;
            }

            bool roomIsRanked = isRankedInput.IsChecked == true;

            try
            {
                await _proxy.CreateRoomAsync(roomName, roomCapacity, roomIsRanked);

                await UpdateLobbyRooms();

                // clear inputs/close
                roomNameInput.Text = "";
                roomCapacityInput.Text = "";
                isRankedInput.IsChecked = false;
                createRoomPopup.IsOpen = false;

                ShowStatus("Room created.");
            }
            catch (Exception ex)
            {
                ShowStatus("Create failed: " + ex.Message);
            }
        }

        // ---------- helpers ----------
        private void ShowStatus(string message)
        {
            if (StatusText != null) StatusText.Text = message ?? "";
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
