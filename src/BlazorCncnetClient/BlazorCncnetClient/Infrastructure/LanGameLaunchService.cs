using System;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using InfiniFrame;
using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// LAN 游戏启动协调服务（M4.4 D3.2）：
/// 订阅 <see cref="LanGameRoomService.GameStartRequested"/>，生成 spawn.ini / spawnmap.ini
/// （LAN 无隧道：不写 [Tunnel] 节，玩家端口固定 1234），导航至加载等待页 /lan/loading，
/// 调用 <see cref="GameProcessLogic"/> 启动游戏进程；启动后按设置最小化宿主窗口，
/// 游戏退出 / 启动失败后恢复窗口并回到房间页。架构对齐在线 <see cref="GameLaunchService"/>。
/// </summary>
public sealed class LanGameLaunchService : IDisposable
{
    private readonly IInfiniFrameWindow _window;
    private readonly NavigationManager _nav;
    private readonly LanGameRoomService _room;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次对局是否正在启动/运行（用于过滤游戏进程退出时的重复/无关回调）。</summary>
    private bool _launchInProgress;

    public LanGameLaunchService(
        IInfiniFrameWindow window,
        NavigationManager nav,
        LanGameRoomService room,
        MessageBoxService messageBox,
        IDispatcher dispatcher)
    {
        _window = window;
        _nav = nav;
        _room = room;
        _messageBox = messageBox;
        _dispatcher = dispatcher;

        _room.GameStartRequested += OnGameStartRequested;
        GameProcessLogic.GameProcessStarted += OnGameProcessStarted;
        GameProcessLogic.GameProcessExited += OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed += OnGameProcessLaunchFailed;
    }

    /// <summary>房主/加入者收到 LAUNCH 后触发：生成配置 → 加载页 → 启动游戏。</summary>
    private void OnGameStartRequested()
    {
        var players = _room.GetPlayerInfos();
        var gameModeMap = _room.SelectedMap;
        if (gameModeMap is null)
            return; // StartGame 已校验，防御性返回

        try
        {
            // 1. 生成 spawn.ini + spawnmap.ini（LAN 无隧道：tunnelIp/tunnelPort 为空 → 不写 [Tunnel] 节；
            //    各玩家 Port 已在 LANPlayerInfo 固定为 1234，OtherN.Ip 为玩家真实 LAN 地址；
            //    gameOptions 写入 [Settings] 并应用自定义地图代码，随 OPTS 广播与房主一致）
            SpawnIniGenerator.WriteSpawnIni(
                players, gameModeMap, _room.RandomSeed, _room.SideCount,
                _room.UniqueGameId, _room.IsHost,
                tunnelIp: string.Empty, tunnelPort: 0,
                gameOptions: _room.GameOptions);
            SpawnIniGenerator.WriteMap(players, gameModeMap, _room.GameOptions);

            // 2. 标记对局进行中并跳转加载等待页
            _launchInProgress = true;
            _nav.NavigateTo("/lan/loading");

            // 3. 启动游戏进程（内部触发 GameProcessStarted / GameProcessExited / GameProcessLaunchFailed）
            GameProcessLogic.StartGameProcess();
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            _nav.NavigateTo("/lan/game");
            _ = _messageBox.ShowAsync("生成游戏配置出错", ex.Message,
                MessageBoxButtons.Ok, MessageBoxKind.Error);
        }
    }

    private void OnGameProcessStarted()
    {
        // 游戏窗口已出现：按用户设置最小化宿主窗口（原版 MinimizeWindowsOnGameStart）
        if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
            _window.Features.State.SetMinimized(true);
    }

    private void OnGameProcessExited()
    {
        // Process.Exited 在后台线程触发，调度回 UI 线程操作窗口/导航
        _dispatcher.Post(() =>
        {
            if (!_launchInProgress)
                return;
            _launchInProgress = false;

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
                _window.Features.State.SetMinimized(false);

            _nav.NavigateTo("/lan/game");
        });
    }

    private void OnGameProcessLaunchFailed(string title, string message)
    {
        // 启动失败：恢复窗口、弹错误框并回到房间（GameProcessExited 随之触发，
        // 但 _launchInProgress 已置 false，OnGameProcessExited 会直接返回避免重复导航）
        _dispatcher.Post(() =>
        {
            _launchInProgress = false;

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
                _window.Features.State.SetMinimized(false);

            _nav.NavigateTo("/lan/game");
            _ = _messageBox.ShowAsync(title, message, MessageBoxButtons.Ok, MessageBoxKind.Error);
        });
    }

    public void Dispose()
    {
        _room.GameStartRequested -= OnGameStartRequested;
        GameProcessLogic.GameProcessStarted -= OnGameProcessStarted;
        GameProcessLogic.GameProcessExited -= OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed -= OnGameProcessLaunchFailed;
    }
}
