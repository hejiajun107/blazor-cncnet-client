using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>启动闪屏配置（ClientDefinitions.ini [LoadingScreen] 节，MOD 可定制）。</summary>
public sealed class LoadingScreenConfig
{
    /// <summary>是否启用启动闪屏（默认 true）。</summary>
    public bool ShowSplash { get; init; } = true;

    /// <summary>背景图（相对游戏根目录，如 Resources/splash.png）。</summary>
    public string SplashImage { get; init; } = string.Empty;

    /// <summary>背景视频（相对游戏根目录，如 Resources/splash.mp4；存在时优先于图片）。</summary>
    public string SplashVideo { get; init; } = string.Empty;

    /// <summary>展示时长（秒）；&lt;=0 表示「初始化完成即关闭」（实现为最短展示后淡出）。</summary>
    public double SplashDuration { get; init; } = 2.0;

    /// <summary>是否显示初始化进度条（动画式，无真实百分比）。</summary>
    public bool ShowProgress { get; init; } = true;

    /// <summary>是否显示版本号。</summary>
    public bool ShowVersion { get; init; } = true;

    /// <summary>底部提示文案（可走 L10N 多语言）。</summary>
    public string SplashText { get; init; } = string.Empty;
}

/// <summary>
/// 启动闪屏配置服务：懒读取 ClientDefinitions.ini 的 [LoadingScreen] 节。
/// 游戏资源未就绪时返回默认配置（闪屏保持可用，仅无自定义媒体）。
/// </summary>
public sealed class LoadingScreenService
{
    private readonly EnvironmentCheck _env;
    private LoadingScreenConfig? _config;

    public LoadingScreenService(EnvironmentCheck env) => _env = env;

    /// <summary>游戏根目录（环境就绪后可用）。</summary>
    public string? RootPath => _env.GamePath;

    public LoadingScreenConfig Config => _config ??= Load();

    private LoadingScreenConfig Load()
    {
        var cfg = new LoadingScreenConfig();

        if (!_env.IsReady || _env.GamePath is null)
            return cfg;

        string defs = SafePath.CombineFilePath(_env.GamePath, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);
        if (!File.Exists(defs))
            return cfg;

        var ini = new IniFile(defs);
        const string section = "LoadingScreen";

        return new LoadingScreenConfig
        {
            ShowSplash = ini.GetBooleanValue(section, "ShowSplash", true),
            SplashImage = ini.GetStringValue(section, "SplashImage", string.Empty).Trim(),
            SplashVideo = ini.GetStringValue(section, "SplashVideo", string.Empty).Trim(),
            SplashDuration = ini.GetDoubleValue(section, "SplashDuration", 2.0),
            ShowProgress = ini.GetBooleanValue(section, "ShowProgress", true),
            ShowVersion = ini.GetBooleanValue(section, "ShowVersion", true),
            SplashText = ini.GetStringValue(section, "SplashText", string.Empty).Trim()
        };
    }
}
