namespace BlazorCncnetClient.Infrastructure.Theme;

/// <summary>
/// 主题提供者：管理当前主题、运行时切换（含过渡动画）与持久化（localStorage）。
/// 对应 spec.md §5.4.6。
/// </summary>
public interface IThemeProvider
{
    /// <summary>当前主题。</summary>
    ThemeInfo Current { get; }

    /// <summary>全部可用主题。</summary>
    IReadOnlyList<ThemeInfo> Themes { get; }

    /// <summary>主题切换完成事件（UI 线程触发，用于刷新界面状态）。</summary>
    event Action<ThemeInfo>? ThemeChanged;

    /// <summary>加载持久化的主题并应用到 DOM（应用启动时调用一次）。</summary>
    Task InitializeAsync();

    /// <summary>运行时切换主题（含过渡动画 + 持久化）。</summary>
    Task ApplyAsync(string key);
}
