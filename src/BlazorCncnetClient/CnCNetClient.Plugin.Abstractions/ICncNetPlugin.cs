using System.Reflection;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

namespace CnCNetClient.Plugin.Abstractions;

/// <summary>
/// 外部插件入口契约。插件程序集内需包含且仅包含一个实现本接口的类型，
/// 由 <c>plugin.ini</c> 的 <c>[Plugin] EntryType</c> 指向。
/// </summary>
/// <remarks>
/// 插件以预编译程序集（RCL）形式放在游戏根目录 <c>Plugins/&lt;name&gt;/</c> 下，
/// 由客户端经 <see cref="Assembly.LoadFrom(string)"/> 载入**默认 AssemblyLoadContext**，
/// 因此插件可直接 <c>@inject CnCNetManager</c> 等宿主服务（类型与宿主同源）。
/// </remarks>
public interface ICncNetPlugin
{
    /// <summary>插件唯一标识（建议与 <c>ClientDefinitions.ini [Plugins]</c> 的键一致）。</summary>
    string Id { get; }

    /// <summary>展示名（主菜单/日志用）。</summary>
    string Name { get; }

    /// <summary>插件编译时针对的契约版本，须等于 <see cref="PluginApi.CurrentVersion"/>。</summary>
    int ApiVersion { get; }

    /// <summary>
    /// 注册插件自有服务。仅在 <c>appBuilder.Build()</c> **之前**调用，
    /// 因此可正常 AddSingleton/AddScoped（Build 之后服务集合不可变）。
    /// </summary>
    void RegisterServices(IPluginContext context);

    /// <summary>声明 UI 贡献面（槽位 / 页面程序集 / 主菜单项）。</summary>
    void RegisterUi(IPluginUiRegistrar ui);

    /// <summary>
    /// 初始化。在 <c>Build()</c> **之后**调用，可解析全部宿主服务（含 <c>CnCNetManager</c>）。
    /// 默认空实现，插件可不必覆写。
    /// </summary>
    void Initialize(IServiceProvider services) { }
}

/// <summary>插件注册阶段（Build 前）可用的上下文。</summary>
public interface IPluginContext
{
    /// <summary>DI 服务集合（仅 Build 前可用）。</summary>
    IServiceCollection Services { get; }

    /// <summary>插件自身目录的绝对路径（读自带配置/资源）。</summary>
    string PluginDirectory { get; }

    /// <summary>游戏根目录的绝对路径。</summary>
    string GameRoot { get; }

    /// <summary>
    /// 把游戏根目录相对路径转为客户端内可加载的 http URL
    /// （由进程内 <c>GameContentServer</c> 伺服，用于 &lt;img&gt;/fetch/脚本等）。
    /// </summary>
    string ContentUrl(string relativePath);
}

/// <summary>插件 UI 贡献注册器。</summary>
public interface IPluginUiRegistrar
{
    /// <summary>
    /// 向槽位**追加**一个组件（多插件/多贡献共存，按加载顺序渲染）。
    /// 组件类型须实现 <see cref="IComponent"/>。
    /// </summary>
    void ContributeSlot(string slotKey, Type componentType);

    /// <summary>
    /// **替换**槽位的解析结果（后注册的插件覆盖先注册的）。请谨慎使用。
    /// </summary>
    void ReplaceSlot(string slotKey, Type componentType);

    /// <summary>追加一个主菜单入口。</summary>
    void AddMenuEntry(PluginMenuEntry entry);

    /// <summary>声明插件提供的页面程序集（其 <c>@page</c> 路由将被 Router 发现）。</summary>
    void AddPageAssembly(Assembly assembly);
}
