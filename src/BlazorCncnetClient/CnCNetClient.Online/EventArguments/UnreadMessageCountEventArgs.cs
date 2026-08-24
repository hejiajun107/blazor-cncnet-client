using System;

namespace CnCNetClient.Online.EventArguments
{
    public class UnreadMessageCountEventArgs : EventArgs
    {
        public int UnreadMessageCount { get; set; }

        public UnreadMessageCountEventArgs(int unreadMessageCount)
        {
            UnreadMessageCount = unreadMessageCount;
        }
    }
}
