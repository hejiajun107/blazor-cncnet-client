using System;

namespace CnCNetClient.Domain.Multiplayer.LAN
{
    public class NetworkMessageEventArgs : EventArgs
    {
        public NetworkMessageEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }
}
