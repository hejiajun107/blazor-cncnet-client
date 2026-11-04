using System;
using System.Collections.Generic;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 开局前把房间内的“随机阵营 / 随机颜色”解析为具体值（对齐上游 GameLobbyBase.Randomize +
/// PlayerHouseInfo.RandomizeSide / RandomizeColor）。所有客户端使用同一 <c>RandomSeed</c> 且
/// 玩家顺序一致（房主 PO 广播顺序 = 各端列表顺序），因此解析结果一致，无需额外协议字段。
/// </summary>
internal static class MultiplayerOptionResolver
{
    /// <summary>解析后的玩家列表（人类 + AI，均为副本，不修改房间状态）。</summary>
    public sealed record ResolvedRoster(List<PlayerInfo> Players, List<PlayerInfo> AIPlayers);

    public static ResolvedRoster Resolve(
        IReadOnlyList<PlayerInfo> players,
        IReadOnlyList<PlayerInfo>? aiPlayers,
        int sideCount,
        int randomSeed,
        GameModeMap gameModeMap)
    {
        sideCount = Math.Max(1, sideCount);
        int randomCount = MultiplayerOptionIndex.RandomCount;
        var selectors = MultiplayerOptionIndex.Selectors;

        // 禁用阵营（上游 GetDisallowedSides）：GameMode.DisallowedPlayerSides + CoopInfo.DisallowedPlayerSides
        bool[] disallowedSides = new bool[sideCount];
        foreach (int side in gameModeMap.GameMode.DisallowedPlayerSides)
        {
            if (side >= 0 && side < sideCount)
                disallowedSides[side] = true;
        }
        if (gameModeMap.CoopInfo is { } coop)
        {
            foreach (int side in coop.DisallowedPlayerSides)
            {
                if (side >= 0 && side < sideCount)
                    disallowedSides[side] = true;
            }
        }

        // 可用颜色（上游 Randomize）：先剔除 coop 禁用色，再剔除所有已选具体颜色的玩家（人类在前，AI 在后）
        var colors = MultiplayerColor.LoadColors();
        var freeColors = new List<int>();
        for (int i = 0; i < colors.Count; i++)
            freeColors.Add(i);
        if (gameModeMap.CoopInfo is { } coopInfo)
        {
            foreach (int color in coopInfo.DisallowedPlayerColors)
                freeColors.Remove(color);
        }
        foreach (var p in players.Concat(aiPlayers ?? Array.Empty<PlayerInfo>()))
        {
            if (p.ColorId > 0)
                freeColors.Remove(p.ColorId - 1);
        }

        var random = new Random(randomSeed);

        // 顺序严格按“人类 → AI”（上游 Randomize 的两个循环），保证随机序列一致
        var resolvedPlayers = new List<PlayerInfo>(players.Count);
        foreach (var p in players)
            resolvedPlayers.Add(ResolveOne(p, isAI: false));

        var resolvedAI = new List<PlayerInfo>(aiPlayers?.Count ?? 0);
        if (aiPlayers is not null)
        {
            foreach (var ai in aiPlayers)
                resolvedAI.Add(ResolveOne(ai, isAI: true));
        }

        return new ResolvedRoster(resolvedPlayers, resolvedAI);

        PlayerInfo ResolveOne(PlayerInfo src, bool isAI)
        {
            bool spectator = MultiplayerOptionIndex.IsSpectator(src.SideId, sideCount);
            int sideIndex;

            if (src.SideId == MultiplayerOptionIndex.Random || spectator)
            {
                // 完全随机或观战：在全部真实阵营内随机（上游 RandomizeSide 的 SideId==0 / 观战分支）
                sideIndex = PickSide(null);
            }
            else if (src.SideId > 0 && src.SideId < randomCount)
            {
                // 自定义随机选择器：在指定阵营集合内随机
                sideIndex = PickSide(selectors[src.SideId - 1].Sides);
            }
            else
            {
                sideIndex = Math.Clamp(src.SideId - randomCount, 0, sideCount - 1);
            }

            int colorId;
            if (src.ColorId <= 0 || colors.Count == 0)
            {
                // 随机颜色：从剩余可用颜色中取一项并移除；无可选时退化为全色板随机
                if (freeColors.Count == 0)
                {
                    colorId = colors.Count == 0 ? 0 : colors[random.Next(colors.Count)].GameColorIndex;
                }
                else
                {
                    int freeIndex = random.Next(freeColors.Count);
                    colorId = colors[freeColors[freeIndex]].GameColorIndex;
                    freeColors.RemoveAt(freeIndex);
                }
            }
            else
            {
                int listIndex = Math.Clamp(src.ColorId - 1, 0, colors.Count - 1);
                colorId = colors[listIndex].GameColorIndex;
                freeColors.Remove(listIndex);
            }

            return new PlayerInfo
            {
                Name = src.Name,
                // 观战者沿用 sideCount（SpawnIniGenerator / PlayerHouseInfo 以 SideId == sideCount 判定观战）
                SideId = spectator ? sideCount : sideIndex,
                StartingLocation = src.StartingLocation,
                ColorId = colorId,
                TeamId = src.TeamId,
                Ready = src.Ready,
                AutoReady = src.AutoReady,
                IsAI = src.IsAI || isAI,
                IsInGame = src.IsInGame,
                IPAddress = src.IPAddress,
                Port = src.Port,
                HashReceived = src.HashReceived,
                Index = src.Index,
                Ping = src.Ping,
                AILevel = src.AILevel
            };
        }

        int PickSide(int[]? allowed)
        {
            if (allowed is { Length: > 0 })
            {
                int[] usable = allowed.Where(s => s >= 0 && s < sideCount && !disallowedSides[s]).ToArray();
                if (usable.Length == 0)
                    usable = allowed.Where(s => s >= 0 && s < sideCount).ToArray();
                if (usable.Length > 0)
                    return usable[random.Next(usable.Length)];
            }

            int[] free = Enumerable.Range(0, sideCount).Where(s => !disallowedSides[s]).ToArray();
            if (free.Length == 0)
                free = Enumerable.Range(0, sideCount).ToArray();
            return free[random.Next(free.Length)];
        }
    }
}
