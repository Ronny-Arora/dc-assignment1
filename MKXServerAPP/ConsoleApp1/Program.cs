using System;
using System.ServiceModel;
using System.ServiceModel.Description;
using SharedContracts;
using WcfService1;

namespace DuplexHost
{
    class Program
    {
        static void Main()
        {
            var baseAddress = new Uri("net.tcp://localhost:9090/MKX");
            var binding = new NetTcpBinding(SecurityMode.None)
            {
                OpenTimeout = TimeSpan.FromSeconds(10),
                SendTimeout = TimeSpan.FromSeconds(10),
                ReceiveTimeout = TimeSpan.FromMinutes(20),
                MaxReceivedMessageSize = 64 * 1024 * 1024
            };

            var singleton = new LobbyDuplexService(); // InstanceContextMode.Single in your class
            using (var host = new ServiceHost(singleton, baseAddress))
            {
                host.AddServiceEndpoint(typeof(ILobbyDuplexService), binding, "Duplex");

                // Optional MEX (handy for testing)
                var smb = new ServiceMetadataBehavior();
                host.Description.Behaviors.Add(smb);
                host.AddServiceEndpoint(ServiceMetadataBehavior.MexContractName,
                    MetadataExchangeBindings.CreateMexTcpBinding(), "mex");

                host.Open();
                Console.WriteLine("Duplex up at: net.tcp://localhost:9090/MKX/Duplex");
                Console.WriteLine("Press ENTER to exit.");
                Console.ReadLine();
                host.Close();
            }
        }
    }
}
