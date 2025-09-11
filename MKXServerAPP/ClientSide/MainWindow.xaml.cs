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

using SharedContracts;
using System.ServiceModel;

namespace ClientSide
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly ChannelFactory<LobbyServices> _factory;
        private readonly LobbyServices _proxy;

        public MainWindow()
        {
            InitializeComponent();

            var binding = new BasicHttpBinding();
            var endpoint = new EndpointAddress("http://localhost:59000/Service1.svc");
            _factory = new ChannelFactory<LobbyServices>(binding, endpoint);
            _proxy = _factory.CreateChannel();
        }

        public Task<PlayerInfo> Register(string username)
        {
            return _proxy.RegisterPlayerAsync(username);
        }

        private async void Button_ClickAsync(object sender, RoutedEventArgs e)
        {
            // TODO: exception handling and display error text
            PlayerInfo currentPlayer = await Register(usernameInput.Text);

            // extra players to test private messaging
            await Register("Bob1");
            await Register("Bob2");
            await Register("Bob3");

            // Open general lobby window
            var generalLobby = new GeneralLobby(currentPlayer, _proxy);
            generalLobby.Show();
            this.Close();

        }
    }
}
