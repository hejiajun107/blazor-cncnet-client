using System;
using System.Net;

using SixLabors.ImageSharp;

namespace CnCNetClient.Domain.Multiplayer.LAN
{
    public class LANLobbyUser
    {
        public LANLobbyUser(string name, Image gameTexture, IPEndPoint endPoint)
        {
            Name = name;
            GameTexture = gameTexture;
            EndPoint = endPoint;
        }

        public string Name { get; private set; }
        public Image GameTexture { get; private set; }
        public IPEndPoint EndPoint { get; private set; }

        private readonly object timeWithoutRefreshLock = new();
        public TimeSpan TimeWithoutRefresh { get; private set; }

        public void ClearTimeWithoutRefresh()
        {
            lock (timeWithoutRefreshLock)
            {
                TimeWithoutRefresh = TimeSpan.Zero;
            }
        }

        public void AddToTimeWithoutRefresh(TimeSpan timeToAdd)
        {
            lock (timeWithoutRefreshLock)
            {
                TimeWithoutRefresh += timeToAdd;
            }
        }
    }
}
