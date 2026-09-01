#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

using CnCNetClient.Domain.Multiplayer.LAN;

using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace CnCNetClient.Online.LAN;

/// <summary>
/// LAN 大厅玩家管理器（M4.4 D3.1）。
/// 移植自原版 <c>DTAClient.DXGUI.Multiplayer.LANPlayerManager</c>（去掉 XNA ListBox，
/// 以快照集合 + 事件驱动 Blazor UI）。按 UDP 端点（IP:Port）标识玩家，线程安全。
/// </summary>
public sealed class LanPlayerManager
{
    private readonly object lockObject = new();
    private readonly Dictionary<string, LANLobbyUser> players = new();
    private readonly Dictionary<string, int> usernameToIndex = new();

    /// <summary>LANLobbyUser 需要 ImageSharp 纹理；大厅 UI 仅显示昵称，共享占位图即可。</summary>
    private static readonly Image PlaceholderIcon = new Image<Rgba32>(1, 1);

    /// <summary>集合变化（增/删）通知，供 UI 刷新。</summary>
    public event Action? PlayersChanged;

    private static string GetKeyFromEndPoint(IPEndPoint endPoint) => endPoint.ToString();

    public LANLobbyUser GetOrCreatePlayer(IPEndPoint endPoint, string name)
    {
        lock (lockObject)
        {
            string key = GetKeyFromEndPoint(endPoint);

            if (players.TryGetValue(key, out LANLobbyUser? existing))
                return existing;

            var newUser = new LANLobbyUser(name, PlaceholderIcon, endPoint);
            players[key] = newUser;

            if (!usernameToIndex.ContainsKey(name))
                usernameToIndex[name] = usernameToIndex.Count;

            PlayersChanged?.Invoke();
            return newUser;
        }
    }

    public LANLobbyUser? GetPlayerIfExist(IPEndPoint endPoint)
    {
        lock (lockObject)
            return players.TryGetValue(GetKeyFromEndPoint(endPoint), out var user) ? user : null;
    }

    public bool RemovePlayer(IPEndPoint endPoint)
    {
        lock (lockObject)
        {
            if (!players.TryGetValue(GetKeyFromEndPoint(endPoint), out LANLobbyUser? user))
                return false;

            _ = players.Remove(GetKeyFromEndPoint(endPoint));

            bool nameStillInUse = players.Values.Any(p => p.Name == user.Name);
            if (!nameStillInUse && usernameToIndex.ContainsKey(user.Name))
                _ = usernameToIndex.Remove(user.Name);

            PlayersChanged?.Invoke();
            return true;
        }
    }

    /// <summary>线程安全快照。</summary>
    public List<LANLobbyUser> GetAllPlayers()
    {
        lock (lockObject)
            return players.Values.ToList();
    }

    public void Clear()
    {
        lock (lockObject)
        {
            players.Clear();
            usernameToIndex.Clear();
            PlayersChanged?.Invoke();
        }
    }

    public int Count
    {
        get
        {
            lock (lockObject)
                return players.Count;
        }
    }
}
