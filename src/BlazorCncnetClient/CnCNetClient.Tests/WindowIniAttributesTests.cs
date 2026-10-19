using CnCNetClient.Core;
using CnCNetClient.Tools;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 窗口 INI 的具名控件属性（Visible / Enabled）：
/// 官方 XNAWindow 按控件名读 [控件名] 节，缺省可见/可用。
/// </summary>
public class WindowIniAttributesTests : IDisposable
{
    private readonly string _dir;
    private readonly string _path;

    public WindowIniAttributesTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cncnet-windowini-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _path = Path.Combine(_dir, "MainMenu.ini");
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
    public void NamedControlsFollowVisibleAndEnabled()
    {
        var ini = WriteIni("""
            [lblVersion]
            Visible=false

            [btnExMapEditor]
            Visible=false ; hide map editor in extras

            [btnExCredits]
            Enabled=false
            """);

        Assert.False(WindowIniAttributes.IsVisible(ini, "lblVersion"));
        Assert.False(WindowIniAttributes.IsVisible(ini, "btnExMapEditor"));

        // 未写 Visible 的控件仍然可见；Enabled 单独生效
        Assert.True(WindowIniAttributes.IsVisible(ini, "btnExCredits"));
        Assert.False(WindowIniAttributes.IsEnabled(ini, "btnExCredits"));
        Assert.True(WindowIniAttributes.IsEnabled(ini, "btnExMapEditor"));
    }

    [Fact]
    public void MissingControlOrFileDefaultsToVisibleAndEnabled()
    {
        var ini = WriteIni("[btnExCancel]\r\nVisible=false\r\n");

        Assert.True(WindowIniAttributes.IsVisible(ini, "btnExStatistics"));
        Assert.True(WindowIniAttributes.IsEnabled(ini, "btnExStatistics"));
        Assert.False(WindowIniAttributes.IsVisible(ini, "btnExCancel"));
        Assert.True(WindowIniAttributes.IsVisible(null, "btnExCancel"));
        Assert.True(WindowIniAttributes.IsEnabled(null, "btnExCancel"));
    }
}
