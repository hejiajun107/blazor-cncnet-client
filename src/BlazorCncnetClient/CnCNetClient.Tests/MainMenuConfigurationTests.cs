using CnCNetClient.Core;
using CnCNetClient.Tools;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// MainMenu.ini 显隐规则：移植版内置菜单入口必须遵循官方的 [btn*] Visible，
/// 缺省（无文件/无节/无键）视为显示，避免旧资源（没有这些键）导致入口消失。
/// </summary>
public class MainMenuConfigurationTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public MainMenuConfigurationTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cncnet-mainmenu-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, MainMenuConfiguration.FILE_NAME);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private IniFile WriteIni(string content)
    {
        File.WriteAllText(_path, content);
        return new IniFile(_path);
    }

    [Fact]
    public void ReadsVisibleFlagsFromMainMenuIni()
    {
        var ini = WriteIni("""
            [btnMapEditor]
            Visible=false

            [btnCredits]
            Visible=false ; Enable only if Credits URL is set in ClientDefinitions.ini

            [btnExtras]
            Visible=true
            """);

        Assert.False(MainMenuConfiguration.IsVisible(ini, "mapeditor"));
        Assert.False(MainMenuConfiguration.IsVisible(ini, "credits"));
        Assert.True(MainMenuConfiguration.IsVisible(ini, "extras"));
    }

    [Fact]
    public void MissingSectionOrKeyDefaultsToVisible()
    {
        var ini = WriteIni("[btnExit]\r\nVisible=false\r\n");

        // 没有对应按钮节的入口（官方缺省可见）
        Assert.True(MainMenuConfiguration.IsVisible(ini, "mapeditor"));
        // 有节且 Visible=false 的入口
        Assert.False(MainMenuConfiguration.IsVisible(ini, "exit"));
    }

    [Fact]
    public void ReadsEnabledFlagIndependentlyFromVisible()
    {
        // 官方 XNAControl：Enabled 可单独置灰（仍显示、不可点）。
        var ini = WriteIni("""
            [btnNewCampaign]
            Enabled=false

            [btnExit]
            Visible=false
            """);

        Assert.False(MainMenuConfiguration.IsEnabled(ini, "campaign"));
        // 未写 Enabled 的入口缺省可用（Visible=false 由可见性负责隐藏）
        Assert.True(MainMenuConfiguration.IsEnabled(ini, "exit"));
        Assert.True(MainMenuConfiguration.IsEnabled(null, "campaign"));
    }

    [Fact]
    public void NullIniOrUnknownKeyDefaultsToVisible()
    {
        var ini = WriteIni("[btnMapEditor]\r\nVisible=false\r\n");

        Assert.True(MainMenuConfiguration.IsVisible(null, "mapeditor"));
        Assert.True(MainMenuConfiguration.IsVisible(ini, "lore"));   // 没有 btnLore
        Assert.True(MainMenuConfiguration.IsVisible(ini, ""));        // 空键
    }
}
