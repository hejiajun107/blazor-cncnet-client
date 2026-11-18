using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CnCNetClient.Core;
using CnCNetClient.Domain;
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
    /// <summary>外壳启动器可执行文件名（安装根；与 ProgramConstants.LAUNCHER_EXECUTABLE_NAME 共用定义）。</summary>
    private const string LauncherExeName = ProgramConstants.LAUNCHER_EXECUTABLE_NAME;

    private readonly EnvironmentCheck _env;
    private readonly AppExitNotifier _exitNotifier;
    private bool _wired;
    private bool _componentEventsWired;
    private readonly Dictionary<string, int> _componentPercentages = new();

    /// <summary>状态变化（版本检查/进度/完成/失败），驱动 UI 刷新。</summary>
    public event Action? StateChanged;

    /// <summary>更新失败（参数为失败信息，供 UI 展示）。</summary>
    public event Action<string>? UpdateFailed;

    /// <summary>自定义组件进度/状态变化（下载进度、完成、失败），驱动组件面板刷新（r9-5）。</summary>
    public event Action<CustomComponent>? ComponentChanged;

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

    /// <summary>自定义组件列表（UpdaterConfig.ini [CustomComponents]，未初始化/未加载时为空；r9-5）。</summary>
    public IReadOnlyList<CustomComponent> CustomComponents
        => IsAvailable && Updater.CustomComponents is not null ? Updater.CustomComponents : Array.Empty<CustomComponent>();

    /// <summary>组件是否正在下载（组件面板「全部下载」禁用态；r9-5）。</summary>
    public bool IsComponentDownloadInProgress => IsAvailable && Updater.IsComponentDownloadInProgress();

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

        // 自更新会替换客户端自身的 exe/dll：SecondStageUpdater 在客户端退出后覆盖 Updater\ 下的文件，
        // 完成后按 ClientDefinitions.ini [Settings] LauncherExe 启动安装根下的同名 exe。
        // 本移植版启动器固定叫 Launcher.exe，模组 ini 常沿用上游旧名（如 WDLauncher.exe），
        // 值对不上就会「更新完成但不再自动重启」，故先做一次契约对齐。
        EnsureLauncherExeConfigured(_env.GamePath);

        Updater.Initialize(
            _env.GamePath,
            SafePath.CombineDirectoryPath(_env.GamePath, ProgramConstants.BASE_RESOURCE_PATH),
            ClientConfiguration.Instance.SettingsIniName,
            ClientConfiguration.Instance.LocalGame,
            // 自更新后将重启的第二阶段入口：必须是位于 GamePath 根（安装根）的可执行，
            // 这样 SecondStageUpdater 才能在 {GamePath}\ 下找到它并拉起。
            // 客户端本体在 ClientApp\ 下，故这里不传客户端自身 exe 名，而传 Launcher（外壳）。
            LauncherExeName);

        WireEvents();
        WireComponentEvents();

        // 读取本地 version 文件，得出本地版本 / 更新系统版本。
        Updater.CheckLocalFileVersions();

        // 复盘元数据需要实际安装的游戏版本号（Domain 无法引用 Updater，经此注入）。
        ReplayManager.UpdaterReplayGameVersion = Updater.GameVersion;

        IsAvailable = true;
        RaiseStateChanged();
    }

    /// <summary>
    /// 对齐自更新重启契约：SecondStageUpdater 替换完文件后读取
    /// <c>Resources/ClientDefinitions.ini [Settings] LauncherExe</c>，启动安装根下的同名 exe。
    /// 仅当该值指向的文件不存在、而本移植版的 <c>Launcher.exe</c> 存在时，才把这一行改写为
    /// <c>LauncherExe=Launcher.exe</c>；改写按行进行并使用 Latin1（字节一一映射），
    /// 不会破坏 ini 的注释与原始编码。其余情况一律不动。
    /// </summary>
    private static void EnsureLauncherExeConfigured(string gamePath)
    {
        try
        {
            string clientDefinitionsPath = SafePath.CombineFilePath(
                gamePath, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);

            if (!File.Exists(clientDefinitionsPath))
                return;

            if (!File.Exists(SafePath.CombineFilePath(gamePath, ProgramConstants.LAUNCHER_EXECUTABLE_NAME)))
                return;

            string[] lines = File.ReadAllLines(clientDefinitionsPath, Encoding.Latin1);
            bool inSettings = false;
            int launcherLineIndex = -1;
            string configured = string.Empty;

            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].Trim();

                if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
                {
                    inSettings = trimmed.Equals("[Settings]", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inSettings ||
                    !trimmed.StartsWith("LauncherExe", StringComparison.OrdinalIgnoreCase) ||
                    !trimmed.Contains('='))
                {
                    continue;
                }

                launcherLineIndex = i;

                string value = trimmed[(trimmed.IndexOf('=') + 1)..];
                int commentIndex = value.IndexOf(';');
                if (commentIndex >= 0)
                    value = value[..commentIndex];

                configured = value.Trim();
                break;
            }

            // 未配置（或配置为空）时不猜测；配置的文件存在时保持原样。
            if (launcherLineIndex < 0 || string.IsNullOrWhiteSpace(configured))
                return;

            if (File.Exists(SafePath.CombineFilePath(gamePath, configured)))
                return;

            // SecondStageUpdater 还会先把 Resources/Launcher/<名字>-<架构><扩展名> 复制到安装根；
            // 该文件存在说明模组自带可用的启动器，同样无需改写。
            string architectureLauncher = SafePath.CombineFilePath(
                gamePath,
                ProgramConstants.BASE_RESOURCE_PATH,
                "Launcher",
                $"{Path.GetFileNameWithoutExtension(configured)}-" +
                $"{System.Runtime.InteropServices.RuntimeInformation.OSArchitecture}" +
                $"{Path.GetExtension(configured)}");

            if (File.Exists(architectureLauncher))
                return;

            // 只替换等号后的值，保留原行的缩进与键名大小写。
            int equalsIndex = lines[launcherLineIndex].IndexOf('=');
            lines[launcherLineIndex] = lines[launcherLineIndex][..(equalsIndex + 1)] +
                ProgramConstants.LAUNCHER_EXECUTABLE_NAME;

            File.WriteAllLines(clientDefinitionsPath, lines, Encoding.Latin1);

            Logger.Log($"ClientDefinitions.ini 的 LauncherExe={configured} 不存在，已改为 " +
                $"{ProgramConstants.LAUNCHER_EXECUTABLE_NAME}，保证自更新后能自动重启。");
        }
        catch (Exception ex)
        {
            Logger.Log("对齐 LauncherExe 失败（自更新后可能无法自动重启）：" + ex);
        }
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

    /// <summary>订阅全部自定义组件的下载进度/完成事件（组件实例在 Initialize 时固定，仅需订阅一次）。</summary>
    private void WireComponentEvents()
    {
        if (_componentEventsWired || Updater.CustomComponents is null)
            return;
        _componentEventsWired = true;

        foreach (var component in Updater.CustomComponents)
        {
            component.DownloadProgressChanged += OnComponentDownloadProgress;
            component.DownloadFinished += OnComponentDownloadFinished;
        }
    }

    private void OnComponentDownloadProgress(CustomComponent component, int percentage)
    {
        if (component.ININame is not null)
            _componentPercentages[component.ININame] = percentage;
        ComponentChanged?.Invoke(component);
    }

    private void OnComponentDownloadFinished(CustomComponent component, bool success)
    {
        if (component.ININame is not null)
            _componentPercentages.Remove(component.ININame);
        ComponentChanged?.Invoke(component);
    }

    /// <summary>组件当前下载百分比（组件面板进度条；r9-5）。</summary>
    public int GetComponentDownloadPercentage(CustomComponent component)
        => component.ININame is not null && _componentPercentages.TryGetValue(component.ININame, out int p) ? p : 0;

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

        if (_componentEventsWired)
        {
            _componentEventsWired = false;
            if (Updater.CustomComponents is not null)
            {
                foreach (var component in Updater.CustomComponents)
                {
                    component.DownloadProgressChanged -= OnComponentDownloadProgress;
                    component.DownloadFinished -= OnComponentDownloadFinished;
                }
            }
        }
    }
}
