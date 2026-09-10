using System;
using System.Threading.Tasks;
using CnCNetClient.Domain;
using CnCNetClient.Integration.Threading;
using Microsoft.Extensions.Logging;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 全局异常兜底（spec §2.8 / checklist G3）。
/// 职责：
///  1) 订阅进程级未处理异常（<see cref="AppDomain.UnhandledException"/>、
///     <see cref="TaskScheduler.UnobservedTaskException"/>），经 Serilog 落盘定位崩溃。
///  2) 迁移原 XNA 客户端的 <c>MainClientConstants.DisplayErrorAction</c> 语义：
///     由 UI 宿主注入实现 —— 调度回 UI 线程用 <see cref="MessageBoxService"/> 呈现错误，
///     exit=true 时先持久化再退出进程（替代原 <c>Environment.Exit(1)</c> 前的弹窗）。
/// 注册为 scoped（依赖 scoped 的 MessageBoxService），由根组件 <c>App.razor</c> 注入即生效。
/// </summary>
public sealed class GlobalExceptionHandler : IDisposable
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;
    private readonly AppExitNotifier _exitNotifier;
    private bool _subscribed;

    public GlobalExceptionHandler(
        ILogger<GlobalExceptionHandler> logger,
        MessageBoxService messageBox,
        IDispatcher dispatcher,
        AppExitNotifier exitNotifier)
    {
        _logger = logger;
        _messageBox = messageBox;
        _dispatcher = dispatcher;
        _exitNotifier = exitNotifier;

        // 迁移 DisplayErrorAction：Domain 层保留静态 hook，UI 宿主在此注入实现。
        MainClientConstants.DisplayErrorAction = DisplayError;
    }

    /// <summary>订阅进程级异常事件；可多次调用（幂等）。在 UI 线程调用。</summary>
    public void Subscribe()
    {
        if (_subscribed)
            return;
        _subscribed = true;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        _logger.LogInformation("全局异常兜底已启用（AppDomain.UnhandledException / TaskScheduler.UnobservedTaskException）");
    }

    private void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        _logger.LogCritical(ex, "进程级未处理异常（IsTerminating={IsTerminating}）", e.IsTerminating);
        ClassifyCrossThreadViolation(ex);
        if (e.IsTerminating)
            DisplayError("客户端发生未处理异常", ex?.ToString() ?? "未知错误", exit: true);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        // 未观察的 Task 异常（fire-and-forget / async void）：仅记录，避免误杀进程。
        _logger.LogError(e.Exception, "未观察的 Task 异常");
        ClassifyCrossThreadViolation(e.Exception);
        e.SetObserved();
    }

    /// <summary>C3：把 Blazor「非调度器线程触发渲染/组件状态」异常单独标记，便于走查核对零跨线程违规。</summary>
    [System.Diagnostics.Conditional("DEBUG")]
    private void ClassifyCrossThreadViolation(Exception? ex)
    {
        if (ex is null)
            return;
        for (Exception? cur = ex; cur is not null; cur = cur.InnerException)
        {
            if (cur.Message.Contains("not associated with the Dispatcher", StringComparison.Ordinal))
            {
                _logger.LogError(cur,
                    "C3 跨线程 UI 变更违规：非调度器线程触发了渲染/组件状态变更，应经 IDispatcher.Post / InvokeAsync 调度回 UI 线程");
                return;
            }
        }
    }

    /// <summary>
    /// <see cref="MainClientConstants.DisplayErrorAction"/> 的 UI 宿主实现。
    /// 可能从任意线程调用：调度回 UI 线程弹出错误框，exit=true 在用户确认后持久化并退出。
    /// </summary>
    private void DisplayError(string title, string error, bool exit)
    {
        _logger.LogError("错误上报（DisplayErrorAction, exit={Exit}）: {Title}{NewLine}{Error}",
            exit, title, Environment.NewLine, error);
        try
        {
            _dispatcher.Post(() => _ = ShowAndMaybeExitAsync(title, error, exit));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "调度错误弹窗失败");
            if (exit)
            {
                _exitNotifier.NotifyExiting();
                Environment.Exit(1);
            }
        }
    }

    private async Task ShowAndMaybeExitAsync(string title, string error, bool exit)
    {
        try
        {
            await _messageBox.ShowAsync(title, error, MessageBoxButtons.Ok, MessageBoxKind.Error);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "错误弹窗显示失败");
        }

        if (exit)
        {
            // 先让宿主持久化（IAppExitNotifier），再以非零码退出。
            _exitNotifier.NotifyExiting();
            Environment.Exit(1);
        }
    }

    public void Dispose()
    {
        if (!_subscribed)
            return;
        _subscribed = false;
        AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
        TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
    }
}
