using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using CnCNetClient.Core;
using CnCNetClient.Core.I18N;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure.Options;

/// <summary>
/// 从游戏目录的 lobby ini 声明式加载游戏选项（对齐原版机制）。
///
/// 原版并不按 <c>[chk*]</c> / <c>[cmb*]</c> 节名推断游戏选项，而是在 <c>[GameOptionsPanel]</c> 里用
/// <b>类型化控件集合</b>声明（<c>$CC-GO01=chkShortGame:GameLobbyCheckBox</c> 等）：只有类型为
/// <c>GameLobbyCheckBox</c> / <c>GameLobbyDropDown</c> 的控件才是游戏选项，其余（<c>XNALabel</c>、
/// <c>XNAControl</c>、<c>XNAClientButton</c>、<c>XNAClientCheckBox</c>…）只是窗口装饰。
/// 例如 <c>[chkAutoReady]</c> 虽以 chk 开头，但注册为 <c>XNAClientCheckBox</c>，<b>不是</b>游戏选项。
///
/// 因此本加载器：
/// ① 按模式读取对应的 lobby ini：遭遇战 <c>SkirmishLobby.ini</c>、在线房间 <c>MultiplayerGameLobby.ini</c>
///    （两者均 <c>BasedOn=GameLobbyBase.ini</c>，后者再 <c>BasedOn=GenericWindow.ini</c>）；
/// ② 经 <see cref="CCIniFile"/> 解析 <c>[INISystem] BasedOn</c> 文件继承与 <c>$BaseSection</c> 节继承
///    （故模式专有选项如 <c>cmbGameSpeedCapSkirmish</c> 会自动继承 <c>[cmbGameSpeedCap]</c> 的属性）；
/// ③ 取 <c>[GameOptionsPanel]</c> 中 <c>$CC*</c> 声明，按控件序号排序：复选框在前（即 GO 位序）、下拉框在后；
/// ④ 排除 <c>Enabled=no</c> / <c>Visible=no</c> 的选项（如原版关闭的 <c>cmbTechLevel</c>，改回 yes 即出现）；
/// ⑤ 纯 UI 几何键（<c>Location</c> / <c>$X</c> / <c>$Y</c> / <c>$Width</c> / <c>$Height</c> / <c>Size</c> /
///    <c>DrawMode</c> / 贴图）一律豁免，由 CSS / 主题承接。
///
/// 严格模式：ini 缺失、<c>[GameOptionsPanel]</c> 缺失或未解析到任何选项时抛
/// <see cref="ClientConfigurationException"/>（对齐原版 <c>DirectDrawWrapperManager</c> 的配置异常范式）。
/// </summary>
public static class GameOptionsIniLoader
{
    /// <summary>遭遇战 lobby ini 文件名（相对 Resources 目录）。</summary>
    public const string SKIRMISH_INI = "SkirmishLobby.ini";

    /// <summary>在线房间 lobby ini 文件名（相对 Resources 目录）。</summary>
    public const string MULTIPLAYER_INI = "MultiplayerGameLobby.ini";

    /// <summary>LAN 房间 lobby ini 文件名（相对 Resources 目录；BasedOn=MultiplayerGameLobby.ini，故共享 [GameOptionsPanel]）。</summary>
    public const string LAN_INI = "LANGameLobby.ini";

    private const string PanelSection = "GameOptionsPanel";
    private const string CollectionPrefix = "$CC";
    private const string CheckBoxType = "GameLobbyCheckBox";
    private const string DropDownType = "GameLobbyDropDown";

    private static readonly Regex IndexPattern = new(@"\d+", RegexOptions.Compiled);

    /// <summary>按文件名从当前游戏目录加载（路径取 <c>ProgramConstants.GetBaseResourcePath()</c>）。</summary>
    public static List<GameSessionOption> Load(string iniFileName) =>
        LoadFromPath(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), iniFileName));

    /// <summary>从指定 lobby ini 路径加载。</summary>
    /// <exception cref="ClientConfigurationException">ini 缺失、面板缺失或未解析到任何游戏选项。</exception>
    public static List<GameSessionOption> LoadFromPath(string iniPath)
    {
        if (!SafePath.GetFile(iniPath).Exists)
        {
            throw new ClientConfigurationException(
                $"Lobby configuration file was not found: '{iniPath}'.");
        }

        var ini = new CCIniFile(iniPath);

        List<string>? keys = ini.GetSectionKeys(PanelSection);
        if (keys is null || !keys.Any(k => k.StartsWith(CollectionPrefix, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ClientConfigurationException(
                $"Section '[{PanelSection}]' with '$CC' control declarations was not found in '{iniPath}'.");
        }

        var checkBoxes = new List<DeclaredControl>();
        var dropDowns = new List<DeclaredControl>();

        foreach (string key in keys)
        {
            if (!key.StartsWith(CollectionPrefix, StringComparison.OrdinalIgnoreCase))
                continue;

            string declaration = ini.GetStringValue(PanelSection, key, string.Empty);
            int separator = declaration.IndexOf(':');
            if (separator < 1)
            {
                throw new ClientConfigurationException(
                    $"Invalid control declaration '{key}={declaration}' in '[{PanelSection}]' of '{iniPath}'.");
            }

            string name = declaration.Substring(0, separator).Trim();
            string type = declaration.Substring(separator + 1).Trim();

            if (string.Equals(type, CheckBoxType, StringComparison.OrdinalIgnoreCase))
                checkBoxes.Add(new DeclaredControl(ParseIndex(key), name));
            else if (string.Equals(type, DropDownType, StringComparison.OrdinalIgnoreCase))
                dropDowns.Add(new DeclaredControl(ParseIndex(key), name));
        }

        var options = new List<GameSessionOption>();

        // 复选框在前：其顺序即 GO 协议位序（GetGameOptionsCode），房主与加入者口径必须一致。
        foreach (DeclaredControl control in checkBoxes.OrderBy(c => c.Index))
        {
            if (IsDisabledOrHidden(ini, control.Name))
                continue;

            options.Add(CreateCheckBox(ini, control.Name, iniPath));
        }

        foreach (DeclaredControl control in dropDowns.OrderBy(c => c.Index))
        {
            if (IsDisabledOrHidden(ini, control.Name))
                continue;

            options.Add(CreateDropDown(ini, control.Name, iniPath));
        }

        if (options.Count == 0)
        {
            throw new ClientConfigurationException(
                $"No game options were declared in '[{PanelSection}]' of '{iniPath}'.");
        }

        ApplyBroadcastOptions(options);
        return options;
    }

    private static GameSessionCheckBoxOption CreateCheckBox(IniFile ini, string section, string iniPath)
    {
        string text = ini.GetStringValue(section, "Text", string.Empty);
        text = Translation.Instance.LookUp($"INI:Controls:GameOptionsPanel:{section}:Text", text);

        return new GameSessionCheckBoxOption(
            section,
            string.IsNullOrWhiteSpace(text) ? section : text,
            spawnIniOption: NullIfEmpty(ini.GetStringValue(section, "SpawnIniOption", string.Empty)),
            customIniPath: NullIfEmpty(ini.GetStringValue(section, "CustomIniPath", string.Empty)),
            reversed: ini.GetBooleanValue(section, "Reversed", false),
            defaultChecked: ini.GetBooleanValue(section, "Checked", false))
        {
            ToolTip = TranslateToolTip(section, ini)
        };
    }

    private static GameSessionDropDownOption CreateDropDown(IniFile ini, string section, string iniPath)
    {
        string itemsRaw = ini.GetStringValue(section, "Items", string.Empty);
        string[] items = itemsRaw
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (items.Length == 0)
        {
            throw new ClientConfigurationException(
                $"Game option drop-down '{section}' declared in '[{PanelSection}]' of '{iniPath}' has no 'Items'.");
        }

        string optionName = ini.GetStringValue(section, "OptionName", string.Empty);
        optionName = Translation.Instance.LookUp($"INI:Controls:GameOptionsPanel:{section}:OptionName", optionName);

        return new GameSessionDropDownOption(
            section,
            string.IsNullOrWhiteSpace(optionName) ? section : optionName,
            items,
            defaultIndex: ini.GetIntValue(section, "DefaultIndex", 0),
            spawnIniOption: NullIfEmpty(ini.GetStringValue(section, "SpawnIniOption", string.Empty)),
            writeMode: ParseWriteMode(ini, section, iniPath))
        {
            ToolTip = TranslateToolTip(section, ini)
        };
    }

    /// <summary>按原版 Translation.ini 键格式查找 ToolTip 翻译（INI:Controls:GameOptionsPanel:&lt;section&gt;:ToolTip）。</summary>
    private static string? TranslateToolTip(string section, IniFile ini)
    {
        string tooltip = ini.GetStringValue(section, "ToolTip", string.Empty);
        tooltip = Translation.Instance.LookUp($"INI:Controls:GameOptionsPanel:{section}:ToolTip", tooltip);
        return NullIfEmpty(tooltip);
    }

    private static GameOptionWriteMode ParseWriteMode(IniFile ini, string section, string iniPath)
    {
        string raw = ini.GetStringValue(section, "DataWriteMode", "String").Trim();

        return raw.ToLowerInvariant() switch
        {
            "" or "string" => GameOptionWriteMode.String,
            "index" => GameOptionWriteMode.Index,
            "boolean" or "bool" => GameOptionWriteMode.Boolean,
            "mapcode" => GameOptionWriteMode.MapCode,
            _ => throw new ClientConfigurationException(
                $"Invalid DataWriteMode '{raw}' for game option '{section}' in '{iniPath}'.")
        };
    }

    /// <summary>
    /// 广播集在 ini 中无对应键（原版 GameLobbyBase.ini 不存在 BroadcaseToLobby 键，源码注释引用有误），
    /// 沿用代码默认集的定义（<see cref="GameSessionOption.CreateDefaultGameOptions"/>）作为结构模板，
    /// 并补充模式专有的「游戏速度上限」下拉——它影响 spawn.ini [Settings] GameSpeed，需与客户端同步。
    /// </summary>
    private static void ApplyBroadcastOptions(List<GameSessionOption> options)
    {
        var broadcast = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (GameSessionOption option in GameSessionOption.CreateDefaultGameOptions())
        {
            if (option.BroadcastToLobby)
                broadcast.Add(option.Name);
        }

        broadcast.Add("cmbGameSpeedCapSkirmish");
        broadcast.Add("cmbGameSpeedCapMultiplayer");

        foreach (GameSessionOption option in options)
            option.BroadcastToLobby = broadcast.Contains(option.Name);
    }

    private static bool IsDisabledOrHidden(IniFile ini, string section) =>
        !ini.GetBooleanValue(section, "Enabled", true) || !ini.GetBooleanValue(section, "Visible", true);

    /// <summary>取控件集合键中的首个数字段作为序号（如 <c>$CC-GO01</c> → 1、<c>$CC-GODD07</c> → 7）。</summary>
    private static int ParseIndex(string key)
    {
        Match match = IndexPattern.Match(key);
        return match.Success && int.TryParse(match.Value, out int index) ? index : 0;
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private readonly record struct DeclaredControl(int Index, string Name);
}
