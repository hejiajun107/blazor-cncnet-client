using System.Reflection;

using CnCNetClient.Plugin.Abstractions;

namespace BlazorCncnetClient.Infrastructure.Plugins;

/// <summary>插件状态。</summary>
public enum PluginState
{
    /// <summary>已成功加载并注册。</summary>
    Enabled,

    /// <summary>已声明但不可用（如契约版本不匹配），未加载。</summary>
    Disabled,

    /// <summary>发现/加载/注册/初始化阶段失败，已跳过（原因见 <see cref="LoadedPlugin.Error"/>）。</summary>
    Failed
}

/// <summary>
/// 单个已发现的插件（F4）。元数据来自 plugin.ini，贡献面由
/// <see cref="PluginUiRegistrar"/> 在注册阶段填充。
/// </summary>
public sealed class LoadedPlugin
{
    public LoadedPlugin(string key, string directory, string relativeDirectory)
    {
        Key = key;
        Directory = directory;
        RelativeDirectory = relativeDirectory;
        // 默认回退 Key：plugin.ini 缺失/读取失败时（尚未读到 Id）日志仍能显示有意义标识
        Id = key;
    }

    /// <summary>ClientDefinitions.ini [Plugins] 的键。</summary>
    public string Key { get; }

    /// <summary>插件目录绝对路径。</summary>
    public string Directory { get; }

    /// <summary>插件目录相对游戏根目录的路径（生成资源 URL 用）。</summary>
    public string RelativeDirectory { get; }

    /// <summary>插件标识（plugin.ini [Plugin] Id，缺失时回退 <see cref="Key"/>）。</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>展示名。</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>插件声明的契约版本。</summary>
    public int ApiVersion { get; set; }

    /// <summary>插件自带样式文件（相对插件目录，可为 null）。</summary>
    public string? Style { get; set; }

    /// <summary>插件自带脚本文件（相对插件目录，可为 null）。</summary>
    public string? Script { get; set; }

    public PluginState State { get; set; } = PluginState.Disabled;

    /// <summary>失败/禁用原因（诊断用）。</summary>
    public string? Error { get; set; }

    /// <summary>插件入口实例（仅 Enabled 时非 null）。</summary>
    public ICncNetPlugin? Instance { get; set; }

    /// <summary>插件提供的页面程序集（供 Router.AdditionalAssemblies）。</summary>
    public List<Assembly> PageAssemblies { get; } = [];

    /// <summary>追加型槽位贡献：slot key → 组件类型（按插件内注册顺序）。</summary>
    public Dictionary<string, List<Type>> SlotContributions { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>替换型槽位贡献：slot key → 组件类型。</summary>
    public Dictionary<string, Type> SlotReplacements { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>主菜单项。</summary>
    public List<PluginMenuEntry> MenuEntries { get; } = [];
}
