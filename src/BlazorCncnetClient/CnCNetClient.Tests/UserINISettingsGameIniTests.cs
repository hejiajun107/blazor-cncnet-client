using System.Reflection;
using CnCNetClient.Core;
using CnCNetClient.Tools;

using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 游戏侧参数（分辨率/音量/滚屏/游戏速度/难度）读写游戏自身 INI（YR/TS 为 RA2MD.ini，
/// 也就是 gamemd 实际读取的文件），与官方 CnCNet 客户端共用；客户端设置 INI 只保留客户端专有项。
/// 回归：此前每次保存设置都会把客户端里的分辨率无条件写回游戏 INI，
/// 导致官方客户端设成 1920x1080 后被本客户端冲成 1024x768。
/// </summary>
public class UserINISettingsGameIniTests : IDisposable
{
    private const string GAME_SETTINGS = "RA2MD.ini";
    private const string CLIENT_SETTINGS = "ClientSettings.ini";

    private readonly TestGameEnvironment _env;
    private readonly string _gameIniPath;
    private readonly string _clientIniPath;

    public UserINISettingsGameIniTests()
    {
        _env = new TestGameEnvironment();

        // ClientDefinitions 声明游戏 INI 文件名（原版 YR 为 RA2MD.ini）
        string nl = TestGameEnvironment.DefaultClientDefinitions.Contains("\r\n") ? "\r\n" : "\n";
        File.WriteAllText(_env.ClientDefinitionsPath,
            TestGameEnvironment.DefaultClientDefinitions.Replace(
                "ClientGameType=YR",
                "ClientGameType=YR" + nl + "SettingsFile=" + GAME_SETTINGS));

        _gameIniPath = Path.Combine(_env.GameDir, GAME_SETTINGS);
        _clientIniPath = Path.Combine(_env.GameDir, CLIENT_SETTINGS);

        _env.ResetConfigurationInstance();
    }

    public void Dispose()
    {
        ClearUserSettingsSingleton();
        _env.Dispose();
    }

    [Fact]
    public void GameSideSettings_AreReadFromGameIni()
    {
        File.WriteAllText(_gameIniPath, "[Video]\r\nScreenWidth=1920\r\nScreenHeight=1080\r\n");
        File.WriteAllText(_clientIniPath, "[MultiPlayer]\r\nTheme=menu\r\n");

        ClearUserSettingsSingleton();
        UserINISettings.Initialize(CLIENT_SETTINGS);

        var ini = UserINISettings.Instance;

        Assert.Equal(1920, ini.IngameScreenWidth.Value);
        Assert.Equal(1080, ini.IngameScreenHeight.Value);
    }

    [Fact]
    public void SavingClientSettings_DoesNotClobberGameResolution()
    {
        File.WriteAllText(_gameIniPath, "[Video]\r\nScreenWidth=1920\r\nScreenHeight=1080\r\n");
        File.WriteAllText(_clientIniPath, "[MultiPlayer]\r\nTheme=menu\r\n");

        ClearUserSettingsSingleton();
        UserINISettings.Initialize(CLIENT_SETTINGS);

        var ini = UserINISettings.Instance;

        // 修改任意一项（客户端专有的）设置并保存
        ini.ClientFPS.Value = 30;
        ini.SaveSettings();

        var gameIni = new IniFile(_gameIniPath);
        Assert.Equal(1920, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenWidth", 0));
        Assert.Equal(1080, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenHeight", 0));

        // 客户端设置 INI 不应再保存游戏侧分辨率
        var clientIni = new IniFile(_clientIniPath);
        Assert.False(clientIni.KeyExists(UserINISettings.VIDEO, "ScreenWidth"));
        Assert.False(clientIni.KeyExists(UserINISettings.VIDEO, "ScreenHeight"));
    }

    [Fact]
    public void ChangingResolutionInClient_WritesToGameIni()
    {
        File.WriteAllText(_gameIniPath, "[Video]\r\nScreenWidth=1920\r\nScreenHeight=1080\r\n");
        File.WriteAllText(_clientIniPath, "[MultiPlayer]\r\nTheme=menu\r\n");

        ClearUserSettingsSingleton();
        UserINISettings.Initialize(CLIENT_SETTINGS);

        var ini = UserINISettings.Instance;
        ini.IngameScreenWidth.Value = 1280;
        ini.IngameScreenHeight.Value = 720;
        ini.SaveSettings();

        var gameIni = new IniFile(_gameIniPath);
        Assert.Equal(1280, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenWidth", 0));
        Assert.Equal(720, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenHeight", 0));
    }

    [Fact]
    public void GameIniWithoutResolution_GetsLaunchSafeFallback()
    {
        // 游戏 INI 缺失：启动守卫需补齐分辨率（否则游戏以 0×0 建屏崩溃）与 [Intro] Play=no
        File.WriteAllText(_clientIniPath, "[MultiPlayer]\r\nTheme=menu\r\n");

        ClearUserSettingsSingleton();
        UserINISettings.Initialize(CLIENT_SETTINGS);

        UserINISettings.Instance.SaveSettings();

        var gameIni = new IniFile(_gameIniPath);
        Assert.Equal(1024, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenWidth", 0));
        Assert.Equal(768, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenHeight", 0));
        Assert.Equal("no", gameIni.GetStringValue("Intro", "Play", string.Empty));
    }

    [Fact]
    public void LegacyClientSettingsResolution_IsMigratedToGameIni()
    {
        // 旧版本把游戏侧参数存在客户端设置 INI：游戏 INI 缺键时按旧值补齐，避免设置「看似丢失」
        File.WriteAllText(_clientIniPath,
            "[Video]\r\nScreenWidth=1280\r\nScreenHeight=720\r\n\r\n[Audio]\r\nSoundVolume=0.5\r\n");

        ClearUserSettingsSingleton();
        UserINISettings.Initialize(CLIENT_SETTINGS);

        var gameIni = new IniFile(_gameIniPath);
        Assert.Equal(1280, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenWidth", 0));
        Assert.Equal(720, gameIni.GetIntValue(UserINISettings.VIDEO, "ScreenHeight", 0));
        Assert.Equal(0.5, gameIni.GetDoubleValue(UserINISettings.AUDIO, "SoundVolume", 0), 3);
    }

    private static void ClearUserSettingsSingleton()
        => typeof(UserINISettings)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
}
