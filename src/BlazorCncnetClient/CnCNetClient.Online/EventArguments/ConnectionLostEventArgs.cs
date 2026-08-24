using System;

namespace CnCNetClient.Online.EventArguments
{
    public class ConnectionLostEventArgs : EventArgs
    {
        public ConnectionLostEventArgs(string reason)
        {
            Reason = reason;
        }
        public string Reason { get; private set; }
    }
}
