using System;
using CnCNetClient.Core;
using CnCNetClient.Domain;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// Discord Rich Presence 集成（r9-4）：包装 Domain 的 <see cref="DiscordHandler"/>，
/// 按页面语义提供呈现更新方法；监听设置变化以运行时启停（原版由各窗口持有 DiscordHandler 并调用 UpdatePresence，
/// 设置切换需重启生效；这里即时生效）。未启用/全局禁用时全部方法为空操作。
/// </summary>
public sealed class DiscordPresenceService : IDisposable
{
    private readonly SettingsService _settings;
    private DiscordHandler? _handler;
    private bool _enabled;
    private bool _disposed;

    public DiscordPresenceService(SettingsService settings)
    {
        _settings = settings;
        _enabled = IsEnabled();
        _handler = _enabled ? CreateHandler() : null;
        _settings.SettingsChanged += OnSettingsChanged;
    }

    /// <summary>主菜单 / 大厅默认呈现（"In Client"）。</summary>
    public void MainMenu() => _handler?.UpdatePresence();

    /// <summary>在线 / 局域网房间呈现（原版 CnCNetGameLobby / LANGameLobby）。</summary>
    public void Game(string map, string mode, string type, string state,
        int players, int maxPlayers, string side, string roomName,
        bool isHost = false, bool isPassworded = false, bool isLocked = false, bool resetTimer = false)
        => _handler?.UpdatePresence(map, mode, type, state, players, maxPlayers, side, roomName,
            isHost, isPassworded, isLocked, resetTimer);

    /// <summary>游戏加载中呈现（原版 GameLoadingLobbyBase）。</summary>
    public void GameLoading(string map, string mode, string type, string state,
        int players, int maxPlayers, string roomName, bool isHost = false, bool resetTimer = false)
        => _handler?.UpdatePresence(map, mode, type, state, players, maxPlayers, roomName, isHost, resetTimer);

    /// <summary>遭遇战呈现（原版 SkirmishLobby）。</summary>
    public void Skirmish(string map, string mode, string state, string side, bool resetTimer = false)
        => _handler?.UpdatePresence(map, mode, state, side, resetTimer);

    /// <summary>战役任务呈现（原版 CampaignSelector）。</summary>
    public void Campaign(string mission, string difficulty, string side, bool resetTimer = false)
        => _handler?.UpdatePresence(mission, difficulty, side, resetTimer);

    /// <summary>载入存档呈现（原版 GameLoadingWindow）。</summary>
    public void SavedGame(string save, bool resetTimer = false)
        => _handler?.UpdatePresence(save, resetTimer);

    /// <summary>设置变化：Discord 开关切换时运行时连接 / 断开。</summary>
    private void OnSettingsChanged()
    {
        if (_disposed)
            return;

        bool enabled = IsEnabled();
        if (enabled == _enabled)
            return;

        _enabled = enabled;
        _handler?.Dispose();
        _handler = enabled ? CreateHandler() : null;
    }

    private static bool IsEnabled() =>
        UserINISettings.Instance.DiscordIntegration
        && !ClientConfiguration.Instance.DiscordIntegrationGloballyDisabled;

    /// <summary>创建并连接 Discord RPC 客户端（构造函数内部读取开关并连接；失败不阻塞客户端）。</summary>
    private static DiscordHandler? CreateHandler()
    {
        try
        {
            return new DiscordHandler();
        }
        catch (Exception ex)
        {
            Logger.Log("DiscordPresenceService: Failed to create Discord handler. Reason: " + ex);
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _settings.SettingsChanged -= OnSettingsChanged;
        _handler?.Dispose();
    }
}
