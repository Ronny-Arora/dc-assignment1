using SharedContracts;
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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for LobbyRoomChat.xaml
    /// </summary>
    public partial class LobbyRoomChat : UserControl
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;
        private List<string> _players;
        private readonly LobbyRoomInfo _roomInfo;
        private readonly ContentControl _generalLobby;
        private List<ChatMessage> _messages;

        public LobbyRoomChat(PlayerInfo currentPlayer, LobbyServices connection, 
                            LobbyRoomInfo currentLobbyRoom, ContentControl generalLobby)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _roomInfo = currentLobbyRoom;
            _generalLobby = generalLobby;

            lobbyRoomLabel.Content += _roomInfo.RoomName;
            UpdatePlayerList();
            GetMessages();

        }

        public void UpdatePlayerList()
        {
            // Update for real-time changes

            _players = _roomInfo.Players;

            listOfPlayers.ItemsSource = _players;
        }

        public async void GetMessages()
        {
            // Update for real-time changes

            int currentYear = DateTime.Now.Year;
            DateTime lastYearDate = new DateTime(currentYear - 1, 1,1,0,0,0, DateTimeKind.Utc);

            _messages = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, lastYearDate);

            lobbyMessages.ItemsSource = _messages;
        }

        private void MessagePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            string otherPlayer = listOfPlayers.SelectedItem as string;

            if (otherPlayer != null)
            {
                var privateMessageWindow = new PrivateMessageChat(_player.Username, otherPlayer, _roomInfo, _proxy);
                privateMessageWindow.Show();
            }
            else
            {
                // display error message

                // for testing private messaging:
                var privateMessageWindow = new PrivateMessageChat(_player.Username, "Bob1", _roomInfo, _proxy);
                privateMessageWindow.Show();
            }
        }

        private void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            // error handling
            _proxy.LeaveRoomAsync(_roomInfo.RoomId, _player.Username);

            _generalLobby.Content = null;
        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            // check for empty message

            ChatMessage newMessage = new ChatMessage
            {
                RoomId = _roomInfo.RoomId,
                Sender = _player.Username,
                Body = messageInput.Text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = false,
            };

            // error handling

            _proxy.SendChatAsync(newMessage);
            messageInput.Text = "";

            GetMessages();
        }
    }
}
