using System;

using CnCNetClient.Core;
using CnCNetClient.Integration.Lifecycle;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 设置中心服务（M4.1，spec §5.3 设置 / §6.10 持久化体系）。
/// 包装原版 <see cref="UserINISettings"/>（settings INI 读写），提供「变更即存」与变更通知。
/// scoped 注册：与组件共享同一 WebView 电路上下文。
/// </summary>
public sealed class SettingsService : IDisposable
{
    private readonly IAppExitNotifier _exitNotifier;

    /// <summary>设置变更（已落盘）通知，供 UI 刷新。</summary>
    public event Action? SettingsChanged;

    /// <summary>原版设置对象（含全部强类型设置项，键值见 settings INI 各节）。</summary>
    public UserINISettings Ini { get; }

    public SettingsService(IAppExitNotifier exitNotifier)
    {
        _exitNotifier = exitNotifier;
        Ini = UserINISettings.Instance;
        // 退出兜底（spec §6.10）：进程退出前落盘未保存的修改
        _exitNotifier.ApplicationExiting += OnApplicationExiting;
    }

    /// <summary>变更即存（spec §6.10）：写回 settings INI 并通知订阅方。</summary>
    public void Save()
    {
        Ini.SaveSettings();
        SettingsChanged?.Invoke();
    }

    /// <summary>就地修改设置并立即保存（控件即改即存）。</summary>
    public void SetAndSave(Action<UserINISettings> mutate)
    {
        mutate(Ini);
        Save();
    }

    private void OnApplicationExiting(object? sender, EventArgs e) => Save();

    public void Dispose() => _exitNotifier.ApplicationExiting -= OnApplicationExiting;
}
