namespace BlazorCncnetClient.Infrastructure.Theme;

/// <summary>
/// 主题元数据（design tokens 以 CSS 变量形式定义于 wwwroot/css/app.css）。
/// 主题切换 = 替换 <c>:root</c> 变量集（通过 <c>data-theme</c> 属性切换）。
/// </summary>
public sealed record ThemeInfo(string Key, string Name, string Hint);
