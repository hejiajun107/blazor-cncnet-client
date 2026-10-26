using CnCNetClient.Plugin.Abstractions;

using Microsoft.Extensions.DependencyInjection;

namespace {{ROOT_NAMESPACE}};

/// <summary>{{PLUGIN_NAME}} 入口。</summary>
public sealed class {{ENTRY_CLASS}} : ICncNetPlugin
{
    public string Id => "{{PLUGIN_ID}}";

    public string Name => "{{PLUGIN_NAME}}";

    /// <summary>必须等于宿主 PluginApi.CurrentVersion（当前为 1）。</summary>
    public int ApiVersion => PluginApi.CurrentVersion;

    /// <summary>Build 之前：注册插件自有服务。</summary>
    public void RegisterServices(IPluginContext context)
    {
        // context.Services.AddSingleton<MyState>();
    }

    /// <summary>声明 UI 贡献面。</summary>
    public void RegisterUi(IPluginUiRegistrar ui)
    {
        ui.ContributeSlot("layout.topbar", typeof(Components.Badge));

        ui.AddPageAssembly(typeof({{ENTRY_CLASS}}).Assembly);
        ui.AddMenuEntry(new PluginMenuEntry(
            Key: "{{PLUGIN_ID}}.home",
            Label: "{{PLUGIN_NAME}}",
            LabelEn: "{{PLUGIN_ID}}",
            Icon: "★",
            Route: "/plugin/{{PLUGIN_ID}}/home"));
    }

    /// <summary>Build 之后：可解析全部宿主服务。</summary>
    public void Initialize(IServiceProvider services) { }
}
