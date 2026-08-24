using System;

namespace CnCNetClient.Online.EventArguments
{
    /// <summary>
    /// Generic event argument class for a IRC server message.
    /// </summary>
    public class ServerMessageEventArgs : EventArgs
    {
        public ServerMessageEventArgs(string message)
        {
            Message = message;
        }

        public string Message { get; private set; }
    }
}
