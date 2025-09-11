using Microsoft.Win32;
using SharedContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private LobbyRoomInfo _roomInfo; // refreshed periodically
        private readonly ContentControl _generalLobby;

        // UI-bound collections
        public ObservableCollection<string> Players { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<ChatMessage> Messages { get; private set; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<SharedFile> Files { get; private set; } = new ObservableCollection<SharedFile>();

        private CancellationTokenSource _pollCts;
        private readonly Random _rng = new Random();

        private DateTime _lastSeenUtc;
        private const int MaxUploadBytes = 50_000; // 49–50 KB limit

        public LobbyRoomChat(PlayerInfo currentPlayer, LobbyServices connection,
                             LobbyRoomInfo currentLobbyRoom, ContentControl generalLobby)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _roomInfo = currentLobbyRoom;
            _generalLobby = generalLobby;

            // Bind UI
            lobbyRoomLabel.Content = "Lobby: " + _roomInfo.RoomName;
            listOfPlayers.ItemsSource = Players;
            lobbyMessages.ItemsSource = Messages;
            lobbyFiles.ItemsSource = Files;

            // Seed from current snapshot
            ReplacePlayers(_roomInfo.Players);
            _lastSeenUtc = DateTime.UtcNow.AddMinutes(-5);

            // One-shot initial loads
            _ = LoadInitialMessagesAsync();
            _ = LoadFilesAsync();

            // Start/stop polling with lifetime
            Loaded += delegate { StartPolling(); };
            Unloaded += delegate { StopPolling(); };
        }

        // --------- Initial loads ---------
        private async Task LoadInitialMessagesAsync()
        {
            try
            {
                DateTime since = DateTime.UtcNow.AddHours(-12);
                List<ChatMessage> history = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, since);
                await Dispatcher.InvokeAsync(delegate
                {
                    Messages.Clear();
                    foreach (ChatMessage m in history.OrderBy(m => m.TimestampUtc)) Messages.Add(m);
                    if (Messages.Count > 0)
                        _lastSeenUtc = Messages[Messages.Count - 1].TimestampUtc.AddTicks(1);
                });
            }
            catch (Exception ex)
            {
                ShowStatus("History load failed: " + ex.Message);
            }
        }

        private async Task LoadFilesAsync()
        {
            try
            {
                List<SharedFile> files = await _proxy.ListFilesInRoomAsync(_roomInfo.RoomId);
                await Dispatcher.InvokeAsync(delegate
                {
                    Files.Clear();
                    foreach (SharedFile f in files.OrderByDescending(f => f.SizeBytes)) Files.Add(f);
                });
            }
            catch (Exception ex)
            {
                ShowStatus("Files load failed: " + ex.Message);
            }
        }

        // --------- Polling ---------
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
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(1200);
            TimeSpan maxDelay = TimeSpan.FromSeconds(8);
            TimeSpan delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // 1) New messages since last seen
                    List<ChatMessage> newMsgs = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, _lastSeenUtc);

                    // 2) Refresh players and files (full refresh is fine for small lists)
                    List<LobbyRoomInfo> allRooms = await _proxy.ListRoomAsync();
                    LobbyRoomInfo refreshed = allRooms.FirstOrDefault(r => r.RoomId == _roomInfo.RoomId);
                    List<SharedFile> latestFiles = await _proxy.ListFilesInRoomAsync(_roomInfo.RoomId);

                    // 3) Apply to UI
                    await Dispatcher.InvokeAsync(delegate
                    {
                        foreach (ChatMessage m in newMsgs.OrderBy(m => m.TimestampUtc))
                        {
                            if (!m.IsPrivate) Messages.Add(m);
                            if (m.TimestampUtc >= _lastSeenUtc)
                                _lastSeenUtc = m.TimestampUtc.AddTicks(1);
                        }

                        if (refreshed != null) _roomInfo = refreshed;
                        if (_roomInfo != null && _roomInfo.Players != null)
                            ReplacePlayers(_roomInfo.Players);

                        if (latestFiles != null)
                        {
                            Files.Clear();
                            foreach (SharedFile f in latestFiles.OrderByDescending(f => f.SizeBytes)) Files.Add(f);
                        }

                        statusText.Text = string.Format("Last refresh: {0:T} • Players: {1} • Msgs: {2} • Files: {3}",
                                                        DateTime.Now, Players.Count, Messages.Count, Files.Count);
                    }, DispatcherPriority.Background);

                    delay = baseDelay;
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    await Dispatcher.InvokeAsync(delegate
                    {
                        statusText.Text = "Polling error: " + ex.Message;
                    });

                    double next = Math.Min(delay.TotalMilliseconds * 2, maxDelay.TotalMilliseconds);
                    delay = TimeSpan.FromMilliseconds(next);
                }

                TimeSpan jitter = TimeSpan.FromMilliseconds(_rng.Next(0, 300));
                try { await Task.Delay(delay + jitter, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // --------- Helpers ---------
        private void ReplacePlayers(IEnumerable<string> names)
        {
            Players.Clear();
            if (names == null) return;
            foreach (string n in names) Players.Add(n);
        }

        private void ShowStatus(string msg)
        {
            if (statusText != null) statusText.Text = msg ?? "";
            System.Diagnostics.Debug.WriteLine(msg);
        }

        // --------- Buttons ---------
        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try { await _proxy.LeaveRoomAsync(_roomInfo.RoomId, _player.Username); }
            catch { /* best effort */ }
            finally
            {
                StopPolling();
                _generalLobby.Content = null;
            }
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
                await _proxy.SendChatAsync(msg);
                messageInput.Text = "";

                // optimistic append
                Messages.Add(msg);
                if (msg.TimestampUtc >= _lastSeenUtc)
                    _lastSeenUtc = msg.TimestampUtc.AddTicks(1);
            }
            catch (Exception ex)
            {
                ShowStatus("Send failed: " + ex.Message);
            }
        }

        private void MessagePlayerButton_Click(object sender, RoutedEventArgs e)
        {
            string otherPlayer = listOfPlayers.SelectedItem as string;
            if (string.IsNullOrEmpty(otherPlayer))
            {
                ShowStatus("Select a player first.");
                return;
            }

            PrivateMessageChat pm = new PrivateMessageChat(_player.Username, otherPlayer, _roomInfo, _proxy);
            pm.Show();
        }

        private async void ShareFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                OpenFileDialog dlg = new OpenFileDialog
                {
                    Title = "Select file to share",
                    Filter = "Images/Text|*.bmp;*.jpg;*.jpeg;*.gif;*.png;*.tiff;*.txt|All files|*.*",
                    Multiselect = false
                };

                bool? ok = dlg.ShowDialog();
                if (ok != true) return;

                byte[] bytes = File.ReadAllBytes(dlg.FileName);
                if (bytes.Length > MaxUploadBytes)
                {
                    ShowStatus("File too large. Max 50 KB.");
                    return;
                }

                SharedFile newFile = new SharedFile
                {
                    RoomID = _roomInfo.RoomId,
                    FileName = System.IO.Path.GetFileName(dlg.FileName),
                    Uploader = _player.Username,
                    SizeBytes = bytes.Length,
                    Content = bytes
                };

                await _proxy.UploadFileAsync(newFile);
                ShowStatus("File uploaded.");

                await LoadFilesAsync(); // refresh immediately
            }
            catch (Exception ex)
            {
                ShowStatus("Upload failed: " + ex.Message);
            }
        }

        private async void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SharedFile fileToDownload = lobbyFiles.SelectedItem as SharedFile;
                if (fileToDownload == null)
                {
                    ShowStatus("Select a file first.");
                    return;
                }

                SaveFileDialog dlg = new SaveFileDialog
                {
                    Title = "Save file",
                    FileName = fileToDownload.FileName,
                    Filter = "All files|*.*"
                };

                bool? ok = dlg.ShowDialog();
                if (ok != true) return;

                byte[] content = fileToDownload.Content;
                if (content == null || content.Length == 0)
                {
                    ShowStatus("No file content available.");
                    return;
                }

                File.WriteAllBytes(dlg.FileName, content);
                ShowStatus("File saved.");
            }
            catch (Exception ex)
            {
                ShowStatus("Download failed: " + ex.Message);
            }
        }
    }
}
