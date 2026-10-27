using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;

using CnCNetClient.Core;
using CnCNetClient.Domain.LAN;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Domain.Multiplayer.LAN;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Online;
using CnCNetClient.Online.LAN;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// LAN 大厅服务（M4.4 D3.1）：UDP 广播发现 + 游戏列表 + 聊天 + 玩家列表 + ALIVE 心跳。
/// 移植原版 <c>DTAClient.DXGUI.Multiplayer.LANLobby</c> 的逻辑部分（去 XNA）。
/// scoped 注册，进入 /lan/lobby 时 Start，离开时 Stop。
/// 收到广播消息在监听线程触发，经 <see cref="IDispatcher"/> 调度回 UI 线程。
/// </summary>
public sealed class LanLobbyService : IDisposable
{
    private const double ALIVE_MESSAGE_INTERVAL = 5.0;
    private const double INACTIVITY_REMOVE_TIME = 10.0;
    private const double GAME_INACTIVITY_REMOVE_TIME = 20.0;
    private const double MESSAGE_ID_EXPIRATION_SECONDS = 60.0;
    private const double TICK_INTERVAL_SECONDS = 1.0;

    private readonly GameCollection _gameCollection;
    private readonly IDispatcher _dispatcher;
    private readonly Random _random;

    private LanBroadcastManager? _broadcastManager;
    private LanMessageDeduplicator? _deduplicator;
    private readonly LanPlayerManager _playerManager = new();
    private Timer? _timer;

    private string _localGame = string.Empty;
    private int _localGameIndex = -1;
    private double _timeSinceAlive;
    private int _chatColorIndex;

    private bool _disposed;

    /// <summary>状态变化（列表/聊天/玩家），驱动 UI 刷新。</summary>
    public event Action? StateChanged;

    /// <summary>已发现房间列表。</summary>
    public List<HostedLANGame> Games { get; } = new();

    /// <summary>大厅聊天记录。</summary>
    public List<ChatMessage> ChatMessages { get; } = new();

    /// <summary>广播管理器是否初始化成功（socket 已绑定）。</summary>
    public bool IsRunning { get; private set; }

    /// <summary>最近一次启动错误（端口被占用/无广播接口时展示）。</summary>
    public string LastError { get; private set; } = string.Empty;

    /// <summary>LAN 聊天颜色表（与 ChatListBox 颜色索引一致）。</summary>
    public static readonly LANColor[] ChatColors =
    [
        new("Gray", new SixLabors.ImageSharp.PixelFormats.Rgba32(128, 128, 128)),
        new("Metallic", new SixLabors.ImageSharp.PixelFormats.Rgba32(192, 192, 192)),
        new("Green", new SixLabors.ImageSharp.PixelFormats.Rgba32(34, 139, 34)),
        new("Lime Green", new SixLabors.ImageSharp.PixelFormats.Rgba32(50, 205, 50)),
        new("Green Yellow", new SixLabors.ImageSharp.PixelFormats.Rgba32(173, 255, 47)),
        new("Goldenrod", new SixLabors.ImageSharp.PixelFormats.Rgba32(218, 165, 32)),
        new("Yellow", new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 255, 0)),
        new("Orange", new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 165, 0)),
        new("Red", new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 0, 0)),
        new("Pink", new SixLabors.ImageSharp.PixelFormats.Rgba32(255, 20, 147)),
        new("Purple", new SixLabors.ImageSharp.PixelFormats.Rgba32(147, 112, 219)),
        new("Sky Blue", new SixLabors.ImageSharp.PixelFormats.Rgba32(135, 206, 250)),
        new("Blue", new SixLabors.ImageSharp.PixelFormats.Rgba32(65, 105, 225)),
        new("Brown", new SixLabors.ImageSharp.PixelFormats.Rgba32(139, 69, 19)),
        new("Teal", new SixLabors.ImageSharp.PixelFormats.Rgba32(0, 128, 128)),
    ];

    public LanLobbyService(GameCollection gameCollection, IDispatcher dispatcher, Random random)
    {
        _gameCollection = gameCollection;
        _dispatcher = dispatcher;
        _random = random;
    }

    /// <summary>当前聊天颜色索引（持久化 [Options] LANChatColor）。</summary>
    public int ChatColorIndex
    {
        get => _chatColorIndex;
        set
        {
            _chatColorIndex = value;
            UserINISettings.Instance.LANChatColor.Value = value;
            UserINISettings.Instance.SaveSettings();
        }
    }

    /// <summary>当前大厅玩家（昵称快照）。</summary>
    public IReadOnlyList<string> PlayerNames
        => _playerManager.GetAllPlayers().Select(p => p.Name).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public string LocalGame => _localGame;

    /// <summary>进入大厅：初始化广播 socket、开始监听、发 ALIVE 心跳。</summary>
    public void Start()
    {
        if (IsRunning)
            return;

        _localGame = ClientConfiguration.Instance.LocalGame;
        _localGameIndex = _gameCollection.GameList.FindIndex(
            g => g?.InternalName?.ToUpperInvariant() == _localGame.ToUpperInvariant());

        _playerManager.Clear();
        _deduplicator?.Dispose();
        _deduplicator = new LanMessageDeduplicator(_random.Next(), MESSAGE_ID_EXPIRATION_SECONDS);

        _broadcastManager?.Dispose();
        _broadcastManager = new LanBroadcastManager(ProgramConstants.LAN_LOBBY_PORT, Encoding.UTF8);
        _broadcastManager.MessageReceived += OnMessageReceived;

        try
        {
            _broadcastManager.Initialize();
            IsRunning = true;
            LastError = string.Empty;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            IsRunning = false;
            AddChatMessage(string.Format(
                UiText.T("LanSvc.SocketCreateFailed",
                    "Failed to create the LAN socket: {0}. Please check your firewall settings and make sure no other program is using UDP ports 1232-1234."),
                ex.Message));
            RaiseStateChanged();
            return;
        }

        _timer?.Dispose();
        _timer = new Timer(_ => Tick(), null,
            TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS),
            TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS));

        SendAlive();
        RaiseStateChanged();
    }

    /// <summary>离开大厅：广播 QUIT、停止心跳、关闭 socket/线程。</summary>
    public void Stop()
    {
        if (!IsRunning && _broadcastManager is null)
            return;

        SendMessage("QUIT");
        _broadcastManager?.Dispose();
        _broadcastManager = null;
        _deduplicator?.Dispose();
        _deduplicator = null;
        _timer?.Dispose();
        _timer = null;
        IsRunning = false;
        RaiseStateChanged();
    }

    /// <summary>每秒心跳：ALIVE 广播 + 玩家/房间超时清理。</summary>
    private void Tick()
    {
        if (!IsRunning)
            return;

        _timeSinceAlive += TICK_INTERVAL_SECONDS;
        if (_timeSinceAlive >= ALIVE_MESSAGE_INTERVAL)
            SendAlive();

        foreach (var player in _playerManager.GetAllPlayers())
        {
            player.AddToTimeWithoutRefresh(TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS));
            if (player.TimeWithoutRefresh > TimeSpan.FromSeconds(INACTIVITY_REMOVE_TIME))
                _playerManager.RemovePlayer(player.EndPoint);
        }

        bool gameChanged = false;
        for (int i = Games.Count - 1; i >= 0; i--)
        {
            Games[i].TimeWithoutRefresh += TimeSpan.FromSeconds(TICK_INTERVAL_SECONDS);
            if (Games[i].TimeWithoutRefresh > TimeSpan.FromSeconds(GAME_INACTIVITY_REMOVE_TIME))
            {
                Games.RemoveAt(i);
                gameChanged = true;
            }
        }

        if (gameChanged || _playerManager.Count > 0)
            RaiseStateChanged();
    }

    private void OnMessageReceived(object? sender, LanBroadcastMessageReceivedEventArgs e)
        => _dispatcher.Post(() => HandleNetworkMessage(e.Data, e.EndPoint));

    private void SendAlive()
    {
        var sb = new StringBuilder("ALIVE ");
        sb.Append(_localGameIndex);
        sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
        sb.Append(ProgramConstants.PLAYERNAME);
        SendMessage(sb.ToString());
        _timeSinceAlive = 0;
    }

    private void SendMessage(string message)
    {
        string wrapped = _deduplicator?.WrapMessage(message) ?? message;
        bool ok = _broadcastManager?.SendMessage(wrapped) ?? false;
        if (!ok)
            AddChatMessage(UiText.T("LanSvc.BroadcastSendFailed",
                "Failed to send the LAN broadcast: the network socket may not be initialized."));
    }

    /// <summary>
    /// 供 LAN 房间服务向大厅广播消息（GAME / QUIT / GAMECLOSED 等），经去重包装。
    /// 房间服务与大厅共用同一广播 socket / 去重器（原版 LANLobby 亦由大厅转发房间广播）。
    /// </summary>
    public void BroadcastRaw(string message) => SendMessage(message);

    /// <summary>处理收到的广播消息：ALIVE / CHAT / QUIT / GAMECLOSED / GAME。</summary>
    private void HandleNetworkMessage(string data, IPEndPoint endPoint)
    {
        if (_deduplicator is null)
            return;

        _deduplicator.UnwrapMessage(data, out string payload, out bool isDuplicate);
        if (isDuplicate || string.IsNullOrWhiteSpace(payload))
            return;

        string[] commandAndParams = payload.Split(' ');
        string command = commandAndParams[0];

        int firstSpace = payload.IndexOf(' ');
        string[] parameters = firstSpace >= 0
            ? payload[(firstSpace + 1)..].Split([ProgramConstants.LAN_DATA_SEPARATOR])
            : [];

        // 不按来源地址过滤（原版同行为）：广播回环让本机玩家/自己的聊天也进入列表；
        // 同机多开时对方实例同样经本地 IP 广播而来，按地址过滤会误删（验收 D3.1 主路径）。

        LANLobbyUser? user = _playerManager.GetPlayerIfExist(endPoint);

        switch (command)
        {
            case "ALIVE":
                if (parameters.Length < 2)
                    return;

                int gameIndex = Conversions.IntFromString(parameters[0], -1);
                string name = parameters[1];

                if (string.IsNullOrWhiteSpace(name))
                    return;

                if (user == null)
                    user = _playerManager.GetOrCreatePlayer(endPoint, name);

                user.ClearTimeWithoutRefresh();
                RaiseStateChanged();
                break;

            case "CHAT":
                if (user == null || parameters.Length < 2)
                    return;

                int colorIndex = Conversions.IntFromString(parameters[0], -1);
                if (colorIndex < 0 || colorIndex >= ChatColors.Length)
                    return;

                AddChatMessage(new ChatMessage(user.Name, ChatColors[colorIndex].XNAColor, DateTime.Now, parameters[1]));
                break;

            case "QUIT":
                if (user == null)
                    return;
                _playerManager.RemovePlayer(endPoint);
                RaiseStateChanged();
                break;

            case "GAMECLOSED":
            {
                int closedIndex = Games.FindIndex(g => g.EndPoint.Equals(endPoint));
                if (closedIndex > -1)
                {
                    Games.RemoveAt(closedIndex);
                    RaiseStateChanged();
                }
                break;
            }

            case "GAME":
                if (user == null)
                    return;

                var game = new HostedLANGame();
                if (!game.SetDataFromStringArray(_gameCollection, parameters))
                    return;

                game.EndPoint = endPoint;

                int existingIndex = Games.FindIndex(g => g.EndPoint.Equals(endPoint));
                if (existingIndex > -1)
                    Games[existingIndex] = game;
                else
                    Games.Add(game);

                RaiseStateChanged();
                break;
        }
    }

    /// <summary>大厅聊天发送（颜色索引 + 文本）。</summary>
    public void SendChat(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !IsRunning)
            return;

        string chatMessage = text.Replace((char)1, '?');

        var sb = new StringBuilder("CHAT ");
        sb.Append(_chatColorIndex);
        sb.Append(ProgramConstants.LAN_DATA_SEPARATOR);
        sb.Append(chatMessage);

        SendMessage(sb.ToString());
        RaiseStateChanged();
    }

    /// <summary>本机加入自己的聊天回显。</summary>
    private void AddChatMessage(string message)
        => AddChatMessage(new ChatMessage(message));

    private void AddChatMessage(ChatMessage message)
    {
        ChatMessages.Add(message);
        if (ChatMessages.Count > 200)
            ChatMessages.RemoveRange(0, ChatMessages.Count - 200);
        RaiseStateChanged();
    }

    /// <summary>把 Rgba32 转 CSS 十六进制色（#RRGGBB）。</summary>
    public static string ToCssHex(SixLabors.ImageSharp.PixelFormats.Rgba32 c)
        => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    /// <summary>触发状态变化通知，驱动 UI 刷新。</summary>
    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_broadcastManager is not null)
            _broadcastManager.MessageReceived -= OnMessageReceived;

        Stop();
        _timer?.Dispose();
        _deduplicator?.Dispose();
        _broadcastManager?.Dispose();
    }
}
