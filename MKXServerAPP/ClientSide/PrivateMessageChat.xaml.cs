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
using System.Windows.Shapes;

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
        private List<ChatMessage> _messages;
        private readonly LobbyRoomInfo _room;

        public PrivateMessageChat(string currentPlayer, string otherPlayer,
            LobbyRoomInfo currentRoom, LobbyServices connection)
        {
            InitializeComponent();

            _proxy = connection;
            _currentPlayer = currentPlayer;
            _otherPlayer = otherPlayer;
            _room = currentRoom;

            privateMessageLabel.Content += _otherPlayer;
            GetMessages();
        }

        public async void GetMessages()
        {
            // Update for real-time changes

            int currentYear = DateTime.Now.Year;
            DateTime lastYearDate = new DateTime(currentYear - 1, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            _messages = await _proxy.GetPrivateHistoryAsync(_currentPlayer, _otherPlayer, lastYearDate);

            privateMessages.ItemsSource = _messages;
        }

        private void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {

        }

        private void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            // check for empty message

            ChatMessage newMessage = new ChatMessage
            {
                RoomId = _room.RoomId,
                Sender = _currentPlayer,
                Body = messageInput.Text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = true,
                PrivateRecipient = _otherPlayer,
            };

            // error handling

            _proxy.SendChatAsync(newMessage);
            messageInput.Text = "";

            GetMessages();
        }
    }
}
