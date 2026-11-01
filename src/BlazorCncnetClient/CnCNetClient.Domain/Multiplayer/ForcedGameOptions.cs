using System;
using System.Collections.Generic;

namespace CnCNetClient.Domain.Multiplayer;

/// <summary>
/// 游戏模式 / 地图强制游戏选项（MPMaps.ini 的 <c>ForcedOptions=&lt;节&gt;</c>、自定义地图
/// <c>[ForcedOptions]</c> 节解析出的 <c>ForcedCheckBoxValues</c> / <c>ForcedDropDownValues</c>）。
/// 对齐原版 <c>GameLobbyBase</c> 语义：换图后把强制值写入对应选项的当前值并视为锁定
/// （UI 禁用、持久化与预设均不得覆盖）；同名时<b>地图值覆盖模式值</b>。
/// </summary>
public static class ForcedGameOptions
{
    /// <summary>取复选框强制值（地图优先，其次游戏模式）。</summary>
    public static bool TryGetForcedCheckBoxValue(GameModeMap? gameModeMap, string optionName, out bool value)
    {
        if (gameModeMap is not null)
        {
            foreach (KeyValuePair<string, bool> pair in gameModeMap.Map.ForcedCheckBoxValues)
            {
                if (pair.Key == optionName)
                {
                    value = pair.Value;
                    return true;
                }
            }

            foreach (KeyValuePair<string, bool> pair in gameModeMap.GameMode.ForcedCheckBoxValues)
            {
                if (pair.Key == optionName)
                {
                    value = pair.Value;
                    return true;
                }
            }
        }

        value = false;
        return false;
    }

    /// <summary>取下拉框强制选中索引（地图优先，其次游戏模式）。</summary>
    public static bool TryGetForcedDropDownValue(GameModeMap? gameModeMap, string optionName, out int index)
    {
        if (gameModeMap is not null)
        {
            foreach (KeyValuePair<string, int> pair in gameModeMap.Map.ForcedDropDownValues)
            {
                if (pair.Key == optionName)
                {
                    index = pair.Value;
                    return true;
                }
            }

            foreach (KeyValuePair<string, int> pair in gameModeMap.GameMode.ForcedDropDownValues)
            {
                if (pair.Key == optionName)
                {
                    index = pair.Value;
                    return true;
                }
            }
        }

        index = 0;
        return false;
    }

    /// <summary>选项是否被当前地图 / 游戏模式强制。</summary>
    public static bool IsForced(GameModeMap? gameModeMap, GameSessionOption option) => option switch
    {
        GameSessionCheckBoxOption => TryGetForcedCheckBoxValue(gameModeMap, option.Name, out _),
        GameSessionDropDownOption => TryGetForcedDropDownValue(gameModeMap, option.Name, out _),
        _ => false
    };

    /// <summary>
    /// 应用当前地图 / 游戏模式的强制值：被强制的选项进入强制状态并显示强制值；
    /// 不再被强制的选项退出强制状态、恢复玩家自己的选择（原版换图语义：强制只作用于当前地图，
    /// 切到不限制该选项的地图后应恢复此前的勾选 / 下拉值）。下拉索引越界时收敛到边界。
    /// </summary>
    public static void Apply(GameModeMap? gameModeMap, IEnumerable<GameSessionOption> options)
    {
        foreach (GameSessionOption option in options)
        {
            switch (option)
            {
                case GameSessionCheckBoxOption checkBox
                    when TryGetForcedCheckBoxValue(gameModeMap, checkBox.Name, out bool forcedChecked):
                    checkBox.BeginForced();
                    checkBox.Checked = forcedChecked;
                    break;
                case GameSessionDropDownOption dropDown
                    when TryGetForcedDropDownValue(gameModeMap, dropDown.Name, out int forcedIndex):
                    dropDown.BeginForced();
                    dropDown.SelectedIndex = dropDown.Items.Count > 0
                        ? Math.Clamp(forcedIndex, 0, dropDown.Items.Count - 1)
                        : 0;
                    break;
                default:
                    // 当前地图 / 模式不再强制该选项 → 恢复用户值
                    option.EndForced();
                    break;
            }
        }
    }
}
