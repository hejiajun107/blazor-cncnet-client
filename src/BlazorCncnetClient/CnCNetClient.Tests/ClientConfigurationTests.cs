using System.Globalization;
using CnCNetClient.Core;
using CnCNetClient.Core.I18N;
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
    public void MissingResourcesDirectory_ReportsResourcesNotFoundNotMissingClientDefinitions()
    {
        // 模拟「Resources 被移走/放错位置」：游戏根下完全没有 Resources。
        // 旧实现会回退到启动目录并报「Couldn't find ClientDefinitions.ini at <启动目录>\Resources」，
        // 把「没找到 Resources」误报成「某个固定目录缺文件」（2026-10-19 实际误导）。
        string emptyRoot = Path.Combine(Path.GetTempPath(), "cncnet-no-res-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(emptyRoot);

        var gamePathField = typeof(ProgramConstants).GetField(
            nameof(ProgramConstants.GamePath),
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!;

        try
        {
            gamePathField.SetValue(null, SafePath.CombineDirectoryPath(emptyRoot));
            _env.ResetConfigurationInstance();

            // 断言值从 resx 解析结果推导，不在 .cs 里写死译文：
            // 1) 同一键在 zh-CN 与 en 下都必须命中且两个值不同 → 证明 UI.zh-CN.resx 卫星真被加载，
            //    而不是只走了代码兜底英文；
            // 2) 异常消息等于用 zh-CN 模板格式化出的期望文本（同时校验 {0} 路径载荷）。
            var previousCulture = CultureInfo.CurrentUICulture;
            try
            {
                const string key = "Client:UI:Startup.ResourcesDirectoryNotFound";
                const string missingSentinel = "__resx_missing__";

                CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
                string zhTemplate = UIStrings.Lookup(key, missingSentinel);

                CultureInfo.CurrentUICulture = new CultureInfo("en-US");
                string enTemplate = UIStrings.Lookup(key, missingSentinel);

                Assert.NotEqual(missingSentinel, zhTemplate);
                Assert.NotEqual(missingSentinel, enTemplate);
                Assert.NotEqual(enTemplate, zhTemplate);

                CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");
                var ex = Assert.Throws<FileNotFoundException>(() => TestClientConfiguration.CreateFresh());

                string expectedPath = ProgramConstants.GetExpectedResourcesPath(ProgramConstants.StartupPath);
                Assert.Equal(string.Format(zhTemplate, expectedPath), ex.Message);
            }
            finally
            {
                CultureInfo.CurrentUICulture = previousCulture;
            }
        }
        finally
        {
            gamePathField.SetValue(null, SafePath.CombineDirectoryPath(_env.GameDir));
            _env.ResetConfigurationInstance();
            try { Directory.Delete(emptyRoot, true); } catch { }
        }
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
