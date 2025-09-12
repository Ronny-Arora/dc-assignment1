using System.Collections.Generic;
using System.ServiceModel;
using System.Runtime.Serialization;


namespace SharedContracts
{
    [ServiceContract]
    public interface ILobbyClientCallbacks
    {
        // Lobby / presence / rooms
        [OperationContract(IsOneWay = true)] void OnLobbySummary(LobbySummary summary);
        [OperationContract(IsOneWay = true)] void OnRoomCreated(LobbyRoomInfo room);
        [OperationContract(IsOneWay = true)] void OnRoomUpdated(LobbyRoomInfo room);
        [OperationContract(IsOneWay = true)] void OnPresenceChanged(string username, bool isOnline);

        // Messages
        [OperationContract(IsOneWay = true)] void OnRoomMessage(ChatMessage message);
        [OperationContract(IsOneWay = true)] void OnPrivateMessage(ChatMessage message);

        // Files
        [OperationContract(IsOneWay = true)] void OnFileUploaded(SharedFile file);


    }
}
