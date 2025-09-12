using System;
using System.ServiceModel;
using System.Threading.Tasks;
using System.Windows;
using SharedContracts;
using ClientSide.Services;  // ClientCallback, DuplexServerProxy

namespace ClientSide
{
    public partial class MainWindow : Window
    {
        private ChannelFactory<LobbyServices> _factory;
        private LobbyServices _pollingProxy;

        public MainWindow()
        {
            InitializeComponent();

            // Keep your HTTP polling service for registration
            var binding = new BasicHttpBinding();
            var endpoint = new EndpointAddress("http://localhost:59000/Service1.svc");
            _factory = new ChannelFactory<LobbyServices>(binding, endpoint);
            _pollingProxy = _factory.CreateChannel();
        }

        private Task<PlayerInfo> Register(string username) =>
            _pollingProxy.RegisterPlayerAsync(username);

        private async void Button_ClickAsync(object sender, RoutedEventArgs e)
        {
            var username = (usernameInput.Text ?? "").Trim();
            if (string.IsNullOrWhiteSpace(username))
            {
                MessageBox.Show("Enter a username.");
                return;
            }

            // 1) Register via HTTP (so you appear online to polling clients too)
            PlayerInfo currentPlayer;
            try { currentPlayer = await Register(username); }
            catch (Exception ex) { MessageBox.Show("Register failed: " + ex.Message); return; }

            // 2) Open duplex channel + join lobby (server push)
            var callback = new ClientCallback();
            var proxy = new DuplexServerProxy(
                callback,
                "net.tcp://localhost:9090/MKX/Duplex"   // <-- change to 9090/MKX/Duplex
            );


            try
            {
                proxy.Open();
                var ok = await proxy.Channel.LoginAsync(username);
                if (!ok) { MessageBox.Show("Username already in use on duplex channel."); return; }
                await proxy.Channel.JoinLobbyAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Duplex connect failed: " + ex.Message);
                try { proxy.Dispose(); } catch { }
                return;
            }

            // 3) Show the duplex lobby and close this window
            var generalLobby = new GeneralLobby(currentPlayer, proxy, callback);
            generalLobby.Show();
            this.Close();
        }
    }
}
