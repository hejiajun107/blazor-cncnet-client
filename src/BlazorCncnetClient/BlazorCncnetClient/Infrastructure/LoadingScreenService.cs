using BlazorCncnetClient.Infrastructure.Plugins;
using CnCNetClient.Core;
using CnCNetClient.Plugin.Abstractions;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>启动闪屏配置（ClientDefinitions.ini [LoadingScreen] 节 + 插件轻量覆盖，MOD 可定制）。</summary>
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

    /// <summary>底部提示文案（TextKey 未命中时的兜底文本）。</summary>
    public string SplashText { get; init; } = string.Empty;

    /// <summary>底部提示文案的翻译键（Translation.ini [Values]）；为空时直接显示 <see cref="SplashText"/>。</summary>
    public string SplashTextKey { get; init; } = string.Empty;

    /// <summary>品牌主标题（默认 CNC NET）。</summary>
    public string BrandTitle { get; init; } = "CNC NET";

    /// <summary>品牌副标题（默认 COMMAND &amp; CONQUER）。</summary>
    public string BrandSubtitle { get; init; } = "COMMAND & CONQUER";
}

/// <summary>
/// 启动闪屏配置服务：懒读取 ClientDefinitions.ini 的 [LoadingScreen] 节，
/// 并叠加插件通过 <see cref="IPluginUiRegistrar.SetLoadingScreenContent"/> 声明的轻量覆盖。
/// 游戏资源未就绪时返回默认配置（闪屏保持可用，仅无自定义媒体）。
/// </summary>
public sealed class LoadingScreenService
{
    private readonly EnvironmentCheck _env;
    private readonly PluginCatalog _plugins;
    private LoadingScreenConfig? _config;

    public LoadingScreenService(EnvironmentCheck env, PluginCatalog plugins)
    {
        _env = env;
        _plugins = plugins;
    }

    /// <summary>游戏根目录（环境就绪后可用）。</summary>
    public string? RootPath => _env.GamePath;

    public LoadingScreenConfig Config => _config ??= Load();

    private LoadingScreenConfig Load()
    {
        var cfg = new LoadingScreenConfig();

        if (!_env.IsReady || _env.GamePath is null)
            return ApplyPluginOverride(cfg);

        string defs = SafePath.CombineFilePath(_env.GamePath, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);
        if (!File.Exists(defs))
            return ApplyPluginOverride(cfg);

        var ini = new IniFile(defs);
        const string section = "LoadingScreen";

        var iniCfg = new LoadingScreenConfig
        {
            ShowSplash = ini.GetBooleanValue(section, "ShowSplash", true),
            SplashImage = ini.GetStringValue(section, "SplashImage", string.Empty).Trim(),
            SplashVideo = ini.GetStringValue(section, "SplashVideo", string.Empty).Trim(),
            SplashDuration = ini.GetDoubleValue(section, "SplashDuration", 2.0),
            ShowProgress = ini.GetBooleanValue(section, "ShowProgress", true),
            ShowVersion = ini.GetBooleanValue(section, "ShowVersion", true),
            SplashText = ini.GetStringValue(section, "SplashText", string.Empty).Trim(),
            SplashTextKey = ini.GetStringValue(section, "SplashTextKey", string.Empty).Trim(),
            BrandTitle = ini.GetStringValue(section, "BrandTitle", "CNC NET").Trim(),
            BrandSubtitle = ini.GetStringValue(section, "BrandSubtitle", "COMMAND & CONQUER").Trim()
        };

        return ApplyPluginOverride(iniCfg);
    }

    /// <summary>
    /// 叠加插件内容覆盖：仅替换 non-null 字段；媒体路径支持插件目录绝对路径（自动转为游戏根相对路径）。
    /// </summary>
    private LoadingScreenConfig ApplyPluginOverride(LoadingScreenConfig cfg)
    {
        LoadingScreenContent? o = _plugins.LoadingScreenContent;
        if (o is null)
            return cfg;

        return new LoadingScreenConfig
        {
            ShowSplash = cfg.ShowSplash,
            SplashImage = Normalize(o.Image) ?? cfg.SplashImage,
            SplashVideo = Normalize(o.Video) ?? cfg.SplashVideo,
            SplashDuration = o.Duration ?? cfg.SplashDuration,
            ShowProgress = o.ShowProgress ?? cfg.ShowProgress,
            ShowVersion = o.ShowVersion ?? cfg.ShowVersion,
            SplashText = o.Text ?? cfg.SplashText,
            SplashTextKey = o.TextKey ?? cfg.SplashTextKey,
            BrandTitle = o.BrandTitle ?? cfg.BrandTitle,
            BrandSubtitle = o.BrandSubtitle ?? cfg.BrandSubtitle
        };
    }

    /// <summary>把游戏根目录内的绝对路径转成内容服务器可用的相对路径；空值返回 null（沿用原配置）。</summary>
    private string? Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        path = path.Trim();
        if (!Path.IsPathRooted(path))
            return path;

        if (_env.GamePath is null)
            return path;

        string relative = Path.GetRelativePath(_env.GamePath, path);
        return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
    }
}
