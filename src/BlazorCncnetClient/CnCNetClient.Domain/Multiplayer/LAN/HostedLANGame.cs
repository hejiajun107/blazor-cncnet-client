using System;
using System.Net;

using CnCNetClient.Core;

using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Domain.Multiplayer.CnCNet;

using CnCNetClient.Tools;

namespace CnCNetClient.Domain.LAN
{
    /// <summary>LAN 广播 GAME 消息解析出的房（D3.1/D3.2）。由 UI 层 LanLobbyService 直接使用。</summary>
    public class HostedLANGame : GenericHostedGame
    {
        public IPEndPoint EndPoint { get; set; }

        public override string RoomName
        {
            get => HostName + "'s Game" + (EndPoint != null ? " [" + EndPoint.Address.ToString() + "]" : "");
            set
            {
                // RoomName is generated from HostName and EndPoint. Setting it has no effect.
            }
        }

        public string LoadedGameID { get; set; }

        public TimeSpan TimeWithoutRefresh { get; set; }

        public override int Ping
        {
            get
            {
                return -1;
            }
        }

        public bool SetDataFromStringArray(GameCollection gc, string[] parameters)
        {
            if (parameters.Length != 10)
            {
                Logger.Log("Ignoring LAN GAME message because of an incorrect number of parameters.");
                return false;
            }

            if (parameters[0] != ProgramConstants.LAN_PROTOCOL_REVISION)
                return false;

            GameVersion = parameters[1];
            Incompatible = GameVersion != ProgramConstants.GAME_VERSION;
            Game = gc.GameList.Find(g => g.InternalName.ToUpperInvariant() == parameters[2]);
            if (Game == null)
                return false;
            Map = parameters[3];
            GameMode = parameters[4];
            LoadedGameID = parameters[5];
            string[] players = parameters[6].Split(',');
            Players = players;
            if (players.Length == 0)
                return false;
            HostName = players[0];
            Locked = Conversions.IntFromString(parameters[7], 1) > 0;
            IsLoadedGame = Conversions.IntFromString(parameters[8], 0) > 0;
            LastRefreshTime = DateTime.Now;
            TimeWithoutRefresh = TimeSpan.Zero;

            // RoomName is now generated from HostName and EndPoint. Setting it has no effect.
            // RoomName = HostName + "'s Game";

            MapHash = parameters[9];

            return true;
        }
    }
}
