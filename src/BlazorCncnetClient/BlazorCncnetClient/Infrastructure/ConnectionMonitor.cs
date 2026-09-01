using System;
using CnCNetClient.Online;
using CnCNetClient.Online.EventArguments;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>连接状态（M3.10 D2.7）。</summary>
public enum ConnectionState
{
    /// <summary>未连接 / 已手动断开。</summary>
    Offline,
    /// <summary>登录连接中。</summary>
    Connecting,
    /// <summary>已连接，正常运行。</summary>
    Online,
    /// <summary>断线后自动重连中。</summary>
    Reconnecting
}

/// <summary>连接通知（驱动全局 Toast，M3.10 D2.7）。</summary>
public enum ConnectionNotificationKind
{
    /// <summary>首次连接成功。</summary>
    Connected,
    /// <summary>断线后自动重连成功。</summary>
    Reconnected,
    /// <summary>手动断开。</summary>
    Disconnected,
    /// <summary>意外断线（将自动重连）。</summary>
    ConnectionLost,
    /// <summary>连接尝试失败。</summary>
    Failed
}

/// <summary>一条连接通知。<see cref="Message"/> 为补充信息（如断线原因）。</summary>
public sealed record ConnectionNotification(ConnectionNotificationKind Kind, string Message);

/// <summary>
/// 全局连接状态监视（M3.10 D2.7）：订阅 <see cref="CnCNetManager"/> 连接事件，
/// 维护 <see cref="State"/> 状态机并产出 <see cref="Notification"/> 供 UI（顶栏状态灯 / Toast）显示。
/// 自动重连由 Connection 层负责（断线后按延迟自动 ConnectAsync），本监视器仅反映状态。
/// </summary>
public sealed class ConnectionMonitor : IDisposable
{
    private readonly CnCNetManager _manager;

    /// <summary>状态变化（顶栏状态灯 / 大厅指示刷新）。</summary>
    public event Action? StateChanged;

    /// <summary>连接事件（驱动全局 Toast）。</summary>
    public event Action<ConnectionNotification>? Notification;

    public ConnectionState State { get; private set; } = ConnectionState.Offline;

    /// <summary>断线原因（ConnectionLost 时记录，供 UI 展示）。</summary>
    public string? LastError { get; private set; }

    public bool IsConnected => State == ConnectionState.Online;

    /// <summary>Orbitron 英文状态文本（顶栏 / 底栏）。</summary>
    public string StateText => State switch
    {
        ConnectionState.Online => "ONLINE",
        ConnectionState.Reconnecting => "RECONNECT",
        ConnectionState.Connecting => "CONNECTING",
        _ => "OFFLINE"
    };

    /// <summary>中文状态文本（大厅指示）。</summary>
    public string StateCn => State switch
    {
        ConnectionState.Online => "在线",
        ConnectionState.Reconnecting => "重连中",
        ConnectionState.Connecting => "连接中",
        _ => "离线"
    };

    /// <summary>状态灯/徽章修饰类：""=在线（青呼吸）、retry=重连（琥珀）、off=离线（红常亮）。</summary>
    public string StatusClass => State switch
    {
        ConnectionState.Online => string.Empty,
        ConnectionState.Reconnecting or ConnectionState.Connecting => "retry",
        _ => "off"
    };

    public ConnectionMonitor(CnCNetManager manager)
    {
        _manager = manager;
        State = manager.IsConnected ? ConnectionState.Online : ConnectionState.Offline;

        _manager.Connected += OnConnected;
        _manager.ConnectionLost += OnConnectionLost;
        _manager.ReconnectAttempt += OnReconnectAttempt;
        _manager.Disconnected += OnDisconnected;
        _manager.ConnectAttemptFailed += OnConnectAttemptFailed;
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        var wasReconnecting = State == ConnectionState.Reconnecting;
        SetState(ConnectionState.Online);
        Raise(new ConnectionNotification(
            wasReconnecting ? ConnectionNotificationKind.Reconnected : ConnectionNotificationKind.Connected,
            wasReconnecting ? "已重新连接 CnCNet" : "已连接 CnCNet"));
    }

    private void OnConnectionLost(object? sender, ConnectionLostEventArgs e)
    {
        LastError = e.Reason;
        SetState(ConnectionState.Reconnecting);
        Raise(new ConnectionNotification(ConnectionNotificationKind.ConnectionLost, e.Reason));
    }

    private void OnReconnectAttempt(object? sender, EventArgs e)
    {
        SetState(ConnectionState.Reconnecting);
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        LastError = null;
        SetState(ConnectionState.Offline);
        Raise(new ConnectionNotification(ConnectionNotificationKind.Disconnected, "已与 CnCNet 断开连接"));
    }

    private void OnConnectAttemptFailed(object? sender, EventArgs e)
    {
        SetState(ConnectionState.Offline);
        Raise(new ConnectionNotification(ConnectionNotificationKind.Failed, "连接 CnCNet 失败"));
    }

    private void SetState(ConnectionState state)
    {
        if (State == state)
            return;
        State = state;
        StateChanged?.Invoke();
    }

    private void Raise(ConnectionNotification notification) => Notification?.Invoke(notification);

    public void Dispose()
    {
        _manager.Connected -= OnConnected;
        _manager.ConnectionLost -= OnConnectionLost;
        _manager.ReconnectAttempt -= OnReconnectAttempt;
        _manager.Disconnected -= OnDisconnected;
        _manager.ConnectAttemptFailed -= OnConnectAttemptFailed;
    }
}
