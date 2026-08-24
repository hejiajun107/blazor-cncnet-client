using System;
using CnCNetClient.Integration.Lifecycle;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// InfiniFrame 宿主实现 <see cref="IAppExitNotifier"/>。
/// 替代原 XNA 客户端的 <c>windowManager.GameClosing</c> 事件。
/// 宿主在窗口关闭、应用退出前调用 <see cref="NotifyExiting"/>，
/// 订阅方（如 CnCNetUserData）在此持久化状态。
/// </summary>
public sealed class AppExitNotifier : IAppExitNotifier
{
    public event EventHandler? ApplicationExiting;

    /// <summary>
    /// 通知订阅方应用即将退出。由宿主在 <c>app.Run()</c> 返回后、进程退出前调用。
    /// </summary>
    public void NotifyExiting()
    {
        ApplicationExiting?.Invoke(this, EventArgs.Empty);
    }
}
