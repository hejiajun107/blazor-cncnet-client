using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Integration.XnaCompat;
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

        /// <summary>私聊会话存储变化（M3.8 多标签私聊中心）：新增/关闭/未读/消息变更时触发。</summary>
        public event Action? PmChanged;

        /// <summary>收到新私聊消息（参数为发送者昵称，M3.8）：UI 用于自动打开/激活会话。</summary>
        public event Action<string>? PmReceived;

        private readonly CnCNetManager _manager;
        private readonly GameCollection _gameCollection;
        private readonly CnCNetUserData _userData;
        private readonly IDispatcher _dispatcher;

        private readonly List<Channel> _channels = new();
        private readonly List<LobbyGameItem> _games = new();
        private readonly Dictionary<string, LobbyGameItem> _gamesByHost = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, PmSession> _pmSessions = new(StringComparer.OrdinalIgnoreCase);
        private Timer? _cleanupTimer;
        private bool _initialized;
        private bool _wired;

        public CnCNetGame? LocalGame { get; }
        public Channel? CurrentChannel { get; private set; }
        public Channel? BroadcastChannel { get; private set; }
        public bool IsConnected => _manager.IsConnected;
        public IReadOnlyList<Channel> Channels => _channels;
        public IReadOnlyList<LobbyGameItem> Games => _games;

        /// <summary>私聊会话列表（M3.8）：按最近活动时间倒序，供多标签栏渲染。</summary>
        public IReadOnlyList<PmSession> PmSessions =>
            _pmSessions.Values.OrderByDescending(s => s.LastActivity).ToList();

        public LobbyService(CnCNetManager manager, GameCollection gameCollection, CnCNetUserData userData, IDispatcher dispatcher)
        {
            _manager = manager;
            _gameCollection = gameCollection;
            _userData = userData;
            _dispatcher = dispatcher;
            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));

            ChannelCommands = new List<ChannelCommand>
            {
                new("KICK", "将玩家踢出频道", RequiresAdmin: true, HandleKick),
                new("BAN", "封禁玩家（按 host）", RequiresAdmin: true, HandleBan),
                new("MUTE", "禁言玩家", RequiresAdmin: true, args => HandleVoice(args, muted: true)),
                new("VOICE", "解除玩家禁言", RequiresAdmin: true, args => HandleVoice(args, muted: false)),
                new("INVITE", "邀请玩家加入当前游戏", RequiresAdmin: false, HandleInvite),
                new("HELP", "显示可用频道指令", RequiresAdmin: false, _ => ShowCommandHelp()),
            };
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
            _pmSessions.Clear();
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
            _manager.PrivateMessageReceived += OnPrivateMessageReceived;
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
            _pmSessions.Clear();
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

        /// <summary>向当前频道发送聊天消息；以 / 开头的输入按频道指令处理（M3.9）。</summary>
        public void SendMessage(string text)
        {
            if (CurrentChannel is null || string.IsNullOrWhiteSpace(text))
                return;
            var message = text.Trim();
            if (message.Length == 0)
                return;

            if (message.StartsWith('/'))
            {
                ExecuteChannelCommand(message);
                return;
            }

            var defaultColor = _manager.GetIRCColors().FirstOrDefault(c => c.Selectable)
                ?? _manager.GetIRCColors().First();
            CurrentChannel.SendChatMessage(message, defaultColor);
        }

        // ── M3.9 频道指令（邀请/踢/禁言，复用指令注册表模式） ──

        /// <summary>可用的频道指令注册表（/ 开头输入触发，见 <see cref="ExecuteChannelCommand"/>）。</summary>
        public IReadOnlyList<ChannelCommand> ChannelCommands { get; }

        /// <summary>当前用户是否为当前频道的管理员（决定指令/菜单权限）。</summary>
        public bool IsCurrentChannelAdmin
        {
            get
            {
                var self = FindChannelUser(ProgramConstants.PLAYERNAME);
                return self is not null && self.IsAdmin;
            }
        }

        /// <summary>解析并执行一条 / 频道指令（未知指令/无权限在频道内给出系统提示）。</summary>
        public void ExecuteChannelCommand(string text)
        {
            if (CurrentChannel is null)
                return;

            var trimmed = text.Trim().Substring(1);
            int spaceIndex = trimmed.IndexOf(' ');
            string commandName = spaceIndex == -1 ? trimmed : trimmed.Substring(0, spaceIndex);
            string args = spaceIndex == -1 ? string.Empty : trimmed.Substring(spaceIndex + 1).Trim();

            var command = ChannelCommands.FirstOrDefault(c =>
                string.Equals(c.Command, commandName, StringComparison.OrdinalIgnoreCase));
            if (command is null)
            {
                AddChannelMessage($"未知指令 /{commandName}，输入 /help 查看可用指令");
                return;
            }

            if (command.RequiresAdmin && !IsCurrentChannelAdmin)
            {
                AddChannelMessage($"/{command.Command} 需要频道管理员权限");
                return;
            }

            command.Handler(args);
        }

        /// <summary>按昵称在当前频道查找玩家（大小写不敏感）。</summary>
        public ChannelUser? FindChannelUser(string playerName)
        {
            if (CurrentChannel is null || string.IsNullOrEmpty(playerName))
                return null;
            return CurrentChannel.Users.Find(playerName);
        }

        private void HandleKick(string args)
        {
            var name = FirstArg(args);
            if (name is null)
            {
                AddChannelMessage("用法：/kick <玩家昵称>");
                return;
            }
            if (IsSelf(name))
            {
                AddChannelMessage("不能对自己执行 /kick");
                return;
            }
            if (FindChannelUser(name) is null)
            {
                AddChannelMessage($"未在当前频道找到玩家 {name}");
                return;
            }
            CurrentChannel!.SendKickMessage(name, 8);
            AddChannelMessage($"已将 {name} 踢出频道");
        }

        private void HandleBan(string args)
        {
            var name = FirstArg(args);
            if (name is null)
            {
                AddChannelMessage("用法：/ban <玩家昵称>");
                return;
            }
            if (IsSelf(name))
            {
                AddChannelMessage("不能对自己执行 /ban");
                return;
            }
            var user = FindChannelUser(name);
            if (user is null)
            {
                AddChannelMessage($"未在当前频道找到玩家 {name}");
                return;
            }
            // 封禁按 host（缺失时回退昵称），并连带踢出
            var host = string.IsNullOrEmpty(user.IRCUser.Hostname) ? name : user.IRCUser.Hostname;
            CurrentChannel!.SendBanMessage(host, 8);
            CurrentChannel.SendKickMessage(name, 8);
            AddChannelMessage($"已封禁 {name}");
        }

        private void HandleVoice(string args, bool muted)
        {
            var name = FirstArg(args);
            if (name is null)
            {
                AddChannelMessage($"用法：/{(muted ? "mute" : "voice")} <玩家昵称>");
                return;
            }
            if (IsSelf(name))
            {
                AddChannelMessage($"不能对自己执行 /{(muted ? "mute" : "voice")}");
                return;
            }
            if (FindChannelUser(name) is null)
            {
                AddChannelMessage($"未在当前频道找到玩家 {name}");
                return;
            }
            _manager.SendCustomMessage(new QueuedMessage(
                $"MODE {CurrentChannel!.ChannelName} {(muted ? "-" : "+")}v {name}",
                QueuedMessageType.INSTANT_MESSAGE, 8));
            AddChannelMessage(muted ? $"已禁言 {name}" : $"已解除 {name} 的禁言");
        }

        private void HandleInvite(string args)
        {
            var name = FirstArg(args);
            if (name is null)
            {
                AddChannelMessage("用法：/invite <玩家昵称>");
                return;
            }
            if (!CanInvite)
            {
                AddChannelMessage("当前不在游戏广播频道，无法邀请");
                return;
            }
            var user = FindChannelUser(name);
            if (user is null)
            {
                AddChannelMessage($"未在当前频道找到玩家 {name}");
                return;
            }
            InvitePlayer(user);
            AddChannelMessage($"已邀请 {name} 加入游戏");
        }

        private void ShowCommandHelp()
        {
            var help = string.Join("  ", ChannelCommands.Select(c => $"/{c.Command}"));
            AddChannelMessage($"可用频道指令：{help}");
        }

        private static string? FirstArg(string args)
        {
            var parts = args.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length == 0 ? null : parts[0];
        }

        /// <summary>向当前频道追加本地系统消息（指令反馈，不发送到服务器）。</summary>
        private void AddChannelMessage(string message)
        {
            CurrentChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow, message));
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

        /// <summary>发送私聊消息（M3.3 快捷单聊；M3.8 扩展为多标签会话存储，本方消息即时入列）。</summary>
        public void SendPrivateMessage(string playerName, string message)
        {
            if (string.IsNullOrWhiteSpace(playerName) || string.IsNullOrWhiteSpace(message))
                return;
            var text = message.Trim();
            if (text.Length == 0)
                return;
            _manager.SendCustomMessage(new QueuedMessage(
                $"PRIVMSG {playerName} :{text}", QueuedMessageType.SYSTEM_MESSAGE, 0));

            var session = GetOrCreateSession(playerName);
            session.Messages.Add(new PmMessage(ProgramConstants.PLAYERNAME, text, DateTime.Now, IsMine: true));
            session.LastActivity = DateTime.Now;
            session.UnreadCount = 0;
            PmChanged?.Invoke();
        }

        // ── M3.8 私聊多标签：会话存储与接收 ──

        /// <summary>确保会话存在（右键「私聊」时先建档）。</summary>
        public void OpenPmSession(string playerName)
        {
            if (string.IsNullOrWhiteSpace(playerName))
                return;
            GetOrCreateSession(playerName);
            PmChanged?.Invoke();
        }

        /// <summary>查找会话（UI 按激活标签取消息列表）。</summary>
        public PmSession? FindPmSession(string playerName) =>
            !string.IsNullOrWhiteSpace(playerName) && _pmSessions.TryGetValue(playerName, out var session)
                ? session
                : null;

        /// <summary>标记会话已读（激活标签 / 展示会话时清零未读）。</summary>
        public void MarkPmRead(string playerName)
        {
            if (_pmSessions.TryGetValue(playerName, out var session) && session.UnreadCount != 0)
            {
                session.UnreadCount = 0;
                PmChanged?.Invoke();
            }
        }

        /// <summary>关闭（移除）会话标签。</summary>
        public void ClosePmSession(string playerName)
        {
            if (_pmSessions.Remove(playerName))
                PmChanged?.Invoke();
        }

        private PmSession GetOrCreateSession(string playerName)
        {
            if (!_pmSessions.TryGetValue(playerName, out var session))
            {
                session = new PmSession(playerName);
                _pmSessions[playerName] = session;
            }
            return session;
        }

        private void OnPrivateMessageReceived(object? sender, CnCNetPrivateMessageEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Sender) || string.IsNullOrEmpty(e.Message))
                return;
            if (IsSelf(e.Sender))
                return;

            // 屏蔽判定按 ident（缺失回退昵称），与右键屏蔽逻辑一致
            var ircUser = _manager.UserList.Find(u =>
                string.Equals(u.Name, e.Sender, StringComparison.OrdinalIgnoreCase));
            var ident = ircUser is not null && !string.IsNullOrEmpty(ircUser.Ident)
                ? ircUser.Ident
                : e.Sender;
            if (_userData.IsIgnored(ident))
                return;

            var session = GetOrCreateSession(e.Sender);
            session.Messages.Add(new PmMessage(e.Sender, e.Message, DateTime.Now, IsMine: false));
            session.LastActivity = DateTime.Now;
            session.UnreadCount++;
            PmReceived?.Invoke(session.UserName);
            PmChanged?.Invoke();
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
                    roomChannel,
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
                _manager.PrivateMessageReceived -= OnPrivateMessageReceived;
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
        string ChannelName,
        DateTime LastRefreshTime)
    {
        public int PlayerCount => Players.Length;
        public bool IsFull => PlayerCount >= MaxPlayers;
    }

    /// <summary>私聊会话（M3.8 多标签）：一个会话对应一个玩家，含消息历史与未读数。</summary>
    public sealed class PmSession
    {
        public string UserName { get; }
        public List<PmMessage> Messages { get; } = new();
        public int UnreadCount { get; set; }
        public DateTime LastActivity { get; set; } = DateTime.Now;

        public PmSession(string userName) => UserName = userName;
    }

    /// <summary>单条私聊消息（M3.8）。<see cref="IsMine"/> 为 true 表示本方发送。</summary>
    public sealed record PmMessage(string SenderName, string Text, DateTime Time, bool IsMine);

    /// <summary>
    /// 频道指令（M3.9，复用原客户端 ChatBoxCommand 注册表模式）。
    /// <see cref="RequiresAdmin"/> 为 true 时仅频道管理员（@ 前缀）可执行。
    /// </summary>
    public sealed record ChannelCommand(string Command, string Description, bool RequiresAdmin, Action<string> Handler);
}
