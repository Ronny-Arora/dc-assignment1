using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

using SharedContracts;
using System.ServiceModel;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for GeneralLobby.xaml
    /// </summary>
    public partial class GeneralLobby : Window
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;
        private List<LobbyRoomInfo> _lobbies;
        private LobbyRoomInfo _currentRoom;

        public GeneralLobby(PlayerInfo currentPlayer, LobbyServices connection)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _currentRoom = null;

            UpdateLobbyRooms();
        }

        public async void UpdateLobbyRooms() {
            _lobbies = await ListRooms();

            listOfLobbies.ItemsSource = _lobbies;
        }

        public async Task<List<LobbyRoomInfo>> ListRooms()
        {
            return await _proxy.ListRoomAsync();
        }

        private void JoinRoomButton_Click(object sender, RoutedEventArgs e)
        {
            LobbyRoomInfo selectedRoom = listOfLobbies.SelectedItem as LobbyRoomInfo;

            if (selectedRoom != null)
            {
                // error handling
                if (_currentRoom != null)
                {
                    _proxy.LeaveRoomAsync(_currentRoom.RoomId, _player.Username);
                    _currentRoom = null;
                }

                _currentRoom = selectedRoom;
                _proxy.JoinRoomAsync(selectedRoom.RoomId, _player.Username);

                // extra players to test private messaging
                _proxy.JoinRoomAsync(selectedRoom.RoomId, "Bob1");
                _proxy.JoinRoomAsync(selectedRoom.RoomId, "Bob2");
                _proxy.JoinRoomAsync(selectedRoom.RoomId, "Bob3");

                lobbyChatContent.Content = new LobbyRoomChat(_player, _proxy, selectedRoom, lobbyChatContent);
            } else
            {
                // display error message
            }
        }

        private async void LogOutButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentRoom != null)
            {
                await _proxy.LeaveRoomAsync(_currentRoom.RoomId, _player.Username);
                _currentRoom = null;
            }

            // Open log in window
            var mainWindow = new MainWindow();
            mainWindow.Show();
            this.Close();
        }

        private void CreateRoomPopup_Click(object sender, RoutedEventArgs e)
        {
            createRoomPopup.IsOpen = true;
        }

        private void CreateRoomButton_Click(object sender, RoutedEventArgs e)
        {
            // Exception handling

            string roomName = roomNameInput.Text;
            int roomCapacity = int.Parse(roomCapacityInput.Text);
            bool roomIsRanked = (bool) isRankedInput.IsChecked;

            _proxy.CreateRoomAsync(roomName, roomCapacity, roomIsRanked);
            UpdateLobbyRooms();

            // if successful
            roomNameInput.Text = "";
            roomCapacityInput.Text = "";
            isRankedInput.IsChecked = false;

            createRoomPopup.IsOpen = false;
        }
    }
}
