namespace CnCNetClient.Plugin.Abstractions;

/// <summary>
/// 插件契约版本：宿主升级导致契约不兼容变更时递增本值，并发布新的主版本 nupkg。
/// 宿主加载插件前会比对 <see cref="ICncNetPlugin.ApiVersion"/>，不匹配则禁用该插件。
/// </summary>
/// <remarks>
/// 该校验不只是友好提示，而是「类型身份不分裂」的硬防线：
/// 插件必须使用与宿主**同一份**契约程序集（共享默认 AssemblyLoadContext）。
/// 若允许版本漂移，宿主可能从插件目录加载到"版本更高"的同名程序集，
/// 导致 <c>ICncNetPlugin</c>/<c>IComponent</c> 等类型出现两份、渲染与注入失败。
/// </remarks>
public static class PluginApi
{
    /// <summary>当前契约版本（与 nupkg 主版本号对应，如 1.0.0 ↔ 1）。</summary>
    public const int CurrentVersion = 1;
}

/// <summary>
/// 插件主菜单项（由插件在 <see cref="IPluginUiRegistrar.AddMenuEntry"/> 中声明）。
/// </summary>
/// <param name="Key">唯一键（同插件内不得重复）。</param>
/// <param name="Label">中文主文本。</param>
/// <param name="LabelEn">英文副文本（Orbitron 小字）。</param>
/// <param name="Icon">图标字符（如 ◆）。</param>
/// <param name="Route">点击后跳转的客户端内路由（如 /plugin/my-plugin/home）。</param>
/// <param name="Order">排序权重，默认 100（位于内置项之后、「退出」之前）。</param>
/// <param name="VisibleWhen">可选可见性条件；当前宿主仅支持 null（始终可见）。</param>
public sealed record PluginMenuEntry(
    string Key,
    string Label,
    string LabelEn,
    string Icon,
    string Route,
    int Order = 100,
    string? VisibleWhen = null);
