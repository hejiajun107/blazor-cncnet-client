using System;
using CnCNetClient.Core;
using CnCNetClient.Integration.Lifecycle;
using CnCNetClient.Tools;
using CnCNetClient.Updater;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 更新服务（M4.3 D6）：包装静态 <see cref="Updater"/>，提供懒初始化、状态/进度读取与事件，
/// 供设置中心「更新」Tab 与主菜单启动检查使用。
/// 与原版一致：更新界面在设置窗口的 Updater 面板（Force update）；主菜单仅展示状态/版本。
/// </summary>
public sealed class UpdateService : IDisposable
{
    private readonly EnvironmentCheck _env;
    private readonly AppExitNotifier _exitNotifier;
    private bool _wired;

    /// <summary>状态变化（版本检查/进度/完成/失败），驱动 UI 刷新。</summary>
    public event Action? StateChanged;

    /// <summary>更新失败（参数为失败信息，供 UI 展示）。</summary>
    public event Action<string>? UpdateFailed;

    /// <summary>服务是否可用（环境就绪且已初始化镜像表）。</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>当前文件下载进度（下载中）。</summary>
    public string CurrentFileName { get; private set; } = string.Empty;

    /// <summary>当前文件下载百分比。</summary>
    public int CurrentFilePercentage { get; private set; }

    /// <summary>总下载百分比。</summary>
    public int TotalPercentage { get; private set; }

    /// <summary>最近一次更新失败信息。</summary>
    public string LastError { get; private set; } = string.Empty;

    public UpdateService(EnvironmentCheck env, AppExitNotifier exitNotifier)
    {
        _env = env;
        _exitNotifier = exitNotifier;
    }

    /// <summary>当前版本状态（未初始化/环境未就绪时为 UNKNOWN）。</summary>
    public VersionState VersionState
        => IsAvailable ? Updater.VersionState : VersionState.UNKNOWN;

    /// <summary>本地版本（version 文件 [DTA] Version）。</summary>
    public string GameVersion => IsAvailable ? Updater.GameVersion : "N/A";

    /// <summary>服务器版本。</summary>
    public string ServerGameVersion => IsAvailable ? Updater.ServerGameVersion : "N/A";

    /// <summary>更新系统版本。</summary>
    public string UpdaterVersion => IsAvailable ? Updater.UpdaterVersion : "N/A";

    /// <summary>本次更新大小（KB）。</summary>
    public int UpdateSizeInKb => IsAvailable ? Updater.UpdateSizeInKb : 0;

    /// <summary>是否需要手动下载安装。</summary>
    public bool ManualUpdateRequired => IsAvailable && Updater.ManualUpdateRequired;

    /// <summary>手动下载 URL。</summary>
    public string ManualDownloadURL => IsAvailable ? Updater.ManualDownloadURL : string.Empty;

    /// <summary>可用更新镜像数。</summary>
    public int MirrorCount => IsAvailable && Updater.UpdateMirrors is not null ? Updater.UpdateMirrors.Count : 0;

    /// <summary>是否正在检查/下载（按钮禁用态）。</summary>
    public bool IsBusy => VersionState is VersionState.UPDATECHECKINPROGRESS or VersionState.UPDATEINPROGRESS;

    /// <summary>
    /// 惰性初始化：环境就绪（游戏资源 + ClientConfiguration）时调用 <see cref="Updater.Initialize"/>。
    /// 未就绪时保持不可用，UI 安全降级；后续可再次调用（如环境就绪后）。
    /// </summary>
    public void EnsureInitialized()
    {
        if (IsAvailable)
            return;

        if (!_env.IsReady || _env.GamePath is null)
            return;

        Updater.Initialize(
            _env.GamePath,
            SafePath.CombineDirectoryPath(_env.GamePath, ProgramConstants.BASE_RESOURCE_PATH),
            ClientConfiguration.Instance.SettingsIniName,
            ClientConfiguration.Instance.LocalGame,
            SafePath.GetFile(ProgramConstants.StartupExecutable).Name);

        WireEvents();

        // 读取本地 version 文件，得出本地版本 / 更新系统版本。
        Updater.CheckLocalFileVersions();

        IsAvailable = true;
        RaiseStateChanged();
    }

    /// <summary>启动一次更新检查（幂等：检查/更新进行中时忽略）。</summary>
    public void CheckForUpdates()
    {
        EnsureInitialized();
        if (!IsAvailable || IsBusy)
            return;

        LastError = string.Empty;
        Updater.CheckForUpdates();
        RaiseStateChanged();
    }

    /// <summary>开始下载并应用更新。</summary>
    public void StartUpdate()
    {
        if (!IsAvailable || IsBusy)
            return;

        LastError = string.Empty;
        CurrentFileName = string.Empty;
        CurrentFilePercentage = 0;
        TotalPercentage = 0;
        Updater.StartUpdate();
        RaiseStateChanged();
    }

    /// <summary>取消当前更新。</summary>
    public void StopUpdate()
    {
        if (!IsAvailable)
            return;
        Updater.StopUpdate();
    }

    private void WireEvents()
    {
        if (_wired)
            return;
        _wired = true;

        Updater.FileIdentifiersUpdated += OnFileIdentifiersUpdated;
        Updater.OnVersionStateChanged += OnVersionStateChanged;
        Updater.UpdateProgressChanged += OnUpdateProgressChanged;
        Updater.OnUpdateCompleted += OnUpdateCompleted;
        Updater.OnUpdateFailed += OnUpdateFailed;
        Updater.Restart += OnRestart;
    }

    private void OnFileIdentifiersUpdated() => RaiseStateChanged();

    private void OnVersionStateChanged() => RaiseStateChanged();

    private void OnUpdateProgressChanged(string currFileName, int currFilePercentage, int totalPercentage)
    {
        CurrentFileName = currFileName;
        CurrentFilePercentage = currFilePercentage;
        TotalPercentage = totalPercentage;
        RaiseStateChanged();
    }

    private void OnUpdateCompleted() => RaiseStateChanged();

    private void OnUpdateFailed(Exception ex)
    {
        LastError = ex.Message;
        RaiseStateChanged();
        UpdateFailed?.Invoke(ex.Message);
    }

    /// <summary>
    /// 更新完成且需要重启（SecondStageUpdater 已由 Updater 启动）：
    /// 先持久化（IAppExitNotifier），再退出进程，让 SecondStageUpdater 替换文件并重启客户端。
    /// </summary>
    private void OnRestart(object? sender, EventArgs e)
    {
        _exitNotifier.NotifyExiting();
        Environment.Exit(0);
    }

    private void RaiseStateChanged() => StateChanged?.Invoke();

    public void Dispose()
    {
        if (!_wired)
            return;
        _wired = false;
        Updater.FileIdentifiersUpdated -= OnFileIdentifiersUpdated;
        Updater.OnVersionStateChanged -= OnVersionStateChanged;
        Updater.UpdateProgressChanged -= OnUpdateProgressChanged;
        Updater.OnUpdateCompleted -= OnUpdateCompleted;
        Updater.OnUpdateFailed -= OnUpdateFailed;
        Updater.Restart -= OnRestart;
    }
}
