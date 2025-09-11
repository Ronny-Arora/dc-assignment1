using Microsoft.Win32;
using SharedContracts;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using OpenFileDialog = System.Windows.Forms.OpenFileDialog;
using SaveFileDialog = System.Windows.Forms.SaveFileDialog;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for LobbyRoomChat.xaml
    /// </summary>
    public partial class LobbyRoomChat : System.Windows.Controls.UserControl
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;
        private List<string> _players;
        private readonly LobbyRoomInfo _roomInfo;
        private readonly ContentControl _generalLobby;
        private List<ChatMessage> _messages;
        private List<SharedFile> _files;

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

        private async void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {
            byte[] fileContent = null;
            var filePath = string.Empty;

            using (OpenFileDialog openFileDialog = new OpenFileDialog())
            {
                openFileDialog.InitialDirectory = "c:\\";
                openFileDialog.Filter = "Image Files (*.PNG;*.BMP;*.JPG;*.JPEG;*.TIFF;*.GIF)|*.BMP;*.JPG;*.GIF;*.PNG;*.JPEG;*.TIFF|Text files (*.txt)|*.txt";
                openFileDialog.FilterIndex = 1;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //Get the path of specified file
                    filePath = openFileDialog.FileName;

                    //Read the contents of the file into a stream
                    var fileStream = openFileDialog.OpenFile();

                    using (BinaryReader r = new BinaryReader(fileStream))
                    {
                        fileContent = r.ReadBytes(50000); // 49KB is max file size allowed
                    }
                }

                SharedFile newFile = new SharedFile
                {
                    RoomID = _roomInfo.RoomId,
                    FileName = openFileDialog.SafeFileName,
                    Uploader = _player.Username,
                    SizeBytes = fileContent.Length,
                    Content = fileContent
                };

                await _proxy.UploadFileAsync(newFile);

                UpdateSharedFiles();
            }
        }

        public async void UpdateSharedFiles()
        {
            // Update for real-time changes

            _files = await _proxy.ListFilesInRoomAsync(_roomInfo.RoomId);

            lobbyFiles.ItemsSource = _files;
        }

        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            // error handling
            await _proxy.LeaveRoomAsync(_roomInfo.RoomId, _player.Username);

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

        private void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            SharedFile fileToDownload = lobbyFiles.SelectedItem as SharedFile;
            var filePath = "";

            if (fileToDownload != null)
            {
                using (SaveFileDialog saveFileDialog = new SaveFileDialog())
                {
                    saveFileDialog.InitialDirectory = "c:\\";
                    saveFileDialog.Filter = "Image Files (*.PNG;*.BMP;*.JPG;*.JPEG;*.TIFF;*.GIF)|*.BMP;*.JPG;*.GIF;*.PNG;*.JPEG;*.TIFF|Text files (*.txt)|*.txt";
                    saveFileDialog.FilterIndex = 1;
                    saveFileDialog.RestoreDirectory = true;

                    if (saveFileDialog.ShowDialog() == DialogResult.OK)
                    {
                        //Get the path of specified file
                        filePath = saveFileDialog.FileName;

                        File.WriteAllBytes(filePath, fileToDownload.Content);
                    }
                }
            }
        }
    }
}
