using CnCNetClient.Core;
using CnCNetClient.Tools;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 允许在测试中以可控配置文件构建 ClientConfiguration（绕过单例缓存与 protected 构造函数）。
/// </summary>
public sealed class TestClientConfiguration : ClientConfiguration
{
    public TestClientConfiguration() { }

    /// <summary>从当前测试环境的静态路径构建全新实例（会触发文件校验）。</summary>
    public static ClientConfiguration CreateFresh() => new TestClientConfiguration();
}

/// <summary>
/// 1.12 ClientConfiguration：验证配置加载、校验与重载。
/// </summary>
public class ClientConfigurationTests : IDisposable
{
    private readonly TestGameEnvironment _env;

    public ClientConfigurationTests()
    {
        _env = new TestGameEnvironment();
        _env.ResetConfigurationInstance();
    }

    public void Dispose() => _env.Dispose();

    [Fact]
    public void MissingClientDefinitions_ThrowsFileNotFound()
    {
        File.Delete(_env.ClientDefinitionsPath);

        Assert.Throws<FileNotFoundException>(() => TestClientConfiguration.CreateFresh());
    }

    [Fact]
    public void GetIRCServers_ParsesSectionValues()
    {
        var config = TestClientConfiguration.CreateFresh();

        List<string> servers = config.GetIRCServers();

        Assert.Contains("irc.gamesurge.net|GameSurge|6667", servers);
        Assert.Contains("irc2.cncnet.org|CnCNet|6667", servers);
    }

    [Fact]
    public void ThemeCount_ReflectsThemesSection()
    {
        var config = TestClientConfiguration.CreateFresh();

        Assert.Equal(1, config.ThemeCount);
    }

    [Fact]
    public void InvalidTranslationGameFileSyntax_ThrowsIniParseException()
    {
        // GameFile 语法要求 source,destination[,checked]；缺 destination 时应显式失败
        string content = File.ReadAllText(_env.ClientDefinitionsPath)
            + "\r\n[Translations]\r\nGameFile0=Resources/Translations/en.csf\r\n";
        File.WriteAllText(_env.ClientDefinitionsPath, content);

        Assert.Throws<IniParseException>(() => TestClientConfiguration.CreateFresh());
    }

    [Fact]
    public void RefreshSettings_ReloadsClientSettingsFromDisk()
    {
        var config = TestClientConfiguration.CreateFresh();

        // 首次读取为默认值
        Assert.Equal(0.005f, config.DefaultAlphaRate);

        // 修改 DTACnCNetClient.ini 后 RefreshSettings 应读到新值
        File.WriteAllText(_env.ClientSettingsPath,
            "[General]\r\nMainMenuTheme=mainmenu\r\nAlphaRate=0.02\r\n");
        config.RefreshSettings();

        Assert.Equal(0.02f, config.DefaultAlphaRate);
    }

    [Fact]
    public void LocalGameAndSkillLevels_LoadedFromDefinitions()
    {
        var config = TestClientConfiguration.CreateFresh();

        Assert.Equal("YR", config.LocalGame);
        Assert.Equal(new[] { "Any", "Beginner", "Intermediate", "Pro" }, config.GetSkillLevelOptions());
        Assert.Equal("Red Alert 2: Yuri's Revenge", config.LongGameName);
    }
}
