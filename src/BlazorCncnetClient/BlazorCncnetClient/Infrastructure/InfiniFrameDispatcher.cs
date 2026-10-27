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
    private int _uiThreadId = -1;

    /// <summary>
    /// 由根组件在 UI 线程初始化时调用，捕获当前同步上下文（并记录调度器线程，供 C3 断言）。
    /// </summary>
    public void Initialize(SynchronizationContext syncContext)
    {
        _syncContext = syncContext ?? throw new ArgumentNullException(nameof(syncContext));
        _uiThreadId = Environment.CurrentManagedThreadId;
    }

    /// <summary>
    /// C3 DEBUG 断言：UI 状态仅允许在调度器线程变更。
    /// 网络/后台线程需经 <see cref="Post"/> / <see cref="InvokeAsync(Action)"/> 调度回 UI 线程；
    /// 若在非调度器线程触发渲染/组件状态变更，Blazor 会抛
    /// 「not associated with the Dispatcher」——DEBUG 下此断言先一步暴露违规点。
    /// </summary>
    [System.Diagnostics.Conditional("DEBUG")]
    public void AssertUiThread(string origin = "")
    {
        int current = Environment.CurrentManagedThreadId;
        System.Diagnostics.Debug.Assert(
            current == _uiThreadId,
            $"C3 跨线程 UI 变更违规：{origin} 在非调度器线程(#{current}，期望 #{_uiThreadId})执行。" +
            "请改用 IDispatcher.Post / InvokeAsync 调度回 UI 线程。");
    }

    public void Post(Action action)
    {
        var ctx = _syncContext ?? throw new InvalidOperationException(
            UiText.T("Startup.DispatcherNotInitialized",
                "InfiniFrameDispatcher has not been initialized (call Initialize on the UI thread)."));
        ctx.Post(_ => action(), null);
    }

    public Task InvokeAsync(Action action)
    {
        var ctx = _syncContext ?? throw new InvalidOperationException(
            UiText.T("Startup.DispatcherNotInitialized",
                "InfiniFrameDispatcher has not been initialized (call Initialize on the UI thread)."));

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
            UiText.T("Startup.DispatcherNotInitialized",
                "InfiniFrameDispatcher has not been initialized (call Initialize on the UI thread)."));

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
