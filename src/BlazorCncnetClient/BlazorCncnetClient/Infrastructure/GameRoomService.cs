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

        // 原版 CnCNetGameLobby 地图共享协议（MAPREQ 请求上传 / MAPOK 下载就绪 / MAPFAIL 传输失败 / MAPSDISABLED 禁用共享）
        private const string MAP_SHARING_FAIL_MESSAGE = "MAPFAIL";
        private const string MAP_SHARING_DOWNLOAD_REQUEST = "MAPOK";
        private const string MAP_SHARING_UPLOAD_REQUEST = "MAPREQ";
        private const string MAP_SHARING_DISABLED_MESSAGE = "MAPSDISABLED";

        /// <summary>房间最大总槽位数（人类 + AI，同原版 MAX_PLAYER_COUNT）。</summary>
        public const int MaxTotalSlots = 8;

        // 第七轮补缺：房间骰子协议（原版 MultiplayerGameLobby DICE_ROLL_MESSAGE）
        private const string DICE_ROLL_MESSAGE = "DR";
        private const int MAX_DICE = 10;
        private const int MAX_DIE_SIDES = 100;

        /// <summary>房间状态变化（玩家/地图/设置/就绪）。</summary>
        public event Action? Changed;

        /// <summary>开始游戏请求（M3.7：GameLaunchService 接此生成配置并启动游戏进程）。</summary>
        public event Action? GameStartRequested;

        /// <summary>房主因长时间无操作被警告（第七轮补缺，原版 GameHostInactiveChecker 弹框语义；UI 弹窗确认后调用 <see cref="ResetHostActivity"/>）。</summary>
        public event Action? HostInactiveWarningRequested;

        /// <summary>本局唯一 GameID（START 广播第一段，写入 spawn.ini [Settings] GameID）。</summary>
        public int UniqueGameId { get; private set; }

        /// <summary>本局隧道服务器地址/端口（M3.11：房主在 StartGame 选定；加入者从 START 解析），写 spawn.ini [Tunnel] 节。</summary>
        public string TunnelAddress { get; private set; } = string.Empty;
        public int TunnelPort { get; private set; }

        /// <summary>房主选择了无效隧道时置位（原版 tunnelErrorMode：加入者据此提示无法参与本局）。</summary>
        public bool TunnelErrorMode { get; private set; }

        /// <summary>当前可用隧道列表（原版 TunnelHandler.Tunnels，驱动房间隧道选择窗口）。</summary>
        public IReadOnlyList<CnCNetTunnel> Tunnels => _tunnelHandler.Tunnels;

        /// <summary>当前选中的隧道服务器（房主手动选择或加入者按 CHTNL/START 同步）。</summary>
        public CnCNetTunnel? CurrentTunnel => _tunnelHandler.CurrentTunnel;

        private const string CHANGE_TUNNEL_SERVER_MESSAGE = "CHTNL";

        private readonly CnCNetManager _manager;
        private readonly GameCollection _gameCollection;
        private readonly MapLoader _mapLoader;
        private readonly IDispatcher _dispatcher;
        private readonly TunnelHandler _tunnelHandler;
        private readonly CnCNetUserData _userData;
        private readonly Random _random = new();

        private Channel? _gameChannel;
        private Timer? _broadcastTimer;
        private bool _wired;
        private string _password = string.Empty;

        // 房主不活跃检测（原版 GameHostInactiveChecker：无操作超警告 → 超时自动离房）
        private Timer? _hostActivityTimer;
        private DateTime _hostLastActivity;
        private bool _hostInactiveWarningShown;
        private bool _hostInactivityEnabled;

        private readonly Dictionary<string, RoomPlayerState> _players = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>房主添加的 AI 玩家（原版 AIPlayers；随 PO 广播同步给加入者，无需占用 IRC 频道成员）。</summary>
        private readonly List<PlayerInfo> _aiPlayers = new();

        /// <summary>房主已就地图传达最终结果（MAPOK 或 MAPFAIL）的 SHA1 集合（原版 hostUploadedMaps，防重复 MAPREQ）。</summary>
        private readonly HashSet<string> _hostUploadedMaps = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>加入者视角最近一次房主所选地图的 SHA1/名称（地图共享用，原版 lastMapSHA1/lastMapName）。</summary>
        private string _lastMapSHA1 = string.Empty;
        private string _lastMapName = string.Empty;

        /// <summary>加入者视角：房主选择但本机未安装的地图 SHA1（GO 解析缺图时置位，下载/更换后清除）。</summary>
        public string? MissingMapSHA1 { get; private set; }

        /// <summary>缺失地图的名称（GO 消息中的地图名，用于下载文件名与提示）。</summary>
        public string? MissingMapName { get; private set; }

        /// <summary>加入者视角缺图处理状态（驱动房间内下载提示条）。</summary>
        public MapDownloadState DownloadState { get; private set; } = MapDownloadState.None;

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

        /// <summary>房主添加的 AI 玩家（加入者经 PO 消息同步，Ready 恒 true，不参与就绪判定）。</summary>
        public IReadOnlyList<PlayerInfo> AIPlayers => _aiPlayers;

        /// <summary>
        /// 房间游戏选项（复选 + 下拉，与原版 GameLobbyBase.ini 一致，与遭遇战共用同一集合）。
        /// 房主修改后经 GO 广播同步给加入者；生成 spawn.ini 时写入 [Settings] 并应用地图代码。
        /// </summary>
        public IReadOnlyList<GameSessionOption> GameOptions { get; } = GameSessionOption.CreateDefaultGameOptions();

        // ── 第八轮补缺 r8-1：玩家附加选项（原版 PlayerExtraOptionsPanel，PEO 协议广播） ──

        private const string PLAYER_EXTRA_OPTIONS_MESSAGE = "PEO";

        private PlayerExtraOptions _extraOptions = new();

        /// <summary>玩家附加选项（强制随机阵营/颜色/出生点、无队伍、自动结盟映射）。
        /// 房主修改经 <c>PEO</c> CTCP 广播（原版 GAME_PLAYERS_EXTRA_MESSAGE）；加入者只读应用。</summary>
        public PlayerExtraOptions ExtraOptions => _extraOptions;

        /// <summary>附加选项变化（房主本地修改或加入者收到 PEO 广播时触发，UI 据此刷新面板）。</summary>
        public event Action? PlayerExtraOptionsChanged;

        /// <summary>当前地图的自动结盟预设（原版 ddTeamStartMappingPreset 数据源）。</summary>
        public IReadOnlyList<TeamStartMappingPreset> TeamStartMappingPresets =>
            SelectedMap?.Map.TeamStartMappingPresets ?? new List<TeamStartMappingPreset>();

        /// <summary>自动结盟是否可用：有预设且非 coop / ForceNoTeams 地图（同原版 UseTeamStartMappingsAllowChecking）。</summary>
        public bool TeamStartMappingsAllowed =>
            SelectedMap is not null
            && !SelectedMap.IsCoop
            && !SelectedMap.ForceNoTeams
            && TeamStartMappingPresets.Count > 0;

        /// <summary>出生点选择是否禁用（r9-3：地图模式强制随机出生点或启用「强制随机出生点」时，
        /// 同原版 MultiplayerGameLobby.UpdateMapPreviewBoxEnabledStatus 的 disablestartlocs）。</summary>
        public bool StartLocationSelectionDisabled =>
            SelectedMap is not null
            && (SelectedMap.ForceRandomStartLocations || _extraOptions.IsForceRandomStarts);

        /// <summary>当前地图允许编辑的自动结盟出生点集合（同原版 AllowedStartingLocations）。</summary>
        public HashSet<int> AllowedTeamStartMappingLocations
        {
            get
            {
                if (SelectedMap is null)
                    return new HashSet<int>(Enumerable.Range(1, MaxPlayers));
                try
                {
                    return SelectedMap.AllowedStartingLocations
                        .Where(l => l > 0)
                        .ToHashSet();
                }
                catch
                {
                    return new HashSet<int>(Enumerable.Range(1, MaxPlayers));
                }
            }
        }

        /// <summary>生效中的出生点→队伍映射（未启用自动结盟时返回空列表，spawn.ini 不写联盟兜底）。</summary>
        public List<TeamStartMapping> GetActiveTeamStartMappings() =>
            _extraOptions.IsUseTeamStartMappings
                ? new List<TeamStartMapping>(_extraOptions.TeamStartMappings)
                : new List<TeamStartMapping>();

        // ── r8-1：房主修改入口（UI 调用；变更统一走 PEO 广播 + 事件） ──

        /// <summary>附加选项变更统一出口：触发本地事件（UI 刷新）+ 房主 PEO 广播（同原版 PlayerExtraOptions_OptionsChanged）。</summary>
        private void NotifyExtraOptionsChanged()
        {
            PlayerExtraOptionsChanged?.Invoke();
            BroadcastPlayerExtraOptions();
        }

        /// <summary>房主切换“强制随机阵营”。</summary>
        public void SetForceRandomSides(bool value)
        {
            if (!IsHost || _extraOptions.IsForceRandomSides == value)
                return;
            _extraOptions.IsForceRandomSides = value;
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主切换“强制随机颜色”。</summary>
        public void SetForceRandomColors(bool value)
        {
            if (!IsHost || _extraOptions.IsForceRandomColors == value)
                return;
            _extraOptions.IsForceRandomColors = value;
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主切换“强制随机出生点”。</summary>
        public void SetForceRandomStarts(bool value)
        {
            if (!IsHost || _extraOptions.IsForceRandomStarts == value)
                return;
            _extraOptions.IsForceRandomStarts = value;
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主切换“强制无队伍”（自动结盟启用时不可单独关闭，同原版 AllowChecking 语义）。</summary>
        public void SetForceNoTeams(bool value)
        {
            if (!IsHost || _extraOptions.IsUseTeamStartMappings || _extraOptions.IsForceNoTeams == value)
                return;
            _extraOptions.IsForceNoTeams = value;
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主切换“启用自动结盟”：启用即强制无队伍并应用首个预设/地图默认映射；
        /// 关闭则清空映射（同原版 ChkBoxUseTeamStartMappings_Changed）。</summary>
        public void SetUseTeamStartMappings(bool use)
        {
            if (!IsHost || _extraOptions.IsUseTeamStartMappings == use)
                return;
            if (use && !TeamStartMappingsAllowed)
                return;
            _extraOptions.IsUseTeamStartMappings = use;
            if (use)
            {
                _extraOptions.IsForceNoTeams = true;
                ResetTeamStartMappingsForCurrentMap();
            }
            else
            {
                _extraOptions.TeamStartMappings.Clear();
            }
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主应用自动结盟预设（下标对应 <see cref="TeamStartMappingPresets"/>，UI 下拉第 0 项为自定义）。</summary>
        public void ApplyTeamStartMappingPreset(int presetIndex)
        {
            if (!IsHost || !_extraOptions.IsUseTeamStartMappings)
                return;
            var presets = TeamStartMappingPresets;
            if (presetIndex < 0 || presetIndex >= presets.Count)
                return;
            _extraOptions.TeamStartMappings = new List<TeamStartMapping>(
                presets[presetIndex].TeamStartMappings.Select(m => new TeamStartMapping { Start = m.Start, Team = m.Team }));
            NotifyExtraOptionsChanged();
        }

        /// <summary>房主设置指定出生点的结盟队伍（TeamStartMapping.NO_PLAYER 阻挡 / NO_TEAM 无队伍 / 队伍字母）。</summary>
        public void SetTeamStartMapping(int start, string team)
        {
            if (!IsHost || !_extraOptions.IsUseTeamStartMappings)
                return;
            var existing = _extraOptions.TeamStartMappings.FirstOrDefault(m => m.Start == start);
            if (existing is not null)
                existing.Team = team;
            else
                _extraOptions.TeamStartMappings.Add(new TeamStartMapping { Start = start, Team = team });
            NotifyExtraOptionsChanged();
        }

        /// <summary>换图后按当前地图重置自动结盟映射（同原版 RefreshTeamStartMappingPresets：
        /// 有预设应用首个预设，否则回退地图默认映射，均无则清空并关闭自动结盟）。</summary>
        private void ResetTeamStartMappingsForCurrentMap()
        {
            var presets = TeamStartMappingPresets;
            if (presets.Count > 0)
            {
                _extraOptions.TeamStartMappings = new List<TeamStartMapping>(
                    presets[0].TeamStartMappings.Select(m => new TeamStartMapping { Start = m.Start, Team = m.Team }));
            }
            else if (SelectedMap?.Map.TeamStartMappings is { Count: > 0 } defaults)
            {
                _extraOptions.TeamStartMappings = new List<TeamStartMapping>(
                    defaults.Select(m => new TeamStartMapping { Start = m.Start, Team = m.Team }));
            }
            else
            {
                _extraOptions.TeamStartMappings = new List<TeamStartMapping>();
                _extraOptions.IsUseTeamStartMappings = false;
            }
        }

        /// <summary>房主将附加选项经 PEO CTCP 广播给房间（同原版 CnCNetGameLobby.BroadcastPlayerExtraOptions）。</summary>
        private void BroadcastPlayerExtraOptions()
        {
            if (!IsHost || _gameChannel is null)
                return;
            _gameChannel.SendCTCPMessage(_extraOptions.ToCncnetMessage(),
                QueuedMessageType.GAME_PLAYERS_EXTRA_MESSAGE, 11);
        }

        /// <summary>观战（Spectator）阵营索引 = 真实阵营数量（本端无 Random/随机选择器，
        /// 同原版 GetSpectatorSideIndex 语义；也同 SpawnIniGenerator 的 IsSpectator 判定 sideId == sideCount）。</summary>
        public int SpectatorSideIndex => SideCount;

        /// <summary>指定玩家是否为观战者（阵营 == SpectatorSideIndex）。</summary>
        public static bool IsSpectatorSide(int side, int sideCount) => side == sideCount;

        public GameRoomService(
            CnCNetManager manager,
            GameCollection gameCollection,
            MapLoader mapLoader,
            IDispatcher dispatcher,
            TunnelHandler tunnelHandler,
            CnCNetUserData userData)
        {
            _manager = manager;
            _gameCollection = gameCollection;
            _mapLoader = mapLoader;
            _dispatcher = dispatcher;
            _tunnelHandler = tunnelHandler;
            _userData = userData;
            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));

            // 隧道列表就绪后：房主在房间内且未选定隧道 → 自动选优并刷新 GAME 广播（房间列表显示真实隧道）
            _tunnelHandler.TunnelsRefreshed += OnTunnelsRefreshed;

            // 地图共享（P1 地图下载）：下载完成自动选中；上传/下载失败按原版通知与协议回执
            _mapLoader.MapChanged += OnMapLoaderMapChanged;
            MapSharer.MapDownloadFailed += OnMapSharerDownloadFailed;
            MapSharer.MapDownloadComplete += OnMapSharerDownloadComplete;
            MapSharer.MapUploadFailed += OnMapSharerUploadFailed;
            MapSharer.MapUploadComplete += OnMapSharerUploadComplete;
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
            RandomSeed = _random.Next(); // 本局随机种子：建房时生成一次，GO 广播沿用（避免选项变更导致种子漂移）
            _players.Clear();
            _aiPlayers.Clear();
            ResetMapSharingState();
            TunnelAddress = string.Empty;
            TunnelPort = 0;
            TunnelErrorMode = false;

            _gameChannel = channel;
            WireChannel(channel);
            channel.Join();

            // M3.11：隧道列表已就绪则建房时立即选优并广播（房间列表显示真实隧道）；
            // 列表未就绪时由 OnTunnelsRefreshed 在下次刷新兜底（此前仍为 [DYN]）。
            EnsureTunnelSelected();
            BroadcastGame();

            _manager.MainChannel.AddMessage(new ChatMessage(ColorConstants.Cyan,
                $"正在创建房间「{roomName}」..."));
            StartHostInactiveCheck();
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
            _aiPlayers.Clear();
            ResetMapSharingState();
            TunnelAddress = string.Empty;
            TunnelPort = 0;
            TunnelErrorMode = false;

            _gameChannel = channel;
            WireChannel(channel);
            channel.Join();

            _manager.MainChannel.AddMessage(new ChatMessage(ColorConstants.Cyan,
                $"正在加入房间「{game.RoomName}」..."));
            NotifyChanged();
        }

        /// <summary>
        /// 邀请指定玩家加入当前房间（原版 GlobalContextMenu.Invite + INVITE 私有 CTCP 协议）。
        /// 消息发往玩家本人：<c>PRIVMSG &lt;player&gt; :\u0001INVITE &lt;房间频道名&gt;;&lt;房名&gt;[;&lt;密码&gt;]\u0001</c>。
        /// </summary>
        public void InvitePlayer(string playerName)
        {
            if (_gameChannel is null || string.IsNullOrWhiteSpace(playerName) || ProgramConstants.IsInGame)
                return;

            var body = $"{ProgramConstants.GAME_INVITE_CTCP_COMMAND} {_gameChannel.ChannelName};{RoomName}";
            if (!string.IsNullOrEmpty(_password))
                body += ";" + _password;

            _manager.SendCustomMessage(new QueuedMessage(
                $"PRIVMSG {playerName} :\u0001{body}\u0001", QueuedMessageType.CHAT_MESSAGE, 0));
        }

        /// <summary>离开房间：停止广播、PART 频道并清理状态。</summary>
        public void LeaveGame()
        {
            if (_gameChannel is null)
                return;
            StopBroadcastTimer();
            StopHostInactiveCheck();
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
            _aiPlayers.Clear();
            ResetMapSharingState();
            TunnelAddress = string.Empty;
            TunnelPort = 0;
            TunnelErrorMode = false;
            NotifyChanged();
        }

        /// <summary>清空地图共享状态（建房/加入/离开时调用，避免跨房间残留）。</summary>
        private void ResetMapSharingState()
        {
            _hostUploadedMaps.Clear();
            _lastMapSHA1 = string.Empty;
            _lastMapName = string.Empty;
            MissingMapSHA1 = null;
            MissingMapName = null;
            DownloadState = MapDownloadState.None;
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

        /// <summary>向房间频道发送聊天消息；以 / 开头视为房间指令（第七轮补缺：/roll /tunnelinfo /downloadmap /help）。</summary>
        public void SendRoomChat(string text)
        {
            if (_gameChannel is null || string.IsNullOrWhiteSpace(text))
                return;
            var message = text.Trim();
            if (message.Length == 0)
                return;
            if (message[0] == '/')
            {
                ExecuteRoomChatCommand(message);
                return;
            }
            var defaultColor = _manager.GetIRCColors().FirstOrDefault(c => c.Selectable)
                ?? _manager.GetIRCColors().First();
            _gameChannel.SendChatMessage(message, defaultColor);
        }

        // ── 房间聊天指令（第七轮补缺，原版 MultiplayerGameLobby ChatBoxCommand 体系） ──

        private void ExecuteRoomChatCommand(string message)
        {
            var spaceIndex = message.IndexOf(' ');
            string command = (spaceIndex > 0 ? message.Substring(0, spaceIndex) : message)
                .TrimStart('/').ToLowerInvariant();
            string args = spaceIndex > 0 ? message.Substring(spaceIndex + 1).Trim() : string.Empty;

            switch (command)
            {
                case "roll": RollDice(args); break;
                case "tunnelinfo": PrintTunnelInfo(); break;
                case "downloadmap": DownloadMapById(args); break;
                case "help":
                    AddRoomMessage("可用房间指令：/roll <骰子数>d<面数> 掷骰子 · /tunnelinfo 查看隧道 · /downloadmap <SHA1> [地图名] 下载地图 · /help 帮助");
                    break;
                default:
                    AddRoomMessage($"未知指令 /{command}，输入 /help 查看可用指令");
                    break;
            }
        }

        /// <summary>掷骰子（原版 RollDiceCommand：默认 1d6，限制 1-10 骰 × 2-100 面；结果经 DR 广播全员可见）。</summary>
        private void RollDice(string dieType)
        {
            int dieSides = 6;
            int dieCount = 1;

            if (!string.IsNullOrEmpty(dieType))
            {
                string[] parts = dieType.Split('d');
                if (parts.Length == 2)
                {
                    if (!int.TryParse(parts[0], out dieCount) || !int.TryParse(parts[1], out dieSides))
                    {
                        AddRoomMessage("骰子格式无效，应为 /roll <骰子数>d<面数>，例：/roll 3d6");
                        return;
                    }
                }
            }

            if (dieCount > MAX_DICE || dieCount < 1)
            {
                AddRoomMessage("一次只能掷 1 到 10 个骰子");
                return;
            }
            if (dieSides > MAX_DIE_SIDES || dieSides < 2)
            {
                AddRoomMessage("骰子面数只能为 2 到 100");
                return;
            }

            int[] results = new int[dieCount];
            for (int i = 0; i < dieCount; i++)
                results[i] = _random.Next(1, dieSides + 1);

            // 原版 CnCNetGameLobby.BroadcastDiceRoll：频道 CTCP 广播 + 本地打印
            string resultString = string.Join(",", results);
            _gameChannel?.SendCTCPMessage($"{DICE_ROLL_MESSAGE} {dieSides},{resultString}",
                QueuedMessageType.CHAT_MESSAGE, 0);
            PrintDiceRollResult(ProgramConstants.PLAYERNAME, dieSides, results);
        }

        /// <summary>解析并展示他人掷骰结果（原版 HandleDiceRollResult，DR 消息形如 "6,3,5,1"）。</summary>
        private void HandleDiceRollResult(string senderName, string result)
        {
            if (string.IsNullOrEmpty(result))
                return;
            string[] parts = result.Split(',');
            if (parts.Length < 2 || parts.Length > MAX_DICE + 1)
                return;
            if (!int.TryParse(parts[0], out int dieSides) || dieSides < 1 || dieSides > MAX_DIE_SIDES)
                return;
            int[] results = new int[parts.Length - 1];
            for (int i = 1; i < parts.Length; i++)
            {
                if (!int.TryParse(parts[i], out int v) || v < 1 || v > dieSides)
                    return;
                results[i - 1] = v;
            }
            PrintDiceRollResult(senderName, dieSides, results);
        }

        private void PrintDiceRollResult(string senderName, int dieSides, int[] results)
        {
            AddRoomMessage($"{senderName} 掷了 {results.Length}d{dieSides}，结果为 {string.Join(", ", results)}");
        }

        /// <summary>查看当前隧道服务器信息（原版 PrintTunnelServerInformation）。</summary>
        private void PrintTunnelInfo()
        {
            if (CurrentTunnel is null)
            {
                AddRoomMessage("隧道服务器不可用");
                return;
            }
            var t = CurrentTunnel;
            AddRoomMessage(
                $"当前隧道服务器：{t.Name} {t.CountryCode}（负载 {t.Clients}/{t.MaxClients}）（官方：{(t.Official ? "是" : "否")}）");
        }

        /// <summary>按地图 SHA1 从 CnCNet 地图服务器下载地图（原版 DownloadMapByIdCommand：sha1 [可选地图名]）。</summary>
        private void DownloadMapById(string parameters)
        {
            if (string.IsNullOrWhiteSpace(parameters))
            {
                AddRoomMessage("用法：/downloadmap <SHA1> [地图名]，例：/downloadmap ABC123DEF... My Battle Map");
                return;
            }
            parameters = parameters.Trim();
            int firstSpaceIndex = parameters.IndexOf(' ');
            string sha1 = firstSpaceIndex == -1 ? parameters : parameters.Substring(0, firstSpaceIndex);
            string mapName = firstSpaceIndex == -1 ? "user_chat_command_download"
                : parameters.Substring(firstSpaceIndex + 1).Trim();

            // 兼容从 cncnet 搜索页复制哈希时混入的 "?"
            sha1 = sha1.Replace("?", "");

            if (sha1.Length == 0)
            {
                AddRoomMessage("地图 ID（SHA1）不能为空");
                return;
            }

            // 本地已存在该地图（任何文件名）则提示删除后再试
            var existing = _mapLoader.GameModeMaps.FirstOrDefault(gmm =>
                string.Equals(gmm.Map.SHA1, sha1, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                AddRoomMessage(
                    $"地图 ID「{sha1}」已从本地加载（{existing.Map.BaseFilePath}.{ClientConfiguration.Instance.MapFileExtension}），请先删除该文件再试");
                return;
            }

            // 替换文件名中的非法字符
            char replaceUnsafeCharactersWith = '-';
            HashSet<char> invalidChars = new(Path.GetInvalidFileNameChars());
            string safeMapName = new string(mapName.Select(c => invalidChars.Contains(c) ? replaceUnsafeCharactersWith : c).ToArray());

            AddRoomMessage($"正在通过聊天指令下载地图：sha1={sha1}, 地图名={mapName}");
            MapSharer.DownloadMap(sha1, LocalGame?.InternalName ?? string.Empty, safeMapName);
        }

        /// <summary>向房间频道追加本地系统消息（指令反馈 / 掷骰结果，不发送到服务器）。</summary>
        private void AddRoomMessage(string text)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan, text));
        }

        /// <summary>向房间频道发送系统警告消息（原版 GameLobby.AddWarning，如邀请送达失败提示）。</summary>
        public void AddRoomWarning(string text)
        {
            if (_gameChannel is null || string.IsNullOrWhiteSpace(text))
                return;
            _gameChannel.AddMessage(new ChatMessage(ColorConstants.Yellow, text));
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

            // 观战者无出生点（原版 CopyPlayerDataFromUI：SideId == Spectator 索引 → StartingLocation = 0）
            if (IsSpectatorSide(self.Side, SideCount))
                self.Start = 0;

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

        // ── AI 玩家与观战（P1：原版 AIPlayers 槽位 + Spectator 阵营） ──

        /// <summary>总槽位数（人类 + AI，上限 <see cref="MaxTotalSlots"/>）。</summary>
        public int TotalSlots => _players.Count + _aiPlayers.Count;

        /// <summary>房主添加一个 AI 玩家：默认 Easy（AILevel=0）、未指定阵营/颜色、出生点取第一个空闲值。</summary>
        public void AddAI()
        {
            if (!IsHost || _gameChannel is null || !IsJoined)
                return;
            if (TotalSlots >= MaxTotalSlots)
                throw new InvalidOperationException($"房间总槽位已达上限（{MaxTotalSlots}，含 AI）");

            var ai = new PlayerInfo(ProgramConstants.AI_PLAYER_NAMES[0], 0, 0, 0, 0)
            {
                IsAI = true,
                AILevel = 0,
                Ready = true,
                Index = _aiPlayers.Count
            };
            ai.StartingLocation = GetFirstFreeStartingLocation();
            _aiPlayers.Add(ai);
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        /// <summary>房主移除指定 AI 玩家（需是列表中的 AI，AI 名称可重复故按引用比较）。</summary>
        public void RemoveAI(PlayerInfo ai)
        {
            if (!IsHost || !_aiPlayers.Remove(ai))
                return;
            for (int i = 0; i < _aiPlayers.Count; i++)
                _aiPlayers[i].Index = i;
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        /// <summary>房主修改指定 AI 玩家的阵营/颜色/出生点/队伍（AI 不可观战，阵营限真实阵营）。</summary>
        public void SetAIOption(PlayerInfo ai, int side, int color, int start, int team)
        {
            if (!IsHost || !_aiPlayers.Contains(ai))
                return;
            ai.SideId = Math.Clamp(side, 0, Math.Max(0, SideCount - 1));
            ai.ColorId = Math.Clamp(color, 0, Math.Max(0, MultiplayerColor.LoadColors().Count));
            ai.StartingLocation = start;
            ai.TeamId = team;
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        /// <summary>房主修改指定 AI 玩家的难度（0=Easy / 1=Medium / 2=Hard，名称随难度同步）。</summary>
        public void SetAILevel(PlayerInfo ai, int level)
        {
            if (!IsHost || !_aiPlayers.Contains(ai))
                return;
            ai.AILevel = Math.Clamp(level, 0, ProgramConstants.AI_PLAYER_NAMES.Count - 1);
            ai.Name = ProgramConstants.AI_PLAYER_NAMES[ai.AILevel];
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        /// <summary>当前未被任何玩家占用的最小出生点（用于新增 AI 的默认出生点，无空闲返回 0=随机）。</summary>
        private int GetFirstFreeStartingLocation()
        {
            var occupied = GetPlayerInfos().Select(p => p.StartingLocation)
                .Concat(_aiPlayers.Select(a => a.StartingLocation))
                .ToHashSet();
            for (int loc = 1; loc <= MaxTotalSlots; loc++)
            {
                if (!occupied.Contains(loc))
                    return loc;
            }
            return 0;
        }

        // ── 地图共享（P1 地图下载：Online 层接线） ──

        /// <summary>
        /// 加入者处理房主所选的本机缺失地图（原版 RequestMap 语义）：
        /// 启用地图共享 → 提示下载（等待用户确认）；禁用 → 告知房主 MAPSDISABLED。
        /// </summary>
        private void RequestMissingMap(string mapSHA1, string mapName)
        {
            MissingMapSHA1 = mapSHA1;
            MissingMapName = mapName;
            if (UserINISettings.Instance.EnableMapSharing.Value)
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow,
                    "主机选择了本机未安装的地图，点击「下载地图」从 CnCNet 地图库获取。"));
                DownloadState = MapDownloadState.Prompt;
            }
            else
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                    "主机选择了本机未安装的地图，且已禁用地图共享，无法自动获取；请让主机更换地图。"));
                _gameChannel?.SendCTCPMessage(MAP_SHARING_DISABLED_MESSAGE, QueuedMessageType.SYSTEM_MESSAGE, 9);
                DownloadState = MapDownloadState.Disabled;
            }
            NotifyChanged();
        }

        /// <summary>用户确认下载缺失地图（房间内下载按钮触发，原版 MapSharingConfirmationPanel 确认语义）。</summary>
        public void DownloadMissingMap()
        {
            if (string.IsNullOrEmpty(MissingMapSHA1))
                return;
            if (!UserINISettings.Instance.EnableMapSharing.Value)
            {
                DownloadState = MapDownloadState.Disabled;
                NotifyChanged();
                return;
            }
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan, "正在尝试下载地图…"));
            DownloadState = MapDownloadState.Downloading;
            MapSharer.DownloadMap(MissingMapSHA1, LocalGame?.InternalName ?? string.Empty, MissingMapName ?? MissingMapSHA1);
            NotifyChanged();
        }

        /// <summary>
        /// 房主收到加入者 MAPREQ：地图已上传则直接回 MAPOK；未上传则查找本地地图并上传到地图库
        /// （官方图不共享，提示房主更换地图；原版 HandleMapUploadRequest）。
        /// </summary>
        private void HandleMapUploadRequest(string sender, string mapSHA1)
        {
            if (MapSharer.IsMapUploaded(mapSHA1))
            {
                if (SelectedMap is not null &&
                    string.Equals(SelectedMap.Map.SHA1, mapSHA1, StringComparison.OrdinalIgnoreCase))
                    _gameChannel?.SendCTCPMessage(MAP_SHARING_DOWNLOAD_REQUEST + " " + mapSHA1, QueuedMessageType.SYSTEM_MESSAGE, 9);
                return;
            }

            Map? map = _mapLoader.FindMapByHash(mapSHA1);
            if (map is null)
            {
                Logger.Log($"未知地图上传请求来自 {sender}: {mapSHA1}");
                return;
            }
            if (map.Official)
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                    $"{sender} 缺少官方地图「{map.Name}」，请更换地图或对方将无法参与对局。"));
                return;
            }
            if (!IsHost)
                return;

            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow,
                $"{sender} 缺少地图「{map.Name}」，正在尝试上传到 CnCNet 地图库。"));
            MapSharer.UploadMap(map, LocalGame?.InternalName ?? string.Empty);
        }

        /// <summary>加入者收到房主 MAPOK（地图已入库）：重试下载（原版 HandleMapDownloadRequest）。</summary>
        private void HandleMapDownloadRequest(string sender, string sha1)
        {
            if (sender != HostName)
                return;
            _hostUploadedMaps.Add(sha1);
            if (string.Equals(_lastMapSHA1, sha1, StringComparison.OrdinalIgnoreCase) && SelectedMap is null)
            {
                DownloadState = MapDownloadState.Downloading;
                MapSharer.DownloadMap(sha1, LocalGame?.InternalName ?? string.Empty, _lastMapName);
                NotifyChanged();
            }
        }

        /// <summary>处理 MAPFAIL（房主或玩家上传/下载失败，原版 HandleMapTransferFailMessage）。</summary>
        private void HandleMapTransferFailMessage(string sender, string sha1)
        {
            if (sender == HostName)
            {
                _hostUploadedMaps.Add(sha1);
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                    "房主未能将地图上传到 CnCNet 地图库。"));
                if (string.Equals(_lastMapSHA1, sha1, StringComparison.OrdinalIgnoreCase) && SelectedMap is null)
                {
                    DownloadState = MapDownloadState.Failed;
                    _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                        "请让房主更换地图，否则无法参与对局。"));
                    NotifyChanged();
                }
                return;
            }

            if (string.Equals(_lastMapSHA1, sha1, StringComparison.OrdinalIgnoreCase))
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                    IsHost
                        ? $"{sender} 无法从地图库下载该地图，请更换地图或对方将无法参与对局。"
                        : $"{sender} 无法从地图库下载该地图，请让房主更换地图。"));
            }
        }

        /// <summary>收到 MAPSDISABLED：某玩家禁用了地图共享（原版 HandleMapSharingBlockedMessage）。</summary>
        private void HandleMapSharingBlockedMessage(string sender)
        {
            _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                $"{sender} 缺少当前地图且已禁用地图共享，请更换为非自定义地图或对方将无法参与对局。"));
        }

        /// <summary>下载失败（原版 MapSharer_HandleMapDownloadFailed）：已上传失败过则回 MAPFAIL，否则请求房主上传。</summary>
        private void OnMapSharerDownloadFailed(object? sender, SHA1EventArgs e)
        {
            _dispatcher.Post(() =>
            {
                if (_hostUploadedMaps.Contains(e.SHA1))
                {
                    DownloadState = MapDownloadState.Failed;
                    _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                        "自定义地图下载失败，请让房主更换地图或对方将无法参与对局。"));
                    _gameChannel?.SendCTCPMessage(MAP_SHARING_FAIL_MESSAGE + " " + e.SHA1, QueuedMessageType.SYSTEM_MESSAGE, 9);
                }
                else
                {
                    _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan,
                        "正在请求房主将地图上传至 CnCNet 地图库…"));
                    _gameChannel?.SendCTCPMessage(MAP_SHARING_UPLOAD_REQUEST + " " + e.SHA1, QueuedMessageType.SYSTEM_MESSAGE, 9);
                }
                NotifyChanged();
            });
        }

        /// <summary>下载完成：地图文件已落地，等待 MapLoader 文件监视加载后自动选中。</summary>
        private void OnMapSharerDownloadComplete(object? sender, SHA1EventArgs e)
        {
            _dispatcher.Post(() =>
            {
                Logger.Log($"地图 {MapSharer.GetMapFileName(e.SHA1, e.MapName)} 下载成功。");
                NotifyChanged();
            });
        }

        /// <summary>房主上传失败（原版 MapSharer_HandleMapUploadFailed）。</summary>
        private void OnMapSharerUploadFailed(object? sender, MapEventArgs e)
        {
            _dispatcher.Post(() =>
            {
                Map map = e.Map;
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                    $"上传地图「{map.Name}」到 CnCNet 地图库失败。"));
                if (SelectedMap is not null && string.Equals(SelectedMap.Map.SHA1, map.SHA1, StringComparison.OrdinalIgnoreCase))
                {
                    _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red,
                        "请更换地图，否则缺少该地图的玩家无法参与对局。"));
                    _gameChannel?.SendCTCPMessage(MAP_SHARING_FAIL_MESSAGE + " " + map.SHA1, QueuedMessageType.SYSTEM_MESSAGE, 9);
                }
            });
        }

        /// <summary>房主上传完成：回 MAPOK 通知缺图玩家下载（原版 MapSharer_HandleMapUploadComplete）。</summary>
        private void OnMapSharerUploadComplete(object? sender, MapEventArgs e)
        {
            _dispatcher.Post(() =>
            {
                Map map = e.Map;
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan,
                    $"上传地图「{map.Name}」到 CnCNet 地图库完成。"));
                if (SelectedMap is not null && string.Equals(SelectedMap.Map.SHA1, map.SHA1, StringComparison.OrdinalIgnoreCase))
                    _gameChannel?.SendCTCPMessage(MAP_SHARING_DOWNLOAD_REQUEST + " " + map.SHA1, QueuedMessageType.SYSTEM_MESSAGE, 9);
            });
        }

        /// <summary>MapLoader 新增地图：若匹配最近房主所选（下载所得），自动选中（原版 HandleMapAdded 语义）。</summary>
        private void OnMapLoaderMapChanged(object? sender, MapChangedEventArgs e)
        {
            if (e.ChangeType != MapChangeType.Added)
                return;
            if (!string.Equals(_lastMapSHA1, e.Map.SHA1, StringComparison.OrdinalIgnoreCase))
                return;
            if (IsHost || _gameChannel is null)
                return;

            _dispatcher.Post(() =>
            {
                var gmm = _mapLoader.GameModeMaps.FirstOrDefault(g =>
                    string.Equals(g.Map.SHA1, e.Map.SHA1, StringComparison.OrdinalIgnoreCase));
                if (gmm is null)
                    return;
                SelectedMap = gmm;
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Cyan, $"地图「{e.Map.Name}」下载完成并已自动选中。"));
                MissingMapSHA1 = null;
                MissingMapName = null;
                DownloadState = MapDownloadState.Complete;
                NotifyChanged();
            });
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
            // 换图后附加选项按新地图重置（coop 图强制关闭自动结盟/无队伍，同原版 ChangeMap）
            if (gameModeMap.IsCoop || gameModeMap.ForceNoTeams)
            {
                _extraOptions.IsUseTeamStartMappings = false;
                _extraOptions.IsForceNoTeams = false;
                _extraOptions.TeamStartMappings.Clear();
            }
            else
            {
                ResetTeamStartMappingsForCurrentMap();
            }
            BroadcastPlayerExtraOptions();
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
        /// 房主设置/修改/移除自定义房间密码（2-3 原版 GameLobbySettingsWindow 密码字段）。
        /// 非空：MODE +k 应用新密码并标记密码房；空：移除自定义密码，回退为频道名哈希密码（公开房）。
        /// </summary>
        public void SetRoomPassword(string password)
        {
            if (!IsHost || _gameChannel is null)
                return;
            string trimmed = password.Trim();
            if (trimmed.Length > 0)
            {
                _password = trimmed;
                IsCustomPassword = true;
                _manager.SendCustomMessage(new QueuedMessage(
                    $"MODE {_gameChannel.ChannelName} +k {_password}",
                    QueuedMessageType.SYSTEM_MESSAGE, 50));
                _gameChannel.AddMessage(new ChatMessage(ColorConstants.Yellow, "已设置房间密码。"));
            }
            else if (IsCustomPassword)
            {
                string oldPassword = _password;
                IsCustomPassword = false;
                _password = Utilities.CalculateSHA1ForString(_gameChannel.ChannelName).Substring(0, 10);
                _manager.SendCustomMessage(new QueuedMessage(
                    $"MODE {_gameChannel.ChannelName} -k {oldPassword}",
                    QueuedMessageType.SYSTEM_MESSAGE, 50));
                _manager.SendCustomMessage(new QueuedMessage(
                    $"MODE {_gameChannel.ChannelName} +k {_password}",
                    QueuedMessageType.SYSTEM_MESSAGE, 50));
                _gameChannel.AddMessage(new ChatMessage(ColorConstants.Yellow, "已移除自定义密码，房间转为公开。"));
            }
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>房主踢出玩家（2-2 原版 GlobalContextMenu KICK，经游戏频道 KICK 消息）。</summary>
        public void KickPlayer(string playerName)
        {
            if (!IsHost || _gameChannel is null || !_players.ContainsKey(playerName))
                return;
            _gameChannel.SendKickMessage(playerName, 8);
            _gameChannel.AddMessage(new ChatMessage(ColorConstants.Yellow, $"{playerName} 已被移出房间。"));
        }

        /// <summary>房主封禁玩家（MODE +b，按 ident；缺失时回退昵称）。</summary>
        public void BanPlayer(string playerName)
        {
            if (!IsHost || _gameChannel is null || !_players.ContainsKey(playerName))
                return;
            _gameChannel.SendBanMessage(GetPlayerIdent(playerName), 8);
            _gameChannel.AddMessage(new ChatMessage(ColorConstants.Yellow, $"{playerName} 已被封禁。"));
        }

        /// <summary>房间玩家 ident（屏蔽/封禁用），缺失时回退昵称。</summary>
        public string GetPlayerIdent(string playerName) =>
            _gameChannel?.Users.Find(playerName)?.IRCUser.Ident ?? playerName;

        // ── 2-4 游戏选项（复选/下拉：房主修改 → GO 广播同步；加入者只读展示） ──

        /// <summary>房主设置复选框游戏选项并广播 GO。</summary>
        public void SetCheckBoxOption(GameSessionCheckBoxOption option, bool isChecked)
        {
            if (!IsHost || _gameChannel is null || !GameOptions.Contains(option))
                return;
            option.Checked = isChecked;
            BroadcastGameOptions();
            NotifyChanged();
        }

        /// <summary>房主设置下拉游戏选项选中索引并广播 GO。</summary>
        public void SetDropDownOption(GameSessionDropDownOption option, int selectedIndex)
        {
            if (!IsHost || _gameChannel is null || !GameOptions.Contains(option))
                return;
            option.SelectedIndex = option.Items.Count > 0 ? Math.Clamp(selectedIndex, 0, option.Items.Count - 1) : 0;
            BroadcastGameOptions();
            NotifyChanged();
        }

        /// <summary>游戏选项预设载入后：房主一次性广播 GO + GAME（同原版 LoadGameOptionPreset 后的 OnGameOptionChanged）。</summary>
        public void BroadcastGameOptionsRefresh()
        {
            if (!IsHost || _gameChannel is null)
                return;
            BroadcastGameOptions();
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>将复选框选项打包为位掩码（原版 GameOptionsToCode；位序 = CreateDefaultGameOptions 复选框顺序）。</summary>
        private int GetGameOptionsCode()
        {
            int code = 0;
            int bitIndex = 0;
            foreach (GameSessionCheckBoxOption checkBox in GameOptions.OfType<GameSessionCheckBoxOption>())
            {
                if (checkBox.Checked)
                    code |= 1 << bitIndex;
                bitIndex++;
            }
            return code;
        }

        /// <summary>按位掩码恢复复选框状态（原版 ApplyGameOptionsCode）。</summary>
        private void ApplyGameOptionsCode(int code)
        {
            int bitIndex = 0;
            foreach (GameSessionCheckBoxOption checkBox in GameOptions.OfType<GameSessionCheckBoxOption>())
            {
                checkBox.Checked = ((code >> bitIndex) & 1) == 1;
                bitIndex++;
            }
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

            // P1 观战校验（同原版 CheckGameValidity）：coop 不可观战；观战者不参与就绪判定；
            // 全部人类均为观战者则无实际玩家，禁止开始。
            if (SelectedMap.IsCoop && _players.Values.Any(p => IsSpectatorSide(p.Side, SideCount)))
                throw new InvalidOperationException("合作任务不能观战，请选择实际阵营");
            if (_players.Values.All(p => IsSpectatorSide(p.Side, SideCount)))
                throw new InvalidOperationException("需要至少一名玩家选择实际阵营才能开始");
            if (Locked && _players.Values.Count(p => !p.Ready && !IsSpectatorSide(p.Side, SideCount)) > 0)
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

            // 第七轮补缺：开始游戏时把本局玩家记入「最近玩家」列表（原版 CnCNetGameLobby.LaunchGame 语义）
            _userData.AddRecentPlayers(_players.Keys, RoomName);

            GameStartRequested?.Invoke();
        }

        // ── 房主不活跃检测（第七轮补缺，原版 GameHostInactiveChecker） ──

        /// <summary>
        /// 房主建房后启动不活跃检测：无任何操作（UI 活动经 <see cref="ResetHostActivity"/> 上报）超过
        /// InactiveHostWarningMessageSeconds 触发 <see cref="HostInactiveWarningRequested"/> 警告；
        /// 超过 InactiveHostKickSeconds 自动离开房间（原版 CloseEvent → LeaveGameLobby）。
        /// 仅在 ClientDefinitions.ini 启用（InactiveHostKickEnabled）时生效。
        /// </summary>
        private void StartHostInactiveCheck()
        {
            _hostInactivityEnabled = IsHost && ClientConfiguration.Instance.InactiveHostKickEnabled;
            if (!_hostInactivityEnabled)
                return;
            _hostLastActivity = DateTime.UtcNow;
            _hostInactiveWarningShown = false;
            _hostActivityTimer?.Dispose();
            _hostActivityTimer = new Timer(_ => CheckHostActivity(), null, 1000, 1000);
        }

        private void StopHostInactiveCheck()
        {
            _hostActivityTimer?.Dispose();
            _hostActivityTimer = null;
            _hostInactivityEnabled = false;
        }

        private void CheckHostActivity()
        {
            if (!_hostInactivityEnabled)
                return;
            double secondsElapsed = (DateTime.UtcNow - _hostLastActivity).TotalSeconds;
            var cfg = ClientConfiguration.Instance;
            if (secondsElapsed > cfg.InactiveHostWarningMessageSeconds && !_hostInactiveWarningShown)
            {
                _hostInactiveWarningShown = true;
                _dispatcher.Post(() => HostInactiveWarningRequested?.Invoke());
            }
            if (secondsElapsed > cfg.InactiveHostKickSeconds)
            {
                StopHostInactiveCheck();
                _dispatcher.Post(() =>
                {
                    AddRoomMessage("房主长时间无操作，房间已自动关闭");
                    LeaveGame();
                });
            }
        }

        /// <summary>房主活动上报（JS 全局 mousemove/keydown 监听回调，原版 MouseMove → Reset 语义）。</summary>
        public void ResetHostActivity()
        {
            _hostLastActivity = DateTime.UtcNow;
            _hostInactiveWarningShown = false;
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

        /// <summary>
        /// 房主手动选择隧道服务器（原版 btnChangeTunnel → TunnelSelectionWindow → TunnelSelected：
        /// 本地生效 + 频道广播 CHTNL + 重广播 GAME 使房间列表显示新隧道）。
        /// </summary>
        public void SelectTunnel(CnCNetTunnel tunnel)
        {
            if (!IsHost || _gameChannel is null || tunnel is null)
                return;
            if (_tunnelHandler.CurrentTunnel is not null &&
                string.Equals(_tunnelHandler.CurrentTunnel.Address, tunnel.Address, StringComparison.OrdinalIgnoreCase) &&
                _tunnelHandler.CurrentTunnel.Port == tunnel.Port)
                return; // 未变更

            _tunnelHandler.CurrentTunnel = tunnel;
            _gameChannel.SendCTCPMessage(
                $"{CHANGE_TUNNEL_SERVER_MESSAGE} {tunnel.Address}:{tunnel.Port}",
                QueuedMessageType.SYSTEM_MESSAGE, 10);
            _gameChannel.AddMessage(new ChatMessage($"已将隧道服务器切换为：{tunnel.Name}"));
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>加入者收到房主 CHTNL 消息：按 address:port 匹配隧道并切换（原版 HandleTunnelServerChangeMessage）。</summary>
        private void HandleTunnelServerChangeMessage(string sender, string args)
        {
            if (IsHost)
                return;
            string[] split = args.Split(':');
            if (split.Length != 2 || !int.TryParse(split[1], out int tunnelPort))
                return;

            var tunnel = _tunnelHandler.Tunnels.Find(t =>
                string.Equals(t.Address, split[0], StringComparison.OrdinalIgnoreCase) && t.Port == tunnelPort);
            if (tunnel is null)
            {
                // 房主选择了无效隧道：置错误态并提示，避免加入者误以为可参与本局
                TunnelErrorMode = true;
                _gameChannel?.AddMessage(new ChatMessage(
                    $"房主选择了无效的隧道服务器（{args}），本局游戏无法进行。"));
                NotifyChanged();
                return;
            }

            TunnelErrorMode = false;
            _tunnelHandler.CurrentTunnel = tunnel;
            _gameChannel?.AddMessage(new ChatMessage($"房主已将隧道服务器切换为：{tunnel.Name}"));
            NotifyChanged();
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
            sb.Append(';').Append(GetGameOptionsCode()); // gameOptionValues（打包复选框选项，与 GO 广播一致）

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

        /// <summary>广播 GO 消息：地图/模式/种子/游戏选项等房间配置（迁移自原版 BroadcastGameOptions）。
        /// 格式：GO 打包复选框;official;sha1;mode;fps;ahead;proto;seed;removeStart;mapName[;下拉索引…]。</summary>
        private void BroadcastGameOptions()
        {
            if (!IsHost || _gameChannel is null)
                return;
            StringBuilder sb = new StringBuilder("GO ");
            sb.Append(GetGameOptionsCode()); // 打包的复选框选项（原版 GameOptionsToCode）
            sb.Append(';').Append(Convert.ToInt32(SelectedMap?.Map.Official ?? false));
            sb.Append(';').Append(SelectedMap?.Map.SHA1 ?? string.Empty);
            sb.Append(';').Append(SelectedMap?.GameMode.Name ?? string.Empty);
            sb.Append(";0;0;0"); // FrameSendRate / MaxAhead / ProtocolVersion
            sb.Append(';').Append(RandomSeed); // RandomSeed（建房时生成，稳定复用）
            sb.Append(";0"); // RemoveStartingLocations
            sb.Append(';').Append(SelectedMap?.Map.UntranslatedName ?? string.Empty);
            // 下拉框选中索引追加在末尾（2-4：GO 协议扩展；与 HandleGameOptions 对称解析）
            foreach (GameSessionDropDownOption dd in GameOptions.OfType<GameSessionDropDownOption>())
                sb.Append(';').Append(dd.SelectedIndex);
            _gameChannel.SendCTCPMessage(sb.ToString(), QueuedMessageType.GAME_SETTINGS_MESSAGE, 11);
        }

        /// <summary>广播 PO 消息：全部玩家（人类 + AI）的队伍/出生点/颜色/阵营/就绪
        /// （迁移自原版 BroadcastPlayerOptions；AI 条目为 AILevel;options 两字段，人类三字段）。</summary>
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
            foreach (var ai in _aiPlayers)
            {
                // AI：名称位填难度编号（原版 AILevelToName 逆映射），无 ready 字段（恒就绪）
                sb.Append(ai.AILevel).Append(';');
                byte[] packed = { (byte)ai.TeamId, (byte)ai.StartingLocation, (byte)ai.ColorId, (byte)ai.SideId };
                sb.Append(BinaryPrimitives.ReadInt32LittleEndian(packed)).Append(';');
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

                // 总槽位超限时移除末尾 AI（原版 Channel_UserAdded 语义：AI 槽位让位给真人）
                if (TotalSlots > MaxTotalSlots && _aiPlayers.Count > 0)
                    _aiPlayers.RemoveAt(_aiPlayers.Count - 1);

                if (IsHost && IsJoined)
                {
                    // 房主：新玩家加入 → 下发当前地图/设置/全部玩家配置/附加选项
                    BroadcastGameOptions();
                    BroadcastGameLobbySettings();
                    BroadcastPlayerOptions();
                    BroadcastPlayerExtraOptions();
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
                case "PEO": HandlePlayerExtraOptions(e.UserName, args); break;
                case "GO": HandleGameOptions(e.UserName, args); break;
                case "GSETTINGS": HandleGameLobbySettings(e.UserName, args); break;
                case "START": HandleStartMessage(e.UserName, args); break;
                case "STRTD": _gameChannel?.AddMessage(new ChatMessage("游戏已开始")); break;
                case DICE_ROLL_MESSAGE: HandleDiceRollResult(e.UserName, args); break;
                case CHANGE_TUNNEL_SERVER_MESSAGE: HandleTunnelServerChangeMessage(e.UserName, args); break;
                case "FHSH": break; // 文件哈希校验：M3.6/M3.7 接入 FileHashCalculator
                case "TNLPNG": break; // 隧道延迟：M3.5 未接隧道
                // 地图共享（P1 地图下载）
                case "MAPREQ": HandleMapUploadRequest(e.UserName, args); break;
                case "MAPOK": HandleMapDownloadRequest(e.UserName, args); break;
                case "MAPFAIL": HandleMapTransferFailMessage(e.UserName, args); break;
                case "MAPSDISABLED": HandleMapSharingBlockedMessage(e.UserName); break;
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
                BroadcastPlayerExtraOptions();
                return;
            }

            if (!_players.TryGetValue(sender, out var p))
                return;
            Span<byte> bytes = stackalloc byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, options);
            p.Side = Math.Clamp((int)bytes[0], 0, Math.Max(0, SideCount));
            p.Color = Math.Clamp((int)bytes[1], 0, Math.Max(0, MultiplayerColor.LoadColors().Count));
            p.Start = Math.Clamp((int)bytes[2], 0, MaxTotalSlots);
            p.Team = Math.Clamp((int)bytes[3], 0, 4);

            // 观战者无出生点（与 ChangeOwnOptions 一致）
            if (IsSpectatorSide(p.Side, SideCount))
                p.Start = 0;

            BroadcastPlayerOptions();
            NotifyChanged();
        }

        private void HandlePlayerOptions(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            _players.Clear();
            _aiPlayers.Clear();
            string[] parts = args.Split(';', StringSplitOptions.RemoveEmptyEntries);
            Span<byte> bytes = stackalloc byte[4];
            for (int i = 0; i < parts.Length;)
            {
                if (parts.Length <= i + 1)
                    break;
                string name = parts[i];
                if (!int.TryParse(parts[i + 1], out int options))
                {
                    i += 3;
                    continue;
                }

                BinaryPrimitives.WriteInt32LittleEndian(bytes, options);

                // AI 条目：名称为难度编号（AILevel;options 两字段，无 ready；原版 AILevelToName 逆映射）
                if (int.TryParse(name, out int aiLevel))
                {
                    int level = Math.Clamp(aiLevel, 0, ProgramConstants.AI_PLAYER_NAMES.Count - 1);
                    _aiPlayers.Add(new PlayerInfo(
                        ProgramConstants.AI_PLAYER_NAMES[level],
                        Math.Clamp((int)bytes[3], 0, Math.Max(0, SideCount - 1)),
                        bytes[1],
                        Math.Clamp((int)bytes[2], 0, Math.Max(0, MultiplayerColor.LoadColors().Count)),
                        Math.Clamp((int)bytes[0], 0, 4))
                    {
                        IsAI = true,
                        AILevel = level,
                        Ready = true,
                        Index = _aiPlayers.Count
                    });
                    i += 2;
                    continue;
                }

                if (parts.Length <= i + 2)
                    break;
                int ready = int.TryParse(parts[i + 2], out var r) ? r : 0;

                var state = new RoomPlayerState
                {
                    Name = name,
                    IsHost = string.Equals(name, HostName, StringComparison.OrdinalIgnoreCase),
                    Team = Math.Clamp((int)bytes[0], 0, 4),
                    Start = Math.Clamp((int)bytes[1], 0, MaxTotalSlots),
                    Color = Math.Clamp((int)bytes[2], 0, Math.Max(0, MultiplayerColor.LoadColors().Count)),
                    Side = Math.Clamp((int)bytes[3], 0, Math.Max(0, SideCount)),
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

        /// <summary>处理房主的 PEO 附加选项广播（仅加入者应用，同原版 ApplyPlayerExtraOptions）。</summary>
        private void HandlePlayerExtraOptions(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            try
            {
                _extraOptions = PlayerExtraOptions.FromMessage(args);
            }
            catch
            {
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Red, "收到无效的玩家附加选项数据。"));
                return;
            }

            PlayerExtraOptionsChanged?.Invoke();
            NotifyChanged();
        }

        private void HandleGameOptions(string sender, string args)
        {
            if (IsHost || sender != HostName)
                return;

            // GO 格式（与 BroadcastGameOptions 对称）：
            // 0(打包复选框);official;sha1;mode;fps;ahead;proto;seed;removeStart;mapName[;下拉索引…]
            string[] parts = args.Split(';');
            if (parts.Length < 9)
                return;

            // 2-4：应用房主打包的复选框选项（位序与 CreateDefaultGameOptions 一致）
            if (int.TryParse(parts[0], out int optionCode))
                ApplyGameOptionsCode(optionCode);

            string mapHash = parts[2];
            string gameMode = parts[3];
            if (int.TryParse(parts[7], out int seed))
                RandomSeed = seed;

            // 2-4：下拉框选中索引（房主新格式追加在末尾；旧广播缺省时保留当前值）
            var dropdowns = GameOptions.OfType<GameSessionDropDownOption>().ToList();
            for (int i = 0; i < dropdowns.Count; i++)
            {
                int fieldIndex = 10 + i;
                if (parts.Length > fieldIndex && int.TryParse(parts[fieldIndex], out int index))
                    dropdowns[i].SelectedIndex = dropdowns[i].Items.Count > 0
                        ? Math.Clamp(index, 0, dropdowns[i].Items.Count - 1)
                        : 0;
            }
            NotifyChanged(); // 游戏选项/种子变化驱动 UI 刷新（缺图分支也需更新选项面板）

            var gmm = _mapLoader.GameModeMaps.FirstOrDefault(g =>
                string.Equals(g.GameMode.Name, gameMode, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.Map.SHA1, mapHash, StringComparison.OrdinalIgnoreCase));
            SelectedMap = gmm;
            if (gmm is null && !string.IsNullOrEmpty(mapHash))
            {
                // P1 地图下载：本机缺少房主所选地图 → 记录共享状态并发起缺图流程（下载提示 / MAPSDISABLED）
                string mapName = parts.Length > 9 && parts[9].Length > 0 ? parts[9] : mapHash;
                _lastMapSHA1 = mapHash;
                _lastMapName = mapName;
                _gameChannel?.AddMessage(new ChatMessage(ColorConstants.Yellow,
                    "主机选择了本机未安装的地图，地图名： " + mapName));
                RequestMissingMap(mapHash, mapName);
            }
            else
            {
                // 地图已具备（或已下载自动选中）→ 清除缺图状态
                MissingMapSHA1 = null;
                MissingMapName = null;
                DownloadState = MapDownloadState.None;
                NotifyChanged();
            }
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
        /// 将房间内人类玩家状态映射为 <see cref="PlayerInfo"/> 列表，供 spawn.ini 生成。
        /// 端口来自 START 广播分配（房主分配 / 加入者解析），Ip 无隧道恒为 0.0.0.0。
        /// AI 玩家不在此列，生成时经 <see cref="AIPlayers"/> 单独传入（AI 无端口/隧道）。
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
            _mapLoader.MapChanged -= OnMapLoaderMapChanged;
            MapSharer.MapDownloadFailed -= OnMapSharerDownloadFailed;
            MapSharer.MapDownloadComplete -= OnMapSharerDownloadComplete;
            MapSharer.MapUploadFailed -= OnMapSharerUploadFailed;
            MapSharer.MapUploadComplete -= OnMapSharerUploadComplete;
            StopBroadcastTimer();
            StopHostInactiveCheck();
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

    /// <summary>加入者视角缺图下载状态（驱动房间内地图下载提示条）。</summary>
    public enum MapDownloadState
    {
        /// <summary>无缺图（地图已具备或房主未选图）。</summary>
        None,

        /// <summary>缺图且地图共享已启用，等待用户确认下载。</summary>
        Prompt,

        /// <summary>正在从 CnCNet 地图库下载。</summary>
        Downloading,

        /// <summary>下载/传输失败（可重试或等待房主更换地图）。</summary>
        Failed,

        /// <summary>下载完成并已自动选中。</summary>
        Complete,

        /// <summary>地图共享已禁用，无法下载。</summary>
        Disabled
    }
}
