// WcfService1/ChannelUserMap.cs
using System;
using System.Collections.Concurrent;
using System.ServiceModel;

namespace WcfService1
{
    internal static class ChannelUserMap
    {
        private static readonly ConcurrentDictionary<IContextChannel, string> _userByChannel =
            new ConcurrentDictionary<IContextChannel, string>();

        public static void AddForCurrentChannel(string username)
        {
            var ch = OperationContext.Current.Channel; // IContextChannel
            _userByChannel[ch] = username;
            void remove(object s, EventArgs e) => _userByChannel.TryRemove(ch, out _);
            ch.Closed += remove; ch.Faulted += remove;
        }

        public static string GetForCurrentChannel()
        {
            var ch = OperationContext.Current.Channel;
            return _userByChannel.TryGetValue(ch, out var u) ? u : null;
        }

        public static void RemoveForCurrentChannel()
        {
            var ch = (IClientChannel)OperationContext.Current.Channel;
            _userByChannel.TryRemove(ch, out _);
        }
    }
}
