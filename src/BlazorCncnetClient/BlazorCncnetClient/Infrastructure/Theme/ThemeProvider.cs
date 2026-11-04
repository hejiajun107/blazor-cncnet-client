using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CnCNetClient.Core;
using CnCNetClient.Tools;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorCncnetClient.Infrastructure.Theme;

/// <summary>
/// 主题提供者默认实现（M4.1：持久化迁移至 settings INI，spec §6.10 方案 A；F1 外部主题目录）。
/// 内置主题：
///   · menu  — 幽能星港 · 青单色 HUD（主菜单，app.css :root 默认）
///   · inner — 幽能星港 · 蓝色指挥台（内页，:root[data-theme="inner"]）
///   · ra    — 经典 Red Alert 风格（:root[data-theme="ra"]，金×橄榄军绿）
/// 外部主题（F1，MOD 可定制）：ClientDefinitions.ini [Themes] 节声明 key=游戏根目录相对主题目录；
/// 目录约定含 theme.css（覆盖 design tokens 整体换肤，url() 相对资源经 GameContentServer 伺服）
/// 与可选 theme.ini（[Theme] Name/Hint 中文名）。
/// 持久化：settings INI <c>[MultiPlayer] Theme</c>（原版语义，跨客户端一致）为权威来源；
/// localStorage（key: cncnet.theme）仅作 WebView 首帧前即时回显缓存（app.js 启动时应用）。
/// </summary>
public sealed class ThemeProvider : IThemeProvider
{
    /// <summary>原版主题配置文件（各主题目录内各一份，[General] MainMenuTheme 指定主菜单音乐）。</summary>
    private const string THEME_INI_NAME = "DTACnCNetClient.ini";
    private const string RESOURCES_DIRECTORY = "Resources";
    private const string DEFAULT_THEME_DIRECTORY = "Resources/ThemeDefault";

    /// <summary>浏览器可直接播放的音频扩展名（原版 .wma 不被 Chromium 支持，故按此顺序探测同名文件）。</summary>
    private static readonly string[] WEB_AUDIO_EXTENSIONS = [".mp3", ".ogg", ".wav"];

    private static readonly ThemeInfo Menu = new("menu",
        UiText.T("Theme.MenuName", "Main menu"), UiText.T("Theme.MenuHint", "Monochrome teal HUD"));
    private static readonly ThemeInfo Inner = new("inner",
        UiText.T("Theme.InnerName", "Inner pages"), UiText.T("Theme.InnerHint", "Blue command deck"));
    private static readonly ThemeInfo Ra = new("ra",
        UiText.T("Theme.RaName", "Red Alert"), UiText.T("Theme.RaHint", "Classic Red Alert · tactical command"));

    private readonly IJSRuntime _js;
    private readonly ILogger<ThemeProvider> _logger;
    private readonly UiDefinitionService _uiDefs;
    private readonly GameContentServer _content;
    private readonly List<ThemeInfo> _themes = [Menu, Inner, Ra];
    private readonly Dictionary<string, string> _externalCss = new(StringComparer.OrdinalIgnoreCase);
    private ThemeInfo _current = Menu;
    private bool _initialized;

    public ThemeProvider(IJSRuntime js, ILogger<ThemeProvider> logger, UiDefinitionService uiDefs, GameContentServer contentServer)
    {
        _js = js;
        _logger = logger;
        _uiDefs = uiDefs;
        _content = contentServer;
    }

    public ThemeInfo Current => _current;

    public IReadOnlyList<ThemeInfo> Themes => _themes;

    public event Action<ThemeInfo>? ThemeChanged;

    public async Task InitializeAsync()
    {
        if (_initialized)
        {
            return;
        }

        _initialized = true;
        try
        {
            // 主菜单音乐：先解析内置主题（原版默认主题资源），再随外部主题目录逐个解析
            ResolveBuiltInMenuMusic();

            // F1：加载外部主题目录（[Themes] 节）并把 CSS 地址表交给前端注入
            LoadExternalThemes();
            await _js.InvokeVoidAsync("cncnet.setThemeUrls", _externalCss);

            // 权威来源：settings INI [MultiPlayer] Theme（spec §6.10 方案 A）；
            // 未持久化时回退 ClientDefinitions.ini [UI] DefaultTheme（F3，MOD 可定制默认主题）；
            // localStorage 仅作首帧前回显缓存，首帧后以 INI 校正。
            var persisted = UserINISettings.Instance.ClientTheme.Value;
            if (string.IsNullOrEmpty(persisted))
                persisted = _uiDefs.DefaultTheme;
            var theme = Resolve(persisted);
            _current = Resolve(Menu.Key);
            if (theme.Key != Menu.Key)
            {
                await ApplyAsync(theme.Key);
            }
            else
            {
                _logger.LogInformation("主题初始化：{Key}（默认）", theme.Key);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "主题初始化失败，回退默认主题");
        }
    }

    public async Task ApplyAsync(string key)
    {
        var theme = Resolve(key);
        if (theme.Key == _current.Key)
        {
            return;
        }

        _current = theme;
        try
        {
            await _js.InvokeVoidAsync("cncnet.applyTheme", theme.Key);
            // 持久化至 settings INI（原版语义，跨客户端一致；localStorage 由 applyTheme 同步作缓存）
            var ini = UserINISettings.Instance;
            if (ini.ClientTheme.Value != theme.Key)
            {
                ini.ClientTheme.Value = theme.Key;
                ini.SaveSettings();
            }
            ThemeChanged?.Invoke(theme);
            _logger.LogInformation("主题已切换：{Key} - {Name}", theme.Key, theme.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "主题切换失败：{Key}", theme.Key);
        }
    }

    private ThemeInfo Resolve(string? key) =>
        _themes.FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)) ?? Menu;

    /// <summary>
    /// 加载外部主题（F1）：ClientDefinitions.ini [Themes] 节 key=主题目录。
    /// 主题目录约定含 theme.css 与可选 theme.ini（[Theme] Name/Hint）。
    /// 与内置主题重名的 key 忽略（保留内置）。服务器未就绪/环境未就绪时跳过（下次初始化再试）。
    /// </summary>
    private void LoadExternalThemes()
    {
        if (_themes.Count > 3 || !_content.IsRunning || _uiDefs.RootPath is null)
            return;

        var defs = _uiDefs.Definitions;
        if (defs is null)
            return;

        foreach ((string name, string dir) in GetThemeEntries())
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;
            if (_themes.Any(t => string.Equals(t.Key, name, StringComparison.OrdinalIgnoreCase)))
                continue; // 与内置主题重名：忽略

            // 原版约定：[Themes] 的值相对 Resources 目录（"Theme directory in Resources directory"）
            string relativeDir = RESOURCES_DIRECTORY + "/" + dir;
            string displayName = name;
            string hint = dir;
            string metaPath = SafePath.CombineFilePath(_uiDefs.RootPath, relativeDir, "theme.ini");
            if (File.Exists(metaPath))
            {
                var meta = new IniFile(metaPath);
                displayName = meta.GetStringValue("Theme", "Name", name).Trim();
                hint = meta.GetStringValue("Theme", "Hint", dir).Trim();
            }

            _themes.Add(new ThemeInfo(name, displayName, hint, ResolveMenuMusicUrl(relativeDir)));
            _externalCss[name] = _content.MapUrl(SafePath.CombineFilePath(relativeDir, "theme.css"));
            _logger.LogInformation("外部主题已加载：{Key} → {Dir}", name, relativeDir);
        }
    }

    /// <summary>
    /// 内置主题（menu/inner/ra）没有独立目录，统一取原版默认主题资源里的主菜单音乐。
    /// </summary>
    private void ResolveBuiltInMenuMusic()
    {
        string? url = ResolveMenuMusicUrl(null);
        for (int i = 0; i < _themes.Count; i++)
        {
            if (_themes[i].MusicUrl is null)
                _themes[i] = _themes[i] with { MusicUrl = url };
        }

        if (url is not null)
            _logger.LogInformation("内置主题主菜单音乐：{Url}", url);
    }

    /// <summary>
    /// 解析主菜单音乐（原版 [General] MainMenuTheme 语义）。
    /// 搜索顺序：传入的主题目录 → Resources/ThemeDefault（原版默认主题）→ ClientDefinitions.ini [Themes]
    /// 声明的各主题目录 → Resources 根，取第一个既有 DTACnCNetClient.ini 且 MainMenuTheme 能落到实际音频文件的配置。
    /// 值可含多首（逗号分隔，原版随机取一首），路径相对配置文件所在目录。
    /// </summary>
    private string? ResolveMenuMusicUrl(string? themeDir)
    {
        string? root = _uiDefs.RootPath;
        if (string.IsNullOrEmpty(root) || !_content.IsRunning)
            return null;

        var dirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(themeDir))
            dirs.Add(themeDir.Trim('/', '\\'));
        dirs.Add(DEFAULT_THEME_DIRECTORY);
        // 对齐上游：主菜单音乐取自主题目录的 DTACnCNetClient.ini。内置主题没有独立目录，
        // 而部分资源包（如按 DTA 布局制作的包）不带 Resources/ThemeDefault（原版默认主题目录名是
        // Defaults Theme），因此再按 [Themes] 声明的主题目录顺序回退，最后才是 Resources 根。
        dirs.AddRange(GetThemeEntries().Select(e => RESOURCES_DIRECTORY + "/" + e.Directory));
        dirs.Add(RESOURCES_DIRECTORY);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string dir in dirs)
        {
            string normalized = dir.Trim('/', '\\').Replace('\\', '/');
            if (normalized.Length == 0 || !visited.Add(normalized))
                continue;
            string iniPath = SafePath.CombineFilePath(root, normalized, THEME_INI_NAME);
            if (!File.Exists(iniPath))
                continue;

            string raw = new IniFile(iniPath).GetStringValue("General", "MainMenuTheme", string.Empty).Trim();
            // 对齐上游：键缺失/为空时回退 "mainmenu"（ClientConfiguration.GetMainMenuMusicName 的默认值）
            if (raw.Length == 0)
                raw = "mainmenu";

            var found = new List<string>();
            string? wmaOnly = null;
            foreach (string name in raw.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? relative = ResolveAudioFile(root, normalized, name, out string? wmaHint);
                if (relative is not null)
                    found.Add(relative);
                else if (wmaHint is not null)
                    wmaOnly ??= wmaHint;
            }

            if (found.Count > 0)
            {
                // 原版 [General] MainMenuTheme 支持逗号分隔多首并随机取一首，此处保持一致
                string chosen = found[Random.Shared.Next(found.Count)];
                _logger.LogInformation("主菜单音乐已解析：{Dir} / {Path}", normalized, chosen);
                return _content.MapUrl(chosen);
            }

            if (wmaOnly is not null)
            {
                // 原版默认资源就是 .wma，但 Chromium/WebView2 不带 WMA 解码器，无法播放
                _logger.LogWarning("主菜单音乐只有 WMA，WebView2 无法解码：{Path}；请转换为同名 .mp3 或 .ogg", wmaOnly);
            }
            else
            {
                _logger.LogInformation("主菜单音乐未找到音频文件：{Ini} 的 MainMenuTheme={Raw}", iniPath, raw);
            }
        }

        return null;
    }

    /// <summary>
    /// ClientDefinitions.ini [Themes] 的条目（显示名, 主题目录），按声明顺序。
    /// 原版值格式为 <c>&lt;显示名&gt;,&lt;相对 Resources 的目录&gt;</c>，例如
    /// <c>0=Default,ThemeDefault/</c> 指向 <c>Resources\ThemeDefault</c>。
    /// </summary>
    private IReadOnlyList<(string Name, string Directory)> GetThemeEntries()
    {
        var defs = _uiDefs.Definitions;
        if (defs is null)
            return [];

        const string section = "Themes";
        var entries = new List<(string Name, string Directory)>();
        foreach (string key in defs.GetSectionKeys(section))
        {
            string raw = defs.GetStringValue(section, key, string.Empty).Trim();
            if (raw.Length == 0)
                continue;

            string[] parts = raw.Split(',', 2, StringSplitOptions.TrimEntries);
            string name = parts.Length > 1 && parts[0].Length > 0 ? parts[0] : key;
            string dir = (parts.Length > 1 ? parts[1] : parts[0]).Trim().Trim('/', '\\');
            if (dir.Length == 0)
                continue;

            entries.Add((name, dir));
        }

        return entries;
    }

    /// <summary>
    /// 把 MainMenuTheme 的值解析成游戏根相对的文件路径。
    /// 原版用 .ogg/.wma，浏览器只认 mp3/ogg/wav，故忽略值里的扩展名，
    /// 按 .mp3 → .ogg → .wav 顺序探测同名文件；都没有时把 .wma 路径通过 <paramref name="wmaHint"/>
    /// 返回，供调用方给出「WMA 无法解码」的明确提示。
    /// 搜索目录：配置文件所在目录 → Resources → 游戏根（原版主题 ini 的值常相对 Resources 根，
    /// 例如 <c>Resources/MainMenuTheme.wma</c>）；值本身自带目录时直接按游戏根相对路径解析。
    /// </summary>
    private static string? ResolveAudioFile(string root, string configDir, string value, out string? wmaHint)
    {
        wmaHint = null;

        string relative = value.Trim().Trim('/', '\\').Replace('\\', '/');
        if (relative.Length == 0)
            return null;

        bool explicitDirectory = relative.Contains('/');
        string withoutExtension = Path.HasExtension(relative)
            ? relative[..relative.LastIndexOf('.')]
            : relative;

        string[] dirs = explicitDirectory
            // 对齐上游：含目录的值以 Resources 为基准（如 Default Theme\MainMenuTheme）；
            // 同时保留相对游戏根的形式，兼容把主题资源直接放在安装根的资源包。
            ? [RESOURCES_DIRECTORY, string.Empty]
            : [configDir, RESOURCES_DIRECTORY, string.Empty];

        foreach (string dir in dirs)
        {
            string prefix = dir.Length == 0 ? string.Empty : dir.Replace('\\', '/').TrimEnd('/') + "/";

            foreach (string extension in WEB_AUDIO_EXTENSIONS)
            {
                string candidate = prefix + withoutExtension + extension;
                if (File.Exists(SafePath.CombineFilePath(root, candidate)))
                    return candidate;
            }

            string wma = prefix + withoutExtension + ".wma";
            if (wmaHint is null && File.Exists(SafePath.CombineFilePath(root, wma)))
                wmaHint = wma;
        }

        return null;
    }
}
