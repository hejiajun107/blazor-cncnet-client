using System;
using System.Collections.Generic;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Core.Statistics;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Tools;
using SixLabors.ImageSharp.PixelFormats;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>对局类别筛选（原版 StatisticsWindow.cmbGameClassFilter）。</summary>
public enum GameClassFilter
{
    All,
    Online,
    PvP,
    CoOp,
    Skirmish
}

/// <summary>对局列表行（原版 lbGameList）。</summary>
public sealed record MatchRow(int Index, string DateText, string MapName, string GameMode,
    string Fps, string Duration, string Completed);

/// <summary>对局内玩家行（原版 lbGameStatistics）。</summary>
public sealed record PlayerRow(string Name, string Kills, string Losses, string Economy,
    string Score, string Won, string Side, string Team, string Color, bool IsLocal, bool IsAi, bool WasSpectator);

/// <summary>总统计标签-值集合（原版 SetTotalStatistics 全部 lbl*Value）。</summary>
public sealed class TotalStatistics
{
    public int GamesStarted { get; set; }
    public int GamesFinished { get; set; }
    public int Wins { get; set; }
    public int Losses { get; set; }
    public string WinLossRatio { get; set; } = "-";
    public string AverageGameLength { get; set; } = "-";
    public string TotalTimePlayed { get; set; } = "-";
    public string AverageEnemyCount { get; set; } = "-";
    public string AverageAllyCount { get; set; } = "-";
    public string TotalKills { get; set; } = "-";
    public string KillsPerGame { get; set; } = "-";
    public string TotalLosses { get; set; } = "-";
    public string LossesPerGame { get; set; } = "-";
    public string KillLossRatio { get; set; } = "-";
    public string TotalScore { get; set; } = "-";
    public string AverageEconomy { get; set; } = "-";
    public string FavouriteSide { get; set; } = "-";
    public string AverageAILevel { get; set; } = "-";
}

/// <summary>
/// 统计窗口服务（M4.8，原版 <c>DXMainClient/DXGUI/Generic/StatisticsWindow.cs</c>）。
/// 包装 <see cref="StatisticsManager"/>：读取 dscore.dat、对局列表过滤（类别/模式/观战）、
/// 选中对局玩家明细、总统计汇总、清空统计。
/// </summary>
public sealed class StatisticsService
{
    private readonly StatisticsManager _sm = StatisticsManager.Instance;
    private (string Name, string UIName)[] _sides = Array.Empty<(string, string)>();
    private List<MultiplayerColor> _colors = new();
    private readonly List<int> _listedIndexes = new();

    /// <summary>数据已读取标记（页面首次渲染时触发 Load）。</summary>
    public bool IsLoaded { get; private set; }

    public GameClassFilter Filter { get; set; } = GameClassFilter.All;

    /// <summary>模式筛选（null = 全部）。</summary>
    public string? SelectedGameMode { get; set; }

    /// <summary>是否包含观战局（原版 chkIncludeSpectatedGames）。</summary>
    public bool IncludeSpectated { get; set; } = true;

    /// <summary>可用游戏模式列表（含翻译名，原版 cmbGameModeFilter）。</summary>
    public List<string> GameModes { get; } = new();

    public List<MatchRow> Matches { get; } = new();

    public List<PlayerRow> Players { get; } = new();

    public TotalStatistics Total { get; } = new();

    /// <summary>读取统计数据库并初始化筛选/阵营/颜色（原版 Initialize 末尾 ReadStatistics + ListGameModes）。</summary>
    public void Load()
    {
        _sm.ReadStatistics(ProgramConstants.GamePath);
        _sides = ClientConfiguration.Instance.Sides.Split(',')
            .Select(s => (Name: s, UIName: s.L10N($"INI:Sides:{s}"))).ToArray();
        _colors = MultiplayerColor.LoadColors();
        ListGameModes();
        Refresh();
        IsLoaded = true;
    }

    /// <summary>按当前筛选条件重建对局列表与总统计（原版 ListGames + SetTotalStatistics）。</summary>
    public void Refresh()
    {
        Matches.Clear();
        Players.Clear();
        _listedIndexes.Clear();

        switch (Filter)
        {
            case GameClassFilter.Online:
                ListOnlineGames();
                break;
            case GameClassFilter.PvP:
                ListPvPGames();
                break;
            case GameClassFilter.CoOp:
                ListCoOpGames();
                break;
            case GameClassFilter.Skirmish:
                ListSkirmishGames();
                break;
            default:
                ListAllGames();
                break;
        }

        _listedIndexes.Reverse(); // 新局在前（原版同）

        foreach (int gameIndex in _listedIndexes)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(gameIndex);
            string dateTime = ms.DateAndTime.ToShortDateString() + " " + ms.DateAndTime.ToShortTimeString();
            Matches.Add(new MatchRow(
                gameIndex,
                dateTime,
                ms.MapName,
                ms.GameMode.L10N($"INI:GameModes:{ms.GameMode}:UIName"),
                ms.AverageFPS == 0 ? "-" : ms.AverageFPS.ToString(),
                TimeSpan.FromSeconds(ms.LengthInSeconds).ToString(),
                Conversions.BooleanToString(ms.SawCompletion, BooleanStringStyle.YESNO)));
        }

        SetTotalStatistics();
    }

    /// <summary>选中对局 → 填充玩家明细（原版 LbGameList_SelectedIndexChanged：按 Score 降序）。</summary>
    public void SelectMatch(int index)
    {
        Players.Clear();

        MatchStatistics ms = _sm.GetMatchByIndex(index);
        List<PlayerStatistics> players = new();
        for (int i = 0; i < ms.GetPlayerCount(); i++)
            players.Add(ms.GetPlayer(i));
        players = players.OrderBy(p => p.Score).Reverse().ToList();

        for (int i = 0; i < ms.GetPlayerCount(); i++)
        {
            PlayerStatistics ps = players[i];
            string color = "#7af3ff";
            if (ps.Color > -1 && ps.Color < _colors.Count)
                color = Rgba32ToCss(_colors[ps.Color].XnaColor);

            string name = ps.IsAI ? ProgramConstants.GetAILevelName(ps.AILevel) : ps.Name;

            string kills, losses, economy, score, won, side, team;
            if (ps.WasSpectator)
            {
                kills = losses = economy = score = won = "-";
                side = "Spectator".L10N("Client:Main:Spectator");
                team = "-";
            }
            else
            {
                if (!ms.SawCompletion)
                {
                    // 未完成局无法得知详细数据（原版同）
                    kills = losses = economy = score = won = "-";
                }
                else
                {
                    kills = ps.Kills.ToString();
                    losses = ps.Losses.ToString();
                    economy = ps.Economy.ToString();
                    score = ps.Score.ToString();
                    won = Conversions.BooleanToString(ps.Won, BooleanStringStyle.YESNO);
                }

                side = ps.Side == 0 || ps.Side > _sides.Length
                    ? "Unknown".L10N("Client:Main:UnknownSide")
                    : _sides[ps.Side - 1].UIName;
                team = TeamIndexToString(ps.Team);
            }

            Players.Add(new PlayerRow(name, kills, losses, economy, score, won, side, team,
                color, ps.IsLocalPlayer, ps.IsAI, ps.WasSpectator));
        }
    }

    /// <summary>清空统计数据库并重载（原版 ClearAllStatistics，UI 先弹确认框）。</summary>
    public void ClearAll()
    {
        _sm.ClearDatabase();
        _sm.ReadStatistics(ProgramConstants.GamePath);
        ListGameModes();
        Refresh();
    }

    private void ListGameModes()
    {
        int gameCount = _sm.GetMatchCount();
        List<string> gameModes = new();
        for (int i = 0; i < gameCount; i++)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(i);
            if (!gameModes.Contains(ms.GameMode))
                gameModes.Add(ms.GameMode);
        }
        gameModes.Sort();
        GameModes.Clear();
        GameModes.AddRange(gameModes.Select(gm => gm.L10N($"INI:GameModes:{gm}:UIName")));
        SelectedGameMode = null;
    }

    private void ListAllGames()
    {
        int gameCount = _sm.GetMatchCount();
        for (int i = 0; i < gameCount; i++)
            ListGameIndexIfPrerequisitesMet(i);
    }

    private void ListOnlineGames()
    {
        int gameCount = _sm.GetMatchCount();
        for (int i = 0; i < gameCount; i++)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(i);
            int hpCount = 0;
            for (int j = 0; j < ms.GetPlayerCount(); j++)
            {
                PlayerStatistics ps = ms.GetPlayer(j);
                if (!ps.IsAI)
                {
                    hpCount++;
                    if (hpCount > 1)
                    {
                        ListGameIndexIfPrerequisitesMet(i);
                        break;
                    }
                }
            }
        }
    }

    private void ListPvPGames()
    {
        int gameCount = _sm.GetMatchCount();
        for (int i = 0; i < gameCount; i++)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(i);
            int pTeam = -1;
            for (int j = 0; j < ms.GetPlayerCount(); j++)
            {
                PlayerStatistics ps = ms.GetPlayer(j);
                if (!ps.IsAI && !ps.WasSpectator)
                {
                    // 存在不同队伍的人类玩家 → PvP 局
                    if (pTeam > -1 && (ps.Team != pTeam || ps.Team == 0))
                    {
                        ListGameIndexIfPrerequisitesMet(i);
                        break;
                    }
                    pTeam = ps.Team;
                }
            }
        }
    }

    private void ListCoOpGames()
    {
        int gameCount = _sm.GetMatchCount();
        for (int i = 0; i < gameCount; i++)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(i);
            int hpCount = 0;
            int pTeam = -1;
            bool add = true;
            for (int j = 0; j < ms.GetPlayerCount(); j++)
            {
                PlayerStatistics ps = ms.GetPlayer(j);
                if (!ps.IsAI && !ps.WasSpectator)
                {
                    hpCount++;
                    if (pTeam > -1 && (ps.Team != pTeam || ps.Team == 0))
                    {
                        add = false;
                        break;
                    }
                    pTeam = ps.Team;
                }
            }
            if (add && hpCount > 1)
                ListGameIndexIfPrerequisitesMet(i);
        }
    }

    private void ListSkirmishGames()
    {
        int gameCount = _sm.GetMatchCount();
        for (int i = 0; i < gameCount; i++)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(i);
            int hpCount = 0;
            bool add = true;
            foreach (PlayerStatistics ps in ms.Players)
            {
                if (!ps.IsAI)
                {
                    hpCount++;
                    if (hpCount > 1)
                    {
                        add = false;
                        break;
                    }
                }
            }
            if (add)
                ListGameIndexIfPrerequisitesMet(i);
        }
    }

    private void ListGameIndexIfPrerequisitesMet(int gameIndex)
    {
        MatchStatistics ms = _sm.GetMatchByIndex(gameIndex);

        // 模式筛选：原版用下拉 Tag 存原始模式名；本移植 GameModes 存翻译名，按原值比对需另存。
        // 这里用翻译后名称比对（与 GameModes 列表一致）。
        if (SelectedGameMode is not null &&
            ms.GameMode.L10N($"INI:GameModes:{ms.GameMode}:UIName") != SelectedGameMode)
            return;

        PlayerStatistics? ps = ms.Players.Find(p => p.IsLocalPlayer);
        if (ps != null && !IncludeSpectated && ps.WasSpectator)
            return;

        _listedIndexes.Add(gameIndex);
    }

    private void SetTotalStatistics()
    {
        int gamesStarted = 0;
        int gamesFinished = 0;
        int gamesPlayed = 0;
        int wins = 0;
        int gameLosses = 0;
        TimeSpan timePlayed = TimeSpan.Zero;
        int numEnemies = 0;
        int numAllies = 0;
        int totalKills = 0;
        int totalLosses = 0;
        int totalScore = 0;
        int totalEconomy = 0;
        int[] sideGameCounts = new int[_sides.Length];
        int numEasyAIs = 0;
        int numMediumAIs = 0;
        int numHardAIs = 0;

        foreach (int gameIndex in _listedIndexes)
        {
            MatchStatistics ms = _sm.GetMatchByIndex(gameIndex);
            gamesStarted++;
            if (ms.SawCompletion)
                gamesFinished++;
            timePlayed += TimeSpan.FromSeconds(ms.LengthInSeconds);

            PlayerStatistics? localPlayer = ms.Players.Find(p => !p.IsAI && p.IsLocalPlayer);
            if (localPlayer is null || localPlayer.WasSpectator)
                continue;

            totalKills += localPlayer.Kills;
            totalLosses += localPlayer.Losses;
            totalScore += localPlayer.Score;
            totalEconomy += localPlayer.Economy;

            if (localPlayer.Side > 0 && localPlayer.Side <= _sides.Length)
                sideGameCounts[localPlayer.Side - 1]++;

            if (!ms.SawCompletion)
                continue;

            if (localPlayer.Won)
                wins++;
            else
                gameLosses++;
            gamesPlayed++;

            for (int i = 0; i < ms.GetPlayerCount(); i++)
            {
                PlayerStatistics ps = ms.GetPlayer(i);
                if (ps.WasSpectator || (ps.IsLocalPlayer && !ps.IsAI))
                    continue;
                if (ps.Team == 0 || localPlayer.Team != ps.Team)
                    numEnemies++;
                else
                    numAllies++;
                if (ps.IsAI)
                {
                    if (ps.AILevel == 0)
                        numEasyAIs++;
                    else if (ps.AILevel == 1)
                        numMediumAIs++;
                    else
                        numHardAIs++;
                }
            }
        }

        Total.GamesStarted = gamesStarted;
        Total.GamesFinished = gamesFinished;
        Total.Wins = wins;
        Total.Losses = gameLosses;

        Total.WinLossRatio = gameLosses > 0 ? Math.Round(wins / (double)gameLosses, 2).ToString() : "-";
        Total.AverageGameLength = gamesStarted > 0
            ? TimeSpan.FromSeconds((int)timePlayed.TotalSeconds / gamesStarted).ToString()
            : "-";
        Total.AverageEnemyCount = gamesPlayed > 0 ? Math.Round(numEnemies / (double)gamesPlayed, 2).ToString() : "-";
        Total.AverageAllyCount = gamesPlayed > 0 ? Math.Round(numAllies / (double)gamesPlayed, 2).ToString() : "-";
        Total.KillsPerGame = gamesPlayed > 0 ? (totalKills / gamesPlayed).ToString() : "-";
        Total.LossesPerGame = gamesPlayed > 0 ? (totalLosses / gamesPlayed).ToString() : "-";
        Total.AverageEconomy = gamesPlayed > 0 ? (totalEconomy / gamesPlayed).ToString() : "-";
        Total.KillLossRatio = totalLosses > 0 ? Math.Round(totalKills / (double)totalLosses, 2).ToString() : "-";
        Total.TotalTimePlayed = timePlayed.ToString();
        Total.TotalKills = totalKills.ToString();
        Total.TotalLosses = totalLosses.ToString();
        Total.TotalScore = totalScore.ToString();

        int highestSide = GetHighestIndex(sideGameCounts);
        Total.FavouriteSide = highestSide >= 0 ? _sides[highestSide].UIName : "-";

        Total.AverageAILevel = numEasyAIs >= numMediumAIs && numEasyAIs >= numHardAIs
            ? "Easy".L10N("Client:Main:EasyAI")
            : numMediumAIs >= numEasyAIs && numMediumAIs >= numHardAIs
                ? "Medium".L10N("Client:Main:MediumAI")
                : "Hard".L10N("Client:Main:HardAI");
    }

    private static string TeamIndexToString(int teamIndex)
        => teamIndex < 1 || teamIndex >= ProgramConstants.TEAMS.Count ? "-" : ProgramConstants.TEAMS[teamIndex - 1];

    private static int GetHighestIndex(int[] t)
    {
        int highestIndex = -1;
        int highest = int.MinValue;
        for (int i = 0; i < t.Length; i++)
        {
            if (t[i] > highest)
            {
                highest = t[i];
                highestIndex = i;
            }
        }
        return highestIndex;
    }

    private static string Rgba32ToCss(Rgba32 color)
        => "#" + color.ToHex()[..6];
}
