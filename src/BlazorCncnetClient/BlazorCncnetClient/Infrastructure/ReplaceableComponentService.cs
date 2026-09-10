using System;
using System.Linq;
using System.Reflection;

using BlazorCncnetClient.Components.Shared;

using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 可替换组件注册表（F2）：页面内「槽位」组件通过 key 解析渲染类型。
/// 解析优先级：ClientDefinitions.ini [Components] 节覆盖（key=程序集内组件类名）
/// → 内置默认。MOD 可在不改代码的情况下切换到程序集内其他组件变体；
/// 追加程序集内新组件（RCL/附加程序集）为后续扩展路径（见 docs）。
/// </summary>
public sealed class ReplaceableComponentService
{
    private readonly UiDefinitionService _uiDefs;
    private readonly Dictionary<string, Type> _defaults = new(StringComparer.OrdinalIgnoreCase);

    private static readonly Assembly AppAssembly = typeof(ReplaceableComponentService).Assembly;

    public ReplaceableComponentService(UiDefinitionService uiDefs)
    {
        _uiDefs = uiDefs;
        _defaults["layout.footer"] = typeof(FooterBar);
    }

    /// <summary>解析槽位组件类型（覆盖优先，其次内置默认）。未知覆盖类型抛异常（配置错误应尽早暴露）。</summary>
    public Type Resolve(string key)
    {
        if (TryResolveOverride(key, out Type? overrideType))
            return overrideType;
        if (_defaults.TryGetValue(key, out Type? defaultType))
            return defaultType;
        throw new InvalidOperationException($"ReplaceableComponent 未注册: {key}");
    }

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
