using System;
using System.Collections.Generic;

using CnCNetClient.Tools;

namespace CnCNetClient.Core
{
    /// <summary>
    /// 原版 XNA 主菜单布局（<c>Resources\MainMenu.ini</c>）与移植版内置菜单入口的显隐映射。
    /// 菜单项的顺序与文案仍由 UI 层定义，但「是否显示」遵循 INI 的 <c>[btn*] Visible</c>，
    /// 与官方客户端一致（缺节/缺键 → 显示）。
    /// </summary>
    public static class MainMenuConfiguration
    {
        /// <summary>主菜单布局文件名（位于 <c>Resources\</c> 下）。</summary>
        public const string FILE_NAME = "MainMenu.ini";

        /// <summary>菜单键 → MainMenu.ini 中对应按钮的节名（与官方 XNA 主菜单按钮一一对应）。</summary>
        private static readonly Dictionary<string, string> SectionsByMenuKey =
            new(StringComparer.OrdinalIgnoreCase)
            {
                ["campaign"] = "btnNewCampaign",
                ["multiplayer"] = "btnCnCNet",
                ["skirmish"] = "btnSkirmish",
                ["lan"] = "btnLan",
                ["load"] = "btnLoadGame",
                ["stats"] = "btnStatistics",
                ["settings"] = "btnOptions",
                ["mapeditor"] = "btnMapEditor",
                ["credits"] = "btnCredits",
                ["extras"] = "btnExtras",
                ["exit"] = "btnExit",
            };

        /// <summary>
        /// 读取某个内置入口的可见性（<c>[btn*] Visible</c>）。
        /// INI、按钮节或 Visible 键缺失时返回 <c>true</c>（官方缺省行为）；
        /// 没有对应按钮的入口（如 <c>lore</c>）恒为 <c>true</c>，由 UI 层的 HiddenMenus 控制。
        /// </summary>
        public static bool IsVisible(IniFile? mainMenuIni, string menuKey)
        {
            if (mainMenuIni is null || string.IsNullOrWhiteSpace(menuKey))
                return true;

            if (!SectionsByMenuKey.TryGetValue(menuKey, out string? section))
                return true;

            return WindowIniAttributes.IsVisible(mainMenuIni, section);
        }

        /// <summary>
        /// 读取某个内置入口的可用性（<c>[btn*] Enabled</c>）。
        /// 官方 <c>XNAControl.ParseControlINIAttribute</c> 中 <c>Visible</c> 会同时置 <c>Enabled</c>，
        /// <c>Enabled</c> 可单独把按钮置灰（仍显示、不可点）；缺省为 <c>true</c>。
        /// </summary>
        public static bool IsEnabled(IniFile? mainMenuIni, string menuKey)
        {
            if (mainMenuIni is null || string.IsNullOrWhiteSpace(menuKey))
                return true;

            if (!SectionsByMenuKey.TryGetValue(menuKey, out string? section))
                return true;

            return WindowIniAttributes.IsEnabled(mainMenuIni, section);
        }
    }
}
