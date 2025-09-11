using Microsoft.Win32;
using SharedContracts;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Media;              // <— for SystemSounds
using System.Runtime.InteropServices; // <— for FlashWindowEx
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ClientSide
{
    public partial class LobbyRoomChat : UserControl
    {
        private readonly LobbyServices _proxy;
        private readonly PlayerInfo _player;
        private LobbyRoomInfo _roomInfo; // refreshed periodically
        private readonly ContentControl _generalLobby;

        public ObservableCollection<string> Players { get; private set; } = new ObservableCollection<string>();
        public ObservableCollection<ChatMessage> Messages { get; private set; } = new ObservableCollection<ChatMessage>();
        public ObservableCollection<SharedFile> Files { get; private set; } = new ObservableCollection<SharedFile>();

        private CancellationTokenSource _pollCts;
        private readonly Random _rng = new Random();

        private DateTime _lastSeenUtc;

        // NEW: last seen private message per counterpart
        private readonly Dictionary<string, DateTime> _lastPmSeenUtc = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private const int MaxUploadBytes = 50_000;

        public LobbyRoomChat(PlayerInfo currentPlayer, LobbyServices connection,
                             LobbyRoomInfo currentLobbyRoom, ContentControl generalLobby)
        {
            InitializeComponent();

            _proxy = connection;
            _player = currentPlayer;
            _roomInfo = currentLobbyRoom;
            _generalLobby = generalLobby;

            lobbyRoomLabel.Content = "Lobby: " + _roomInfo.RoomName;
            listOfPlayers.ItemsSource = Players;
            lobbyMessages.ItemsSource = Messages;
            lobbyFiles.ItemsSource = Files;

            ReplacePlayers(_roomInfo.Players);
            _lastSeenUtc = DateTime.UtcNow.AddMinutes(-5);

            // init PM cursors for current players (so we don’t notify old history)
            SeedPmCursors(_roomInfo.Players);

            _ = LoadInitialMessagesAsync();
            _ = LoadFilesAsync();

            Loaded += delegate { StartPolling(); };
            Unloaded += delegate { StopPolling(); };
        }

        private void SeedPmCursors(IEnumerable<string> names)
        {
            if (names == null) return;
            foreach (var n in names)
            {
                if (string.Equals(n, _player.Username, StringComparison.OrdinalIgnoreCase)) continue;
                if (!_lastPmSeenUtc.ContainsKey(n)) _lastPmSeenUtc[n] = DateTime.UtcNow.AddMinutes(-2);
            }
        }

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
            catch (Exception ex) { ShowStatus("History load failed: " + ex.Message); }
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
            catch (Exception ex) { ShowStatus("Files load failed: " + ex.Message); }
        }

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
            TimeSpan baseDelay = TimeSpan.FromMilliseconds(1100);
            TimeSpan maxDelay = TimeSpan.FromSeconds(8);
            TimeSpan delay = baseDelay;

            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // --- Room messages ---
                    List<ChatMessage> newMsgs = await _proxy.GetRoomHistoryAsync(_roomInfo.RoomId, _lastSeenUtc);

                    // --- Refresh players + files ---
                    List<LobbyRoomInfo> allRooms = await _proxy.ListRoomAsync();
                    LobbyRoomInfo refreshed = allRooms.FirstOrDefault(r => r.RoomId == _roomInfo.RoomId);
                    List<SharedFile> latestFiles = await _proxy.ListFilesInRoomAsync(_roomInfo.RoomId);

                    // --- Private messages for me (from each other player) ---
                    var pmAlerts = new List<(string fromUser, int count)>();
                    var counterparts = (refreshed?.Players ?? _roomInfo.Players ?? new List<string>())
                                       .Where(u => !string.Equals(u, _player.Username, StringComparison.OrdinalIgnoreCase))
                                       .Distinct(StringComparer.OrdinalIgnoreCase)
                                       .ToList();

                    // ensure cursors exist for newcomers
                    foreach (var u in counterparts)
                        if (!_lastPmSeenUtc.ContainsKey(u)) _lastPmSeenUtc[u] = DateTime.UtcNow.AddMinutes(-2);

                    foreach (var other in counterparts)
                    {
                        DateTime since = _lastPmSeenUtc[other];
                        List<ChatMessage> pmNew = await _proxy.GetPrivateHistoryAsync(_player.Username, other, since);

                        // Only count new messages SENT BY 'other' to me (ignore my outgoing)
                        var incoming = pmNew.Where(m =>
                            m.IsPrivate &&
                            !string.Equals(m.Sender, _player.Username, StringComparison.OrdinalIgnoreCase) &&
                            (string.Equals(m.PrivateRecipient, _player.Username, StringComparison.OrdinalIgnoreCase) ||
                             string.IsNullOrEmpty(m.PrivateRecipient))) // in case server omits on history
                            .OrderBy(m => m.TimestampUtc)
                            .ToList();

                        if (incoming.Count > 0)
                        {
                            // advance cursor
                            _lastPmSeenUtc[other] = incoming.Last().TimestampUtc.AddTicks(1);
                            pmAlerts.Add((other, incoming.Count));
                        }
                        else
                        {
                            // advance if server has older echoes
                            var last = pmNew.OrderBy(m => m.TimestampUtc).LastOrDefault();
                            if (last != null && last.TimestampUtc >= _lastPmSeenUtc[other])
                                _lastPmSeenUtc[other] = last.TimestampUtc.AddTicks(1);
                        }
                    }

                    // --- Apply to UI ---
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
                        {
                            ReplacePlayers(_roomInfo.Players);
                            // ensure cursors exist after refresh
                            SeedPmCursors(_roomInfo.Players);
                        }

                        if (latestFiles != null)
                        {
                            Files.Clear();
                            foreach (SharedFile f in latestFiles.OrderByDescending(f => f.SizeBytes)) Files.Add(f);
                        }

                        statusText.Text = string.Format("Last refresh: {0:T} • Players: {1} • Msgs: {2} • Files: {3}",
                                                        DateTime.Now, Players.Count, Messages.Count, Files.Count);

                        // Show one toast per cycle (aggregate if multiple)
                        if (pmAlerts.Count == 1)
                        {
                            ShowPmToast(pmAlerts[0].fromUser + " sent you " + pmAlerts[0].count + " private message(s)");
                            TryFlashTaskbar();
                            TryPing();
                        }
                        else if (pmAlerts.Count > 1)
                        {
                            int sum = pmAlerts.Sum(a => a.count);
                            ShowPmToast(string.Format("{0} users sent you {1} private messages", pmAlerts.Count, sum));
                            TryFlashTaskbar();
                            TryPing();
                        }
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

                TimeSpan jitter = TimeSpan.FromMilliseconds(_rng.Next(0, 280));
                try { await Task.Delay(delay + jitter, ct); }
                catch (OperationCanceledException) { break; }
            }
        }

        // ----- Toast / Attention -----
        private async void ShowPmToast(string text)
        {
            if (pmToast == null || pmToastText == null) return;
            pmToastText.Text = text ?? "New private message";
            pmToast.IsOpen = true;

            // auto-hide after ~3 seconds
            try
            {
                await Task.Delay(3000);
                pmToast.IsOpen = false;
            }
            catch { /* ignore */ }
        }

        private void TryPing()
        {
            try { SystemSounds.Asterisk.Play(); } catch { }
        }

        // Flash taskbar (Windows) to attract attention
        private void TryFlashTaskbar()
        {
            try
            {
                var wnd = Window.GetWindow(this);
                if (wnd == null) return;

                FLASHWINFO fw = new FLASHWINFO();
                fw.cbSize = Convert.ToUInt32(Marshal.SizeOf(fw));
                fw.hwnd = new System.Windows.Interop.WindowInteropHelper(wnd).Handle;
                fw.dwFlags = 0x00000003 /* FLASHW_ALL */ | 0x0000000C /* FLASHW_TIMERNOFG */;
                fw.uCount = 3;
                fw.dwTimeout = 0;
                FlashWindowEx(ref fw);
            }
            catch { /* best-effort */ }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct FLASHWINFO
        {
            public uint cbSize;
            public IntPtr hwnd;
            public uint dwFlags;
            public uint uCount;
            public uint dwTimeout;
        }

        [DllImport("user32.dll")]
        private static extern bool FlashWindowEx(ref FLASHWINFO pfwi);

        // ----- Helpers / UI -----
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

        // ----- Buttons -----
        private async void LeaveRoomButton_Click(object sender, RoutedEventArgs e)
        {
            try { await _proxy.LeaveRoomAsync(_roomInfo.RoomId, _player.Username); }
            catch { }
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
                Messages.Add(msg);
                if (msg.TimestampUtc >= _lastSeenUtc) _lastSeenUtc = msg.TimestampUtc.AddTicks(1);
            }
            catch (Exception ex) { ShowStatus("Send failed: " + ex.Message); }
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
                var dlg = new OpenFileDialog
                {
                    Title = "Select file to share",
                    Filter = "Images/Text|*.bmp;*.jpg;*.jpeg;*.gif;*.png;*.tiff;*.txt|All files|*.*",
                    Multiselect = false
                };
                bool? ok = dlg.ShowDialog();
                if (ok != true) return;

                byte[] bytes = File.ReadAllBytes(dlg.FileName);
                if (bytes.Length > MaxUploadBytes) { ShowStatus("File too large (max 50 KB)."); return; }

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
                await LoadFilesAsync();
            }
            catch (Exception ex) { ShowStatus("Upload failed: " + ex.Message); }
        }

        private async void DownloadFileButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SharedFile fileToDownload = lobbyFiles.SelectedItem as SharedFile;
                if (fileToDownload == null) { ShowStatus("Select a file first."); return; }

                var dlg = new SaveFileDialog
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
                    ShowStatus("No file content available."); // or call DownloadFileAsync here if you have it
                    return;
                }

                File.WriteAllBytes(dlg.FileName, content);
                ShowStatus("File saved.");
            }
            catch (Exception ex) { ShowStatus("Download failed: " + ex.Message); }
        }
    }
}
