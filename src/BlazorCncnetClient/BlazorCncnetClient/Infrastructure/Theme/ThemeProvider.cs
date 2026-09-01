using System;

using CnCNetClient.Core;

using Microsoft.Extensions.Logging;
using Microsoft.JSInterop;

namespace BlazorCncnetClient.Infrastructure.Theme;

/// <summary>
/// 主题提供者默认实现（M4.1：持久化迁移至 settings INI，spec §6.10 方案 A）。
/// 已知主题：
///   · menu  — 幽能星港 · 青单色 HUD（主菜单，app.css :root 默认）
///   · inner — 幽能星港 · 蓝色指挥台（内页，:root[data-theme="inner"]）
///   · ra    — 经典 Red Alert 风格（:root[data-theme="ra"]，金×橄榄军绿）
/// 持久化：settings INI <c>[MultiPlayer] Theme</c>（原版语义，跨客户端一致）为权威来源；
/// localStorage（key: cncnet.theme）仅作 WebView 首帧前即时回显缓存（app.js 启动时应用）。
/// </summary>
public sealed class ThemeProvider : IThemeProvider
{
    private static readonly ThemeInfo Menu = new("menu", "主菜单", "幽能星港 · 青单色 HUD");
    private static readonly ThemeInfo Inner = new("inner", "内页", "幽能星港 · 蓝色指挥台");
    private static readonly ThemeInfo Ra = new("ra", "红警", "经典 Red Alert · 指挥作战");

    private readonly IJSRuntime _js;
    private readonly ILogger<ThemeProvider> _logger;
    private ThemeInfo _current = Menu;
    private bool _initialized;

    public ThemeProvider(IJSRuntime js, ILogger<ThemeProvider> logger)
    {
        _js = js;
        _logger = logger;
    }

    public ThemeInfo Current => _current;

    public IReadOnlyList<ThemeInfo> Themes { get; } = [Menu, Inner, Ra];

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
            // 权威来源：settings INI [MultiPlayer] Theme（spec §6.10 方案 A）；
            // localStorage 仅作首帧前回显缓存，首帧后以 INI 校正。
            var persisted = UserINISettings.Instance.ClientTheme.Value;
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

    private static ThemeInfo Resolve(string? key) =>
        (Menu.Key == key ? Menu : Inner.Key == key ? Inner : Ra.Key == key ? Ra : Menu);
}
