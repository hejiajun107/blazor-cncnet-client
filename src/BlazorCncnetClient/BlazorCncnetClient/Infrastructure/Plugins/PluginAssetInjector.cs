using CnCNetClient.Tools;

using Microsoft.JSInterop;

using Serilog;

namespace BlazorCncnetClient.Infrastructure.Plugins;

/// <summary>
/// 插件资源注入（F4 阶段 0）：把插件 <c>plugin.ini</c> 声明的 Style/Script
/// 经进程内 <see cref="GameContentServer"/> 转为 URL 后注入 WebView，
/// 使"纯文件插件"（不编译）即可换肤/加样式。挂载点为 MainLayout 首帧。
/// </summary>
public sealed class PluginAssetInjector
{
    private readonly IJSRuntime _js;
    private readonly PluginCatalog _catalog;
    private readonly GameContentServer _content;

    public PluginAssetInjector(IJSRuntime js, PluginCatalog catalog, GameContentServer content)
    {
        _js = js;
        _catalog = catalog;
        _content = content;
    }

    /// <summary>注入全部启用插件的样式/脚本（幂等，可重复调用）。</summary>
    public async Task InjectAllAsync()
    {
        if (_catalog.GameRoot.Length == 0 || !_catalog.Enabled.Any())
            return;

        // 内容伺服幂等启动（与启动闪屏/地图预览共用同一实例）
        if (!_content.IsRunning)
            _content.Start(_catalog.GameRoot);

        if (!_content.IsRunning)
        {
            Log.Warning("插件资源未注入：内容伺服不可用");
            return;
        }

        foreach (var plugin in _catalog.Enabled)
        {
            try
            {
                if (plugin.Style is { Length: > 0 } style)
                {
                    string url = _content.MapUrl(SafePath.CombineFilePath(plugin.RelativeDirectory, style));
                    await _js.InvokeVoidAsync("cncnet.injectCss", plugin.Key, url);
                }

                if (plugin.Script is { Length: > 0 } script)
                {
                    string url = _content.MapUrl(SafePath.CombineFilePath(plugin.RelativeDirectory, script));
                    await _js.InvokeVoidAsync("cncnet.injectJs", url);
                }
            }
            catch (Exception ex)
            {
                // 单个插件的资源注入失败不影响页面
                Log.Warning(ex, "插件 {Id} 资源注入失败", plugin.Id);
            }
        }
    }
}
