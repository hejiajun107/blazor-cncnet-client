using System;
using CnCNetClient.Domain.Multiplayer;

namespace CnCNetClient.Online.EventArguments
{
    public class FavoriteMapEventArgs : EventArgs
    {
        public readonly Map Map;

        public FavoriteMapEventArgs(Map map)
        {
            Map = map;
        }
    }
}
