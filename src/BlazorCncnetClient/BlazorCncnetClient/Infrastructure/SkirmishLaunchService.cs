using System;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using InfiniFrame;
using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 遭遇战启动协调服务（M4.5 spec §5.3）：
/// 订阅 <see cref="SkirmishService.GameStartRequested"/>，生成含 AI 的 spawn.ini / spawnmap.ini，
/// 导航至加载等待页 /skirmish/loading，调用 <see cref="GameProcessLogic"/> 启动游戏进程；
/// 启动后按设置最小化宿主窗口，游戏退出 / 启动失败后恢复窗口并回到遭遇战页。
/// 迁移自原版 SkirmishLobby 的启动语义 + 在线 <see cref="GameLaunchService"/> 的窗口管理。
/// </summary>
public sealed class SkirmishLaunchService : IDisposable
{
    private readonly IInfiniFrameWindow _window;
    private readonly NavigationManager _nav;
    private readonly SkirmishService _skirmish;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次对局是否正在启动/运行（用于过滤游戏进程退出时的重复/无关回调）。</summary>
    private bool _launchInProgress;

    public SkirmishLaunchService(
        IInfiniFrameWindow window,
        NavigationManager nav,
        SkirmishService skirmish,
        MessageBoxService messageBox,
        IDispatcher dispatcher)
    {
        _window = window;
        _nav = nav;
        _skirmish = skirmish;
        _messageBox = messageBox;
        _dispatcher = dispatcher;

        _skirmish.GameStartRequested += OnGameStartRequested;
        GameProcessLogic.GameProcessStarted += OnGameProcessStarted;
        GameProcessLogic.GameProcessExited += OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed += OnGameProcessLaunchFailed;
    }

    /// <summary>开始游戏请求（StartGame 校验通过后触发）：生成配置 → 加载页 → 启动游戏。</summary>
    private void OnGameStartRequested()
    {
        var gameModeMap = _skirmish.SelectedMap;
        if (gameModeMap is null)
            return; // StartGame 已校验，防御性返回

        try
        {
            // 1. 生成含 AI 的 spawn.ini + spawnmap.ini（遭遇战为本地主机：无隧道、固定本机）
            //    players 仅含人类玩家，aiPlayers 紧随其后，由 SpawnIniGenerator 写入 AIPlayers 等节。
            SpawnIniGenerator.WriteSpawnIni(
                new[] { _skirmish.HumanPlayer! },
                gameModeMap,
                _skirmish.RandomSeed,
                _skirmish.SideCount,
                _skirmish.UniqueGameId,
                isHost: true,
                tunnelIp: string.Empty,
                tunnelPort: 0,
                aiPlayers: _skirmish.AIPlayers,
                teamStartMappings: _skirmish.GetActiveTeamStartMappings(), // G6 自动结盟（出生点→队伍）
                gameOptions: _skirmish.GameOptions); // G7 游戏选项（复选/下拉 → spawn.ini [Settings]）
            SpawnIniGenerator.WriteMap(new[] { _skirmish.HumanPlayer! }, gameModeMap,
                _skirmish.GameOptions); // G7 游戏选项自定义地图代码（AlliesAllowed 等）

            // 2. 标记对局进行中并跳转加载等待页
            _launchInProgress = true;
            _nav.NavigateTo("/skirmish/loading");

            // 3. 启动游戏进程（内部触发 GameProcessStarted / GameProcessExited / GameProcessLaunchFailed）
            GameProcessLogic.StartGameProcess();
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            _nav.NavigateTo("/skirmish");
            _ = _messageBox.ShowAsync(UiText.T("SkirmishSvc.ConfigGenerateFailedTitle", "Failed to generate the game configuration"), ex.Message,
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

            // G1：原版 GameProcessExited 在每局结束后重生成随机种子，避免下一局复用相同 Seed
            _skirmish.RegenerateSeed();

            _nav.NavigateTo("/skirmish");
        });
    }

    private void OnGameProcessLaunchFailed(string title, string message)
    {
        // 启动失败：恢复窗口、弹错误框并回到遭遇战页（GameProcessExited 随之触发，
        // 但 _launchInProgress 已置 false，OnGameProcessExited 会直接返回避免重复导航）
        _dispatcher.Post(() =>
        {
            _launchInProgress = false;

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
                _window.Features.State.SetMinimized(false);

            _nav.NavigateTo("/skirmish");
            _ = _messageBox.ShowAsync(title, message, MessageBoxButtons.Ok, MessageBoxKind.Error);
        });
    }

    public void Dispose()
    {
        _skirmish.GameStartRequested -= OnGameStartRequested;
        GameProcessLogic.GameProcessStarted -= OnGameProcessStarted;
        GameProcessLogic.GameProcessExited -= OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed -= OnGameProcessLaunchFailed;
    }
}
