using System.Collections.Generic;
using System.ServiceModel;
using System.Threading.Tasks;

namespace SharedContracts
{
    [ServiceContract(CallbackContract = typeof(ILobbyClientCallbacks))]
    public interface ILobbyDuplexService
    {
        // Session
        [OperationContract] Task<bool> LoginAsync(string username);
        [OperationContract] Task LogoutAsync();
        [OperationContract(IsOneWay = true)] void Heartbeat();

        // Lobby / Rooms
        [OperationContract] Task<LobbyRoomInfo> CreateRoomAsync(string roomName, int capacity, bool isRanked);
        [OperationContract] Task<bool> JoinRoomAsync(string roomId);
        [OperationContract] Task<bool> LeaveRoomAsync(string roomId);

        // Subscriptions
        [OperationContract] Task JoinLobbyAsync();
        [OperationContract] Task LeaveLobbyAsync();
        [OperationContract] Task SubscribeRoomAsync(string roomId);
        [OperationContract] Task UnsubscribeRoomAsync(string roomId);

        // Messaging
        [OperationContract(IsOneWay = true)] void SendRoomMessage(ChatMessage message);
        [OperationContract(IsOneWay = true)] void SendPrivateMessage(ChatMessage message);
        [OperationContract]
        Task<List<ChatMessage>> GetPrivateHistoryAsync(string user1, string user2, System.DateTime sinceUtc);

        // Files
        [OperationContract] Task<SharedFile> UploadFileAsync(SharedFile file);
    }
}
