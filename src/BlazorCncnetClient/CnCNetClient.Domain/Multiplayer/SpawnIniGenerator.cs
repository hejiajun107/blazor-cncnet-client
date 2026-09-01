using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CnCNetClient.Core;
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
        settings.SetIntValue("AIPlayers", aiCount);
        settings.SetIntValue("Seed", randomSeed);
        if (gameModeMap.IsCoop)
            settings.SetBooleanValue("AutoSurrender", false);
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
        spawnIni.SetStringValue("Settings", "BroadcastedGameOptionValues", "0"); // M3.7 无打包游戏选项

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

    private static int FindPlayerIndex(IReadOnlyList<PlayerInfo> players, string name)
    {
        for (int i = 0; i < players.Count; i++)
        {
            if (string.Equals(players[i].Name, name, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return 0;
    }
}
