// WcfService1/InMemoryStore.cs
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using SharedContracts;

namespace WcfService1
{
    internal static class InMemoryStore
    {
        internal static readonly ConcurrentDictionary<string, PlayerInfo> Players =
            new ConcurrentDictionary<string, PlayerInfo>(StringComparer.OrdinalIgnoreCase);

        internal static readonly ConcurrentDictionary<string, LobbyRoomInfo> Rooms =
            new ConcurrentDictionary<string, LobbyRoomInfo>(StringComparer.OrdinalIgnoreCase);

        internal static readonly ConcurrentDictionary<string, SharedFile> Files =
            new ConcurrentDictionary<string, SharedFile>(StringComparer.OrdinalIgnoreCase);

        internal static readonly ConcurrentDictionary<string, List<ChatMessage>> RoomLogs =
            new ConcurrentDictionary<string, List<ChatMessage>>(StringComparer.OrdinalIgnoreCase);

        internal static readonly ConcurrentDictionary<string, List<ChatMessage>> DmLogs =
            new ConcurrentDictionary<string, List<ChatMessage>>(StringComparer.OrdinalIgnoreCase);

        internal const int MaxRoomMessages = 1000;
        internal const int MaxDmMessages = 1000;
    }
}
