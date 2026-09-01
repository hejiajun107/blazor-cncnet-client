using System;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using InfiniFrame;
using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 游戏启动协调服务（M3.7 spec §5.3）：
/// 订阅 <see cref="GameRoomService.GameStartRequested"/>，生成 spawn.ini / spawnmap.ini，
/// 导航至加载等待页 /online/loading，调用 <see cref="GameProcessLogic"/> 启动游戏进程；
/// 启动后按设置最小化宿主窗口，游戏退出 / 启动失败后恢复窗口并回到房间页。
/// 迁移自原版 GameLoadingLobby + GameProcessLogic 的窗口管理语义。
/// </summary>
public sealed class GameLaunchService : IDisposable
{
    private readonly IInfiniFrameWindow _window;
    private readonly NavigationManager _nav;
    private readonly GameRoomService _room;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次对局是否正在启动/运行（用于过滤游戏进程退出时的重复/无关回调）。</summary>
    private bool _launchInProgress;

    public GameLaunchService(
        IInfiniFrameWindow window,
        NavigationManager nav,
        GameRoomService room,
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

    /// <summary>房主/加入者收到 START 后触发：生成配置 → 加载页 → 启动游戏。</summary>
    private void OnGameStartRequested()
    {
        var players = _room.GetPlayerInfos();
        var gameModeMap = _room.SelectedMap;
        if (gameModeMap is null)
            return; // StartGame 已校验，防御性返回

        try
        {
            // 1. 生成 spawn.ini + spawnmap.ini（游戏以 -SPAWN 启动时读取；多人局写入 [Tunnel] 隧道信息）
            SpawnIniGenerator.WriteSpawnIni(
                players, gameModeMap, _room.RandomSeed, _room.SideCount,
                _room.UniqueGameId, _room.IsHost,
                _room.TunnelAddress, _room.TunnelPort);
            SpawnIniGenerator.WriteMap(players, gameModeMap);

            // 2. 标记对局进行中并跳转加载等待页
            _launchInProgress = true;
            _nav.NavigateTo("/online/loading");

            // 3. 启动游戏进程（内部触发 GameProcessStarted / GameProcessExited / GameProcessLaunchFailed）
            GameProcessLogic.StartGameProcess();
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            _nav.NavigateTo("/online/game");
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

            _nav.NavigateTo("/online/game");
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

            _nav.NavigateTo("/online/game");
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
