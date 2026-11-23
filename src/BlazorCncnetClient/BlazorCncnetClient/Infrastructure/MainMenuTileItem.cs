namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 主菜单槽位条目（替换槽位 <c>page.menu.list</c>）。
/// 宿主只负责提供数据与点击回调，外观与摆放由插件组件决定。
/// </summary>
/// <param name="Key">菜单键（内置项为 campaign/multiplayer/…，插件项为 plugin:xxx）。</param>
/// <param name="Label">当前语言下的显示文案。</param>
/// <param name="LabelEn">英文副标题（不受当前语言影响，供双行贴片使用）。</param>
/// <param name="Icon">宿主兜底图标字符（插件可用自己的线稿图标替代）。</param>
/// <param name="IsPrimary">是否主入口（旧客户端「战役」高亮项）。</param>
/// <param name="IsEnabled">是否可点击（MainMenu.ini [btn*] Enabled=false 时为 false）。</param>
public sealed record MainMenuTileItem(
    string Key,
    string Label,
    string LabelEn,
    string Icon,
    bool IsPrimary,
    bool IsEnabled);
