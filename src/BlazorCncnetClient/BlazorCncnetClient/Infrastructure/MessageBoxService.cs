using System;
using System.Threading.Tasks;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>弹窗按钮组合（spec §5.3 MessageBox）。</summary>
public enum MessageBoxButtons
{
    Ok,
    OkCancel,
    YesNo
}

/// <summary>弹窗类型（决定图标与配色，映射 app.css 的 .msgbox.kind-*）。</summary>
public enum MessageBoxKind
{
    Info,
    Warn,
    Error,
    Confirm
}

/// <summary>
/// 全局弹窗服务（spec §5.3 MessageBox「全局弹窗服务」）。
/// 宿主组件 <c>Components/Shared/MessageBox.razor</c> 订阅 <see cref="Changed"/> 渲染模态层；
/// 调用方用 <see cref="ShowAsync"/> 异步等待用户确认结果。
/// </summary>
public sealed class MessageBoxService
{
    /// <summary>状态变化（打开/按钮态/关闭）时触发，宿主据此刷新。</summary>
    public event Action? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>关闭动画进行中（模态层保留以播 fade-out 120ms，随后 <see cref="CompleteClose"/>）。</summary>
    public bool IsClosing { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Message { get; private set; } = string.Empty;

    public MessageBoxButtons Buttons { get; private set; } = MessageBoxButtons.Ok;

    public MessageBoxKind Kind { get; private set; } = MessageBoxKind.Info;

    private TaskCompletionSource<bool>? _tcs;

    /// <summary>打开弹窗并等待用户结果；用户点 OK/Yes 返回 true，Cancel/No 返回 false。</summary>
    public Task<bool> ShowAsync(
        string title,
        string message,
        MessageBoxButtons buttons = MessageBoxButtons.Ok,
        MessageBoxKind kind = MessageBoxKind.Info)
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Title = title;
        Message = message;
        Buttons = buttons;
        Kind = kind;
        IsClosing = false;
        IsOpen = true;
        Changed?.Invoke();
        return _tcs.Task;
    }

    /// <summary>用户点击按钮：进入关闭动画（fade-out），由宿主完成实际关闭并回填结果。</summary>
    public void Resolve(bool result)
    {
        if (!IsOpen || IsClosing)
            return;
        _result = result;
        IsClosing = true;
        Changed?.Invoke();
    }

    private bool _result;

    /// <summary>关闭动画结束，真正关闭并完成 <see cref="ShowAsync"/> 的等待任务。</summary>
    public void CompleteClose()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        IsClosing = false;
        _tcs?.TrySetResult(_result);
        Changed?.Invoke();
    }
}
