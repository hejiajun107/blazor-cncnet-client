using System;
using System.Threading;
using System.Threading.Tasks;
using CnCNetClient.Integration.Threading;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// InfiniFrame 宿主实现 <see cref="IDispatcher"/>。
/// 包装 <c>InfiniFrame.BlazorWebView.InfiniFrameSynchronizationContext</c>（即 Blazor 渲染/UI 线程同步上下文），
/// 替代原 XNA 客户端的 <c>WindowManager.AddCallback</c>。
/// 网络/后台线程通过它把回调调度回 UI 线程。
/// </summary>
public sealed class InfiniFrameDispatcher : IDispatcher
{
    private SynchronizationContext? _syncContext;

    /// <summary>
    /// 由根组件在 UI 线程初始化时调用，捕获当前同步上下文。
    /// </summary>
    public void Initialize(SynchronizationContext syncContext)
    {
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
    }

    public void Post(Action action)
    {
        var ctx = _syncContext ?? throw new InvalidOperationException(
            "InfiniFrameDispatcher 尚未初始化（请在 UI 线程调用 Initialize）。");
        ctx.Post(_ => action(), null);
    }

    public Task InvokeAsync(Action action)
    {
        var ctx = _syncContext ?? throw new InvalidOperationException(
            "InfiniFrameDispatcher 尚未初始化（请在 UI 线程调用 Initialize）。");

        // InfiniFrameSynchronizationContext 自带 InvokeAsync；退化为 Post + TCS。
        if (ctx is InfiniFrame.BlazorWebView.InfiniFrameSynchronizationContext iffCtx)
            return iffCtx.InvokeAsync(action);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.Post(_ =>
        {
            try
            {
                action();
                tcs.SetResult();
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }

    public Task<T> InvokeAsync<T>(Func<T> func)
    {
        var ctx = _syncContext ?? throw new InvalidOperationException(
            "InfiniFrameDispatcher 尚未初始化（请在 UI 线程调用 Initialize）。");

        if (ctx is InfiniFrame.BlazorWebView.InfiniFrameSynchronizationContext iffCtx)
            return iffCtx.InvokeAsync(func);

        var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        ctx.Post(_ =>
        {
            try
            {
                tcs.SetResult(func());
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        }, null);
        return tcs.Task;
    }
}
