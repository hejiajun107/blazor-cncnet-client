using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure.Options;

/// <summary>设置页控件类型（原版 <c>OptionsWindow.ini</c> 的 <c>&lt;控件名&gt;:&lt;类型&gt;</c>）。</summary>
public enum SettingControlType
{
    /// <summary>普通设置复选框（勾选值写入 SettingSection / SettingKey）。</summary>
    SettingCheckBox,

    /// <summary>文件设置复选框（勾选时按 EnabledFile* 策略复制文件，并写回 SettingSection / SettingKey）。</summary>
    FileSettingCheckBox
}

/// <summary><c>EnabledFile</c> 的文件更新策略（原版实测两种）。</summary>
public enum FileUpdatePolicy
{
    /// <summary>目标已存在且内容不同时覆盖。</summary>
    OverwriteOnMismatch,

    /// <summary>保留现状：仅当目标不存在时复制。</summary>
    KeepChanges
}

/// <summary><c>EnabledFile&lt;n&gt;=源,目标[,策略]</c> 的解析结果（源 / 目标均相对游戏目录）。</summary>
public sealed record EnabledFileEntry(string Source, string Target, FileUpdatePolicy Policy);

/// <summary>额外控件注册项：<c>[&lt;面板&gt;ExtraControls] n=控件名:类型</c>。</summary>
public sealed record SettingControl(string Name, SettingControlType Type);

/// <summary>控件属性节 <c>[&lt;控件名&gt;]</c> 的解析结果。</summary>
public sealed record SettingControlAttributes(
    string? Text,
    string? ToolTip,
    string? DefaultValue,
    string? SettingSection,
    string? SettingKey,
    bool Enabled,
    bool Visible,
    string? ParentCheckBoxName,
    string? ParentCheckBoxRequiredValue,
    IReadOnlyList<EnabledFileEntry> EnabledFiles);

/// <summary>一个 <c>OptionsWindow.ini</c> 的解析结果。</summary>
public sealed class OptionsWindowConfig
{
    private readonly Dictionary<string, List<SettingControl>> _bySection;
    private readonly Dictionary<string, SettingControlAttributes> _attributes;

    internal OptionsWindowConfig(
        Dictionary<string, List<SettingControl>> bySection,
        Dictionary<string, SettingControlAttributes> attributes)
    {
        _bySection = bySection;
        _attributes = attributes;
    }

    /// <summary>该移植版分区注册的全部额外控件（按 ini 中的序号顺序）。</summary>
    public IReadOnlyList<SettingControl> ControlsFor(string section) =>
        _bySection.TryGetValue(section, out List<SettingControl>? list)
            ? list
            : Array.Empty<SettingControl>();

    /// <summary>全部已注册的额外控件（用于保存时统一提交）。</summary>
    public IEnumerable<SettingControl> AllControls => _bySection.Values.SelectMany(v => v);

    /// <summary>控件属性节；ini 中无该节时返回 <see langword="null"/>（无覆盖）。</summary>
    public SettingControlAttributes? Attributes(string controlName) =>
        _attributes.TryGetValue(controlName, out SettingControlAttributes? a) ? a : null;
}

/// <summary>
/// 设置页 ini 驱动机制的解析器（对齐原版 <c>OptionsWindow.ini</c>）。
///
/// 原版设置窗口的控件清单由代码创建，ini 只负责三件事：
/// ① <c>[&lt;面板&gt;ExtraControls]</c> 额外控件注册（<c>n=控件名:类型</c>，面板名见 <see cref="PanelToSection"/>）；
/// ② 各控件节 <c>[&lt;控件名&gt;]</c> 的属性覆盖（<c>Text</c> / <c>ToolTip</c> / <c>DefaultValue</c> /
///    <c>SettingSection</c> / <c>SettingKey</c> / <c>Enabled</c> / <c>Visible</c> /
///    <c>ParentCheckBoxName</c> / <c>ParentCheckBoxRequiredValue</c> / <c>EnabledFile&lt;n&gt;</c>）；
/// ③ 值绑定：<c>SettingSection</c> + <c>SettingKey</c> 指向任意 ini 节/键。
/// 纯 UI 几何（<c>Location</c> / <c>$X</c> / <c>$Y</c> / <c>$Width</c> / <c>$Height</c>）豁免，由 CSS 承接。
///
/// 严格模式：ini 缺失、控件类型不认识、<c>EnabledFile</c> 策略非法、或注册控件缺少值绑定键时，
/// 抛 <see cref="ClientConfigurationException"/>（对齐原版 <c>DirectDrawWrapperManager</c> 的配置异常范式）。
/// </summary>
public static class OptionsWindowIni
{
    /// <summary>设置页 ini 文件名（相对 Resources 目录）。</summary>
    public const string FILE_NAME = "OptionsWindow.ini";

    /// <summary>原版面板名 → 移植版分区键（<c>privacy</c> 为移植版独有，不接受 ExtraControls）。</summary>
    public static readonly IReadOnlyList<(string Panel, string Section)> PanelToSection = new[]
    {
        ("DisplayOptionsPanel", "display"),
        ("AudioOptionsPanel", "audio"),
        ("GameOptionsPanel", "game"),
        ("CnCNetOptionsPanel", "network"),
        ("UpdaterOptionsPanel", "update"),
        ("ComponentsPanel", "components"),
    };

    private const string ExtraControlsSuffix = "ExtraControls";

    private static readonly Lazy<OptionsWindowConfig> LazyDefault =
        new(() => Load(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), FILE_NAME)));

    /// <summary>当前配置（首次访问时从 <c>Resources/OptionsWindow.ini</c> 加载并缓存）。</summary>
    public static OptionsWindowConfig Current => LazyDefault.Value;

    /// <summary>从指定 ini 路径加载（不做缓存；供测试使用）。</summary>
    /// <exception cref="ClientConfigurationException">ini 缺失或内容非法。</exception>
    public static OptionsWindowConfig Load(string iniPath)
    {
        if (!SafePath.GetFile(iniPath).Exists)
        {
            throw new ClientConfigurationException(
                $"Settings configuration file was not found: '{iniPath}'.");
        }

        var ini = new CCIniFile(iniPath);
        var bySection = new Dictionary<string, List<SettingControl>>(StringComparer.OrdinalIgnoreCase);
        var attributes = new Dictionary<string, SettingControlAttributes>(StringComparer.OrdinalIgnoreCase);

        foreach ((string panel, string section) in PanelToSection)
        {
            List<string>? keys = ini.GetSectionKeys(panel + ExtraControlsSuffix);
            if (keys is null)
                continue;

            var controls = keys
                .Select(key => ParseRegistration(ini, panel + ExtraControlsSuffix, key, iniPath))
                .OrderBy(entry => entry.Index)
                .Select(entry => entry.Control)
                .ToList();

            if (controls.Count == 0)
                continue;

            bySection[section] = controls;

            foreach (SettingControl control in controls)
            {
                SettingControlAttributes parsed = ParseAttributes(ini, control.Name, iniPath);
                if (string.IsNullOrWhiteSpace(parsed.SettingSection) || string.IsNullOrWhiteSpace(parsed.SettingKey))
                {
                    throw new ClientConfigurationException(
                        $"Setting control '{control.Name}' registered in '[{panel}{ExtraControlsSuffix}]' of '{iniPath}' " +
                        "must declare both 'SettingSection' and 'SettingKey'.");
                }

                attributes[control.Name] = parsed;
            }
        }

        // 内置控件（代码清单）也可能被 ini 覆盖属性（如 chkMEDDraw / chkWriteInstallPathToRegistry 等）。
        foreach (string controlName in CollectAttributeSectionNames(iniPath))
        {
            if (!attributes.ContainsKey(controlName))
                attributes[controlName] = ParseAttributes(ini, controlName, iniPath);
        }

        return new OptionsWindowConfig(bySection, attributes);
    }

    private static (int Index, SettingControl Control) ParseRegistration(
        IniFile ini, string section, string key, string iniPath)
    {
        string declaration = ini.GetStringValue(section, key, string.Empty);
        int separator = declaration.IndexOf(':');
        if (separator < 1)
        {
            throw new ClientConfigurationException(
                $"Invalid control registration '{key}={declaration}' in '[{section}]' of '{iniPath}'.");
        }

        string name = declaration.Substring(0, separator).Trim();
        string rawType = declaration.Substring(separator + 1).Trim();

        SettingControlType type = rawType.ToLowerInvariant() switch
        {
            "settingcheckbox" => SettingControlType.SettingCheckBox,
            "filesettingcheckbox" => SettingControlType.FileSettingCheckBox,
            _ => throw new ClientConfigurationException(
                $"Unknown setting control type '{rawType}' for '{name}' in '{iniPath}'.")
        };

        int index = 0;
        int digits = key.TakeWhile(c => char.IsDigit(c)).Count();
        if (digits > 0)
            int.TryParse(key.Substring(0, digits), out index);

        return (index, new SettingControl(name, type));
    }

    private static SettingControlAttributes ParseAttributes(IniFile ini, string controlName, string iniPath)
    {
        List<string>? keys = ini.GetSectionKeys(controlName);
        var enabledFiles = new List<EnabledFileEntry>();

        if (keys is not null)
        {
            var fileKeys = keys
                .Select(key => (Key: key, Index: EnabledFileIndex(key)))
                .Where(entry => entry.Index >= 0)
                .OrderBy(entry => entry.Index);

            foreach ((string key, int _) in fileKeys)
                enabledFiles.Add(ParseEnabledFile(ini, controlName, key, iniPath));
        }

        return new SettingControlAttributes(
            Text: NullIfEmpty(ini.GetStringValue(controlName, "Text", string.Empty)),
            ToolTip: NullIfEmpty(ini.GetStringValue(controlName, "ToolTip", string.Empty)),
            DefaultValue: NullIfEmpty(ini.GetStringValue(controlName, "DefaultValue", string.Empty)),
            SettingSection: NullIfEmpty(ini.GetStringValue(controlName, "SettingSection", string.Empty)),
            SettingKey: NullIfEmpty(ini.GetStringValue(controlName, "SettingKey", string.Empty)),
            Enabled: ini.GetBooleanValue(controlName, "Enabled", true),
            Visible: ini.GetBooleanValue(controlName, "Visible", true),
            ParentCheckBoxName: NullIfEmpty(ini.GetStringValue(controlName, "ParentCheckBoxName", string.Empty)),
            ParentCheckBoxRequiredValue: NullIfEmpty(ini.GetStringValue(controlName, "ParentCheckBoxRequiredValue", string.Empty)),
            EnabledFiles: enabledFiles);
    }

    private static EnabledFileEntry ParseEnabledFile(IniFile ini, string controlName, string key, string iniPath)
    {
        string[] parts = ini.GetStringValue(controlName, key, string.Empty)
            .Split(',', StringSplitOptions.TrimEntries);

        if (parts.Length < 2 || parts[0].Length == 0 || parts[1].Length == 0)
        {
            throw new ClientConfigurationException(
                $"Invalid '{key}' in '[{controlName}]' of '{iniPath}': expected 'source,target[,policy]'.");
        }

        FileUpdatePolicy policy = parts.Length >= 3 && parts[2].Length > 0
            ? parts[2].ToLowerInvariant() switch
            {
                "overwriteonmismatch" => FileUpdatePolicy.OverwriteOnMismatch,
                "keepchanges" => FileUpdatePolicy.KeepChanges,
                _ => throw new ClientConfigurationException(
                    $"Unknown file update policy '{parts[2]}' in '[{controlName}]' of '{iniPath}'.")
            }
            : FileUpdatePolicy.KeepChanges;

        return new EnabledFileEntry(parts[0], parts[1], policy);
    }

    /// <summary><c>EnabledFile3</c> → 3；非该前缀返回 -1。</summary>
    private static int EnabledFileIndex(string key)
    {
        const string prefix = "EnabledFile";
        if (!key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return -1;

        string suffix = key.Substring(prefix.Length);
        return int.TryParse(suffix, out int index) ? index : -1;
    }

    /// <summary>取 ini 中除面板 / 注册节 / 窗口级节之外的所有节名，作为内置控件的属性节候选。</summary>
    private static IEnumerable<string> CollectAttributeSectionNames(string iniPath)
    {
        const string optionsWindowSection = "OptionsWindow";
        const string iniSystemSection = "INISystem";
        const string tabControlSection = "tabControl";

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            optionsWindowSection, iniSystemSection, tabControlSection, "ExtraControls"
        };

        foreach ((string panel, string _) in PanelToSection)
            reserved.Add(panel);

        foreach ((string panel, string _) in PanelToSection)
            reserved.Add(panel + ExtraControlsSuffix);

        var ini = new IniFile(iniPath);
        foreach (string section in ini.GetSections())
        {
            if (!reserved.Contains(section))
                yield return section;
        }
    }

    private static string? NullIfEmpty(string value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary><c>EnabledFile</c> 的执行结果。</summary>
public enum EnabledFileResult
{
    /// <summary>已复制到目标。</summary>
    Copied,

    /// <summary>源文件不存在，未做改动。</summary>
    SourceMissing,

    /// <summary><c>KeepChanges</c>：目标已存在，保留现状。</summary>
    TargetKept,

    /// <summary><c>OverwriteOnMismatch</c>：目标与源一致，无需复制。</summary>
    Identical
}

/// <summary>
/// <c>EnabledFile</c> 的执行器：按策略把源文件（相对游戏目录）复制到目标位置。
/// 源文件不存在或按策略无需复制时不改动任何文件，返回对应结果由调用方记录。
/// </summary>
public static class EnabledFileApplier
{
    /// <summary>应用单条 EnabledFile。</summary>
    public static EnabledFileResult Apply(EnabledFileEntry entry)
    {
        string source = SafePath.CombineFilePath(ProgramConstants.GamePath, entry.Source);
        string target = SafePath.CombineFilePath(ProgramConstants.GamePath, entry.Target);

        if (!File.Exists(source))
            return EnabledFileResult.SourceMissing;

        if (File.Exists(target))
        {
            if (entry.Policy == FileUpdatePolicy.KeepChanges)
                return EnabledFileResult.TargetKept;
            if (FilesEqual(source, target))
                return EnabledFileResult.Identical;
        }

        string? directory = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.Copy(source, target, overwrite: true);
        return EnabledFileResult.Copied;
    }

    private static bool FilesEqual(string left, string right)
    {
        var leftInfo = new FileInfo(left);
        var rightInfo = new FileInfo(right);
        if (leftInfo.Length != rightInfo.Length)
            return false;

        using FileStream a = File.OpenRead(left);
        using FileStream b = File.OpenRead(right);
        const int bufferSize = 81920;
        byte[] bufferA = new byte[bufferSize];
        byte[] bufferB = new byte[bufferSize];

        while (true)
        {
            int readA = a.Read(bufferA, 0, bufferSize);
            int readB = b.Read(bufferB, 0, bufferSize);
            if (readA != readB)
                return false;
            if (readA == 0)
                return true;
            if (!bufferA.AsSpan(0, readA).SequenceEqual(bufferB.AsSpan(0, readB)))
                return false;
        }
    }
}
