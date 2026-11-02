using System.IO;
using CnCNetClient.Core;
using CnCNetClient.Tools;
using Serilog;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 主菜单视口视频（首页左侧）：默认播放嵌入程序集的 YR 视频；
/// ClientDefinitions.ini <c>[MainMenu] Video=</c> 可指定 <c>yr</c> / <c>ra2</c> 切换内置视频，
/// 或写游戏根目录相对路径（如 <c>Resources/mainmenu.mp4</c>）使用目录里的自定义视频
/// （与闪屏 [LoadingScreen] SplashVideo 同样的路径语义，经 <see cref="GameContentServer"/> 伺服）。
/// </summary>
public sealed class MainMenuVideoService
{
    /// <summary>配置节（ClientDefinitions.ini）。</summary>
    public const string CONFIG_SECTION = "MainMenu";

    /// <summary>配置键：视频选择（空=内置 YR；yr/ra2=内置；其他=游戏根目录相对路径）。</summary>
    public const string CONFIG_KEY = "Video";

    private const string EMBEDDED_YR = "yr.mp4";
    private const string EMBEDDED_RA2 = "ra2.mp4";
    private const string EMBEDDED_PREFIX = "BlazorCncnetClient.Assets.MainMenu.";

    private static readonly object EmbeddedLock = new();
    private static readonly HashSet<string> EmbeddedRegistered = new(StringComparer.OrdinalIgnoreCase);

    private readonly EnvironmentCheck _env;
    private readonly GameContentServer _server;

    public MainMenuVideoService(EnvironmentCheck env, GameContentServer server)
    {
        _env = env;
        _server = server;
    }

    /// <summary>游戏根目录：优先环境检测结果，未检测时退回进程级 GamePath（内置视频不依赖此路径）。</summary>
    private string GameRoot => _env.GamePath ?? ProgramConstants.GamePath;

    /// <summary>解析当前应播放的视频 URL；内容服务器不可用时返回空串（视口保持纯色底）。</summary>
    public string ResolveUrl()
    {
        if (!_server.IsRunning)
            return string.Empty;

        EnsureEmbeddedRegistered();

        string configured = ReadConfiguredVideo();
        if (configured.Length > 0 && !IsBuiltIn(configured))
        {
            string rel = configured.Replace('\\', '/').TrimStart('/');
            string full = SafePath.CombineFilePath(GameRoot, rel);
            if (File.Exists(full))
                return _server.MapUrl(rel);

            Log.Warning("主菜单视频 {Video} 不存在，回退内置 YR 视频", configured);
        }

        return _server.MapEmbeddedUrl(IsRa2(configured) ? EMBEDDED_RA2 : EMBEDDED_YR);
    }

    /// <summary>读取 ClientDefinitions.ini [MainMenu] Video；文件缺失时返回空串（用默认内置视频）。</summary>
    private string ReadConfiguredVideo()
    {
        string defs = SafePath.CombineFilePath(GameRoot, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);
        if (!File.Exists(defs))
            return string.Empty;

        return new IniFile(defs).GetStringValue(CONFIG_SECTION, CONFIG_KEY, string.Empty).Trim();
    }

    private static bool IsBuiltIn(string value)
        => value.Equals("yr", StringComparison.OrdinalIgnoreCase)
            || value.Equals("ra2", StringComparison.OrdinalIgnoreCase);

    private static bool IsRa2(string value) => value.Equals("ra2", StringComparison.OrdinalIgnoreCase);

    /// <summary>把两个内置视频从程序集注册到内容服务器（静态去重，多电路共享）。</summary>
    private void EnsureEmbeddedRegistered()
    {
        lock (EmbeddedLock)
        {
            RegisterEmbedded(EMBEDDED_YR);
            RegisterEmbedded(EMBEDDED_RA2);
        }
    }

    private void RegisterEmbedded(string name)
    {
        if (!EmbeddedRegistered.Add(name))
            return;

        string resource = EMBEDDED_PREFIX + name;
        using Stream? stream = typeof(MainMenuVideoService).Assembly.GetManifestResourceStream(resource);
        if (stream is null)
        {
            EmbeddedRegistered.Remove(name);
            Log.Warning("主菜单视频嵌入资源缺失：{Resource}", resource);
            return;
        }

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        _server.RegisterEmbedded(name, buffer.ToArray());
    }
}
