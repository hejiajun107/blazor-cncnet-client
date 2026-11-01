using CnCNetClient.Domain.Multiplayer;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// ForcedGameOptions：MPMaps.ini <c>ForcedOptions=</c> 解析出的强制复选框 / 下拉值，
/// 换图后写入选项当前值（同名地图值覆盖模式值），UI 据此锁定控件。
/// </summary>
public class ForcedGameOptionsTests : IDisposable
{
    private readonly TestGameEnvironment _env;

    public ForcedGameOptionsTests()
    {
        _env = new TestGameEnvironment();
        _env.ResetConfigurationInstance();
        _env.InitializeUserSettings();
    }

    public void Dispose() => _env.Dispose();

    private static GameModeMap CreateGameModeMap()
    {
        var gameMode = new GameMode("Standard");
        var map = new Map("Maps/Standard/testmap", isCustomMap: false);
        return new GameModeMap(gameMode, map);
    }

    [Fact]
    public void Apply_WritesForcedCheckBoxAndDropDownValues()
    {
        GameModeMap gmm = CreateGameModeMap();
        gmm.GameMode.ForcedCheckBoxValues.Add(new("chkNavalCombat", false));
        gmm.GameMode.ForcedDropDownValues.Add(new("cmbCredits", 2));

        var naval = new GameSessionCheckBoxOption("chkNavalCombat", "Naval", defaultChecked: true);
        var credits = new GameSessionDropDownOption("cmbCredits", "Credits", new[] { "1000", "5000", "10000" });
        var crates = new GameSessionCheckBoxOption("chkCrates", "Crates", defaultChecked: true);
        var options = new List<GameSessionOption> { naval, credits, crates };

        ForcedGameOptions.Apply(gmm, options);

        Assert.False(naval.Checked);            // 强制关闭（默认勾选被覆盖）
        Assert.Equal(2, credits.SelectedIndex); // 强制选中索引 2
        Assert.True(crates.Checked);            // 未强制项保持当前值
    }

    [Fact]
    public void Apply_MapValueOverridesGameModeValue()
    {
        GameModeMap gmm = CreateGameModeMap();
        gmm.GameMode.ForcedCheckBoxValues.Add(new("chkIngameAllying", true));
        gmm.Map.ForcedCheckBoxValues.Add(new("chkIngameAllying", false));

        var allying = new GameSessionCheckBoxOption("chkIngameAllying", "Allying", defaultChecked: true);

        ForcedGameOptions.Apply(gmm, new GameSessionOption[] { allying });

        Assert.False(allying.Checked); // 地图强制值覆盖模式强制值
    }

    [Fact]
    public void Apply_ClampsForcedDropDownIndexToItems()
    {
        GameModeMap gmm = CreateGameModeMap();
        gmm.Map.ForcedDropDownValues.Add(new("cmbCredits", 99));

        var credits = new GameSessionDropDownOption("cmbCredits", "Credits", new[] { "a", "b" });

        ForcedGameOptions.Apply(gmm, new GameSessionOption[] { credits });

        Assert.Equal(1, credits.SelectedIndex);
    }

    [Fact]
    public void IsForced_ReflectsGameModeAndMapValues()
    {
        GameModeMap gmm = CreateGameModeMap();
        gmm.GameMode.ForcedCheckBoxValues.Add(new("chkA", true));
        gmm.Map.ForcedDropDownValues.Add(new("cmbB", 1));

        Assert.True(ForcedGameOptions.IsForced(gmm, new GameSessionCheckBoxOption("chkA", "A")));
        Assert.True(ForcedGameOptions.IsForced(gmm, new GameSessionDropDownOption("cmbB", "B", new[] { "x", "y" })));
        Assert.False(ForcedGameOptions.IsForced(gmm, new GameSessionCheckBoxOption("chkC", "C")));
        Assert.False(ForcedGameOptions.IsForced(null, new GameSessionCheckBoxOption("chkA", "A")));
    }

    [Fact]
    public void Apply_RestoresUserCheckBoxValueAfterLeavingForcedMap()
    {
        GameModeMap forcedMap = CreateGameModeMap();
        forcedMap.Map.ForcedCheckBoxValues.Add(new("chkNavalCombat", false));

        var naval = new GameSessionCheckBoxOption("chkNavalCombat", "Naval", defaultChecked: true);

        ForcedGameOptions.Apply(forcedMap, new GameSessionOption[] { naval });
        Assert.True(naval.IsForced);
        Assert.False(naval.Checked);              // 强制期间显示强制值
        Assert.Equal("True", naval.GetPersistedValue()); // 持久化保存的是用户值

        // 切到不强制该选项的普通图 → 恢复玩家自己的选择（允许海军）
        ForcedGameOptions.Apply(CreateGameModeMap(), new GameSessionOption[] { naval });
        Assert.False(naval.IsForced);
        Assert.True(naval.Checked);

        // 再切回强制图 → 再次强制
        ForcedGameOptions.Apply(forcedMap, new GameSessionOption[] { naval });
        Assert.True(naval.IsForced);
        Assert.False(naval.Checked);
    }

    [Fact]
    public void Apply_RestoresUserDropDownIndexAfterLeavingForcedMap()
    {
        GameModeMap forcedMap = CreateGameModeMap();
        forcedMap.Map.ForcedDropDownValues.Add(new("cmbCredits", 2));

        var credits = new GameSessionDropDownOption("cmbCredits", "Credits", new[] { "1000", "5000", "10000" });
        credits.SelectedIndex = 0; // 玩家自己的选择

        ForcedGameOptions.Apply(forcedMap, new GameSessionOption[] { credits });
        Assert.True(credits.IsForced);
        Assert.Equal(2, credits.SelectedIndex);
        Assert.Equal("0", credits.GetPersistedValue());

        ForcedGameOptions.Apply(CreateGameModeMap(), new GameSessionOption[] { credits });
        Assert.False(credits.IsForced);
        Assert.Equal(0, credits.SelectedIndex);
    }
}
