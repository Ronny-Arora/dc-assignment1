using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.ServiceModel;

namespace SharedContracts
{
    [ServiceContract]
    public interface LobbyServices
    {
        // Players
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<PlayerInfo> RegisterPlayerAsync(string username);

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<bool> HeartbeatAsync(string username); // keep alive

        // Rooms
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<LobbyRoomInfo> CreateRoomAsync(string roomName, int capacity, bool isRanked);

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<bool> JoinRoomAsync(string roomId, string username);

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<bool> LeaveRoomAsync(string roomId, string username);

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<List<LobbyRoomInfo>> ListRoomAsync();

        // Chat
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<bool> SendChatAsync(ChatMessage message);

        // Chat history (pull)
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<List<ChatMessage>> GetRoomHistoryAsync(string roomId, DateTime sinceUtc);

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<List<ChatMessage>> GetPrivateHistoryAsync(string user1, string user2, DateTime sinceUtc);

        // Files (byte[] version for now)
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<SharedFile> UploadFileAsync(SharedFile file); // returns metadata with FileId

        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<SharedFile> DownloadFileAsync(string fileId);

        // Summary
        [OperationContract, FaultContract(typeof(ApiFault))]
        Task<LobbySummary> GetLobbySummaryAsync();

    }
}
