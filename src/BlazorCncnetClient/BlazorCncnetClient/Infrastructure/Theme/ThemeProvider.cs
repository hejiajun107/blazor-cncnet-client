using System;
using System.Collections.Generic;
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
    private static readonly ThemeInfo Menu = new("menu", "主菜单", "青单色 HUD");
    private static readonly ThemeInfo Inner = new("inner", "内页", "蓝色指挥台");
    private static readonly ThemeInfo Ra = new("ra", "红警", "经典 Red Alert · 指挥作战");

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
            _current = Menu;
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
    /// 加载外部主题（F1）：ClientDefinitions.ini [Themes] 节 key=游戏根目录相对主题目录。
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

        const string section = "Themes";
        foreach (string key in defs.GetSectionKeys(section))
        {
            if (string.IsNullOrWhiteSpace(key))
                continue;
            if (_themes.Any(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase)))
                continue; // 与内置主题重名：忽略

            string dir = defs.GetStringValue(section, key, string.Empty).Trim().Trim('/', '\\');
            if (dir.Length == 0)
                continue;

            string name = key;
            string hint = dir;
            string metaPath = SafePath.CombineFilePath(_uiDefs.RootPath, dir, "theme.ini");
            if (File.Exists(metaPath))
            {
                var meta = new IniFile(metaPath);
                name = meta.GetStringValue("Theme", "Name", key).Trim();
                hint = meta.GetStringValue("Theme", "Hint", dir).Trim();
            }

            _themes.Add(new ThemeInfo(key, name, hint));
            _externalCss[key] = _content.MapUrl(SafePath.CombineFilePath(dir, "theme.css"));
            _logger.LogInformation("外部主题已加载：{Key} → {Dir}", key, dir);
        }
    }
}
