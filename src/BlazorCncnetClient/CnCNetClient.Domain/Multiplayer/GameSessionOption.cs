using System;
using System.Collections.Generic;
using CnCNetClient.Tools;

#nullable enable

namespace CnCNetClient.Domain.Multiplayer;

/// <summary>
/// 下拉框值写入 spawn.ini 的模式（同原版 <c>GameSessionDropDown</c> 的 DropDownDataWriteMode）。
/// </summary>
public enum GameOptionWriteMode
{
    /// <summary>写入布尔（选中索引 &gt; 0 为 True）。</summary>
    Boolean,

    /// <summary>直接写入选中索引。</summary>
    Index,

    /// <summary>写入选中项的原始字符串值。</summary>
    String,

    /// <summary>不写 spawn.ini，选中项值作为自定义地图代码 INI 路径应用（AffectsMapCode）。</summary>
    MapCode
}

/// <summary>
/// 游戏选项（遭遇战 / 房间的下拉框与复选框）抽象基类。
/// 移植原版 <c>GameSessionDropDown</c> / <c>GameSessionCheckBox</c>（去 XNA）：
/// 持有选项元数据（SpawnIniOption / CustomIniPath / 默认值）与当前状态，
/// 负责将选项值写入 spawn.ini（<see cref="ApplySpawnIniCode"/>）或应用自定义地图代码
/// （<see cref="ApplyMapCode"/>），并向 [GameOptions] 节持久化 / 加载。
/// </summary>
public abstract class GameSessionOption
{
    protected GameSessionOption(string name, string displayName)
    {
        Name = name;
        DisplayName = displayName;
    }

    /// <summary>选项唯一名（spawn.ini / [GameOptions] 节键名，如 chkShortGame / cmbCredits）。</summary>
    public string Name { get; }

    /// <summary>界面显示名（复选为 Text，下拉为 OptionName）。</summary>
    public string DisplayName { get; }

    /// <summary>是否影响 spawn.ini（无则仅影响地图代码，如 StolenTech / NavalCombat）。</summary>
    public abstract bool AffectsSpawnIni { get; }

    /// <summary>是否应用自定义地图代码（复选 CustomIniPath / MAPCODE 下拉）。</summary>
    public abstract bool AffectsMapCode { get; }

    /// <summary>将选项当前值写入 spawn.ini 的 [Settings] 节（同原版 ApplySpawnIniCode）。</summary>
    public abstract void ApplySpawnIniCode(IniFile spawnIni);

    /// <summary>将选项当前值作为地图代码应用到 mapIni（同原版 ApplyMapCode）。</summary>
    public abstract void ApplyMapCode(IniFile mapIni, GameMode gameMode);

    /// <summary>返回持久化值（写入 [GameOptions] 节：下拉为选中索引字符串，复选为 True/False）。</summary>
    public abstract string GetPersistedValue();

    /// <summary>从持久化值恢复状态（读取 [GameOptions] 节；无效值保留当前状态）。</summary>
    public abstract void SetPersistedValue(string value);
}

/// <summary>
/// 游戏选项复选框（同原版 <c>GameSessionCheckBox</c>）：
/// SpawnIniOption 写入 spawn.ini，CustomIniPath 在勾选时应用地图代码；Reversed 反转语义。
/// </summary>
public sealed class GameSessionCheckBoxOption : GameSessionOption
{
    public GameSessionCheckBoxOption(
        string name,
        string displayName,
        string? spawnIniOption = null,
        string? customIniPath = null,
        bool reversed = false,
        bool defaultChecked = false,
        string enabledSpawnIniValue = "True",
        string disabledSpawnIniValue = "False")
        : base(name, displayName)
    {
        SpawnIniOption = spawnIniOption;
        CustomIniPath = customIniPath;
        Reversed = reversed;
        DefaultChecked = defaultChecked;
        EnabledSpawnIniValue = enabledSpawnIniValue;
        DisabledSpawnIniValue = disabledSpawnIniValue;
        Checked = defaultChecked;
    }

    /// <summary>写入 spawn.ini [Settings] 的选项键（空则仅影响地图代码）。</summary>
    public string? SpawnIniOption { get; }

    /// <summary>勾选生效时应用的地图代码 INI 路径（相对游戏目录）。</summary>
    public string? CustomIniPath { get; }

    /// <summary>是否反转（Reversed=yes：勾选 = 不应用；原版 StolenTech / NavalCombat 等）。</summary>
    public bool Reversed { get; }

    /// <summary>默认勾选状态（同原版 DefaultChecked，加载失败时回退）。</summary>
    public bool DefaultChecked { get; }

    /// <summary>勾选时写入 spawn.ini 的字符串值。</summary>
    public string EnabledSpawnIniValue { get; }

    /// <summary>未勾选时写入 spawn.ini 的字符串值。</summary>
    public string DisabledSpawnIniValue { get; }

    /// <summary>当前勾选状态。</summary>
    public bool Checked { get; set; }

    public override bool AffectsSpawnIni => !string.IsNullOrWhiteSpace(SpawnIniOption);

    public override bool AffectsMapCode => !string.IsNullOrWhiteSpace(CustomIniPath);

    public override void ApplySpawnIniCode(IniFile spawnIni)
    {
        if (!AffectsSpawnIni || SpawnIniOption is null)
            return;

        string value = Checked != Reversed ? EnabledSpawnIniValue : DisabledSpawnIniValue;
        spawnIni.SetStringValue("Settings", SpawnIniOption, value);
    }

    public override void ApplyMapCode(IniFile mapIni, GameMode gameMode)
    {
        if (!AffectsMapCode || CustomIniPath is null || Checked == Reversed)
            return;

        MapCodeHelper.ApplyMapCode(mapIni, CustomIniPath, gameMode);
    }

    public override string GetPersistedValue() => Checked.ToString();

    public override void SetPersistedValue(string value) =>
        Checked = bool.TryParse(value, out bool b) ? b : DefaultChecked;
}

/// <summary>
/// 游戏选项下拉框（同原版 <c>GameSessionDropDown</c>）：
/// Items 为原始可选值，按 <see cref="WriteMode"/> 写入 spawn.ini；MAPCODE 模式将选中项值作为地图代码路径应用。
/// </summary>
public sealed class GameSessionDropDownOption : GameSessionOption
{
    public GameSessionDropDownOption(
        string name,
        string displayName,
        IReadOnlyList<string> items,
        int defaultIndex = 0,
        string? spawnIniOption = null,
        GameOptionWriteMode writeMode = GameOptionWriteMode.String)
        : base(name, displayName)
    {
        Items = items;
        DefaultIndex = items.Count > 0 ? Math.Clamp(defaultIndex, 0, items.Count - 1) : 0;
        SelectedIndex = DefaultIndex;
        SpawnIniOption = spawnIniOption;
        WriteMode = writeMode;
    }

    /// <summary>可选项（原始值，即写入 spawn.ini 的字符串 / 地图代码路径）。</summary>
    public IReadOnlyList<string> Items { get; }

    /// <summary>默认选中索引（同原版 DefaultIndex，加载失败时回退）。</summary>
    public int DefaultIndex { get; }

    /// <summary>当前选中索引。</summary>
    public int SelectedIndex { get; set; }

    /// <summary>写入 spawn.ini [Settings] 的选项键。</summary>
    public string? SpawnIniOption { get; }

    /// <summary>值写入模式。</summary>
    public GameOptionWriteMode WriteMode { get; }

    public override bool AffectsSpawnIni => WriteMode != GameOptionWriteMode.MapCode;

    public override bool AffectsMapCode => WriteMode == GameOptionWriteMode.MapCode;

    public override void ApplySpawnIniCode(IniFile spawnIni)
    {
        if (!AffectsSpawnIni || SpawnIniOption is null)
            return;
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return;

        switch (WriteMode)
        {
            case GameOptionWriteMode.Boolean:
                spawnIni.SetBooleanValue("Settings", SpawnIniOption, SelectedIndex > 0);
                break;
            case GameOptionWriteMode.Index:
                spawnIni.SetIntValue("Settings", SpawnIniOption, SelectedIndex);
                break;
            default:
                spawnIni.SetStringValue("Settings", SpawnIniOption, Items[SelectedIndex]);
                break;
        }
    }

    public override void ApplyMapCode(IniFile mapIni, GameMode gameMode)
    {
        if (!AffectsMapCode || SpawnIniOption is null)
            return;
        if (SelectedIndex < 0 || SelectedIndex >= Items.Count)
            return;

        MapCodeHelper.ApplyMapCode(mapIni, Items[SelectedIndex], gameMode);
    }

    public override string GetPersistedValue() => SelectedIndex.ToString();

    public override void SetPersistedValue(string value)
    {
        // 同原版 LoadSettings：仅当保存的索引在有效范围内才应用，否则保留当前（默认）值
        if (int.TryParse(value, out int index) && index >= 0 && index < Items.Count)
            SelectedIndex = index;
    }
}
