namespace BlazorCncnetClient.Infrastructure.Theme;

/// <summary>
/// 主题元数据（design tokens 以 CSS 变量形式定义于 wwwroot/css/app.css）。
/// 主题切换 = 替换 <c>:root</c> 变量集（通过 <c>data-theme</c> 属性切换）。
/// <para><c>MusicUrl</c>：主菜单音乐的可播放地址。由主题目录内 DTACnCNetClient.ini
/// [General] MainMenuTheme 指定的音频文件解析而来（原版为 .ogg/.wma，web 端按 .mp3/.ogg/.wav 探测），
/// 主题未配置或无对应文件时为 null（不播放）。</para>
/// </summary>
public sealed record ThemeInfo(string Key, string Name, string Hint, string? MusicUrl = null);
