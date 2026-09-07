using System;
using CnCNetClient.Core;
using CnCNetClient.Domain;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using InfiniFrame;
using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 战役启动协调服务（M4.6 spec §5.3）：
/// 订阅 <see cref="CampaignService.GameStartRequested"/>，生成任务 spawn.ini / spawnmap.ini
/// （难度、加载画面、任务配置节、CampaignForcedSpawnIniOptions 强制选项），
/// 导航至加载等待页 /campaign/loading，调用 <see cref="GameProcessLogic"/> 启动游戏进程；
/// 启动后按设置最小化宿主窗口，游戏退出 / 启动失败后恢复窗口并回到战役选择页，
/// 同时清理自定义任务补充文件（.shp/.pal 等）。
/// 迁移自原版 CampaignSelector.LaunchMission 的启动语义 + 在线 <see cref="GameLaunchService"/> 的窗口管理。
/// </summary>
public sealed class CampaignLaunchService : IDisposable
{
    private readonly IInfiniFrameWindow _window;
    private readonly NavigationManager _nav;
    private readonly CampaignService _campaign;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次任务是否正在启动/运行（用于过滤游戏进程退出时的重复/无关回调）。</summary>
    private bool _launchInProgress;

    public CampaignLaunchService(
        IInfiniFrameWindow window,
        NavigationManager nav,
        CampaignService campaign,
        MessageBoxService messageBox,
        IDispatcher dispatcher)
    {
        _window = window;
        _nav = nav;
        _campaign = campaign;
        _messageBox = messageBox;
        _dispatcher = dispatcher;

        _campaign.GameStartRequested += OnGameStartRequested;
        GameProcessLogic.GameProcessStarted += OnGameProcessStarted;
        GameProcessLogic.GameProcessExited += OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed += OnGameProcessLaunchFailed;
    }

    /// <summary>任务启动请求（StartGame 校验通过后触发）：生成配置 → 加载页 → 启动游戏。</summary>
    private void OnGameStartRequested()
    {
        Mission? mission = _campaign.SelectedMission;
        if (mission is null)
            return; // StartGame 已校验，防御性返回

        try
        {
            // 1. 生成任务 spawn.ini / spawnmap.ini（单机：无隧道；难度/加载画面/任务配置节在生成器内处理，
            //    自定义任务补充文件在生成器内复制到游戏目录）
            SpawnIniGenerator.WriteMissionSpawnIni(mission, _campaign.Difficulty);

            // 2. 标记对局进行中并跳转加载等待页
            _launchInProgress = true;
            _nav.NavigateTo("/campaign/loading");

            // 3. 启动游戏进程（内部触发 GameProcessStarted / GameProcessExited / GameProcessLaunchFailed）
            GameProcessLogic.StartGameProcess();
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            _nav.NavigateTo("/campaign");
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

            // 原版 GameProcessExited：清理复制到游戏目录的自定义任务补充文件（.shp/.pal 等）
            SpawnIniGenerator.DeleteMissionSupplementalFiles();

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
                _window.Features.State.SetMinimized(false);

            _nav.NavigateTo("/campaign");
        });
    }

    private void OnGameProcessLaunchFailed(string title, string message)
    {
        // 启动失败：恢复窗口、弹错误框并回到战役选择页（GameProcessExited 随之触发，
        // 但 _launchInProgress 已置 false，OnGameProcessExited 会直接返回避免重复导航）
        _dispatcher.Post(() =>
        {
            _launchInProgress = false;

            if (UserINISettings.Instance.MinimizeWindowsOnGameStart.Value)
                _window.Features.State.SetMinimized(false);

            _nav.NavigateTo("/campaign");
            _ = _messageBox.ShowAsync(title, message, MessageBoxButtons.Ok, MessageBoxKind.Error);
        });
    }

    public void Dispose()
    {
        _campaign.GameStartRequested -= OnGameStartRequested;
        GameProcessLogic.GameProcessStarted -= OnGameProcessStarted;
        GameProcessLogic.GameProcessExited -= OnGameProcessExited;
        GameProcessLogic.GameProcessLaunchFailed -= OnGameProcessLaunchFailed;
    }
}
