using CnCNetClient.Domain.Multiplayer;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 1.12 MapLoader：验证自定义地图加载、SHA1 查找与游戏模式分配。
/// </summary>
public class MapLoaderTests : IDisposable
{
    private readonly TestGameEnvironment _env;

    public MapLoaderTests()
    {
        _env = new TestGameEnvironment();
        _env.ResetConfigurationInstance();
        _env.InitializeUserSettings();
    }

    public void Dispose() => _env.Dispose();

    /// <summary>写入一个可通过 InitializeFromCustomMap 的最小自定义地图。</summary>
    private string WriteCustomMap(string fileName, string name, string gameModes)
    {
        string dir = Path.Combine(_env.GameDir, "Maps", "Custom");
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, fileName);
        File.WriteAllText(path, $"""
            [Basic]
            Name={name}
            Author=Tester
            GameModes={gameModes}

            [Map]
            Size=64,64,0,0

            [Waypoints]
            0=8,8
            1=48,48
            """);
        return path;
    }

    [Fact]
    public void LoadCustomMap_AddsMapAndFindMapByHashLocatesIt()
    {
        WriteCustomMap("test_duel.map", "Test Duel", "Custom Map");
        var loader = new MapLoader();

        Map map = loader.LoadCustomMap("Maps/Custom/test_duel", out string message);

        Assert.NotNull(map);
        Assert.Contains("loaded successfully", message);
        Assert.Equal("Test Duel", map.UntranslatedName);
        Assert.False(string.IsNullOrEmpty(map.SHA1));

        // 同一 MapLoader 快照内可按 SHA1 精确查回
        Map found = loader.FindMapByHash(map.SHA1);
        Assert.NotNull(found);
        Assert.Equal(map.SHA1, found.SHA1);
        Assert.Equal("Test Duel", found.UntranslatedName);
    }

    [Fact]
    public void LoadCustomMap_MissingFileReturnsNullAndMessage()
    {
        var loader = new MapLoader();

        Map map = loader.LoadCustomMap("Maps/Custom/does_not_exist", out string message);

        Assert.Null(map);
        Assert.Contains("doesn't exist", message);
    }

    [Fact]
    public void LoadCustomMap_InvalidGameModeIsFilteredOut()
    {
        // 地图声明不在 AllowedCustomGameModes 内的模式，不应进入任何游戏模式
        WriteCustomMap("test_badmode.map", "Bad Mode Map", "NotAllowedMode");
        var loader = new MapLoader();

        Map map = loader.LoadCustomMap("Maps/Custom/test_badmode", out _);

        Assert.NotNull(map);
        Assert.Empty(loader.GameModes);
    }

    [Fact]
    public void LoadCustomMap_SameFileTwiceIsReportedAsAlreadyLoaded()
    {
        WriteCustomMap("test_dup.map", "Dup Map", "Custom Map");
        var loader = new MapLoader();

        Map first = loader.LoadCustomMap("Maps/Custom/test_dup", out _);
        Assert.NotNull(first);

        Map second = loader.LoadCustomMap("Maps/Custom/test_dup", out string message);

        Assert.Null(second);
        Assert.Contains("already loaded", message);
    }
}
