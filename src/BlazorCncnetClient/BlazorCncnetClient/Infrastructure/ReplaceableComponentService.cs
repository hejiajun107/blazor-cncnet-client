using System;
using System.Linq;
using System.Reflection;

using BlazorCncnetClient.Components.Shared;
using BlazorCncnetClient.Infrastructure.Plugins;

using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 可替换组件注册表（F2 + F4）：页面内「槽位」组件通过 key 解析渲染类型。
/// 解析优先级：ClientDefinitions.ini [Components] 节覆盖（key=程序集内组件类名）
/// → 插件 ReplaceSlot（F4）→ 内置默认。
/// MOD 可在不改代码的情况下切换到程序集内其他组件变体；
/// 外部插件（阶段 1）可替换槽位、追加组件（<see cref="ResolveSlot"/>）、提供新页面。
/// </summary>
public sealed class ReplaceableComponentService
{
    private readonly UiDefinitionService _uiDefs;
    private readonly PluginCatalog _plugins;
    private readonly Dictionary<string, Type> _defaults = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Assembly AppAssembly = typeof(ReplaceableComponentService).Assembly;

    public ReplaceableComponentService(UiDefinitionService uiDefs, PluginCatalog plugins)
    {
        _uiDefs = uiDefs;
        _plugins = plugins;
        _defaults["layout.footer"] = typeof(FooterBar);
    }

    /// <summary>解析槽位组件类型（ini 覆盖 → 插件替换 → 内置默认）。未知槽位抛异常（配置错误应尽早暴露）。</summary>
    public Type Resolve(string key)
    {
        if (TryResolveOverride(key, out Type? overrideType))
            return overrideType;
        if (_plugins.TryGetReplacement(key, out Type pluginType))
            return pluginType;
        if (_defaults.TryGetValue(key, out Type? defaultType))
            return defaultType;
        throw new InvalidOperationException($"ReplaceableComponent 未注册: {key}");
    }

    /// <summary>
    /// 解析「追加型」槽位（F4）：返回全部插件贡献的组件类型（按插件加载顺序）。
    /// 无贡献时返回空集合（<see cref="PluginSlot"/> 不渲染任何内容）。
    /// </summary>
    public IReadOnlyList<Type> ResolveSlot(string key) => _plugins.GetContributions(key);

    private bool TryResolveOverride(string key, out Type type)
    {
        type = null!;
        var defs = _uiDefs.Definitions;
        if (defs is null)
            return false;

        string name = defs.GetStringValue("Components", key, string.Empty).Trim();
        if (name.Length == 0)
            return false;

        type = AppAssembly.GetType(name, throwOnError: false)
            ?? AppAssembly.GetTypes().FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal))
            ?? throw new InvalidOperationException($"[Components] {key}={name} 指向未知组件类型");

        if (!typeof(IComponent).IsAssignableFrom(type))
            throw new InvalidOperationException($"[Components] {key}={name} 不是 Blazor 组件");

        return true;
    }
}
