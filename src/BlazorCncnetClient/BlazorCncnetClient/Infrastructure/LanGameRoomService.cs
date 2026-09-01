using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using CnCNetClient.Core;
using CnCNetClient.Domain.LAN;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Domain.Multiplayer.LAN;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Integration.XnaCompat;
using CnCNetClient.Online;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// LAN 游戏房间服务（M4.4 D3.2）：TCP 1233 大厅通信 + GAME/OPTS/POPTS/POREQ/READY/GETREADY/LAUNCH 协议。
    /// 移植原版 <c>LANGameLobby</c> 逻辑部分（去 XNA），架构对齐在线 <see cref="GameRoomService"/>：
    /// 房主 TCP 监听并广播房间配置，加入者 TCP 连接并按主机下发的配置渲染。
    /// GAME / GAMECLOSED 经 <see cref="LanLobbyService.BroadcastRaw"/> 由大厅 UDP 广播转发。
    /// </summary>
    public sealed class LanGameRoomService : IDisposable
    {
        // ── 命令常量（与原版 LANGameLobby 一致） ──
        private const string JOIN_COMMAND = "JOIN";
        private const string CHAT_COMMAND = "GLCHAT";
        private const string RETURN_COMMAND = "RETURN";
        private const string PLAYER_OPTIONS_REQUEST_COMMAND = "POREQ";
        private const string PLAYER_QUIT_COMMAND = "QUIT";
        private const string PLAYER_READY_REQUEST = "READY";
        private const string FILE_HASH_COMMAND = "FHASH";
        private const string GET_READY_COMMAND = "GETREADY";
        private const string PLAYER_OPTIONS_BROADCAST_COMMAND = "POPTS";
        private const string LAUNCH_GAME_COMMAND = "LAUNCH";
        private const string GAME_OPTIONS_COMMAND = "OPTS";
        private const string PING_COMMAND = "PING";

        private const int MAX_PLAYER_COUNT = 8;
        private const double GAME_BROADCAST_INTERVAL_SECONDS = 2.0;
        private const double TICK_INTERVAL_SECONDS = 1.0;
        private const double CLIENT_DROPOUT_TIMEOUT_SECONDS = 20.0;
        private const int MAX_CHAT_MESSAGES = 200;

        /// <summary>LAN 房间最大玩家数（供 UI 展示）。</summary>
        public const int MaxPlayersConst = MAX_PLAYER_COUNT;

        /// <summary>房间状态变化（玩家/地图/配置/就绪/聊天），驱动 UI 刷新。</summary>
        public event Action? Changed;

        /// <summary>开始游戏请求（GameLaunchService 接此生成 spawn.ini 并启动游戏进程）。</summary>
        public event Action? GameStartRequested;

        /// <summary>离开房间事件（主动/断线/超时），供页面跳回大厅。</summary>
        public event Action? Left;

        private readonly LanLobbyService _lobby;
        private readonly MapLoader _mapLoader;
        private readonly GameCollection _gameCollection;
        private readonly IDispatcher _dispatcher;
        private readonly Random _random;

        private readonly List<LANPlayerInfo> _players = new();
        private readonly List<ChatMessage> _chatMessages = new();

        private LANServerCommandHandler[] _serverHandlers;
        private LANClientCommandHandler[] _clientHandlers;

        private TcpListener? _listener;
        private TcpClient? _client;
        private NetworkStream? _clientStream;
        private string _overMessage = string.Empty;
        private Timer? _broadcastTimer;
        private Timer? _tickTimer;
        private int _sessionId;
        private bool _leaving;
        private bool _disposed;
        private string _localFileHash = string.Empty;
        private IPEndPoint? _hostEndPoint;
        private double _timeSinceLastReceivedCommand;

        public CnCNetGame? LocalGame { get; }

        /// <summary>当前是否房主。</summary>
        public bool IsHost { get; private set; }

        /// <summary>是否已进入房间（连接已建立）。</summary>
        public bool IsJoined { get; private set; }

        public string RoomName { get; private set; } = string.Empty;
        public int MaxPlayers { get; private set; } = MAX_PLAYER_COUNT;
        public bool Locked { get; private set; }
        public GameModeMap? SelectedMap { get; private set; }
        public int RandomSeed { get; private set; }
        public int UniqueGameId { get; private set; }

        public IReadOnlyList<LANPlayerInfo> Players => _players;
        public IReadOnlyList<ChatMessage> ChatMessages => _chatMessages;

        /// <summary>房主名（玩家列表首项，GAME 广播首元素亦为主机名）。</summary>
        public string HostName => _players.Count > 0 ? _players[0].Name : string.Empty;

        public bool IsInGameRoom => _client is not null || _listener is not null;

        public LanGameRoomService(
            LanLobbyService lobby,
            MapLoader mapLoader,
            GameCollection gameCollection,
            IDispatcher dispatcher,
            Random random)
        {
            _lobby = lobby;
            _mapLoader = mapLoader;
            _gameCollection = gameCollection;
            _dispatcher = dispatcher;
            _random = random;

            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));

            // 主机侧处理器：接收来自玩家的命令（Server* 传完整消息，需按命令名剥离参数）
            _serverHandlers =
            [
                new ServerStringCommandHandler(CHAT_COMMAND, Host_HandleChat),
                new ServerNoParamCommandHandler(RETURN_COMMAND, Host_HandleReturn),
                new ServerStringCommandHandler(PLAYER_OPTIONS_REQUEST_COMMAND, Host_HandleOptionsRequest),
                new ServerNoParamCommandHandler(PLAYER_QUIT_COMMAND, Host_HandlePlayerQuit),
                new ServerStringCommandHandler(PLAYER_READY_REQUEST, Host_HandleReadyRequest),
                new ServerStringCommandHandler(FILE_HASH_COMMAND, Host_HandleFileHash),
                new ServerNoParamCommandHandler(PING_COMMAND, _ => { }), // 心跳保活，空处理
            ];

            // 客户端侧处理器：接收来自主机的命令（Client* 已剥离命令名前缀）
            _clientHandlers =
            [
                new ClientStringCommandHandler(CHAT_COMMAND, Client_HandleChat),
                new ClientNoParamCommandHandler(GET_READY_COMMAND, Client_HandleGetReady),
                new ClientNoParamCommandHandler(PLAYER_QUIT_COMMAND, Client_HandleHostQuit),
                new ClientStringCommandHandler(RETURN_COMMAND, Client_HandleReturn),
                new ClientStringCommandHandler(PLAYER_OPTIONS_BROADCAST_COMMAND, Client_HandlePlayerOptions),
                new ClientStringCommandHandler(LAUNCH_GAME_COMMAND, Client_HandleGameLaunch),
                new ClientStringCommandHandler(GAME_OPTIONS_COMMAND, Client_HandleGameOptions),
                new ClientNoParamCommandHandler(PING_COMMAND, Client_HandlePing),
            ];
        }

        // ── 建房间 / 加入 / 离开 ──

        /// <summary>创建 LAN 房间：TCP 监听 1233、主机自连、计算文件哈希、周期广播 GAME。</summary>
        public void CreateGame(string roomName)
        {
            if (IsInGameRoom)
                throw new InvalidOperationException("已在 LAN 房间中，请先离开当前房间");

            _leaving = false;
            _sessionId++;
            ResetState();
            IsHost = true;
            RoomName = string.IsNullOrWhiteSpace(roomName) ? ProgramConstants.PLAYERNAME + "'s Game" : roomName.Trim();
            MaxPlayers = MAX_PLAYER_COUNT;
            RandomSeed = _random.Next();
            Locked = false;

            try
            {
                _listener = new TcpListener(IPAddress.Any, ProgramConstants.LAN_GAME_LOBBY_PORT);
                _listener.Start();
            }
            catch (Exception ex)
            {
                _listener = null;
                throw new InvalidOperationException($"TCP {ProgramConstants.LAN_GAME_LOBBY_PORT} 监听失败：{ex.Message}", ex);
            }

            var listenerThread = new Thread(ListenForClients) { IsBackground = true };
            listenerThread.Start();

            // 主机以 JOIN 自连本机（同原版），走统一客户端流程
            try
            {
                _client = new TcpClient();
                _client.Connect(IPAddress.Loopback.ToString(), ProgramConstants.LAN_GAME_LOBBY_PORT);
            }
            catch (Exception ex)
            {
                _listener.Stop();
                _listener = null;
                throw new InvalidOperationException("连接本机 LAN 大厅失败：" + ex.Message, ex);
            }

            _clientStream = _client.GetStream();
            IsJoined = true;
            SendToHost(JOIN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR + ProgramConstants.PLAYERNAME);
            StartReceiveLoop();
            CalculateFileHash();
            StartBroadcastTimer();
            StartTickTimer();

            AddChatMessage(new ChatMessage(ColorConstants.Cyan,
                $"已创建 LAN 房间「{RoomName}」，等待其他玩家加入"));
            NotifyChanged();
        }

        /// <summary>加入 LAN 房间：TCP 连接主机 1233、发 JOIN、计算文件哈希（迁移自原版 LANLobby 双击加入）。</summary>
        public void JoinGame(HostedLANGame game)
        {
            if (IsInGameRoom)
                throw new InvalidOperationException("已在 LAN 房间中，请先离开当前房间");

            if (game.Locked)
                throw new InvalidOperationException("该房间已锁定，无法加入");
            if (game.Game is null || !string.Equals(game.Game.InternalName,
                    ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase))
                throw new InvalidOperationException("该房间的游戏与本机不同，无法加入");

            _leaving = false;
            _sessionId++;
            ResetState();
            IsHost = false;
            RoomName = game.RoomName;
            MaxPlayers = MAX_PLAYER_COUNT;
            _hostEndPoint = game.EndPoint;

            try
            {
                _client = new TcpClient(game.EndPoint.Address.ToString(), ProgramConstants.LAN_GAME_LOBBY_PORT);
            }
            catch (Exception ex)
            {
                _client = null;
                throw new InvalidOperationException("无法连接主机 " + game.EndPoint.Address + "：" + ex.Message, ex);
            }

            _clientStream = _client.GetStream();
            IsJoined = true;
            SendToHost(JOIN_COMMAND + ProgramConstants.LAN_DATA_SEPARATOR + ProgramConstants.PLAYERNAME);
            StartReceiveLoop();
            CalculateFileHash();
            StartTickTimer();

            AddChatMessage(new ChatMessage(ColorConstants.Cyan, $"正在加入「{game.RoomName}」..."));
            NotifyChanged();
        }

        /// <summary>离开房间：主机广播 GAMECLOSED + QUIT，客户端发 QUIT，关闭监听/连接并清理。</summary>
        public void LeaveGame()
        {
            if (_leaving)
                return;
            _leaving = true;
            _sessionId++;

            try
            {
                if (IsHost)
                {
                    _lobby.BroadcastRaw("GAMECLOSED");
                    BroadcastMessage(PLAYER_QUIT_COMMAND);
                }
                else
                {
                    SendToHost(PLAYER_QUIT_COMMAND);
                }
            }
            catch
            {
                // 忽略发送失败（房间已关闭等）
            }

            CleanUp();
            IsHost = false;
            IsJoined = false;
            ResetState();
            NotifyChanged();
            Left?.Invoke();
        }

        // ── 房间内交互 ──

        /// <summary>房间聊天发送（主机转发 / 客户端发主机）。颜色沿用大厅聊天颜色偏好。</summary>
        public void SendRoomChat(string text)
        {
            if (!IsJoined || string.IsNullOrWhiteSpace(text))
                return;
            string chat = text.Trim().Replace(ProgramConstants.LAN_DATA_SEPARATOR, '?');
            SendToHost(CHAT_COMMAND + " " + _lobby.ChatColorIndex
                + ProgramConstants.LAN_DATA_SEPARATOR + chat);
        }

        /// <summary>房主切换地图/模式，广播 OPTS + GAME。</summary>
        public void ChangeMap(GameModeMap gameModeMap)
        {
            if (!IsHost)
                return;
            if (SelectedMap == gameModeMap)
                return;
            SelectedMap = gameModeMap;
            OnGameOptionChanged();
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>房主锁定/解锁房间（锁定后新连接被拒绝）。</summary>
        public void ToggleLock()
        {
            if (!IsHost)
                return;
            Locked = !Locked;
            BroadcastGame();
            NotifyChanged();
        }

        /// <summary>修改自己的阵营/颜色/出生点/队伍：房主直接改并广播 POPTS，加入者发 POREQ 请求主机校验后统一下发。</summary>
        public void ChangeOwnOptions(int side, int color, int start, int team)
        {
            if (!IsJoined)
                return;
            var self = _players.FirstOrDefault(p => p.Name == ProgramConstants.PLAYERNAME);
            if (self is null)
                return;

            if (IsHost)
            {
                self.SideId = side;
                self.ColorId = color;
                self.StartingLocation = start;
                self.TeamId = team;
                ClearReadyStatuses();
                BroadcastPlayerOptions();
                NotifyChanged();
            }
            else
            {
                var sb = new StringBuilder(PLAYER_OPTIONS_REQUEST_COMMAND + " ");
                sb.Append(side).Append(ProgramConstants.LAN_DATA_SEPARATOR)
                  .Append(color).Append(ProgramConstants.LAN_DATA_SEPARATOR)
                  .Append(start).Append(ProgramConstants.LAN_DATA_SEPARATOR)
                  .Append(team);
                SendToHost(sb.ToString());
            }
        }

        /// <summary>切换自己的就绪状态：房主直接改并广播，加入者发 READY 上报主机。</summary>
        public void ToggleReady()
        {
            if (!IsJoined)
                return;
            var self = _players.FirstOrDefault(p => p.Name == ProgramConstants.PLAYERNAME);
            if (self is null)
                return;

            self.Ready = !self.Ready;
            if (IsHost)
            {
                BroadcastPlayerOptions();
            }
            else
            {
                SendToHost(PLAYER_READY_REQUEST + " " + Convert.ToInt32(self.Ready));
            }
            NotifyChanged();
        }

        /// <summary>房主开始游戏：校验（锁定/选图/全员就绪）→ 生成唯一 GameID → 广播 LAUNCH → 触发启动。</summary>
        public void StartGame()
        {
            if (!IsHost || !IsJoined)
                return;
            if (SelectedMap is null)
                throw new InvalidOperationException("请先选择地图");
            if (!Locked)
                throw new InvalidOperationException("请先锁定房间再开始游戏");
            if (_players.Any(p => !p.Ready))
                throw new InvalidOperationException("仍有玩家未就绪");

            GenerateGameId();
            BroadcastMessage(LAUNCH_GAME_COMMAND + " " + UniqueGameId);
            GameStartRequested?.Invoke();
        }

        /// <summary>将房间内玩家状态映射为 <see cref="PlayerInfo"/> 列表，供 spawn.ini 生成。
        /// LAN 下无隧道：IP 为玩家真实地址（客户端解析 POPTS 时已替换主机 127.0.0.1），端口固定 1234。</summary>
        public List<PlayerInfo> GetPlayerInfos()
        {
            var result = new List<PlayerInfo>(_players.Count);
            foreach (var p in _players)
            {
                result.Add(new PlayerInfo
                {
                    Name = p.Name,
                    SideId = p.SideId,
                    ColorId = p.ColorId,
                    StartingLocation = p.StartingLocation,
                    TeamId = p.TeamId,
                    Ready = p.Ready,
                    IPAddress = p.IPAddress,
                    Port = p.Port
                });
            }
            return result;
        }

        /// <summary>本局阵营数量（spawn.ini 地图强制选项用，取 Sides 逗号分隔数量）。</summary>
        public int SideCount =>
            Math.Max(1, ClientConfiguration.Instance.Sides.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

        /// <summary>地图预览上的出生点（百分比坐标 + 当前占用者），与在线房间一致。</summary>
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

            int shown = Math.Min(coords.Count, MaxPlayers);
            for (int i = 0; i < shown; i++)
            {
                var occupant = _players.FirstOrDefault(p => p.StartingLocation == i + 1);
                result.Add(new SpawnPointState(
                    i + 1,
                    Math.Clamp(coords[i].X / 1000.0, 0.0, 1.0),
                    Math.Clamp(coords[i].Y / 1000.0, 0.0, 1.0),
                    occupant?.Name,
                    occupant?.ColorId ?? -1,
                    occupant is not null && occupant.Name == ProgramConstants.PLAYERNAME));
            }
            return result;
        }

        // ── 主机：TCP 监听 / 玩家接入 ──

        private void ListenForClients()
        {
            var listener = _listener;
            if (listener is null)
                return;

            try
            {
                while (!_leaving)
                {
                    TcpClient client;
                    try
                    {
                        client = listener.AcceptTcpClient();
                    }
                    catch
                    {
                        break; // 监听已停止
                    }

                    if (_players.Count >= MAX_PLAYER_COUNT || Locked)
                    {
                        client.Close();
                        continue;
                    }

                    var lpInfo = new LANPlayerInfo(ProgramConstants.LAN_ENCODING);
                    try
                    {
                        lpInfo.SetClient(client);
                    }
                    catch
                    {
                        client.Close();
                        continue;
                    }

                    // 首条消息必须为 JOIN <name>（同原版 HandleClientConnection）
                    string? joinMessage = ReadFirstMessage(lpInfo);
                    if (joinMessage is null)
                    {
                        client.Close();
                        continue;
                    }

                    string[] parts = joinMessage.Split(ProgramConstants.LAN_DATA_SEPARATOR);
                    if (parts.Length != 2 || parts[0] != JOIN_COMMAND || string.IsNullOrWhiteSpace(parts[1].Trim()))
                    {
                        client.Close();
                        continue;
                    }

                    string name = parts[1].Trim();
                    if (_players.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                    {
                        client.Close();
                        continue;
                    }

                    lpInfo.Name = name;
                    int session = _sessionId;
                    _dispatcher.Post(() =>
                    {
                        if (session != _sessionId || _leaving)
                            return;
                        AddPlayer(lpInfo);
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Log("LAN listener error: " + ex);
            }
        }

        private static string? ReadFirstMessage(LANPlayerInfo lpInfo)
        {
            byte[] buffer = new byte[1024];
            try
            {
                int bytesRead = lpInfo.TcpClient.GetStream().Read(buffer, 0, buffer.Length);
                if (bytesRead <= 0)
                    return null;
                string data = ProgramConstants.LAN_ENCODING.GetString(buffer, 0, bytesRead);
                int sep = data.IndexOf(ProgramConstants.LAN_MESSAGE_SEPARATOR);
                return sep >= 0 ? data.Substring(0, sep) : data;
            }
            catch
            {
                return null;
            }
        }

        private void AddPlayer(LANPlayerInfo pInfo)
        {
            if (_leaving)
                return;
            if (_players.Any(p => string.Equals(p.Name, pInfo.Name, StringComparison.OrdinalIgnoreCase)))
            {
                try { pInfo.TcpClient.Close(); } catch { }
                return;
            }

            _players.Add(pInfo);
            if (IsHost && _players.Count == 1)
                _players[0].Ready = true; // 主机默认就绪

            pInfo.MessageReceived += OnPlayerMessageReceived;
            pInfo.ConnectionLost += OnPlayerConnectionLost;
            pInfo.StartReceiveLoop();

            AddChatMessage(new ChatMessage(ColorConstants.Cyan, pInfo.Name + " 加入了房间"));
            OnGameOptionChanged();    // 下发当前地图/规则
            BroadcastPlayerOptions(); // 下发全部玩家配置
            BroadcastGame();          // UDP GAME 刷新房间列表
            NotifyChanged();
        }

        private void OnPlayerMessageReceived(object? sender, NetworkMessageEventArgs e)
        {
            if (sender is not LANPlayerInfo pInfo)
                return;
            pInfo.TimeSinceLastReceivedMessage = TimeSpan.Zero;
            _dispatcher.Post(() => Host_HandleServerCommand(pInfo, e.Message));
        }

        private void OnPlayerConnectionLost(object? sender, EventArgs e)
        {
            if (sender is not LANPlayerInfo pInfo)
                return;
            _dispatcher.Post(() =>
            {
                if (_leaving)
                    return;
                AddChatMessage(new ChatMessage(ColorConstants.Red, pInfo.Name + " 已断开连接"));
                RemovePlayer(pInfo);
            });
        }

        private void RemovePlayer(LANPlayerInfo pInfo)
        {
            if (_leaving)
                return;
            if (!_players.Remove(pInfo))
                return;
            try { pInfo.TcpClient.Close(); } catch { }

            AddChatMessage(new ChatMessage(ColorConstants.Cyan, pInfo.Name + " 离开了房间"));
            ClearReadyStatuses();
            BroadcastPlayerOptions();
            BroadcastGame();
            NotifyChanged();
        }

        // ── 主机：命令处理 ──

        private void Host_HandleServerCommand(LANPlayerInfo pInfo, string message)
        {
            if (_leaving || !IsHost)
                return;
            foreach (var handler in _serverHandlers)
            {
                if (handler.Handle(pInfo, message))
                    return;
            }
        }

        /// <summary>主机转发玩家聊天：GLCHAT {sender}\x01{color}\x01{text}。</summary>
        private void Host_HandleChat(LANPlayerInfo pInfo, string message)
        {
            string args = ArgsAfter(message, CHAT_COMMAND);
            string[] parts = args.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length < 2)
                return;

            BroadcastMessage(CHAT_COMMAND + " " + pInfo.Name
                + ProgramConstants.LAN_DATA_SEPARATOR + parts[0]
                + ProgramConstants.LAN_DATA_SEPARATOR + parts[1]);
        }

        /// <summary>玩家结束游戏返回房间：广播 RETURN {sender}。</summary>
        private void Host_HandleReturn(LANPlayerInfo pInfo)
            => BroadcastMessage(RETURN_COMMAND + " " + pInfo.Name);

        /// <summary>玩家请求改选项：校验范围 → 写回 → 重发 POPTS（side/start/team 变化则清就绪）。</summary>
        private void Host_HandleOptionsRequest(LANPlayerInfo pInfo, string message)
        {
            string args = ArgsAfter(message, PLAYER_OPTIONS_REQUEST_COMMAND);
            string[] parts = args.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length != 4)
                return;
            if (!int.TryParse(parts[0], out int side) || !int.TryParse(parts[1], out int color)
                || !int.TryParse(parts[2], out int start) || !int.TryParse(parts[3], out int team))
                return;

            int sideCount = Math.Max(1, ClientConfiguration.Instance.Sides.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);
            if (side < 0 || side >= sideCount || color < 0 || color >= 0x10
                || start < 0 || start > MAX_PLAYER_COUNT || team < 0 || team > 4)
                return;

            if (pInfo.SideId != side || pInfo.StartingLocation != start || pInfo.TeamId != team)
                ClearReadyStatuses();

            pInfo.SideId = side;
            pInfo.ColorId = color;
            pInfo.StartingLocation = start;
            pInfo.TeamId = team;
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        private void Host_HandlePlayerQuit(LANPlayerInfo pInfo) => RemovePlayer(pInfo);

        /// <summary>玩家上报就绪状态：READY {0/1} → 重发 POPTS。</summary>
        private void Host_HandleReadyRequest(LANPlayerInfo pInfo, string message)
        {
            string args = ArgsAfter(message, PLAYER_READY_REQUEST);
            pInfo.Ready = args == "1";
            BroadcastPlayerOptions();
            NotifyChanged();
        }

        /// <summary>玩家上报文件哈希：不一致则提示作弊嫌疑。</summary>
        private void Host_HandleFileHash(LANPlayerInfo pInfo, string message)
        {
            string args = ArgsAfter(message, FILE_HASH_COMMAND);
            if (!string.IsNullOrEmpty(_localFileHash) && !string.IsNullOrEmpty(args)
                && !string.Equals(args, _localFileHash, StringComparison.OrdinalIgnoreCase))
            {
                AddChatMessage(new ChatMessage(ColorConstants.Red,
                    pInfo.Name + " 的文件与你的不一致，可能存在作弊！"));
            }
            pInfo.HashReceived = true;
            NotifyChanged();
        }

        // ── 客户端：命令处理 ──

        private void HandleMessageFromServer(string message)
        {
            if (_leaving)
                return;
            _timeSinceLastReceivedCommand = 0;
            foreach (var handler in _clientHandlers)
            {
                if (handler.Handle(message))
                    return;
            }
        }

        private void Client_HandleChat(string args)
        {
            // args: {sender}\x01{color}\x01{text}
            string[] parts = args.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length < 3)
                return;
            if (!int.TryParse(parts[1], out int colorIdx))
                return;
            Rgba32 color = colorIdx >= 0 && colorIdx < LanLobbyService.ChatColors.Length
                ? LanLobbyService.ChatColors[colorIdx].XNAColor
                : ColorConstants.White;
            AddChatMessage(new ChatMessage(parts[0], color, DateTime.Now, parts[2]));
        }

        private void Client_HandleGetReady()
        {
            AddChatMessage(new ChatMessage(ColorConstants.Yellow, "主机正在等待你准备就绪！"));
            NotifyChanged();
        }

        private void Client_HandleHostQuit()
        {
            if (IsHost || _leaving)
                return;
            AddChatMessage(new ChatMessage(ColorConstants.Red, "主机已关闭房间"));
            LeaveGame();
        }

        /// <summary>收到 RETURN {sender}：将该玩家标记为已返回房间。</summary>
        private void Client_HandleReturn(string args)
        {
            var p = _players.FirstOrDefault(x => string.Equals(x.Name, args, StringComparison.OrdinalIgnoreCase));
            if (p is not null)
                p.IsInGame = false;
            AddChatMessage(new ChatMessage(ColorConstants.Cyan, args + " 已返回房间"));
            NotifyChanged();
        }

        /// <summary>客户端解析 POPTS：每玩家 8 字段（名字/side/color/start/team/ready/ip/aiLevel），整体重建玩家列表。</summary>
        private void Client_HandlePlayerOptions(string args)
        {
            if (IsHost)
                return; // 房主已维护真实玩家列表

            string[] parts = args.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length == 0 || parts.Length % 8 != 0)
                return;
            int count = parts.Length / 8;

            var newPlayers = new List<LANPlayerInfo>(count);
            for (int i = 0; i < count; i++)
            {
                int idx = i * 8;
                var p = new LANPlayerInfo(ProgramConstants.LAN_ENCODING)
                {
                    Name = parts[idx],
                    SideId = int.TryParse(parts[idx + 1], out int side) ? side : 0,
                    ColorId = int.TryParse(parts[idx + 2], out int color) ? color : 0,
                    StartingLocation = int.TryParse(parts[idx + 3], out int start) ? start : 0,
                    TeamId = int.TryParse(parts[idx + 4], out int team) ? team : 0,
                    Ready = int.TryParse(parts[idx + 5], out int ready) && ready > 0,
                    Port = ProgramConstants.LAN_INGAME_PORT,
                };

                string ip = parts[idx + 6];
                if (string.Equals(ip, "127.0.0.1") && _hostEndPoint is not null)
                    ip = _hostEndPoint.Address.ToString(); // 主机条目 127.0.0.1 → 真实主机 IP
                p.IPAddress = ip;

                int aiLevel = int.TryParse(parts[idx + 7], out int ai) ? ai : -1;
                p.IsAI = aiLevel > -1;
                p.AILevel = aiLevel;
                newPlayers.Add(p);
            }

            _players.Clear();
            _players.AddRange(newPlayers);
            NotifyChanged();
        }

        /// <summary>客户端收到 LAUNCH {gameId}：记录唯一 GameID → 标记全员已进入游戏 → 触发启动。</summary>
        private void Client_HandleGameLaunch(string args)
        {
            if (IsHost)
                return; // 房主已在 StartGame 触发

            if (int.TryParse(args, out int id))
                UniqueGameId = id;
            foreach (var p in _players)
                p.IsInGame = true;
            AddChatMessage(new ChatMessage(ColorConstants.Cyan, "主机已开始游戏"));
            GameStartRequested?.Invoke();
        }

        /// <summary>客户端解析 OPTS：末尾字段（seed/mapSHA1/mode/frameSendRate/removeStart），找不到地图则提示。</summary>
        private void Client_HandleGameOptions(string args)
        {
            if (IsHost)
                return; // 房主已直接设置 SelectedMap

            string[] parts = args.Split(ProgramConstants.LAN_DATA_SEPARATOR);
            if (parts.Length < 5)
                return;

            RandomSeed = int.TryParse(parts[0], out int seed) ? seed : RandomSeed;
            string mapHash = parts[1];
            string modeName = parts[2];

            var gmm = _mapLoader.GameModeMaps.FirstOrDefault(g =>
                string.Equals(g.GameMode.Name, modeName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(g.Map.SHA1, mapHash, StringComparison.OrdinalIgnoreCase));

            SelectedMap = gmm;
            if (gmm is null && !string.IsNullOrEmpty(mapHash))
            {
                AddChatMessage(new ChatMessage(ColorConstants.Yellow,
                    "主机选择了本机未安装的地图（SHA1: " + mapHash + "）"));
            }
            NotifyChanged();
        }

        private void Client_HandlePing() => SendToHost(PING_COMMAND);

        // ── 主机 → 房间广播（OPTS / POPTS / LAUNCH / 聊天） ──

        private void BroadcastMessage(string message)
        {
            foreach (var p in _players)
                p.SendMessage(message);
        }

        /// <summary>广播 OPTS：seed\x01mapSHA1\x01modeName\x01frameSendRate\x01removeStartLocations。</summary>
        private void OnGameOptionChanged()
        {
            if (!IsHost)
                return;
            var sb = new StringBuilder(GAME_OPTIONS_COMMAND + " ");
            sb.Append(RandomSeed)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(SelectedMap?.Map.SHA1 ?? string.Empty)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(SelectedMap?.GameMode.Name ?? string.Empty)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append('0') // FrameSendRate
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append('0'); // RemoveStartingLocations
            BroadcastMessage(sb.ToString());
        }

        /// <summary>广播 POPTS：每玩家 8 字段（名字/side/color/start/team/ready/ip/aiLevel）。</summary>
        private void BroadcastPlayerOptions()
        {
            if (!IsHost)
                return;
            var sb = new StringBuilder(PLAYER_OPTIONS_BROADCAST_COMMAND + " ");
            for (int i = 0; i < _players.Count; i++)
            {
                if (i > 0)
                    sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
                var p = _players[i];
                sb.Append(p.Name)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.SideId)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.ColorId)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.StartingLocation)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.TeamId)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(Convert.ToInt32(p.Ready))
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.IPAddress)
                  .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(p.IsAI ? p.AILevel : -1);
            }
            BroadcastMessage(sb.ToString());
        }

        /// <summary>UDP 广播 GAME（经大厅转发）：RL8\x01version\x01game\x01map\x01mode\x01loadedId\x01players\x01locked\x01isLoaded\x01mapHash。</summary>
        private void BroadcastGame()
        {
            if (!IsHost || LocalGame is null)
                return;
            var sb = new StringBuilder("GAME ");
            sb.Append(ProgramConstants.LAN_PROTOCOL_REVISION)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(ProgramConstants.GAME_VERSION)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(LocalGame.InternalName)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(SelectedMap?.Map.UntranslatedName ?? string.Empty)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(SelectedMap?.GameMode.UntranslatedUIName ?? string.Empty)
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append('0') // LoadedGameID
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(string.Join(",", _players.Select(p => p.Name)))
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(Convert.ToInt32(Locked))
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append('0') // IsLoadedGame
              .Append(ProgramConstants.LAN_DATA_SEPARATOR).Append(SelectedMap?.Map.SHA1 ?? string.Empty);
            _lobby.BroadcastRaw(sb.ToString());
        }

        // ── 周期任务 ──

        private void StartBroadcastTimer()
        {
            StopBroadcastTimer();
            _broadcastTimer = new Timer(_ => _dispatcher.Post(BroadcastGame), null,
                TimeSpan.FromMilliseconds(200), TimeSpan.FromSeconds(GAME_BROADCAST_INTERVAL_SECONDS));
        }

        private void StopBroadcastTimer()
        {
            _broadcastTimer?.Dispose();
            _broadcastTimer = null;
        }

        private void StartTickTimer()
        {
            StopTickTimer();
            _tickTimer = new Timer(_ => Tick(), null,
                TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS), TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS));
        }

        private void StopTickTimer()
        {
            _tickTimer?.Dispose();
            _tickTimer = null;
        }

        private void Tick()
        {
            if (_leaving)
                return;

            if (IsHost)
            {
                // 主机：心跳检查（跳过自身索引 0，PING 10s / DROP 20s 阈值）
                for (int i = _players.Count - 1; i >= 1; i--)
                {
                    var p = _players[i];
                    if (!p.Update(TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS)))
                        _dispatcher.Post(() => RemovePlayer(p));
                }
            }
            else
            {
                // 客户端：20s 未收到任何主机命令 → 判定掉线
                _timeSinceLastReceivedCommand += TICK_INTERVAL_SECONDS;
                if (_timeSinceLastReceivedCommand > CLIENT_DROPOUT_TIMEOUT_SECONDS)
                {
                    _dispatcher.Post(() =>
                    {
                        AddChatMessage(new ChatMessage(ColorConstants.Red, "连接超时，正在离开房间"));
                        LeaveGame();
                    });
                }
            }
        }

        // ── 网络收发（客户端 / 主机自连共用） ──

        private void StartReceiveLoop()
        {
            var thread = new Thread(ReceiveFromServer) { IsBackground = true };
            thread.Start();
        }

        private void ReceiveFromServer()
        {
            byte[] buffer = new byte[1024];
            while (!_leaving)
            {
                int bytesRead;
                try
                {
                    bytesRead = _clientStream!.Read(buffer, 0, buffer.Length);
                }
                catch
                {
                    break;
                }

                if (bytesRead <= 0)
                    break;

                string data = ProgramConstants.LAN_ENCODING.GetString(buffer, 0, bytesRead);
                data = _overMessage + data;
                _overMessage = string.Empty;

                int sep;
                while ((sep = data.IndexOf(ProgramConstants.LAN_MESSAGE_SEPARATOR)) >= 0)
                {
                    string msg = data.Substring(0, sep);
                    data = data.Substring(sep + 1);
                    if (msg.Length > 0)
                        _dispatcher.Post(() => HandleMessageFromServer(msg));
                }
                _overMessage = data;
            }

            // 连接断开（主机关闭房间 / 网络异常）
            _dispatcher.Post(() =>
            {
                if (_leaving)
                    return;
                AddChatMessage(new ChatMessage(ColorConstants.Red, "与主机的连接已断开"));
                LeaveGame();
            });
        }

        private void SendToHost(string message)
        {
            if (_clientStream is null)
                return;
            byte[] buffer = ProgramConstants.LAN_ENCODING.GetBytes(message + ProgramConstants.LAN_MESSAGE_SEPARATOR);
            try
            {
                _clientStream.Write(buffer, 0, buffer.Length);
                _clientStream.Flush();
            }
            catch
            {
                // 忽略发送失败
            }
        }

        private void CalculateFileHash()
        {
            try
            {
                var fhc = new FileHashCalculator();
                fhc.CalculateHashes();
                _localFileHash = fhc.GetCompleteHash();
                SendToHost(FILE_HASH_COMMAND + " " + _localFileHash);
            }
            catch (Exception ex)
            {
                Logger.Log("LAN file hash calculation failed: " + ex);
            }
        }

        // ── 工具方法 ──

        /// <summary>剥离命令名前缀 + 空格后的参数（Server* 处理器收到完整消息）。</summary>
        private static string ArgsAfter(string message, string commandName)
            => message.Length > commandName.Length + 1 ? message.Substring(commandName.Length + 1) : string.Empty;

        private void ClearReadyStatuses()
        {
            foreach (var p in _players)
            {
                if (p.Name != ProgramConstants.PLAYERNAME)
                    p.Ready = false;
            }
        }

        private void GenerateGameId()
        {
            // 原版：i + 日月时分；本端无战绩库，直接取随机正数保证唯一性
            UniqueGameId = _random.Next(1, int.MaxValue);
        }

        private void AddChatMessage(string message) => AddChatMessage(new ChatMessage(message));

        private void AddChatMessage(ChatMessage message)
        {
            _chatMessages.Add(message);
            if (_chatMessages.Count > MAX_CHAT_MESSAGES)
                _chatMessages.RemoveRange(0, _chatMessages.Count - MAX_CHAT_MESSAGES);
            NotifyChanged();
        }

        private void ResetState()
        {
            _players.Clear();
            _chatMessages.Clear();
            RoomName = string.Empty;
            MaxPlayers = MAX_PLAYER_COUNT;
            Locked = false;
            SelectedMap = null;
            RandomSeed = 0;
            UniqueGameId = 0;
            _overMessage = string.Empty;
            _timeSinceLastReceivedCommand = 0;
            _localFileHash = string.Empty;
        }

        private void CleanUp()
        {
            StopBroadcastTimer();
            StopTickTimer();

            var listener = _listener;
            _listener = null;
            try { listener?.Stop(); } catch { }

            foreach (var p in _players)
            {
                try { p.TcpClient.Close(); } catch { }
            }
            _players.Clear();

            try { _client?.Close(); } catch { }
            _client = null;
            _clientStream = null;
        }

        private void NotifyChanged() => Changed?.Invoke();

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _leaving = true;
            _sessionId++;
            CleanUp();
        }
    }
}
