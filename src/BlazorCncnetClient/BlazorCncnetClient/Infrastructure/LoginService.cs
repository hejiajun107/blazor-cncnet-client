using System;
using System.Threading.Tasks;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>登录浮窗结果（spec §5.3.1 在线登录 Modal）。</summary>
public enum LoginResult
{
    /// <summary>用户取消（Cancel / 遮罩点击）。</summary>
    Cancelled,
    /// <summary>连接尝试失败（ConnectAttemptFailed）。</summary>
    Failed,
    /// <summary>连接成功（Connected）。</summary>
    Connected
}

/// <summary>
/// 全局登录浮窗服务（spec §5.3.1）。
/// 宿主组件 <c>Components/Shared/LoginModal.razor</c> 订阅 <see cref="Changed"/> 渲染模态层；
/// 调用方用 <see cref="ShowAsync"/> 异步等待登录结果。
/// </summary>
public sealed class LoginService
{
    /// <summary>状态变化（打开/字段/连接态/关闭）时触发，宿主据此刷新。</summary>
    public event Action? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>关闭动画进行中（模态层保留以播 fade-out 120ms，随后 <see cref="CompleteClose"/>）。</summary>
    public bool IsClosing { get; private set; }

    /// <summary>玩家名（输入框值；M3 过渡期读 localStorage，M4 由 UserINISettings 落盘）。</summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>记住我：下次进入主菜单跳过登录浮窗直接连接（SkipConnectDialog）。</summary>
    public bool RememberMe { get; set; }

    /// <summary>持久连接模式（PersistentMode）。</summary>
    public bool PersistentMode { get; set; }

    /// <summary>启动自动连接（AutomaticCnCNetLogin，仅当 RememberMe+PersistentMode 同勾选可选中）。</summary>
    public bool AutoConnect { get; set; }

    /// <summary>连接进行中（Connect 按钮 loading 态）。</summary>
    public bool IsConnecting { get; set; }

    /// <summary>昵称校验 / 连接失败提示（显示在浮窗内）。</summary>
    public string? Error { get; set; }

    private TaskCompletionSource<LoginResult>? _tcs;

    /// <summary>打开登录浮窗并等待结果。</summary>
    public Task<LoginResult> ShowAsync()
    {
        _tcs = new TaskCompletionSource<LoginResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Error = null;
        IsConnecting = false;
        IsClosing = false;
        IsOpen = true;
        Changed?.Invoke();
        return _tcs.Task;
    }

    /// <summary>用户点击 Connect / Cancel：进入关闭动画（fade-out），由宿主调用 <see cref="CompleteClose"/> 收尾。</summary>
    public void Resolve(LoginResult result)
    {
        if (!IsOpen || IsClosing)
            return;
        _result = result;
        IsClosing = true;
        Changed?.Invoke();
    }

    private LoginResult _result;

    /// <summary>关闭动画结束，真正关闭并完成 <see cref="ShowAsync"/> 的等待任务。</summary>
    public void CompleteClose()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        IsClosing = false;
        IsConnecting = false;
        _tcs?.TrySetResult(_result);
        Changed?.Invoke();
    }

    /// <summary>连接期间字段/状态变化（IsConnecting/Error/复选框）时通知宿主刷新。</summary>
    public void NotifyChanged() => Changed?.Invoke();
}
