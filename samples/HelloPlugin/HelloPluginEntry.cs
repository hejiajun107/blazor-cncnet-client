using CnCNetClient.Plugin.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace HelloPlugin;

/// <summary>
/// 示例插件入口（F4）。演示三类扩展面：
/// <list type="bullet">
/// <item>槽位贡献：顶栏徽标 + 设置页侧栏说明（追加型）</item>
/// <item>新增独立页面：<c>/plugin/hello-plugin/home</c>（含主菜单入口）</item>
/// <item>自有服务：<see cref="HelloGreeter"/> 注册进 DI 并注入组件</item>
/// </list>
/// </summary>
public sealed class HelloPluginEntry : ICncNetPlugin
{
    public string Id => "hello-plugin";

    public string Name => "示例：你好插件";

    /// <summary>插件按当前契约编译（不匹配时宿主会禁用本插件）。</summary>
    public int ApiVersion => PluginApi.CurrentVersion;

    /// <summary>Build 之前调用：注册插件自有服务。</summary>
    public void RegisterServices(IPluginContext context)
    {
        context.Services.AddSingleton<HelloGreeter>();
    }

    /// <summary>声明 UI 贡献面。</summary>
    public void RegisterUi(IPluginUiRegistrar ui)
    {
        // 追加型槽位（多插件共存；宿主 .plugin-slot 处渲染）
        ui.ContributeSlot("layout.topbar", typeof(Components.HelloTopBarBadge));
        ui.ContributeSlot("page.settings.aside", typeof(Components.HelloSettingsNote));
        ui.ContributeSlot("menu.entry", typeof(Components.HelloMenuEntry));
        ui.ContributeSlot("page.lobby.aside", typeof(Components.HelloLobbyPanel));

        // 本程序集内的 @page 路由交给宿主 Router 发现
        ui.AddPageAssembly(typeof(HelloPluginEntry).Assembly);

        // 主菜单入口（Order 默认 100 → 排在「退出」之前）
        ui.AddMenuEntry(new PluginMenuEntry(
            Key: "hello-plugin.home",
            Label: "你好插件",
            LabelEn: "HELLO PLUGIN",
            Icon: "★",
            Route: "/plugin/hello-plugin/home"));
    }

    /// <summary>Build 之后调用：可解析全部宿主服务（示例仅解析自有服务）。</summary>
    public void Initialize(IServiceProvider services)
    {
        services.GetRequiredService<HelloGreeter>().Prime();
    }
}

/// <summary>插件自有服务示例（由 <see cref="HelloPluginEntry.RegisterServices"/> 注册）。</summary>
public sealed class HelloGreeter
{
    public string Message { get; private set; } = "你好插件尚未初始化";

    public void Prime() => Message = "你好！本消息来自示例插件（HelloPlugin）。";
}
