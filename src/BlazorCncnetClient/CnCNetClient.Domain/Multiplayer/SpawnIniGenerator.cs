using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Core.Enums;
using CnCNetClient.Tools;

namespace CnCNetClient.Domain.Multiplayer;

/// <summary>
/// Generates the spawn.ini / spawnmap.ini files that the game reads when launched
/// with the -SPAWN argument. Migrated from <c>GameLobbyBase.WriteSpawnIni</c> /
/// <c>WriteMap</c>, adapted to the Blazor room model: 支持遭遇战 AI 玩家（aiPlayers）、
/// coop 地图（强制 TeamId=1 + AutoSurrender=false）、自动结盟（teamStartMappings）
/// 与游戏选项（gameOptions，复选/下拉写入 spawn.ini [Settings] 并应用自定义地图代码）。
/// </summary>
public static class SpawnIniGenerator
{
    /// <summary>
    /// Writes spawn.ini for the given players and returns the per-player house info.
    /// Player side/color/start are treated as concrete selections (the Blazor room
    /// has no "Random" options), so no randomization is applied.
    /// </summary>
    public static PlayerHouseInfo[] WriteSpawnIni(
        IReadOnlyList<PlayerInfo> players,
        GameModeMap gameModeMap,
        int randomSeed,
        int sideCount,
        int uniqueGameId,
        bool isHost,
        string tunnelIp = "",
        int tunnelPort = 0,
        IReadOnlyList<PlayerInfo>? aiPlayers = null,
        List<TeamStartMapping>? teamStartMappings = null,
        IReadOnlyList<GameSessionOption>? gameOptions = null)
    {
        Logger.Log("Writing spawn.ini");

        int aiCount = aiPlayers?.Count ?? 0;

        // G2 原版语义（GameLobbyBase.WriteSpawnIni）：coop 对局强制全员 TeamId=1（人类与 AI 同盟），
        // 再配合下方 AutoSurrender=false，保证 coop 任务局可正常协同作战。
        if (gameModeMap.IsCoop)
        {
            foreach (PlayerInfo p in players)
                p.TeamId = 1;
            if (aiPlayers is not null)
            {
                foreach (PlayerInfo ai in aiPlayers)
                    ai.TeamId = 1;
            }
        }

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        spawnerSettingsFile.Delete();

        // 侧别/颜色为具体选择（Side 为 0-based 真实阵营、Color 为 GameColorIndex），直接映射。
        // 出生点走原版 Randomize / PlayerHouseInfo.RandomizeStart 语义（G3）：
        //   - StartingLocation=0（随机出生）：默认交给游戏自身随机（StartingWaypoint=-1，游戏会放在地图对侧）；
        //     当存在自动结盟映射 / 出生点非连续 / 客户端开启 UseClientRandomStartLocations 时，由客户端随机分配。
        //   - 具体出生点：直接采用；与已占用冲突时 StartingWaypoint=-1。
        // AI 玩家紧随人类玩家之后（索引 [players.Count, players.Count+aiCount)），
        // 与 AllianceHolder / [HouseHandicaps] / SpawnLocations 的索引约定一致。
        var pseudoRandom = new Random(randomSeed);
        List<int> freeStartingLocations = new List<int>();
        List<int> takenStartingLocations = new List<int>();
        try
        {
            foreach (int i in gameModeMap.AllowedStartingLocations)
                freeStartingLocations.Add(i - 1);
        }
        catch
        {
            freeStartingLocations.Clear();
        }

        for (int i = 0; i < players.Count; i++)
        {
            if (players[i].StartingLocation > 0)
                freeStartingLocations.Remove(players[i].StartingLocation - 1);
        }
        for (int aiId = 0; aiId < aiCount; aiId++)
        {
            if (aiPlayers![aiId].StartingLocation > 0)
                freeStartingLocations.Remove(aiPlayers[aiId].StartingLocation - 1);
        }

        var mappings = teamStartMappings ?? new List<TeamStartMapping>();
        foreach (var block in mappings.Where(m => m.IsBlock))
            freeStartingLocations.Remove(block.StartingWaypoint);

        // 原版 overrideGameRandomLocations：有自动结盟映射，或出生点非连续（超出 MaxPlayers 的 waypoint）
        bool overrideGameRandomLocations = mappings.Any()
            || (gameModeMap.AllowedStartingLocations.Count > 0
                && gameModeMap.AllowedStartingLocations.Max() > gameModeMap.MaxPlayers);

        PlayerHouseInfo[] houseInfos = new PlayerHouseInfo[players.Count + aiCount];
        for (int i = 0; i < players.Count; i++)
        {
            PlayerInfo p = players[i];
            houseInfos[i] = new PlayerHouseInfo
            {
                SideIndex = p.SideId,
                ColorIndex = p.ColorId,
                // G5：阵营索引 == 观战索引（= 真实阵营数量，本端无 Random/随机选择器）即视为观战者。
                // 观战者 InternalSideIndex 取 SpectatorInternalSideIndex；出生点固定 waypoint 90（RandomizeStart）。
                IsSpectator = p.SideId == sideCount
            };
            houseInfos[i].RandomizeStart(p, pseudoRandom, freeStartingLocations, takenStartingLocations, overrideGameRandomLocations);
        }

        for (int aiId = 0; aiId < aiCount; aiId++)
        {
            PlayerInfo ai = aiPlayers![aiId];
            houseInfos[players.Count + aiId] = new PlayerHouseInfo
            {
                SideIndex = ai.SideId,
                ColorIndex = ai.ColorId
            };
            houseInfos[players.Count + aiId].RandomizeStart(ai, pseudoRandom, freeStartingLocations, takenStartingLocations, overrideGameRandomLocations);
        }

        IniFile spawnIni = new IniFile(spawnerSettingsFile.FullName);

        IniSection settings = new IniSection("Settings");
        settings.SetStringValue("Name", ProgramConstants.PLAYERNAME);
        settings.SetStringValue("Scenario", ProgramConstants.SPAWNMAP_INI);
        settings.SetStringValue("UIGameMode", gameModeMap.GameMode.UntranslatedUIName);
        settings.SetStringValue("UIMapName", gameModeMap.Map.UntranslatedName);

        // needed for translation in game loading lobbies
        if (gameModeMap.Map.Official)
            settings.SetStringValue("MapID", gameModeMap.Map.BaseFilePath);

        settings.SetIntValue("PlayerCount", players.Count);
        int myIndex = FindPlayerIndex(players, ProgramConstants.PLAYERNAME);
        settings.SetIntValue("Side", houseInfos[myIndex].InternalSideIndex);
        settings.SetBooleanValue("IsSpectator", houseInfos[myIndex].IsSpectator);
        settings.SetIntValue("Color", houseInfos[myIndex].ColorIndex);

        // 对齐旧版 G2：本端加载画面随阵营名（原版 GameLobbyBase.WriteSpawnIni L1706）
        settings.SetStringValue("CustomLoadScreen", LoadingScreenController.GetLoadScreenName(houseInfos[myIndex].InternalSideIndex.ToString()));

        settings.SetIntValue("AIPlayers", aiCount);
        settings.SetIntValue("Seed", randomSeed);
        if (gameModeMap.IsCoop)
            settings.SetBooleanValue("AutoSurrender", false);

        // 对齐旧版：PVP 的人类队伍数 >1 时进入教练模式（原版 L1709-1710）
        if (GetPvPTeamCount(players, houseInfos) > 1)
            settings.SetBooleanValue("CoachMode", true);

        spawnIni.AddSection(settings);

        // M3.7 网关/对局标识：GameID + 本端是否主机 + 本端端口（隧道接入后追加 [Tunnel] 节）
        spawnIni.SetIntValue("Settings", "GameID", uniqueGameId);
        spawnIni.SetBooleanValue("Settings", "Host", isHost);
        if (players[myIndex].Port > 0)
            spawnIni.SetIntValue("Settings", "Port", players[myIndex].Port);

        // M3.11 隧道：多人对局写入 [Tunnel] 节（Ip = 隧道服务器地址，Port = 隧道监听端口）。
        // 各端用 [Settings] Port（隧道下发的本端端口）连入隧道，OtherN.Ip 保持 0.0.0.0。
        if (!string.IsNullOrEmpty(tunnelIp) && tunnelPort > 0)
        {
            spawnIni.SetStringValue("Tunnel", "Ip", tunnelIp);
            spawnIni.SetIntValue("Tunnel", "Port", tunnelPort);
        }

        // 游戏选项（G7）：先写默认/用户选择值，随后 GameMode/Map 的强制选项覆盖（同原版顺序）
        if (gameOptions is not null)
        {
            foreach (GameSessionOption option in gameOptions)
                option.ApplySpawnIniCode(spawnIni);
        }

        // 对齐旧版：GameOptions.ini [ForcedSpawnIniOptions] 强制选项（原版 L1722-1733）
        var forcedGameOptionsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS));
        List<string> forcedSpawnKeys = forcedGameOptionsIni.GetSectionKeys("ForcedSpawnIniOptions");
        if (forcedSpawnKeys is not null)
        {
            foreach (string key in forcedSpawnKeys)
                spawnIni.SetStringValue("Settings", key,
                    forcedGameOptionsIni.GetStringValue("ForcedSpawnIniOptions", key, string.Empty));
        }

        // 强制选项：游戏模式 + 地图（含 Credits/UnitCount/中立/特殊颜色等；遭遇战携带 AI 数量与 coop 标记）
        gameModeMap.GameMode.ApplySpawnIniCode(spawnIni);
        gameModeMap.Map.ApplySpawnIniCode(spawnIni, players.Count + aiCount, aiCount,
            isCoop: gameModeMap.IsCoop, coopInfo: gameModeMap.CoopInfo,
            coopDifficultyLevel: gameModeMap.CoopDifficultyLevel,
            new Random(randomSeed), sideCount);

        // 其他玩家（不含自己）：Name / Side / Color / Ip / Port
        int otherId = 1;
        for (int pId = 0; pId < players.Count; pId++)
        {
            PlayerInfo p = players[pId];
            if (p.Name == ProgramConstants.PLAYERNAME)
                continue;

            string sectionName = "Other" + otherId;
            spawnIni.SetStringValue(sectionName, "Name", p.Name);
            spawnIni.SetIntValue(sectionName, "Side", houseInfos[pId].InternalSideIndex);
            spawnIni.SetBooleanValue(sectionName, "IsSpectator", houseInfos[pId].IsSpectator);
            spawnIni.SetIntValue(sectionName, "Color", houseInfos[pId].ColorIndex);
            spawnIni.SetStringValue(sectionName, "Ip", p.IPAddress);
            spawnIni.SetIntValue(sectionName, "Port", p.Port);
            otherId++;
        }

        // The spawner assigns players to SpawnX houses based on their in-game color index.
        // 按颜色（GameColorIndex）升序排列玩家，得到 Multi1..MultiN 的顺序（各端一致）。
        List<int> multiCmbIndexes = Enumerable.Range(0, players.Count)
            .OrderBy(i => houseInfos[i].ColorIndex)
            .ToList();

        // 出生点：Multi{i+1} = startingWaypoint；-1 表示交给游戏随机（G3：不写 SpawnLocations）
        for (int pId = 0; pId < players.Count; pId++)
        {
            int startingWaypoint = houseInfos[multiCmbIndexes[pId]].StartingWaypoint;
            if (startingWaypoint > -1)
                spawnIni.SetIntValue("SpawnLocations", "Multi" + (pId + 1), startingWaypoint);
        }

        // AI 房屋：HouseHandicaps（难度，注意 AILevel→HouseHandicapAILevel 反转）/HouseCountries/HouseColors
        // MultiN 索引紧随人类玩家颜色排序之后（与 AllianceHolder 一致）。
        if (aiCount > 0)
        {
            for (int aiId = 0; aiId < aiCount; aiId++)
            {
                int multiId = multiCmbIndexes.Count + aiId + 1;
                string keyName = "Multi" + multiId;
                spawnIni.SetIntValue("HouseHandicaps", keyName, aiPlayers![aiId].HouseHandicapAILevel);
                spawnIni.SetIntValue("HouseCountries", keyName, houseInfos[players.Count + aiId].InternalSideIndex);
                spawnIni.SetIntValue("HouseColors", keyName, houseInfos[players.Count + aiId].ColorIndex);
            }
        }

        // AI 出生点：Multi 索引在人类玩家之后（Players.Count + aiId + 1，与原版一致）
        for (int aiId = 0; aiId < aiCount; aiId++)
        {
            int startingWaypoint = houseInfos[players.Count + aiId].StartingWaypoint;
            if (startingWaypoint > -1)
            {
                int multiIndex = players.Count + aiId + 1;
                spawnIni.SetIntValue("SpawnLocations", "Multi" + multiIndex, startingWaypoint);
            }
        }

        // 观战者（G5）：[IsSpectator] 节，观战的 Multi 索引置 true（同原版 GameLobbyBase.WriteSpawnIni）
        for (int multiId = 0; multiId < multiCmbIndexes.Count; multiId++)
        {
            int pIndex = multiCmbIndexes[multiId];
            if (houseInfos[pIndex].IsSpectator)
                spawnIni.SetBooleanValue("IsSpectator", "Multi" + (multiId + 1), true);
        }

        // 联盟（玩家按队伍分组，无队伍则无联盟；自动结盟时按出生点→队伍映射兜底；AI 玩家同样参与联盟分组）
        AllianceHolder.WriteInfoToSpawnIni(
            players.ToList(),
            aiPlayers?.ToList() ?? new List<PlayerInfo>(),
            multiCmbIndexes,
            houseInfos.ToList(),
            teamStartMappings ?? new List<TeamStartMapping>(),
            spawnIni);

        spawnIni.SetStringValue("Settings", "MapSHA1", gameModeMap.Map.SHA1);
        spawnIni.SetStringValue("Settings", "BroadcastedGameOptionValues",
            gameOptions is null ? string.Empty : GetPackedGameOptionValuesString(gameOptions)); // 对齐旧版 GetPackedGameOptionValuesString

        // 对齐旧版 GameLobbyBase.WriteSpawnIni：写复盘录制元数据（EnableReplayRecording / GamePackageVersion / ReplayFileOut）
        ReplayManager.PrepareRecording(spawnIni, gameModeMap.Map.UntranslatedName);

        spawnIni.WriteIniFile();

        return houseInfos;
    }

    /// <summary>
    /// Writes spawnmap.ini: loads the original map INI, applies GlobalCode.ini and
    /// MultiplayerGlobalCode.ini map code, then applies game-option custom INI code
    /// (e.g. AlliesAllowed / StolenTech / BrutalAI), and writes the result to the game path.
    /// </summary>
    public static void WriteMap(
        IReadOnlyList<PlayerInfo> players,
        GameModeMap gameModeMap,
        IReadOnlyList<GameSessionOption>? gameOptions = null)
    {
        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
        spawnMapIniFile.Delete();

        Logger.Log("Writing map.");

        IniFile mapIni = gameModeMap.Map.GetMapIni();

        IniFile globalCodeIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "INI", "Map Code", "GlobalCode.ini"));
        MapCodeHelper.ApplyMapCode(mapIni, globalCodeIni);

        IniFile mpGlobalCodeIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "INI", "Map Code", "MultiplayerGlobalCode.ini"));
        MapCodeHelper.ApplyMapCode(mapIni, mpGlobalCodeIni);

        // 游戏选项自定义地图代码（G7：AlliesAllowed / StolenTech / NavalCombat 等，勾选生效）
        if (gameOptions is not null)
        {
            foreach (GameSessionOption option in gameOptions)
                option.ApplyMapCode(mapIni, gameModeMap.GameMode);
        }

        mapIni.MoveSectionToFirst("MultiplayerDialogSettings"); // Required by YR

        mapIni.WriteIniFile(spawnMapIniFile.FullName);

        _ = players; // 出生点叠加（ManipulateStartingLocations）在 M3.7 简化版未做，后续 M3.9 补
    }

    /// <summary>
    /// 打包广播到大厅的游戏选项值字符串（对齐旧版 <c>GameLobbyBase.GetPackedGameOptionValuesString</c>）：
    /// 取 BroadcastToLobby 的复选框 Checked → BoolArrayIntoBytes 编码 → 补足 4 字节 → 每 4 字节 ReadInt32LittleEndian，
    /// 再接 BroadcastToLobby 下拉框的 SelectedIndex；逗号拼接，空则空串。
    /// </summary>
    private static string GetPackedGameOptionValuesString(IReadOnlyList<GameSessionOption> gameOptions)
    {
        var values = new List<int>();

        var broadcastCheckBoxes = gameOptions.OfType<GameSessionCheckBoxOption>().Where(cb => cb.BroadcastToLobby).ToList();
        if (broadcastCheckBoxes.Count > 0)
        {
            bool[] checkboxValues = broadcastCheckBoxes.Select(cb => cb.Checked).ToArray();
            List<byte> byteList = Conversions.BoolArrayIntoBytes(checkboxValues).ToList();
            while (byteList.Count % 4 != 0)
                byteList.Add(0);
            byte[] byteArray = byteList.ToArray();
            for (int i = 0; i < byteArray.Length / 4; i++)
                values.Add(BinaryPrimitives.ReadInt32LittleEndian(byteArray.AsSpan(i * 4)));
        }

        var broadcastDropDowns = gameOptions.OfType<GameSessionDropDownOption>().Where(dd => dd.BroadcastToLobby).ToList();
        if (broadcastDropDowns.Count > 0)
            values.AddRange(broadcastDropDowns.Select(dd => dd.SelectedIndex));

        return values.Count > 0 ? string.Join(",", values) : string.Empty;
    }

    private static int FindPlayerIndex(IReadOnlyList<PlayerInfo> players, string name)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (string.Equals(players[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }

    /// <summary>
    /// 计算人类（非观战）玩家的队伍数（原版 GameLobbyBase.GetPvPTeamCount）：
    /// 用于决定是否启用 CoachMode。观战者与未分队的玩家不计入。
    /// </summary>
    private static int GetPvPTeamCount(IReadOnlyList<PlayerInfo> players, PlayerHouseInfo[] houseInfos)
    {
        var teamIds = new HashSet<int>();
        for (int i = 0; i < players.Count; i++)
        {
            if (houseInfos[i].IsSpectator)
                continue;
            int teamId = players[i].TeamId;
            if (teamId > 0)
                teamIds.Add(teamId);
        }
        return teamIds.Count;
    }

    // ── 单机任务（战役）spawn.ini（M4.6，移植原版 CampaignSelector.LaunchMission + WriteMissionSectionToSpawnIni） ──

    /// <summary>难度名（Easy/Medium/Hard，索引即原版 trbDifficultySelector.Value）。</summary>
    public static readonly string[] MissionDifficultyNames = { "Easy", "Medium", "Hard" };

    /// <summary>难度对应的地图代码 INI 路径（相对游戏目录）。</summary>
    private static readonly string[] MissionDifficultyIniPaths =
    {
        "INI/Map Code/Difficulty Easy.ini",
        "INI/Map Code/Difficulty Medium.ini",
        "INI/Map Code/Difficulty Hard.ini"
    };

    /// <summary>难度取反（原版 GetComputerDifficulty：电脑难度 = |难度 - 2|，Hard→Easy）。</summary>
    private static int GetComputerDifficulty(int difficulty) => Math.Abs(difficulty - 2);

    /// <summary>初始化自定义任务补充文件定义（生成/清理任务补充文件的前置条件；公开包装 internal 的 CustomMissionHelper）。</summary>
    public static void InitializeMissionSupplementFiles() => CustomMissionHelper.Initialize();

    /// <summary>游戏进程退出后清理自定义任务补充文件（公开包装 internal 的 CustomMissionHelper）。</summary>
    public static void DeleteMissionSupplementalFiles() => CustomMissionHelper.DeleteSupplementalMissionFiles();

    /// <summary>
    /// 生成单机任务（战役）的 spawn.ini / spawnmap.ini：
    /// [Settings]（Scenario / CampaignID / GameSpeed / Ra2Mode·Firestorm / CustomLoadScreen /
    /// IsSinglePlayer / SidebarHack / Side / BuildOffAlly / 人机难度 / CustomMissionID）、
    /// 任务配置节（ReadMissionSection=Yes，加载画面与任务数据）、战役游戏选项与 GameOptions.ini
    /// [CampaignForcedSpawnIniOptions] 强制选项；CopyMissionsToSpawnmapINI 开启时合并难度地图代码写 spawnmap.ini。
    /// 生成前调用 CustomMissionHelper 复制自定义任务补充文件（.shp/.pal 等）。
    /// </summary>
    public static void WriteMissionSpawnIni(
        Mission mission,
        int difficulty,
        IReadOnlyList<GameSessionOption>? campaignOptions = null)
    {
        Logger.Log("About to write spawn.ini.");
        CustomMissionHelper.CopySupplementalMissionFiles(mission);

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        spawnerSettingsFile.Delete();

        bool copyMapsToSpawnmapINI = ClientConfiguration.Instance.CopyMissionsToSpawnmapINI;

        string scenario = mission.Scenario;
        bool scenarioPathFound = mission.TryGetScenarioFilePath(out string scenarioPath);

        if (!scenarioPathFound)
        {
            Logger.Log($"CampaignSelector: mission scenario contains invalid path characters. Mission code name: {mission.CodeName}. Scenario: {mission.Scenario}. This mission will be launched without applying CopyMissionsToSpawnmapINI.");
            copyMapsToSpawnmapINI = false;
        }

        IniFile spawnIni = new IniFile(spawnerSettingsFile.FullName)
        {
            Comment = "Generated by CnCNet Client"
        };
        IniSection spawnIniSettings = new("Settings");

        spawnIniSettings.SetStringValue("Scenario", copyMapsToSpawnmapINI ? "spawnmap.ini" : scenario);

        // 没人想在 Fastest 下打任务，强制降到 Faster（原版注释语义）
        if (UserINISettings.Instance.GameSpeed.Value == 0)
            UserINISettings.Instance.GameSpeed.Value = 1;

        spawnIniSettings.SetIntValue("CampaignID", mission.CampaignID);
        spawnIniSettings.SetIntValue("GameSpeed", UserINISettings.Instance.GameSpeed.Value);

        switch (ClientConfiguration.Instance.ClientGameType)
        {
            case ClientType.YR or ClientType.Ares:
                spawnIniSettings.SetBooleanValue("Ra2Mode", !mission.RequiredAddon);
                break;
            case ClientType.TS:
                spawnIniSettings.SetBooleanValue("Firestorm", mission.RequiredAddon);
                break;
        }

        spawnIniSettings.SetStringValue("CustomLoadScreen", LoadingScreenController.GetLoadScreenName(mission.Side.ToString()));

        spawnIniSettings.SetStringValue("IsSinglePlayer", "Yes");
        spawnIniSettings.SetBooleanValue("SidebarHack", ClientConfiguration.Instance.SidebarHack);
        spawnIniSettings.SetIntValue("Side", mission.Side);
        spawnIniSettings.SetBooleanValue("BuildOffAlly", mission.BuildOffAlly);

        UserINISettings.Instance.Difficulty.Value = difficulty;

        spawnIniSettings.SetStringValue("DifficultyModeHuman", mission.PlayerAlwaysOnNormalDifficulty ? "1" : difficulty.ToString(CultureInfo.InvariantCulture));
        spawnIniSettings.SetStringValue("DifficultyModeComputer", GetComputerDifficulty(difficulty).ToString(CultureInfo.InvariantCulture));

        if (mission.IsCustomMission)
            spawnIniSettings.SetIntValue("CustomMissionID", mission.CustomMissionID);

        spawnIni.AddSection(spawnIniSettings);
        WriteMissionSectionToSpawnIni(spawnIni, mission);

        // 战役游戏选项（MOD 提供时，如 BattleFS 配套 CampaignOptions；DTA 默认无）
        if (campaignOptions is not null)
        {
            foreach (GameSessionOption option in campaignOptions)
                option.ApplySpawnIniCode(spawnIni);
        }

        // GameOptions.ini [CampaignForcedSpawnIniOptions] 强制选项（同原版）
        var gameOptionsIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), ClientConfiguration.GAME_OPTIONS));
        List<string> forcedKeys = gameOptionsIni.GetSectionKeys("CampaignForcedSpawnIniOptions");
        if (forcedKeys is not null)
        {
            foreach (string key in forcedKeys)
                spawnIni.SetStringValue("Settings", key,
                    gameOptionsIni.GetStringValue("CampaignForcedSpawnIniOptions", key, string.Empty));
        }

        spawnIni.WriteIniFile();

        // CopyMissionsToSpawnmapINI：难度地图代码合并进 spawnmap.ini（原版对任务图应用 Difficulty Easy/Medium/Hard.ini）
        if (copyMapsToSpawnmapINI)
        {
            var difficultyIni = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, MissionDifficultyIniPaths[difficulty]));
            var mapIni = new IniFile(scenarioPath);

            IniFile.ConsolidateIniFiles(mapIni, difficultyIni);

            if (campaignOptions is not null)
            {
                foreach (GameSessionOption option in campaignOptions)
                    option.ApplyMapCode(mapIni, gameMode: null);
            }

            mapIni.WriteIniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, "spawnmap.ini"));
        }

        UserINISettings.Instance.SaveSettings();
    }

    /// <summary>
    /// 生成"加载单机存档"的 spawn.ini / spawnmap.ini（原版 GameLoadingWindow.BtnLaunch_LeftClick 语义）：
    /// Scenario=spawnmap.ini + SaveGameName + LoadSaveGame=Yes；若存档属于自定义任务（CustomMissionID 匹配到
    /// <see cref="Mission"/>），先复制补充文件并附加 CustomMissionID 与任务配置节。
    /// </summary>
    public static void WriteSaveGameSpawnIni(SavedGame saveGame, Mission? mission)
    {
        if (mission is not null)
            CustomMissionHelper.CopySupplementalMissionFiles(mission);

        FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
        if (spawnerSettingsFile.Exists)
            spawnerSettingsFile.Delete();

        IniFile spawnIni = new()
        {
            Comment = "Generated by CnCNet Client"
        };

        IniSection spawnIniSettings = new("Settings");
        spawnIniSettings.SetStringValue("Scenario", "spawnmap.ini");
        spawnIniSettings.SetStringValue("SaveGameName", saveGame.FileName);
        spawnIniSettings.SetStringValue("LoadSaveGame", "Yes");
        spawnIniSettings.SetBooleanValue("SidebarHack", ClientConfiguration.Instance.SidebarHack);
        spawnIniSettings.SetStringValue("CustomLoadScreen", LoadingScreenController.GetLoadScreenName("g"));
        spawnIniSettings.SetStringValue("Firestorm", "No");
        spawnIniSettings.SetIntValue("GameSpeed", UserINISettings.Instance.GameSpeed.Value);

        if (mission is not null)
        {
            spawnIniSettings.SetIntValue("CustomMissionID", saveGame.CustomMissionID);
            WriteMissionSectionToSpawnIni(spawnIni, mission);
        }

        spawnIni.AddSection(spawnIniSettings);
        spawnIni.WriteIniFile(spawnerSettingsFile.FullName);

        // spawnmap.ini：加载存档时游戏以 50×50 全图占位（原版同款写法）
        FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, "spawnmap.ini");
        if (spawnMapIniFile.Exists)
            spawnMapIniFile.Delete();

        using (var spawnMapStreamWriter = new StreamWriter(spawnMapIniFile.FullName))
        {
            spawnMapStreamWriter.WriteLine("[Map]");
            spawnMapStreamWriter.WriteLine("Size=0,0,50,50");
            spawnMapStreamWriter.WriteLine("LocalSize=0,0,50,50");
            spawnMapStreamWriter.WriteLine();
        }
    }

    /// <summary>
    /// 将任务的 [GameMissionConfig] 节复制进 spawn.ini（原版 WriteMissionSectionToSpawnIni）：
    /// 官方任务从地图文件读取配置节；自定义任务用 CampaignService 加载时解析的节。
    /// 缺失加载画面键时回退自定义任务补充文件（.shp/.pal），并写 [Settings] ReadMissionSection=Yes。
    /// </summary>
    public static void WriteMissionSectionToSpawnIni(IniFile spawnIni, Mission mission)
    {
        bool hasGameMissionData = false;

        bool scenarioPathFound = mission.TryGetScenarioFilePath(out string scenarioPath);
        if (!scenarioPathFound)
        {
            Logger.Log($"CampaignSelector: mission scenario contains invalid path characters. Mission code name: {mission.CodeName}. Scenario: {mission.Scenario}. This mission will be launched without mission section data.");
            return;
        }

        if (!mission.IsCustomMission && File.Exists(scenarioPath))
        {
            var mapIni = new IniFile(scenarioPath);
            mission.GameMissionConfigSection = mapIni.GetSection("GameMissionConfig");

            if (mission.GameMissionConfigSection is not null)
                hasGameMissionData = true;
        }

        if ((mission.IsCustomMission && mission.GameMissionConfigSection is not null) || hasGameMissionData)
        {
            // 复制 IniSection 到 spawn.ini（节名为 Scenario 路径）
            IniSection spawnIniMissionIniSection = new(mission.Scenario);
            string loadingScreenName = string.Empty;
            string loadingScreenPalName = string.Empty;
            foreach (var kvp in mission.GameMissionConfigSection!.Keys)
            {
                if (string.IsNullOrEmpty(kvp.Value))
                {
                    if (kvp.Key.Equals("LS640BkgdName", StringComparison.InvariantCulture) || kvp.Key.Equals("LS800BkgdName", StringComparison.InvariantCulture))
                        loadingScreenName = kvp.Value;
                    else if (kvp.Key.Equals("LS800BkgdPal", StringComparison.InvariantCulture))
                        loadingScreenPalName = kvp.Value;
                }

                spawnIniMissionIniSection.AddKey(kvp.Key, kvp.Value);
            }

            if (string.IsNullOrEmpty(loadingScreenName))
            {
                string lsFilename = CustomMissionHelper.CustomMissionSupplementDefinition
                    ?.FirstOrDefault(x => x.extension.Equals("shp", StringComparison.InvariantCultureIgnoreCase)).filename ?? string.Empty;

                if (!string.IsNullOrEmpty(lsFilename))
                {
                    spawnIniMissionIniSection.AddOrReplaceKey("LS640BkgdName", lsFilename);
                    spawnIniMissionIniSection.AddOrReplaceKey("LS800BkgdName", lsFilename);
                }
            }

            if (string.IsNullOrEmpty(loadingScreenPalName))
            {
                string palFilename = CustomMissionHelper.CustomMissionSupplementDefinition
                    ?.FirstOrDefault(x => x.extension.Equals("pal", StringComparison.InvariantCultureIgnoreCase)).filename ?? string.Empty;

                if (!string.IsNullOrEmpty(palFilename))
                    spawnIniMissionIniSection.AddOrReplaceKey("LS800BkgdPal", palFilename);
            }

            spawnIni.AddSection(spawnIniMissionIniSection);
            spawnIni.SetStringValue("Settings", "ReadMissionSection", "Yes");
        }
    }
}
