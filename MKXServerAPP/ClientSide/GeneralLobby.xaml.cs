using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using SharedContracts;
using ClientSide.Services;   // DuplexServerProxy, ClientCallback

namespace ClientSide
{
    public partial class GeneralLobby : Window
    {
        private readonly DuplexServerProxy _proxy;
        private readonly ClientCallback _callback;
        private readonly PlayerInfo _player;

        private List<LobbyRoomInfo> _lobbies = new List<LobbyRoomInfo>();
        private LobbyRoomInfo _currentRoom;

        // NEW ctor: pass duplex proxy + callback instead of LobbyServices
        public GeneralLobby(PlayerInfo currentPlayer, DuplexServerProxy proxy, ClientCallback callback)
        {
            InitializeComponent();
            _player = currentPlayer;
            _proxy = proxy;
            _callback = callback;

            // Wire push events
            _callback.LobbySummaryReceived += OnLobbySummary;
            _callback.RoomCreated += OnRoomCreated;
            _callback.RoomUpdated += OnRoomUpdated;
        }

        // ----- Callbacks -----
        private void OnLobbySummary(LobbySummary snap)
        {
            if (snap == null) return;
            Dispatcher.Invoke(delegate
            {
                _lobbies = snap.LobbyRooms ?? new List<LobbyRoomInfo>();
                listOfLobbies.ItemsSource = null;
                listOfLobbies.ItemsSource = _lobbies.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase).ToList();
                StatusText.Text = "Lobby updated (push)";
            }, DispatcherPriority.Background);
        }

        private void OnRoomCreated(LobbyRoomInfo room)
        {
            if (room == null) return;
            Dispatcher.Invoke(delegate
            {
                _lobbies.Add(room);
                listOfLobbies.ItemsSource = null;
                listOfLobbies.ItemsSource = _lobbies.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase).ToList();
                StatusText.Text = "Room created: " + room.RoomName;
            });
        }

        private void OnRoomUpdated(LobbyRoomInfo room)
        {
            if (room == null) return;
            Dispatcher.Invoke(delegate
            {
                int i = _lobbies.FindIndex(r => r.RoomId == room.RoomId);
                if (i >= 0) _lobbies[i] = room; else _lobbies.Add(room);
                listOfLobbies.ItemsSource = null;
                listOfLobbies.ItemsSource = _lobbies.OrderBy(r => r.RoomName, StringComparer.OrdinalIgnoreCase).ToList();
            });
        }

        // ----- UI events -----
        private async void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            LobbyRoomInfo selectedRoom = listOfLobbies.SelectedItem as LobbyRoomInfo;
            if (selectedRoom == null) { ShowStatus("Please select a lobby to join."); return; }

            try
            {
                if (_currentRoom != null)
                {
                    await _proxy.Channel.UnsubscribeRoomAsync(_currentRoom.RoomId);
                    await _proxy.Channel.LeaveRoomAsync(_currentRoom.RoomId);
                    _currentRoom = null;
                }

                await _proxy.Channel.JoinRoomAsync(selectedRoom.RoomId);
                await _proxy.Channel.SubscribeRoomAsync(selectedRoom.RoomId);
                _currentRoom = selectedRoom;

                lobbyChatContent.Content = new LobbyRoomChat(_player, _proxy, _callback, selectedRoom, lobbyChatContent);
                ShowStatus("Joined room: " + selectedRoom.RoomName);
            }
            catch (Exception ex) { ShowStatus("Failed to join room: " + ex.Message); }
        }

        private void CreateRoomPopup_Click(object sender, RoutedEventArgs e)
        {
            createRoomPopup.IsOpen = true;
        }

        private async void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            string roomName = (roomNameInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(roomName)) { ShowStatus("Room name is required."); return; }

            int roomCapacity;
            if (!int.TryParse(roomCapacityInput.Text, out roomCapacity) || roomCapacity <= 0)
            { ShowStatus("Room capacity must be a positive integer."); return; }

            bool roomIsRanked = isRankedInput.IsChecked == true;

            try
            {
                await _proxy.Channel.CreateRoomAsync(roomName, roomCapacity, roomIsRanked);
                // list updates via OnRoomCreated/OnLobbySummary
                roomNameInput.Text = "";
                roomCapacityInput.Text = "";
                isRankedInput.IsChecked = false;
                createRoomPopup.IsOpen = false;
                ShowStatus("Room created.");
            }
            catch (Exception ex) { ShowStatus("Create failed: " + ex.Message); }
        }

        private async void LogOutButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_currentRoom != null)
                {
                    await _proxy.Channel.UnsubscribeRoomAsync(_currentRoom.RoomId);
                    await _proxy.Channel.LeaveRoomAsync(_currentRoom.RoomId);
                    _currentRoom = null;
                }
                await _proxy.Channel.LogoutAsync();
            }
            catch { /* best-effort */ }

            _proxy.Dispose();
            var mainWindow = new MainWindow();
            mainWindow.Show();
            Close();
        }

        private void ShowStatus(string message)
        {
            if (StatusText != null) StatusText.Text = message ?? "";
            System.Diagnostics.Debug.WriteLine(message);
        }
    }
}
