using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Serialization;
using System.Diagnostics.Contracts;

namespace SharedContracts
{
    [DataContract]
    public class PlayerInfo // Fixed typo: PkayerInfo -> PlayerInfo
    {
        [DataMember(Order = 1)] public string Username { get; set; } = "";
        [DataMember(Order = 2)] public DateTime LoggedInAt { get; set; } = DateTime.UtcNow;
    }

    [DataContract]
    public class LobbyRoomInfo
    {
        [DataMember(Order = 1)] public string RoomId { get; set; } = Guid.NewGuid().ToString();
        [DataMember(Order = 2)] public string RoomName { get; set; } = "";
        [DataMember(Order = 3)] public int Capacity { get; set; } = 2;
        [DataMember(Order = 4)] public List<string> Players { get; set; } = new List<string>();
        [DataMember(Order = 5)] public bool IsRanked { get; set; } // PascalCase for property
    }

    [DataContract]
    public class ChatMessage
    {
        [DataMember(Order = 1)] public string RoomId { get; set; } = "";
        [DataMember(Order = 2)] public string Sender { get; set; } = "";
        [DataMember(Order = 3)] public string Body { get; set; } = "";
        [DataMember(Order = 4)] public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
        [DataMember(Order = 5)] public bool IsPrivate { get; set; } // PascalCase for property
        [DataMember(Order = 6)] public string PrivateRecipient { get; set; } = null;
        [DataMember(Order = 7, EmitDefaultValue = false)] public bool IsSystem { get; set; }
    }

    [DataContract]
    public class SharedFile
    {
        [DataMember] public string FieldID { get; set; } = Guid.NewGuid().ToString();
        [DataMember] public string FileName { get; set; }
        [DataMember] public string Uploader { get; set; }
        [DataMember] public DateTime UploadedAtUtc { get; set; } = DateTime.UtcNow;
        [DataMember] public long SizeBytes { get; set; }
        [DataMember] public byte[] Content { get; set; } = Array.Empty<byte>();
    }

    [DataContract]
    public class LobbySummary
    {
        [DataMember(Order = 1)] public List<LobbyRoomInfo> LobbyRooms { get; set; } = new List<LobbyRoomInfo>(); // PascalCase for property
        [DataMember(Order = 2)] public int OnlinePlayers { get; set; } // Fixed DataMember attribute syntax
    }

    [DataContract]
    // Typed fault so WPF can show friendly errors
    public class ApiFault
    {
        [DataMember(Order = 1)] public string Code { get; set; } = "BadRequest";
        [DataMember(Order = 2)] public string Message { get; set; } = "";
        [DataMember(Order = 3)] public string Details { get; set; } // Removed nullable for .NET 4.7.2 compatibility
    }
}