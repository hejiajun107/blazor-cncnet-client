using System.Reflection;

using CnCNetClient.Plugin.Abstractions;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace BlazorCncnetClient.Infrastructure.Plugins;

/// <summary>插件注册阶段（Build 前）上下文实现。</summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly Func<string, string>? _contentUrl;

    public PluginContext(IServiceCollection services, LoadedPlugin plugin, string gameRoot, Func<string, string>? contentUrl)
    {
        Services = services;
        PluginDirectory = plugin.Directory;
        GameRoot = gameRoot;
        _contentUrl = contentUrl;
    }

    public IServiceCollection Services { get; }

    public string PluginDirectory { get; }

    public string GameRoot { get; }

    public string ContentUrl(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return string.Empty;
        // 内容伺服未就绪时降级为相对 URL（不抛出，避免插件注册失败）
        return _contentUrl?.Invoke(relativePath)
            ?? "/" + relativePath.TrimStart('/', '\\').Replace('\\', '/');
    }
}

/// <summary>插件 UI 贡献注册器实现（写回 <see cref="LoadedPlugin"/> 的贡献集合）。</summary>
internal sealed class PluginUiRegistrar : IPluginUiRegistrar
{
    private readonly LoadedPlugin _plugin;

    public PluginUiRegistrar(LoadedPlugin plugin) => _plugin = plugin;

    public void ContributeSlot(string slotKey, Type componentType)
    {
        if (!IsValid(slotKey, componentType, "ContributeSlot"))
            return;

        if (!_plugin.SlotContributions.TryGetValue(slotKey, out var list))
        {
            list = [];
            _plugin.SlotContributions[slotKey] = list;
        }
        list.Add(componentType);
    }

    public void ReplaceSlot(string slotKey, Type componentType)
    {
        if (!IsValid(slotKey, componentType, "ReplaceSlot"))
            return;

        // 日志提示覆盖关系（多个插件争抢同一槽位时以"后注册者"为准）
        if (_plugin.SlotReplacements.TryGetValue(slotKey, out var previous) && previous != componentType)
        {
            Log.Warning("插件 {Id} 覆盖槽位 {Slot}：{Old} → {New}",
                _plugin.Id, slotKey, previous.FullName, componentType.FullName);
        }
        _plugin.SlotReplacements[slotKey] = componentType;
    }

    public void AddMenuEntry(PluginMenuEntry entry)
    {
        if (entry is null || string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Route))
        {
            Log.Warning("插件 {Id} 声明了无效的菜单项（Key/Route 不能为空），已跳过", _plugin.Id);
            return;
        }
        _plugin.MenuEntries.Add(entry);
    }

    public void AddPageAssembly(Assembly assembly)
    {
        if (assembly is null || _plugin.PageAssemblies.Contains(assembly))
            return;
        _plugin.PageAssemblies.Add(assembly);
    }

    private bool IsValid(string slotKey, Type componentType, string api)
    {
        if (string.IsNullOrWhiteSpace(slotKey) || componentType is null)
        {
            Log.Warning("插件 {Id} 调用了无效的 {Api}（槽位键/类型为空），已跳过", _plugin.Id, api);
            return false;
        }
        if (!typeof(IComponent).IsAssignableFrom(componentType))
        {
            Log.Warning("插件 {Id} 的 {Api} 目标 {Type} 不是 Blazor 组件，已跳过",
                _plugin.Id, api, componentType.FullName);
            return false;
        }
        return true;
    }
}
