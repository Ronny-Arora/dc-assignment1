using System;
using System.ServiceModel;
using SharedContracts;

namespace ClientSide.Services
{
    public sealed class DuplexServerProxy : IDisposable
    {
        private readonly DuplexChannelFactory<ILobbyDuplexService> _factory;
        public ILobbyDuplexService Channel { get; private set; }

        public DuplexServerProxy(ClientCallback callback, string endpoint = "net.tcp://localhost:8080/WcfService1/LobbyDuplexService.svc")
        {
            var binding = new NetTcpBinding(SecurityMode.None);
            binding.OpenTimeout = TimeSpan.FromSeconds(5);
            binding.SendTimeout = TimeSpan.FromSeconds(5);
            binding.ReceiveTimeout = TimeSpan.FromMinutes(20);
            binding.MaxReceivedMessageSize = 64 * 1024 * 1024;

            var address = new EndpointAddress(endpoint);
            var ctx = new InstanceContext(callback);
            _factory = new DuplexChannelFactory<ILobbyDuplexService>(ctx, binding, address);
            Channel = _factory.CreateChannel();
        }

        public void Open() { ((ICommunicationObject)Channel).Open(); }
        public void Close() { ((ICommunicationObject)Channel).Close(); }

        public void Dispose()
        {
            try { Close(); } catch { ((ICommunicationObject)Channel).Abort(); }
            try { _factory.Close(); } catch { _factory.Abort(); }
        }
    }
}
