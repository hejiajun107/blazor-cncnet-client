using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// UI 扩展配置（F3）：懒读取 ClientDefinitions.ini 的 [UI] 节，MOD 可定制 UI 行为。
/// 环境未就绪时返回默认值；不存在的键返回默认。为 F1 主题目录/F2 组件覆盖打底。
/// </summary>
public sealed class UiDefinitionService
{
    private const string Section = "UI";

    private readonly EnvironmentCheck _env;
    private IniFile? _defs;

    public UiDefinitionService(EnvironmentCheck env) => _env = env;

    /// <summary>游戏根目录（环境就绪后可用）。</summary>
    public string? RootPath => _env.GamePath;

    /// <summary>ClientDefinitions.ini（已缓存；环境未就绪为 null）。</summary>
    public IniFile? Definitions => EnsureLoaded();

    /// <summary>初始主题（settings 未持久化主题时使用；空 = 内置默认「menu」）。</summary>
    public string DefaultTheme => GetString("DefaultTheme", string.Empty);

    /// <summary>
    /// 隐藏的主菜单入口键（逗号分隔，不区分大小写）。
    /// 默认隐藏「lore」（原版客户端无资料库入口，占位项默认不显示）；
    /// MOD 显式写入 HiddenMenus=（空）或省略 lore 即可重新显示。
    /// 可选：campaign,multiplayer,skirmish,lan,load,lore,stats,settings,mapeditor,credits,extras,exit。
    /// </summary>
    public IReadOnlySet<string> HiddenMenus => GetString("HiddenMenus", "lore")
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private IniFile? _mainMenuIni;
    private bool _mainMenuIniLoaded;

    /// <summary>
    /// 内置主菜单入口是否可见：遵循 <c>Resources\MainMenu.ini</c> 的 <c>[btn*] Visible</c>
    /// （与官方 XNA 主菜单同一套开关；文件/节/键缺失时视为可见）。
    /// 与 <see cref="HiddenMenus"/> 叠加：任一隐藏即不显示。
    /// </summary>
    public bool IsMainMenuEntryVisible(string menuKey)
    {
        EnsureMainMenuLoaded();
        return MainMenuConfiguration.IsVisible(_mainMenuIni, menuKey);
    }

    private void EnsureMainMenuLoaded()
    {
        if (_mainMenuIniLoaded)
            return;
        _mainMenuIniLoaded = true;

        if (!_env.IsReady || _env.GamePath is null)
            return;

        string path = SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), MainMenuConfiguration.FILE_NAME);
        if (File.Exists(path))
            _mainMenuIni = new IniFile(path);
    }

    /// <summary>读取 [UI] 节任意键的字符串值（组件可注入使用）。</summary>
    public string GetString(string key, string fallback = "")
    {
        var ini = EnsureLoaded();
        return ini?.GetStringValue(Section, key, fallback).Trim() ?? fallback;
    }

    /// <summary>读取 [UI] 节布尔键值（不存在返回 fallback）。</summary>
    public bool GetBool(string key, bool fallback = false)
    {
        var ini = EnsureLoaded();
        return ini?.GetBooleanValue(Section, key, fallback) ?? fallback;
    }

    private IniFile? EnsureLoaded()
    {
        if (_defs is not null)
            return _defs;

        if (!_env.IsReady || _env.GamePath is null)
            return null;

        string path = SafePath.CombineFilePath(_env.GamePath, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);
        if (!File.Exists(path))
            return null;

        _defs = new IniFile(path);
        return _defs;
    }
}
