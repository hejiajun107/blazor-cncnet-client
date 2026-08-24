using System;

namespace CnCNetClient.Domain.Multiplayer.CnCNet
{
    public class MapEventArgs : EventArgs
    {
        public MapEventArgs(Map map)
        {
            Map = map;
        }

        public Map Map { get; private set; }
    }
}
