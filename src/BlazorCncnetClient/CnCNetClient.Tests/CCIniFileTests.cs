using CnCNetClient.Core;
using CnCNetClient.Tools;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 1.12 CCIniFile：验证 $BaseSection 继承合并与 BasedOn 基础 INI 合并。
/// </summary>
public class CCIniFileTests : IDisposable
{
    private readonly string _dir;

    public CCIniFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ccinifile-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    private string WriteIni(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void BaseSection_InheritsKeysFromBaseAndKeepsOwnValues()
    {
        // [General] 作为基底，[Derived] 通过 $BaseSection 继承，且自带覆盖值
        string path = WriteIni("inherit.ini", """
            [General]
            AlphaRate=0.01
            DefaultChatColor=0,255,0

            [Derived]
            $BaseSection=General
            AlphaRate=0.02
            DerivedOnly=42
            """);

        var ini = new CCIniFile(path);

        // 继承但未覆盖的键从基底带入
        Assert.Equal("0,255,0", ini.GetStringValue("Derived", "DefaultChatColor", null));
        // 自身键优先于基底
        Assert.Equal(0.02f, ini.GetSingleValue("Derived", "AlphaRate", -1f));
        // 基底自身的键不受影响
        Assert.Equal(0.01f, ini.GetSingleValue("General", "AlphaRate", -1f));
        // 派生节自身键保留
        Assert.Equal(42, ini.GetIntValue("Derived", "DerivedOnly", -1));
    }

    [Fact]
    public void BaseSection_MissingBaseIsSkippedWithoutThrowing()
    {
        string path = WriteIni("missing-base.ini", """
            [Orphan]
            $BaseSection=NotThere
            Key=Value
            """);

        var ini = new CCIniFile(path); // 不应抛异常

        Assert.True(ini.SectionExists("Orphan"));
        Assert.Equal("Value", ini.GetStringValue("Orphan", "Key", null));
    }

    [Fact]
    public void BasedOn_MergesBaseIniWithDerivedOverrides()
    {
        // base.ini 提供 [INISystem]/[Audio]，child.ini 引用 base.ini 并覆盖
        WriteIni("base.ini", """
            [Audio]
            SoundGameLobbyJoinCooldown=0.25
            SoundMessageCooldown=0.25

            [BaseOnly]
            BaseKey=base-value
            """);

        string path = WriteIni("child.ini", """
            [INISystem]
            BasedOn=base.ini

            [Audio]
            SoundGameLobbyJoinCooldown=0.50

            [ChildOnly]
            ChildKey=child-value
            """);

        var ini = new CCIniFile(path);

        // 覆盖生效
        Assert.Equal(0.50f, ini.GetSingleValue("Audio", "SoundGameLobbyJoinCooldown", -1f));
        // 未覆盖的基底值带入
        Assert.Equal(0.25f, ini.GetSingleValue("Audio", "SoundMessageCooldown", -1f));
        // 基底独有节带入
        Assert.Equal("base-value", ini.GetStringValue("BaseOnly", "BaseKey", null));
        // 子节独有内容保留
        Assert.Equal("child-value", ini.GetStringValue("ChildOnly", "ChildKey", null));
    }
}
