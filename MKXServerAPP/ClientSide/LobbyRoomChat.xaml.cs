using Microsoft.Win32;
using SharedContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Controls;
using ClientSide.Services;   // DuplexServerProxy, ClientCallback
using System.Threading.Tasks; // for Task.Run

namespace ClientSide
{
    public partial class LobbyRoomChat : UserControl
    {
        private readonly DuplexServerProxy _proxy;
        private readonly ClientCallback _callback;
        private readonly PlayerInfo _player;
        private LobbyRoomInfo _roomInfo; // refreshed via push
        private readonly ContentControl _generalLobby;

        public ObservableCollection<string> Players { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<ChatMessage> Messages { get; private set; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<SharedFile> Files { get; private set; } = new ObservableCollection<SharedFile>();

        private const int MaxUploadBytes = 50_000;

        public LobbyRoomChat(PlayerInfo currentPlayer,
                             DuplexServerProxy proxy,
                             ClientCallback callback,
                             LobbyRoomInfo currentLobbyRoom,
                             ContentControl generalLobby)
        {
            InitializeComponent();

            _proxy = proxy;
            _callback = callback;
            _player = currentPlayer;
            _roomInfo = currentLobbyRoom;
            _generalLobby = generalLobby;

            lobbyRoomLabel.Content = "Lobby: " + _roomInfo.RoomName;
            listOfPlayers.ItemsSource = Players;
            lobbyMessages.ItemsSource = Messages;
            lobbyFiles.ItemsSource = Files;

            ReplacePlayers(_roomInfo.Players);

            // wire callbacks for this room
            _callback.RoomUpdated += OnRoomUpdated;
            _callback.RoomMessage += OnRoomMessage;
            _callback.FileUploaded += OnFileUploaded;
            _callback.PrivateMessage += OnPrivateMessage;

            Unloaded += delegate
            {
                _callback.RoomUpdated -= OnRoomUpdated;
                _callback.RoomMessage -= OnRoomMessage;
                _callback.FileUploaded -= OnFileUploaded;
                _callback.PrivateMessage -= OnPrivateMessage;
            };
        }

        private void OnRoomUpdated(LobbyRoomInfo room)
        {
            if (room == null || !string.Equals(room.RoomId, _roomInfo.RoomId, StringComparison.OrdinalIgnoreCase)) return;
            Dispatcher.Invoke(delegate
            {
                _roomInfo = room;
                ReplacePlayers(room.Players);
                statusText.Text = "Room updated (push).";
            });
        }

        private void OnRoomMessage(ChatMessage msg)
        {
            if (msg == null || msg.IsPrivate) return;
            if (!string.Equals(msg.RoomId, _roomInfo.RoomId, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.Invoke(delegate
            {
                Messages.Add(msg);
                TryPing();
            });
        }

        private void OnPrivateMessage(ChatMessage msg)
        {
            if (msg == null || !msg.IsPrivate) return;
            if (!string.Equals(msg.RoomId, _roomInfo.RoomId, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.Equals(msg.PrivateRecipient, _player.Username, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.Invoke(delegate
            {
                pmToastText.Text = (msg.Sender ?? "someone") + " sent you a private message";
                pmToast.IsOpen = true;
                TryPing();
            });
        }

        private void OnFileUploaded(SharedFile file)
        {
            if (file == null) return;
            if (!string.Equals(file.RoomID, _roomInfo.RoomId, StringComparison.OrdinalIgnoreCase)) return;

            Dispatcher.Invoke(delegate
            {
                Files.Add(file);
                statusText.Text = "New file: " + file.FileName;
            });
        }

        // ----- Buttons -----
        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _proxy.Channel.UnsubscribeRoomAsync(_roomInfo.RoomId);
                await _proxy.Channel.LeaveRoomAsync(_roomInfo.RoomId);
            }
            catch { }
            finally { _generalLobby.Content = null; }
        }

        private void TryPing() { try { SystemSounds.Asterisk.Play(); } catch { } }

        private void ReplacePlayers(IEnumerable<string> names)
        {
            Players.Clear();
            if (names == null) return;
            foreach (var n in names.Distinct(StringComparer.OrdinalIgnoreCase)) Players.Add(n);
        }

        private void MessagePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            string otherPlayer = listOfPlayers.SelectedItem as string;
            if (string.IsNullOrEmpty(otherPlayer))
            {
                statusText.Text = "Select a player first.";
                return;
            }

            // IMPORTANT: pass the duplex callback so PM window appends pushed messages,
            // and (with the updated PM window) loads history on open.
            var pm = new PrivateMessageChat(_player.Username, otherPlayer, _roomInfo, _proxy, _callback);
            pm.Owner = Window.GetWindow(this);
            pm.Show();
        }

        private async void SendMessageButton_Click(object sender, RoutedEventArgs e)
        {
            string text = (messageInput.Text ?? "").Trim();
            if (string.IsNullOrEmpty(text)) return;

            ChatMessage msg = new ChatMessage
            {
                RoomId = _roomInfo.RoomId,
                Sender = _player.Username,
                Body = text,
                TimestampUtc = DateTime.UtcNow,
                IsPrivate = false
            };

            try
            {
                _proxy.Channel.SendRoomMessage(msg); // IsOneWay
                messageInput.Text = "";
                Messages.Add(msg); // optimistic; server will also push
            }
            catch (Exception ex) { statusText.Text = "Send failed: " + ex.Message; }
        }

        private async void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new OpenFileDialog
                {
                    Title = "Select file to share",
                    Filter = "Images/Text|*.bmp;*.jpg;*.jpeg;*.gif;*.png;*.tiff;*.txt|All files|*.*",
                    Multiselect = false
                };
                bool? ok = dlg.ShowDialog();
                if (ok != true) return;

                byte[] bytes = File.ReadAllBytes(dlg.FileName);
                if (bytes.Length > MaxUploadBytes) { statusText.Text = "File too large (max 50 KB)."; return; }

                SharedFile newFile = new SharedFile
                {
                    RoomID = _roomInfo.RoomId,
                    FileName = System.IO.Path.GetFileName(dlg.FileName),
                    Uploader = _player.Username,
                    SizeBytes = bytes.Length,
                    Content = bytes
                };

                await _proxy.Channel.UploadFileAsync(newFile);
                statusText.Text = "File uploaded.";
                // list updates by push (OnFileUploaded)
            }
            catch (Exception ex) { statusText.Text = "Upload failed: " + ex.Message; }
        }

        private async void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SharedFile fileToDownload = lobbyFiles.SelectedItem as SharedFile;
                if (fileToDownload == null) { statusText.Text = "Select a file first."; return; }

                var dlg = new SaveFileDialog
                {
                    Title = "Save file",
                    FileName = fileToDownload.FileName,
                    Filter = "All files|*.*"
                };
                bool? ok = dlg.ShowDialog();
                if (ok != true) return;

                // If your pushed object contains content, use it; otherwise you could
                // add a duplex DownloadFileAsync and call it here.
                byte[] content = fileToDownload.Content;
                if (content == null || content.Length == 0)
                {
                    statusText.Text = "No file content available for download.";
                    return;
                }

                // Make the write non-blocking for the UI thread
                await Task.Run(() => File.WriteAllBytes(dlg.FileName, content));

                statusText.Text = "File saved.";
            }
            catch (Exception ex) { statusText.Text = "Download failed: " + ex.Message; }
        }
    }
}
