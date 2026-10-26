using CnCNetClient.Plugin.Abstractions;

namespace Cnc3Home;

/// <summary>
/// CNC3 风格主页示例插件（F4）。
/// </summary>
/// <remarks>
/// 通过 <see cref="IPluginUiRegistrar.ReplaceSlot"/> 接管 <c>page.mainmenu</c>：
/// 宿主 MainMenu 页仍保留自动连接 / ESC / 更新检查 / 隐私声明等逻辑，
/// 仅把主体视图换成 <c>Cnc3HomeMenu</c>。
/// 若宿主尚未提供该槽位，则退化为普通插件页，经主菜单入口进入。
/// </remarks>
public sealed class Cnc3HomePluginEntry : ICncNetPlugin
{
    public string Id => "cnc3-home";

    public string Name => "CNC3 风格主页";

    public int ApiVersion => PluginApi.CurrentVersion;

    public void RegisterServices(IPluginContext context)
    {
        // 本插件不需要自有服务：菜单动作全部复用宿主服务。
    }

    public void RegisterUi(IPluginUiRegistrar ui)
    {
        // 替换主菜单主体（宿主 MainMenu.razor 内 <DynamicComponent> 渲染）
        ui.ReplaceSlot("page.mainmenu", typeof(Components.Cnc3HomeMenu));

        // 本程序集内的 @page 路由交给宿主 Router 发现
        ui.AddPageAssembly(typeof(Cnc3HomePluginEntry).Assembly);

        // 兜底入口：宿主未支持 page.mainmenu 时，仍可从内置主菜单进入插件页
        ui.AddMenuEntry(new PluginMenuEntry(
            Key: "cnc3-home.open",
            Label: "CNC3 风格主页",
            LabelEn: "CNC3 HOME",
            Icon: "◉",
            Route: "/plugin/cnc3-home/menu"));
    }
}
