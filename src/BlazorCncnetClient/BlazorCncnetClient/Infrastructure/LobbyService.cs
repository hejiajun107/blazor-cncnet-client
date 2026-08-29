using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Online;
using CnCNetClient.Online.EventArguments;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 大厅状态服务（M3.2 spec §5.3）：维护频道 / 聊天 / 房间 / 玩家状态。
    /// 网络事件已由 CnCNetManager 派发到 UI 线程，此处仅做聚合与缓存。
    /// 房间列表协议迁移自原客户端 GameBroadcastChannel_CTCPReceived。
    /// </summary>
    public sealed class LobbyService : IDisposable
    {
        private const int STALE_GAME_SECONDS = 15;
        private const int CLEANUP_INTERVAL_MS = 5000;

        public event Action? Changed;

        private readonly CnCNetManager _manager;
        private readonly GameCollection _gameCollection;
        private readonly CnCNetUserData _userData;
        private readonly IDispatcher _dispatcher;

        private readonly List<Channel> _channels = new();
        private readonly List<LobbyGameItem> _games = new();
        private readonly Dictionary<string, LobbyGameItem> _gamesByHost = new(StringComparer.OrdinalIgnoreCase);
        private Timer? _cleanupTimer;
        private bool _initialized;
        private bool _wired;

        public CnCNetGame? LocalGame { get; }
        public Channel? CurrentChannel { get; private set; }
        public Channel? BroadcastChannel { get; private set; }
        public bool IsConnected => _manager.IsConnected;
        public IReadOnlyList<Channel> Channels => _channels;
        public IReadOnlyList<LobbyGameItem> Games => _games;

        public LobbyService(CnCNetManager manager, GameCollection gameCollection, CnCNetUserData userData, IDispatcher dispatcher)
        {
            _manager = manager;
            _gameCollection = gameCollection;
            _userData = userData;
            _dispatcher = dispatcher;
            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));
        }

        /// <summary>订阅管理器事件并（若已连接）直接进入大厅状态。</summary>
        public void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;
            WireManager();
            if (_manager.IsConnected)
                JoinDefaultChannels();
        }

        /// <summary>离开大厅：断开连接并清理状态。</summary>
        public void Logout()
        {
            foreach (var channel in _channels)
            {
                _manager.LeaveFromChannel(channel);
                UnwireChannel(channel);
            }
            _channels.Clear();
            BroadcastChannel = null;
            ClearGames();
            if (_manager.IsConnected)
                _manager.Disconnect();
            CurrentChannel = null;
            NotifyChanged();
        }

        private void WireManager()
        {
            if (_wired)
                return;
            _wired = true;
            _manager.Connected += OnConnected;
            _manager.Disconnected += OnDisconnected;
            _manager.ConnectionLost += OnConnectionLost;
            _cleanupTimer = new Timer(_ => _dispatcher.Post(CleanupStaleGames), null, CLEANUP_INTERVAL_MS, CLEANUP_INTERVAL_MS);
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            JoinDefaultChannels();
            NotifyChanged();
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            ClearState();
            NotifyChanged();
        }

        private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
        {
            ClearState();
            NotifyChanged();
        }

        private void ClearState()
        {
            foreach (var channel in _channels)
                UnwireChannel(channel);
            _channels.Clear();
            BroadcastChannel = null;
            CurrentChannel = null;
            ClearGames();
        }

        private void ClearGames()
        {
            _games.Clear();
            _gamesByHost.Clear();
        }

        private void JoinDefaultChannels()
        {
            foreach (var game in _gameCollection.GameList.Where(g => g.AlwaysEnabled && !string.IsNullOrEmpty(g.ChatChannel)))
                EnsureChannel(game.UIName ?? game.InternalName ?? "CnCNet", game.ChatChannel!, persistent: true);

            if (LocalGame is not null)
            {
                if (!string.IsNullOrEmpty(LocalGame.ChatChannel))
                    EnsureChannel(LocalGame.UIName ?? LocalGame.InternalName ?? "CnCNet", LocalGame.ChatChannel!, persistent: true);
                if (!string.IsNullOrEmpty(LocalGame.GameBroadcastChannel))
                    EnsureBroadcastChannel(LocalGame.UIName ?? LocalGame.InternalName ?? "CnCNet", LocalGame.GameBroadcastChannel!);
            }

            var target = _channels.FirstOrDefault(c => c.ChannelName == LocalGame?.ChatChannel)
                ?? _channels.FirstOrDefault();
            if (target is not null && CurrentChannel != target)
            {
                CurrentChannel = target;
                target.RequestUserInfo();
            }
        }

        private Channel EnsureChannel(string uiName, string channelName, bool persistent)
        {
            var channel = _manager.FindChannel(channelName);
            if (channel is null)
            {
                channel = _manager.CreateChannel(uiName, channelName, persistent, isChatChannel: true, password: null);
                _manager.AddChannel(channel);
                _channels.Add(channel);
                WireChannel(channel);
                channel.Join();
            }
            else if (!_channels.Contains(channel))
            {
                _channels.Add(channel);
                WireChannel(channel);
            }

            return channel;
        }

        private Channel EnsureBroadcastChannel(string uiName, string channelName)
        {
            var channel = _manager.FindChannel(channelName);
            if (channel is null)
            {
                channel = _manager.CreateChannel(uiName + " 广播", channelName, persistent: true, isChatChannel: false, password: null);
                _manager.AddChannel(channel);
                channel.CTCPReceived += OnBroadcastCTCP;
                channel.Join();
            }

            BroadcastChannel = channel;
            return channel;
        }

        private void WireChannel(Channel channel)
        {
            channel.MessageAdded += OnChannelMessageAdded;
            channel.UserAdded += OnChannelUserChanged;
            channel.UserLeft += OnChannelUserChanged;
            channel.UserKicked += OnChannelUserChanged;
            channel.UserQuitIRC += OnChannelUserChanged;
            channel.UserListReceived += OnChannelUserChanged;
        }

        private void UnwireChannel(Channel channel)
        {
            channel.MessageAdded -= OnChannelMessageAdded;
            channel.UserAdded -= OnChannelUserChanged;
            channel.UserLeft -= OnChannelUserChanged;
            channel.UserKicked -= OnChannelUserChanged;
            channel.UserQuitIRC -= OnChannelUserChanged;
            channel.UserListReceived -= OnChannelUserChanged;
            channel.CTCPReceived -= OnBroadcastCTCP;
        }

        /// <summary>切换当前聊天频道。</summary>
        public void SwitchChannel(Channel channel)
        {
            if (channel == CurrentChannel || !channel.IsChatChannel)
                return;
            CurrentChannel = channel;
            channel.RequestUserInfo();
            NotifyChanged();
        }

        /// <summary>向当前频道发送聊天消息。</summary>
        public void SendMessage(string text)
        {
            if (CurrentChannel is null || string.IsNullOrWhiteSpace(text))
                return;
            var message = text.Trim();
            if (message.Length == 0)
                return;
            var defaultColor = _manager.GetIRCColors().FirstOrDefault(c => c.Selectable)
                ?? _manager.GetIRCColors().First();
            CurrentChannel.SendChatMessage(message, defaultColor);
        }

        // ── 玩家右键动作（M3.3 spec §5.3.3：私聊/邀请/加友/屏蔽） ──

        /// <summary>目标是否为自己（对自己不出菜单）。</summary>
        public bool IsSelf(string playerName) =>
            string.Equals(playerName, ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase);

        /// <summary>加友 / 取消加友，并同步 <see cref="IRCUser.IsFriend"/>。</summary>
        public void ToggleFriend(ChannelUser user)
        {
            _userData.ToggleFriend(user.IRCUser.Name);
            user.IRCUser.IsFriend = _userData.IsFriend(user.IRCUser.Name);
            NotifyChanged();
        }

        /// <summary>屏蔽 / 取消屏蔽（按 ident，缺失时回退昵称）。</summary>
        public void ToggleIgnore(ChannelUser user)
        {
            var ident = string.IsNullOrEmpty(user.IRCUser.Ident) ? user.IRCUser.Name : user.IRCUser.Ident;
            _userData.ToggleIgnoreUser(ident);
            user.IRCUser.IsIgnored = _userData.IsIgnored(ident);
            NotifyChanged();
        }

        /// <summary>是否可邀请：已加入本机游戏的广播频道（即当前在房间广播中）。</summary>
        public bool CanInvite => BroadcastChannel is not null && LocalGame is not null;

        /// <summary>邀请玩家加入当前游戏：向游戏广播频道发送 CTCP INVITE。</summary>
        public void InvitePlayer(ChannelUser user)
        {
            if (BroadcastChannel is null || LocalGame is null || string.IsNullOrEmpty(LocalGame.GameBroadcastChannel))
                return;
            var ctcp = $"\u0001{ProgramConstants.GAME_INVITE_CTCP_COMMAND} {user.IRCUser.Name}\u0001";
            _manager.SendCustomMessage(new QueuedMessage(
                $"PRIVMSG {LocalGame.GameBroadcastChannel} :{ctcp}", QueuedMessageType.SYSTEM_MESSAGE, 0));
        }

        /// <summary>发送私聊消息（M3.3 快捷单聊；M3.8 扩展多标签私聊中心）。</summary>
        public void SendPrivateMessage(string playerName, string message)
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(message))
                return;
            _manager.SendCustomMessage(new QueuedMessage(
                $"PRIVMSG {playerName} :{message.Trim()}", QueuedMessageType.SYSTEM_MESSAGE, 0));
        }

        private void OnChannelMessageAdded(object? sender, IRCMessageEventArgs e) => NotifyChanged();

        private void OnChannelUserChanged(object? sender, EventArgs e) => NotifyChanged();

        // ── 房间列表（游戏广播协议解析，迁移自原客户端 GameBroadcastChannel_CTCPReceived） ──

        private void OnBroadcastCTCP(object? sender, ChannelCTCPEventArgs e)
        {
            if (!e.Message.StartsWith("GAME "))
                return;

            var split = e.Message.Substring(5).Split(';');
            if (split.Length != 14)
                return;

            try
            {
                if (split[0] != ProgramConstants.CNCNET_PROTOCOL_REVISION)
                    return;

                string gameVersion = split[1];
                int maxPlayers = int.TryParse(split[2], out var mp) ? mp : 0;
                string roomChannel = split[3];
                string roomName = split[4];
                string flags = split[5];
                bool locked = flags.Length > 0 && flags[0] == '1';
                bool customPassword = flags.Length > 1 && flags[1] == '1';
                bool closed = flags.Length > 2 && flags[2] == '1';
                bool isLoaded = flags.Length > 3 && flags[3] == '1';
                bool isLadder = flags.Length > 4 && flags[4] == '1';
                string[] players = split[6].Split(',', StringSplitOptions.RemoveEmptyEntries);
                string mapName = split[7];
                string gameMode = split[8];
                string tunnelField = split[9];
                bool tunneled = tunnelField != "[DYN]";
                // GAME 广播字段：10=游戏内人数 11=观战 12=地图 SHA1 哈希 13=模式ID
                string mapHash = split[12];

                var item = new LobbyGameItem(
                    roomName,
                    e.UserName,
                    mapName,
                    mapHash,
                    gameMode,
                    players,
                    maxPlayers,
                    customPassword,
                    locked || (isLoaded && !players.Contains(ProgramConstants.PLAYERNAME, StringComparer.OrdinalIgnoreCase)),
                    tunneled,
                    isLadder,
                    DateTime.Now);

                if (closed)
                {
                    _gamesByHost.Remove(e.UserName);
                    NotifyChanged();
                    return;
                }

                _gamesByHost[e.UserName] = item;
                NotifyChanged();
            }
            catch (Exception)
            {
                // 单条广播解析失败不影响大厅运行
            }
        }

        private void CleanupStaleGames()
        {
            if (_gamesByHost.Count == 0)
                return;
            var cutoff = DateTime.Now.AddSeconds(-STALE_GAME_SECONDS);
            var stale = _gamesByHost.Where(kv => kv.Value.LastRefreshTime < cutoff).Select(kv => kv.Key).ToList();
            if (stale.Count == 0)
                return;
            foreach (var host in stale)
                _gamesByHost.Remove(host);
            NotifyChanged();
        }

        private void NotifyChanged()
        {
            RefreshGames();
            Changed?.Invoke();
        }

        private void RefreshGames()
        {
            _games.Clear();
            _games.AddRange(_gamesByHost.Values
                .OrderBy(g => g.RoomName, StringComparer.OrdinalIgnoreCase));
        }

        public void Dispose()
        {
            _cleanupTimer?.Dispose();
            _cleanupTimer = null;
            if (_wired)
            {
                _manager.Connected -= OnConnected;
                _manager.Disconnected -= OnDisconnected;
                _manager.ConnectionLost -= OnConnectionLost;
                _wired = false;
            }
        }
    }

    /// <summary>大厅房间列表条目（广播解析产物，UI 只读展示）。</summary>
    public sealed record LobbyGameItem(
        string RoomName,
        string HostName,
        string Map,
        string MapHash,
        string GameMode,
        string[] Players,
        int MaxPlayers,
        bool Passworded,
        bool Locked,
        bool Tunneled,
        bool IsLadder,
        DateTime LastRefreshTime)
    {
        public int PlayerCount => Players.Length;
        public bool IsFull => PlayerCount >= MaxPlayers;
    }
}
