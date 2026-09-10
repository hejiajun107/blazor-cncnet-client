using System.Reflection;
using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace CnCNetClient.Tests;

/// <summary>
/// 共享测试环境：在临时目录构造最小"游戏根目录"（含 Resources/ClientDefinitions.ini 等），
/// 并通过反射把 <see cref="ProgramConstants.GamePath"/> 指向该目录，
/// 使 ClientConfiguration / MapLoader / Connection 的静态路径解析可测。
/// </summary>
public sealed class TestGameEnvironment : IDisposable
{
    public string GameDir { get; }
    public string ResourceDir { get; }
    public string ClientDefinitionsPath { get; }
    public string ClientSettingsPath { get; }
    public string NetworkDefinitionsPath { get; }
    public string GameOptionsPath { get; }
    public string UserSettingsPath { get; }

    private static readonly FieldInfo GamePathField = typeof(ProgramConstants)
        .GetField(nameof(ProgramConstants.GamePath), BindingFlags.Static | BindingFlags.Public)!;

    private static readonly FieldInfo BaseResourcePathField = typeof(ProgramConstants)
        .GetField(nameof(ProgramConstants.BASE_RESOURCE_PATH), BindingFlags.Static | BindingFlags.Public)!;

    public TestGameEnvironment()
    {
        GameDir = Path.Combine(Path.GetTempPath(), "cncnet-tests-" + Guid.NewGuid().ToString("N"));
        ResourceDir = Path.Combine(GameDir, "Resources");
        Directory.CreateDirectory(ResourceDir);
        Directory.CreateDirectory(Path.Combine(GameDir, "Client"));
        Directory.CreateDirectory(Path.Combine(GameDir, "INI"));
        Directory.CreateDirectory(Path.Combine(GameDir, "Maps", "Custom"));
        Directory.CreateDirectory(Path.Combine(ResourceDir, "Translations"));

        ClientDefinitionsPath = Path.Combine(ResourceDir, ClientConfiguration.CLIENT_DEFS);
        ClientSettingsPath = Path.Combine(ResourceDir, ClientConfiguration.CLIENT_SETTINGS);
        NetworkDefinitionsPath = Path.Combine(ResourceDir, ClientConfiguration.NETWORK_DEFS);
        GameOptionsPath = Path.Combine(ResourceDir, ClientConfiguration.GAME_OPTIONS);
        UserSettingsPath = Path.Combine(GameDir, "User.ini");

        // 基准配置（可被单测覆盖后重建）
        File.WriteAllText(ClientDefinitionsPath, DefaultClientDefinitions);
        File.WriteAllText(ClientSettingsPath, "[General]\r\nMainMenuTheme=mainmenu\r\n");
        File.WriteAllText(NetworkDefinitionsPath,
            "[Settings]\r\nCnCNetTunnelListURL=https://example.invalid/master-list\r\n\r\n[IRCServers]\r\n0=irc.gamesurge.net|GameSurge|6667\r\n1=irc2.cncnet.org|CnCNet|6667\r\n");
        File.WriteAllText(GameOptionsPath, "[General]\r\nSides=GDI,Nod\r\n");

        // 让静态 GamePath 指向测试目录（生产代码路径不受影响）
        BaseResourcePathField.SetValue(null, "Resources");
        GamePathField.SetValue(null, SafePath.CombineDirectoryPath(GameDir));

        Logger.Initialize(GameDir, "test.log");
        Logger.WriteLogFile = false;
        Logger.WriteToConsole = false;
    }

    /// <summary>清除单例缓存，允许单测以可控配置重建 ClientConfiguration。</summary>
    public void ResetConfigurationInstance()
    {
        typeof(ClientConfiguration)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);
    }

    /// <summary>初始化 UserINISettings 单例，供 GameModeMapCollection 等检查收藏地图时使用。</summary>
    public void InitializeUserSettings()
    {
        // 静态单例跨测试残留，先清空以便基于当前临时目录重建
        typeof(UserINISettings)
            .GetField("_instance", BindingFlags.Static | BindingFlags.NonPublic)!
            .SetValue(null, null);

        File.WriteAllText(UserSettingsPath, "[MultiPlayer]\r\nHandle=Tester\r\n");
        UserINISettings.Initialize(Path.GetFileName(UserSettingsPath));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(GameDir, true);
        }
        catch
        {
            // 测试清理失败不掩盖断言结果
        }
    }

    public const string DefaultClientDefinitions = """
        [Settings]
        ClientGameType=YR
        LocalGame=YR
        LongGameName=Red Alert 2: Yuri's Revenge
        SkillLevelOptions=Any,Beginner,Intermediate,Pro
        MapFileExtension=map
        MPMapsPath=INI/MPMaps.ini
        MapEditorExePath=FinalSun/FinalSun.exe
        CompatibilityCheckExecutables=game.exe,spawn.exe
        GameExecutableNames=game.exe
        UnixGameExecutableName=wine-game.sh

        [Themes]
        0=Standard,Standard

        [Translations]
        TranslationsFolder=Resources/Translations

        [UserDefaults]
        BorderlessWindowedClient=true
        """;
}
