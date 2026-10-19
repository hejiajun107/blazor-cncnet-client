using CnCNetClient.Tools;

namespace CnCNetClient.Core
{
    /// <summary>
    /// 窗口 INI（<c>MainMenu.ini</c> / <c>ExtrasWindow.ini</c> / <c>GenericWindow.ini</c>）的通用属性读取。
    /// 对应官方 <c>XNAControl.GetAttributes</c> + <c>ParseControlINIAttribute</c>：
    /// 按控件名读取 <c>[控件名]</c> 节中的 <c>Visible</c> / <c>Enabled</c>。
    /// 文件、节或键缺失时按官方缺省返回 <c>true</c>。
    /// </summary>
    public static class WindowIniAttributes
    {
        /// <summary>控件是否可见（<c>[控件名] Visible</c>，缺省 true）。</summary>
        public static bool IsVisible(IniFile? iniFile, string controlName)
        {
            if (iniFile is null || string.IsNullOrWhiteSpace(controlName))
                return true;

            return iniFile.GetBooleanValue(controlName, "Visible", true);
        }

        /// <summary>
        /// 控件是否可用（<c>[控件名] Enabled</c>，缺省 true）。
        /// 官方语义：<c>Enabled=false</c> 时控件仍显示但置灰、不可交互。
        /// </summary>
        public static bool IsEnabled(IniFile? iniFile, string controlName)
        {
            if (iniFile is null || string.IsNullOrWhiteSpace(controlName))
                return true;

            return iniFile.GetBooleanValue(controlName, "Enabled", true);
        }
    }
}
