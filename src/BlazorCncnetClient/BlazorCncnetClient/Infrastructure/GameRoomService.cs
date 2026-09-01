using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Integration.XnaCompat;
using CnCNetClient.Online;
using CnCNetClient.Online.EventArguments;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 游戏房间状态服务（M3.5 spec §5.3）：建房间 / 加入房间 / 离开、房间配置同步
    /// （地图 / 规则 / 队伍 / 颜色 / 出生点）、Ready / 开始。
    /// 协议迁移自原客户端 CnCNetLobby.cs（RandomizeChannelName / _JoinGame）与
    /// CnCNetGameLobby.cs（BroadcastGame / GSETTINGS / GO / PO / OR / R / START）。
    /// </summary>
    public sealed class GameRoomService : IDisposable
    {
        private const int INITIAL_GAME_BROADCAST_DELAY_MS = 600;
        private const int GAME_BROADCAST_INTERVAL_MS = 30000; // 原版 30s 一次

        /// <summary>房间状态变化（玩家/地图/设置/就绪）。</summary>
        public event Action? Changed;

        /// <summary>开始游戏请求（M3.7：GameLaunchService 接此生成配置并启动游戏进程）。</summary>
        public event Action? GameStartRequested;

        /// <summary>本局唯一 GameID（START 广播第一段，写入 spawn.ini [Settings] GameID）。</summary>
        public int UniqueGameId { get; private set; }

        /// <summary>本局隧道服务器地址/端口（M3.11：房主在 StartGame 选定；加入者从 START 解析），写 spawn.ini [Tunnel] 节。</summary>
        public string TunnelAddress { get; private set; } = string.Empty;
        public int TunnelPort { get; private set; }

        private readonly CnCNetManager _manager;
        private readonly GameCollection _gameCollection;
        private readonly MapLoader _mapLoader;
        private readonly IDispatcher _dispatcher;
        private readonly TunnelHandler _tunnelHandler;
        private readonly Random _random = new();

        private Channel? _gameChannel;
        private Timer? _broadcastTimer;
        private bool _wired;
        private string _password = string.Empty;

        private readonly Dictionary<string, RoomPlayerState> _players = new(StringComparer.OrdinalIgnoreCase);

        public CnCNetGame? LocalGame { get; }

        /// <summary>当前是否处于房间（已建 / 已加入频道）。</summary>
        public bool IsInGameRoom => _gameChannel is not null;

        /// <summary>当前是否房主。</summary>
        public bool IsHost { get; private set; }

        /// <summary>自己是否已进入房间频道（JOIN 成功后）。</summary>
        public bool IsJoined { get; private set; }

        public string RoomName { get; private set; } = string.Empty;
        public int MaxPlayers { get; private set; } = 8;
        public bool IsCustomPassword { get; private set; }
        public bool Locked { get; private set; }
        public int SkillLevel { get; private set; }
        public GameModeMap? SelectedMap { get; private set; }
        public string HostName { get; private set; } = string.Empty;
        public Channel? GameChannel => _gameChannel;
        public IReadOnlyCollection<RoomPlayerState> Players => _players.Values;

        public GameRoomService(
            CnCNetManager manager,
            GameCollection gameCollection,
            MapLoader mapLoader,
            IDispatcher dispatcher,
            TunnelHandler tunnelHandler)
        {
            _manager = manager;
            _gameCollection = gameCollection;
            _mapLoader = mapLoader;
            _dispatcher = dispatcher;
            _tunnelHandler = tunnelHandler;
            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));

            // 隧道列表就绪后：房主在房间内且未选定隧道 → 自动选优并刷新 GAME 广播（房间列表显示真实隧道）
            _tunnelHandler.TunnelsRefreshed += OnTunnelsRefreshed;
        }

        // ── 建房间 / 加入 / 离开 ──

        /// <summary>创建房间并加入（迁移自原版 Gcw_GameCreated）。</summary>
        public void CreateGame(string roomName, int maxPlayers, string password = "")
        {
            if (IsInGameRoom)
                throw new InvalidOperationException("已在游戏房间中，请先离开当前房间");

            string channelName = RandomizeChannelName();

            bool isCustomPassword = !string.IsNullOrEmpty(password);
            if (!isCustomPassword)
                password = Utilities.CalculateSHA1ForString(channelName).Substring(0, 10);

            var channel = _manager.CreateChannel(roomName, channelName, persistent: false, isChatChannel: true, password);
            _manager.AddChannel(channel);

            IsHost = true;
            IsJoined = false;
            IsCustomPassword = isCustomPassword;
            RoomName = roomName;
            MaxPlayers = Math.Clamp(maxPlayers, 2, 8);
            HostName = ProgramConstants.PLAYERNAME;
            _password = password;
            SkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(0);
            Locked = false;
            SelectedMap = null;
            _players.Clear();
            TunnelAddress = string.Empty;
            TunnelPort = 0;

            _gameChannel = channel;
            WireChannel(channel);
            channel.Join();

            // M3.11：隧道列表已就绪则建房时立即选优并广播（房间列表显示真实隧道）；
            // 列表未就绪时由 OnTunnelsRefreshed 在下次刷新兜底（此前仍为 [DYN]）。
            EnsureTunnelSelected();
            BroadcastGame();

            _manager.MainChannel.AddMessage(new ChatMessage(ColorConstants.Cyan,
                $"正在创建房间「{roomName}」..."));
            NotifyChanged();
        }

        /// <summary>加入房间（迁移自原版 JoinGame / _JoinGame）。</summary>
        public void JoinGame(LobbyGameItem game, string? password)
        {
            if (IsInGameRoom)
                throw new InvalidOperationException("已在游戏房间中，请先离开当前房间");

            if (string.IsNullOrEmpty(game.ChannelName))
                throw new InvalidOperationException("该房间缺少频道信息，无法加入");

            if (game.IsFull)
                throw new InvalidOperationException("该房间已满员，无法加入");

            string joinPassword;
            if (game.Passworded)
            {
                if (string.IsNullOrEmpty(password))
                    throw new InvalidOperationException("该房间需要密码才能加入");
                joinPassword = password;
            }
            else
            {
                // 无自定义密码的房间：密码 = SHA1(频道名) 前 10 位
                joinPassword = Utilities.CalculateSHA1ForString(game.ChannelName).Substring(0, 10);
            }

            var channel = _manager.CreateChannel(game.RoomName, game.ChannelName, persistent: false, isChatChannel: true, joinPassword);
            _manager.AddChannel(channel);

            IsHost = false;
            IsJoined = false;
            IsCustomPassword = game.Passworded;
            RoomName = game.RoomName;
            MaxPlayers = Math.Max(2, game.MaxPlayers);
            HostName = game.HostName;
            _password = joinPassword;
            SkillLevel = 0;
            Locked = game.Locked;
            SelectedMap = null;
            _players.Clear();
            TunnelAddress = string.Empty;
            TunnelPort = 0;

            _gameChannel = channel;
            WireChannel(channel);
            channel.Join();

            _manager.MainChannel.AddMessage(new ChatMessage(ColorConstants.Cyan,
                $"正在加入房间「{game.RoomName}」..."));
            NotifyChanged();
        }

        /// <summary>离开房间：停止广播、PART 频道并清理状态。</summary>
        public void LeaveGame()
        {
            if (_gameChannel is null)
                return;
            StopBroadcastTimer();
            _manager.LeaveFromChannel(_gameChannel);
            UnwireChannel(_gameChannel);
            _gameChannel = null;
            IsHost = false;
            IsJoined = false;
            RoomName = string.Empty;
            MaxPlayers = 8;
            IsCustomPassword = false;
            Locked = false;
            SkillLevel = 0;
            SelectedMap = null;
            HostName = string.Empty;
            _password = string.Empty;
            _players.Clear();
            TunnelAddress = string.Empty;
            TunnelPort = 0;
            NotifyChanged();
        }

        /// <summary>生成随机、未占用的频道名（迁移自原版 RandomizeChannelName）。</summary>
        private string RandomizeChannelName()
        {
            if (LocalGame is null || string.IsNullOrEmpty(LocalGame.InternalName))
                throw new InvalidOperationException("未选择本地游戏，无法创建房间");

            const int maxTries = 10000;
            for (int i = 0; i < maxTries; i++)
            {
                string channelName = _gameCollection.GetGameChatChannelNameFromIdentifier(LocalGame.InternalName)
                    + "-game" + _random.Next(1000000, 9999999);
                if (_manager.FindChannel(channelName) is null)
                    return channelName;
            }
            throw new Exception($"尝试 {maxTries} 次仍未找到可用频道名");
        }

        // ── 房间内交互 ──

        /// <summary>向房间频道发送聊天消息。</summary>
        public void SendRoomChat(string text)
        {
            if (_gameChannel is null || string.IsNullOrWhiteSpace(text))
                return;
            var message = text.Trim();
            if (message.Length == 0)
                return;
            var defaultColor = _manager.GetIRCColors().FirstOrDefault(c => c.Selectable)
                ?? _manager.GetIRCColors().First();
            _gameChannel.SendChatMessage(message, defaultColor);
        }

        /// <summary>切换自己的就绪状态（迁移自原版 R 消息）。</summary>
        public void ToggleReady()
        {
            if (_gameChannel is null || !IsJoined)
                return;
            if (!_players.TryGetValue(ProgramConstants.PLAYERNAME, out var self))
                return;
            self.Ready = !self.Ready;
            _gameChannel.SendCTCPMessage($"R {Convert.ToInt32(self.Ready)}",
                QueuedMessageType.GAME_PLAYERS_READY_STATUS_MESSAGE, 5);
            if (IsHost)
                BroadcastPlayerOptions(); // 房主直接广播，其他玩家经 HandleReadyRequest 广播
            NotifyChanged();
        }

        /// <summary>修改自己的队伍/颜色/出生点/阵营（迁移自原版 RequestPlayerOptions / PO）。</summary>
        public void ChangeOwnOptions(int side, int color, int start, int team)
        {
            if (_gameChannel is null || !IsJoined)
                return;
            if (!_players.TryGetValue(ProgramConstants.PLAYERNAME, out var self))
                return;

            self.Side = side;
            self.Color = color;
            self.Start = start;
            self.Team = team;

            if (IsHost)
            {
                BroadcastPlayerOptions();
            }
            else
            {
                // 加入者把期望选项发给房主，房主校验后统一下发 PO
                byte[] packed = { (byte)side, (byte)color, (byte)start, (byte)team };
                int value = BinaryPrimitives.ReadInt32LittleEndian(packed);
                _gameChannel.SendCTCPMessage($"OR {value}", QueuedMessageType.SYSTEM_MESSAGE, 10);
            }
            NotifyChanged();
        }

        /// <summary>房主切换地图（GameModeMap 内已含模式），并广播 GO / GAME。</summary>
        public void ChangeMap(GameModeMap gameModeMap)
        {
            if (!IsHost || _gameChannel is null)
                return;
            if (SelectedMap == gameModeMap)
                return;
            SelectedMap = gameModeMap;
            BroadcastGameOptions();
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>房主修改房间设置（房间名 / 最大人数 / 技能等级），广播 GSETTINGS + GAME。</summary>
        public void ChangeRoomSettings(string roomName, int maxPlayers, int skillLevel)
        {
            if (!IsHost || _gameChannel is null)
                return;
            RoomName = string.IsNullOrWhiteSpace(roomName) ? RoomName : roomName.Trim();
            MaxPlayers = Math.Clamp(maxPlayers, 2, 8);
            SkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(skillLevel);
            BroadcastGameLobbySettings();
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>房主锁定/解锁房间（MODE +i / -i，锁定后他人无法加入）。</summary>
        public void ToggleLock()
        {
            if (!IsHost || _gameChannel is null)
                return;
            Locked = !Locked;
            _manager.SendCustomMessage(new QueuedMessage(
                $"MODE {_gameChannel.ChannelName} {(Locked ? "+i" : "-i")}",
                QueuedMessageType.INSTANT_MESSAGE, 0));
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>
        /// 房主开始游戏（M3.11：多人局先选优隧道并向隧道服务器请求端口，再广播 START；
        /// GameLaunchService 接 GameStartRequested 启动游戏）。单人局无需隧道。
        /// </summary>
        public void StartGame()
        {
            if (!IsHost || _gameChannel is null || !IsJoined)
                return;
            if (SelectedMap is null)
                throw new InvalidOperationException("请先选择地图");
            if (Locked && _players.Values.Count(p => !p.Ready) > 0)
                throw new InvalidOperationException("仍有玩家未就绪");

            int playerCount = _players.Count;

            // 多人对局：选定隧道服务器并请求各玩家端口（迁移自原版 HostLaunchGame / HostStartGame）。
            if (playerCount > 1)
            {
                EnsureTunnelSelected();
                var tunnel = _tunnelHandler.CurrentTunnel;
                if (tunnel is null)
                    throw new InvalidOperationException("无法选择隧道服务器（列表尚未加载），请稍后重试");

                List<int> playerPorts = tunnel.GetPlayerPortInfo(playerCount);
                if (playerPorts.Count < playerCount)
                    throw new InvalidOperationException(
                        $"隧道服务器「{tunnel.Name}」端口不足（需要 {playerCount} 个，仅分配 {playerPorts.Count} 个），请稍后重试");

                int index = 0;
                foreach (var p in _players.Values)
                {
                    p.Port = playerPorts[index];
                    index++;
                }

                TunnelAddress = tunnel.Address;
                TunnelPort = tunnel.Port;
            }

            // 原版 START 消息：START {UniqueGameID};{Name};{address}:{port};...
            // 多人时 address 为隧道服务器地址，单人时保持 0.0.0.0。
            UniqueGameId = _random.Next();
            string address = playerCount > 1 ? TunnelAddress : "0.0.0.0";
            StringBuilder sb = new StringBuilder("START ").Append(UniqueGameId);
            foreach (var p in _players.Values)
            {
                sb.Append(';').Append(p.Name).Append(';').Append(address).Append(':').Append(p.Port);
            }
            _gameChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 10);

            GameStartRequested?.Invoke();
        }

        /// <summary>
        /// 选择最优隧道服务器（迁移自原版 TunnelListBox 评分：仅 Official/Recommended 且 ping 有效，
        /// rating = ping² × 负载率取最低；无候选时退化为列表首个）。已选定（CurrentTunnel 非空）则不覆盖。
        /// </summary>
        private void EnsureTunnelSelected()
        {
            if (_tunnelHandler.CurrentTunnel is not null || _tunnelHandler.Tunnels.Count == 0)
                return;

            CnCNetTunnel? best = null;
            long lowestRating = long.MaxValue;
            foreach (var tunnel in _tunnelHandler.Tunnels)
            {
                if (!(tunnel.Official || tunnel.Recommended) || tunnel.PingInMs <= -1)
                    continue;
                double usageRatio = (double)tunnel.Clients / Math.Max(1, tunnel.MaxClients);
                if (usageRatio == 0)
                    usageRatio = 0.1;
                usageRatio *= 100.0;
                long rating = (long)(Math.Pow(tunnel.PingInMs, 2.0) * usageRatio);
                if (rating < lowestRating)
                {
                    lowestRating = rating;
                    best = tunnel;
                }
            }
            _tunnelHandler.CurrentTunnel = best ?? _tunnelHandler.Tunnels[0];
        }

        /// <summary>隧道列表刷新后：房主在房间内且未选定隧道 → 自动选优并刷新 GAME 广播（房间列表显示真实隧道）。</summary>
        private void OnTunnelsRefreshed(object? sender, EventArgs e)
        {
            if (IsHost && IsJoined && _tunnelHandler.CurrentTunnel is null)
            {
                EnsureTunnelSelected();
                BroadcastGame();
                NotifyChanged();
            }
        }

        // ── 房间协议：主机 → 大厅（GAME 广播） ──

        private void BroadcastGame()
        {
            if (!IsHost || _gameChannel is null || LocalGame is null || string.IsNullOrEmpty(LocalGame.InternalName))
                return;

            Channel broadcastChannel = _manager.FindChannel(
                _gameCollection.GetGameBroadcastingChannelNameFromIdentifier(LocalGame.InternalName));
            if (broadcastChannel is null)
                return;

            StringBuilder sb = new StringBuilder("GAME ");
            sb.Append(ProgramConstants.CNCNET_PROTOCOL_REVISION);
            sb.Append(';').Append(ProgramConstants.GAME_VERSION);
            sb.Append(';').Append(MaxPlayers);
            sb.Append(';').Append(_gameChannel.ChannelName);
            sb.Append(';').Append(RoomName);
            sb.Append(';').Append(Convert.ToInt32(Locked))
              .Append(Convert.ToInt32(IsCustomPassword))
              .Append('0') // closed
              .Append('0') // IsLoadedGame
              .Append('0'); // IsLadder
            sb.Append(';');
            var names = _players.Values.Select(p => p.Name).ToList();
            if (names.Count > 0)
                sb.Append(string.Join(",", names));
            sb.Append(';').Append(SelectedMap?.Map.UntranslatedName ?? string.Empty);
            sb.Append(';').Append(SelectedMap?.GameMode.UntranslatedUIName ?? string.Empty);
            // 隧道：已选定则广播真实 address:port（房间列表识别为隧道房），否则动态 [DYN]
            sb.Append(';')
              .Append(_tunnelHandler.CurrentTunnel is not null
                  ? $"{_tunnelHandler.CurrentTunnel.Address}:{_tunnelHandler.CurrentTunnel.Port}"
                  : "[DYN]");
            sb.Append(";0"); // LoadedGameId
            sb.Append(';').Append(SkillLevel);
            sb.Append(';').Append(SelectedMap?.Map.SHA1 ?? string.Empty);
            sb.Append(";0"); // gameOptionValues（M3.6 接选项打包）

            broadcastChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.SYSTEM_MESSAGE, 20);
        }

        // ── 房间协议：主机 → 房间（GSETTINGS / GO / PO） ──

        private void BroadcastGameLobbySettings()
        {
            if (!IsHost || _gameChannel is null)
                return;
            StringBuilder sb = new StringBuilder("GSETTINGS ");
            sb.Append(RoomName).Append(';')
              .Append(MaxPlayers).Append(';')
              .Append(SkillLevel).Append(';')
              .Append(Convert.ToInt32(IsCustomPassword));
            _gameChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
        }

        /// <summary>广播 GO 消息：地图/模式/种子等房间配置（迁移自原版 BroadcastGameOptions 精简版）。</summary>
        private void BroadcastGameOptions()
        {
            if (!IsHost || _gameChannel is null)
                return;
            StringBuilder sb = new StringBuilder("GO ");
            sb.Append('0'); // 打包的复选框选项（M3.5 无游戏选项）
            sb.Append(';').Append(Convert.ToInt32(SelectedMap?.Map.Official ?? false));
            sb.Append(';').Append(SelectedMap?.Map.SHA1 ?? string.Empty);
            sb.Append(';').Append(SelectedMap?.GameMode.Name ?? string.Empty);
            sb.Append(";0;0;0"); // FrameSendRate / MaxAhead / ProtocolVersion
            sb.Append(';').Append(_random.Next()); // RandomSeed
            sb.Append(";0"); // RemoveStartingLocations
            sb.Append(';').Append(SelectedMap?.Map.UntranslatedName ?? string.Empty);
            _gameChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
        }

        /// <summary>广播 PO 消息：全部玩家的队伍/出生点/颜色/阵营/就绪（迁移自原版 BroadcastPlayerOptions）。</summary>
        private void BroadcastPlayerOptions()
        {
            if (!IsHost || _gameChannel is null)
                return;
            StringBuilder sb = new StringBuilder("PO ");
            foreach (var p in _players.Values)
            {
                sb.Append(p.Name).Append(';');
                byte[] packed = { (byte)p.Team, (byte)p.Start, (byte)p.Color, (byte)p.Side };
                int value = BinaryPrimitives.ReadInt32LittleEndian(packed);
                sb.Append(value).Append(';');
                sb.Append(Convert.ToInt32(p.Ready)).Append(';');
            }
            _gameChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.GAME_PLAYERS_MESSAGE, 11);
        }

        // ── 频道事件 / 协议处理 ──

        private void WireChannel(Channel channel)
        {
            if (_wired)
                return;
            _wired = true;
            channel.UserAdded += OnChannelUserAdded;
            channel.UserLeft += OnChannelUserLeft;
            channel.UserKicked += OnChannelUserLeft;
            channel.UserQuitIRC += OnChannelUserLeft;
            channel.UserListReceived += OnChannelUserListReceived;
            channel.MessageAdded += OnChannelMessageAdded;
            channel.CTCPReceived += OnChannelCTCPReceived;
            channel.InvalidPasswordEntered += OnInvalidPasswordEntered;
            channel.InviteOnlyErrorOnJoin += OnInviteOnlyError;
            channel.ChannelFull += OnChannelFull;
            channel.TargetChangeTooFast += OnTargetChangeTooFast;
        }

        private void UnwireChannel(Channel channel)
        {
            if (!_wired)
                return;
            _wired = false;
            channel.UserAdded -= OnChannelUserAdded;
            channel.UserLeft -= OnChannelUserLeft;
            channel.UserKicked -= OnChannelUserLeft;
            channel.UserQuitIRC -= OnChannelUserLeft;
            channel.UserListReceived -= OnChannelUserListReceived;
            channel.MessageAdded -= OnChannelMessageAdded;
            channel.CTCPReceived -= OnChannelCTCPReceived;
            channel.InvalidPasswordEntered -= OnInvalidPasswordEntered;
            channel.InviteOnlyErrorOnJoin -= OnInviteOnlyError;
            channel.ChannelFull -= OnChannelFull;
            channel.TargetChangeTooFast -= OnTargetChangeTooFast;
        }

        private void OnChannelUserListReceived(object? sender, EventArgs e)
        {
            if (_gameChannel is null)
                return;
            _gameChannel.Users.DoForAllUsers(u =>
            {
                if (!_players.ContainsKey(u.IRCUser.Name))
                    _players[u.IRCUser.Name] = new RoomPlayerState { Name = u.IRCUser.Name };
            });
            NotifyChanged();
        }

        private void OnChannelUserAdded(object? sender, ChannelUserEventArgs e)
        {
            var name = e.User.IRCUser.Name;
            if (name == ProgramConstants.PLAYERNAME)
            {
                if (IsJoined)
                    return;
                IsJoined = true;
                _players[name] = new RoomPlayerState { Name = name, IsHost = IsHost };
                if (IsHost)
                {
                    // 房主默认就绪；设置频道模式 +klnNs 与协议版本 TOPIC，启动周期广播
                    _players[name].Ready = true;
                    _manager.SendCustomMessage(new QueuedMessage(
                        $"MODE {_gameChannel!.ChannelName} +klnNs {_password} {MaxPlayers}",
                        QueuedMessageType.SYSTEM_MESSAGE, 50));
                    _manager.SendCustomMessage(new QueuedMessage(
                        $"TOPIC {_gameChannel.ChannelName} :{ProgramConstants.CNCNET_PROTOCOL_REVISION};{LocalGame?.InternalName?.ToLower() ?? string.Empty}",
                        QueuedMessageType.SYSTEM_MESSAGE, 50));
                    StartBroadcastTimer();
                    BroadcastGame();
                }
                else
                {
                    // 加入者：请求房主下发当前地图/设置/玩家配置
                    _gameChannel!.SendCTCPMessage("OR 0", QueuedMessageType.SYSTEM_MESSAGE, 10);
                }
            }
            else
            {
                if (!_players.ContainsKey(name))
                    _players[name] = new RoomPlayerState { Name = name };
                if (IsHost && IsJoined)
                {
                    // 房主：新玩家加入 → 下发当前地图/设置/全部玩家配置
                    BroadcastGameOptions();
                    BroadcastGameLobbySettings();
                    BroadcastPlayerOptions();
                    BroadcastGame();
                }
            }
            NotifyChanged();
        }

        private void OnChannelUserLeft(object? sender, UserNameEventArgs e)
        {
            _players.Remove(e.UserName);
            if (IsHost && IsJoined && Locked)
            {
                // 有人离开后自动解锁（原版 RemovePlayer 语义）
                Locked = false;
                BroadcastGame();
            }
            NotifyChanged();
        }

        private void OnChannelMessageAdded(object? sender, IRCMessageEventArgs e) => NotifyChanged();

        private void OnInvalidPasswordEntered(object? sender, EventArgs e)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red, "密码错误，无法加入该房间"));
            LeaveGame();
            NotifyChanged();
        }

        private void OnInviteOnlyError(object? sender, EventArgs e)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red, "房间已锁定，无法加入"));
            LeaveGame();
            NotifyChanged();
        }

        private void OnChannelFull(object? sender, EventArgs e)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red, "房间已满员，无法加入"));
            LeaveGame();
            NotifyChanged();
        }

        private void OnTargetChangeTooFast(object? sender, MessageEventArgs e)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow, e.Message));
            LeaveGame();
            NotifyChanged();
        }

        private void OnChannelCTCPReceived(object? sender, ChannelCTCPEventArgs e)
        {
            if (string.IsNullOrEmpty(e.Message))
                return;
            var message = e.Message;
            var spaceIndex = message.IndexOf(' ');
            string command = spaceIndex > 0 ? message.Substring(0, spaceIndex) : message;
            string args = spaceIndex > 0 ? message.Substring(spaceIndex + 1) : string.Empty;

            switch (command)
            {
                case "R": HandleReadyMessage(e.UserName, args); break;
                case "OR": HandleOptionsRequest(e.UserName, args); break;
                case "PO": HandlePlayerOptions(e.UserName, args); break;
                case "GO": HandleGameOptions(e.UserName, args); break;
                case "GSETTINGS": HandleGameLobbySettings(e.UserName, args); break;
                case "START": HandleStartMessage(e.UserName, args); break;
                case "STRTD": _gameChannel?.AddMessage(new ChatMessage("游戏已开始")); break;
                case "FHSH": break; // 文件哈希校验：M3.6/M3.7 接入 FileHashCalculator
                case "TNLPNG": break; // 隧道延迟：M3.5 未接隧道
                default: break;
            }
        }

        private void HandleReadyMessage(string sender, string args)
        {
            if (!IsHost || !int.TryParse(args, out int ready))
                return;
            if (_players.TryGetValue(sender, out var p))
            {
                p.Ready = ready > 0;
                BroadcastPlayerOptions();
                NotifyChanged();
            }
        }

        private void HandleOptionsRequest(string sender, string args)
        {
            if (!IsHost || !int.TryParse(args, out int options))
                return;
            if (args == "0")
            {
                // 加入者请求当前配置：直接下发
                BroadcastGameOptions();
                BroadcastGameLobbySettings();
                BroadcastPlayerOptions();
                return;
            }

            if (!_players.TryGetValue(sender, out var p))
                return;
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, options);
            p.Side = bytes[0];
            p.Color = bytes[1];
            p.Start = bytes[2];
            p.Team = bytes[3];
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        private void HandlePlayerOptions(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            _players.Clear();
            string[] parts = args.Split(';', StringSplitOptions.RemoveEmptyEntries);
            Span<byte> bytes = stackalloc byte[4];
            for (int i = 0; i < parts.Length;)
            {
                if (parts.Length <= i + 2)
                    break;
                string name = parts[i];
                if (!int.TryParse(parts[i + 1], out int options))
                {
                    i += 3;
                    continue;
                }
                int ready = int.TryParse(parts[i + 2], out var r) ? r : 0;

                BinaryPrimitives.WriteInt32LittleEndian(bytes, options);
                var state = new RoomPlayerState
                {
                    Name = name,
                    IsHost = string.Equals(name, HostName, StringComparison.OrdinalIgnoreCase),
                    Team = bytes[0],
                    Start = bytes[1],
                    Color = bytes[2],
                    Side = bytes[3],
                    Ready = ready > 0
                };
                _players[name] = state;
                i += 3;
            }

            // 保证自己始终在列表里
            if (!_players.ContainsKey(ProgramConstants.PLAYERNAME))
            {
                _players[ProgramConstants.PLAYERNAME] = new RoomPlayerState { Name = ProgramConstants.PLAYERNAME };
                ChangeOwnOptions(0, 0, 0, 0);
            }
            NotifyChanged();
        }

        private void HandleGameOptions(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            // GO 格式（与 BroadcastGameOptions 对称）：
            // 0(打包复选框);official;sha1;mode;fps;ahead;proto;seed;removeStart;mapName
            string[] parts = args.Split(';');
            if (parts.Length < 9)
                return;
            string mapHash = parts[2];
            string gameMode = parts[3];
            if (int.TryParse(parts[7], out int seed))
                RandomSeed = seed;

            var gmm = _mapLoader.GameModeMaps.FirstOrDefault(g =>
                string.Equals(g.GameMode.Name, gameMode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.Map.SHA1, mapHash, StringComparison.OrdinalIgnoreCase));
            SelectedMap = gmm;
            if (gmm is null && !string.IsNullOrEmpty(mapHash))
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow,
                    "主机选择了本机未安装的地图，地图名： " + (parts.Length > 8 ? parts[8] : mapHash)));
            }
            NotifyChanged();
        }

        private void HandleGameLobbySettings(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;
            string[] parts = args.Split(';');
            if (parts.Length < 4)
                return;
            RoomName = parts[0];
            MaxPlayers = int.TryParse(parts[1], out var mp) ? Math.Max(2, mp) : MaxPlayers;
            SkillLevel = ClientConfiguration.Instance.NormalizeSkillLevel(
                int.TryParse(parts[2], out var sl) ? sl : SkillLevel);
            IsCustomPassword = parts[3] == "1";
            if (_gameChannel is not null)
                _gameChannel.UIName = RoomName;
            NotifyChanged();
        }

        private void HandleStartMessage(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            // START 格式：{UniqueGameID};{Name};{ip}:{port};...（M3.11：多人局 ip 为隧道服务器地址）
            string[] parts = args.Split(';', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 1 || !int.TryParse(parts[0], out int gameId))
                return;
            UniqueGameId = gameId;

            string tunnelAddress = string.Empty;
            for (int i = 1; i + 1 < parts.Length; i += 2)
            {
                string name = parts[i];
                string[] ipPort = parts[i + 1].Split(':');
                int port = ipPort.Length > 1 && int.TryParse(ipPort[^1], out var p) ? p : 0;
                if (ipPort.Length > 0 && ipPort[0] != "0.0.0.0")
                    tunnelAddress = ipPort[0];
                if (_players.TryGetValue(name, out var ps))
                    ps.Port = port;
            }

            // 记录隧道地址；监听端口从隧道列表按地址反查（START 广播的是玩家端口，非隧道监听端口）
            TunnelAddress = tunnelAddress;
            TunnelPort = string.IsNullOrEmpty(tunnelAddress) ? 0
                : (_tunnelHandler.Tunnels.FirstOrDefault(t =>
                    string.Equals(t.Address, tunnelAddress, StringComparison.OrdinalIgnoreCase))?.Port ?? 0);

            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan, "主机已开始游戏"));
            GameStartRequested?.Invoke();
        }

        /// <summary>加入者视角记录的随机种子（供 M3.6 出生点随机化对齐）。</summary>
        public int RandomSeed { get; private set; }

        // ── spawn 生成数据（M3.7：GameLaunchService 读取） ──

        /// <summary>
        /// 将房间内玩家状态映射为 <see cref="PlayerInfo"/> 列表，供 spawn.ini 生成。
        /// 端口来自 START 广播分配（房主分配 / 加入者解析），Ip 无隧道恒为 0.0.0.0。
        /// </summary>
        public List<PlayerInfo> GetPlayerInfos()
        {
            var result = new List<PlayerInfo>(_players.Count);
            foreach (var p in _players.Values)
            {
                result.Add(new PlayerInfo
                {
                    Name = p.Name,
                    SideId = p.Side,
                    ColorId = p.Color,
                    StartingLocation = p.Start,
                    TeamId = p.Team,
                    Ready = p.Ready,
                    IPAddress = "0.0.0.0",
                    Port = p.Port
                });
            }
            return result;
        }

        /// <summary>本局阵营数量（spawn.ini 地图强制选项用，取 Sides 逗号分隔数量）。</summary>
        public int SideCount =>
            Math.Max(1, ClientConfiguration.Instance.Sides.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

        // ── 出生点映射（M3.6 spec §5.3：地图预览叠加出生点） ──

        /// <summary>
        /// 计算当前所选地图的出生点列表（百分比坐标 + 占用者）。
        /// 坐标取自 <see cref="Map.GetStartingLocationPreviewCoords"/>：其返回值为
        /// "比率 × previewSize"，与 previewSize 线性无关，故用 1000×1000 归一化为 0..1。
        /// </summary>
        public List<SpawnPointState> GetSpawnPointStates()
        {
            var result = new List<SpawnPointState>();
            var map = SelectedMap?.Map;
            if (map is null)
                return result;

            List<Point> coords;
            try
            {
                coords = map.GetStartingLocationPreviewCoords(new Point(1000, 1000));
            }
            catch
            {
                return result;
            }

            // 仅展示房间允许范围内的出生点（Start 为 1-based，对应 waypoint 下标）
            int shown = Math.Min(coords.Count, MaxPlayers);
            for (int i = 0; i < shown; i++)
            {
                var occupant = _players.Values.FirstOrDefault(p => p.Start == i + 1);
                result.Add(new SpawnPointState(
                    i + 1,
                    Math.Clamp(coords[i].X / 1000.0, 0.0, 1.0),
                    Math.Clamp(coords[i].Y / 1000.0, 0.0, 1.0),
                    occupant?.Name,
                    occupant?.Color ?? -1,
                    occupant?.IsSelf ?? false));
            }
            return result;
        }

        // ── 周期广播（房主） ──

        private void StartBroadcastTimer()
        {
            StopBroadcastTimer();
            _broadcastTimer = new Timer(_ => _dispatcher.Post(BroadcastGame),
                null, INITIAL_GAME_BROADCAST_DELAY_MS, GAME_BROADCAST_INTERVAL_MS);
        }

        private void StopBroadcastTimer()
        {
            _broadcastTimer?.Dispose();
            _broadcastTimer = null;
        }

        private void NotifyChanged() => Changed?.Invoke();

        public void Dispose()
        {
            _tunnelHandler.TunnelsRefreshed -= OnTunnelsRefreshed;
            StopBroadcastTimer();
            if (_gameChannel is not null)
                UnwireChannel(_gameChannel);
        }
    }

    /// <summary>房间内单个玩家的实时状态（队伍/颜色/出生点/阵营/就绪/端口）。</summary>
    public sealed class RoomPlayerState
    {
        public required string Name { get; init; }
        public bool IsHost { get; set; }
        public int Side { get; set; }
        public int Color { get; set; }
        public int Start { get; set; }
        public int Team { get; set; }
        public bool Ready { get; set; }
        /// <summary>本局游戏端口（房主分配 / 加入者从 START 解析，写 spawn.ini）。</summary>
        public int Port { get; set; }
        public bool IsSelf => string.Equals(Name, ProgramConstants.PLAYERNAME, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>地图预览上的一个出生点（百分比坐标 + 当前占用者）。</summary>
    public sealed record SpawnPointState(
        int Index,
        double X,
        double Y,
        string? OccupantName,
        int ColorIndex,
        bool IsSelf);
}
