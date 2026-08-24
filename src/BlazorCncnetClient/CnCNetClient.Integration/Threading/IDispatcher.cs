namespace CnCNetClient.Integration.Threading;

/// <summary>
/// Abstraction for thread dispatching, replacing <c>WindowManager.AddCallback</c>.
/// Network/background threads call <see cref="Post"/> to schedule work on the UI thread.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Schedules an action to run on the UI thread (fire-and-forget).
    /// Equivalent to the original <c>WindowManager.AddCallback</c>.
    /// </summary>
    void Post(Action action);

    /// <summary>
    /// Schedules an action on the UI thread and awaits its completion.
    /// Wraps the Blazor <c>Dispatcher</c> for component state updates.
    /// </summary>
    Task InvokeAsync(Action action);

    /// <summary>
    /// Schedules a function on the UI thread and returns its result.
    /// </summary>
    Task<T> InvokeAsync<T>(Func<T> func);
}
