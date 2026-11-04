namespace BlazorCncnetClient.Components.Shared;

/// <summary>
/// 自绘下拉（<see cref="IconSelect"/>）的一项：原生 select 的 option 无法渲染图片，
/// 这里允许每项带图标（阵营图标/随机图标）或色块（玩家颜色），对齐原版 XNA 下拉的渲染方式。
/// </summary>
/// <param name="Value">状态值（阵营/颜色索引，可为负数表示随机项）。</param>
/// <param name="Label">显示文本。</param>
/// <param name="IconUrl">图标 URL（为空时改用 <paramref name="Swatch"/>）。</param>
/// <param name="Swatch">色块 CSS 颜色（如 <c>rgb(255,60,60)</c>）。</param>
public sealed record IconSelectItem(int Value, string Label, string? IconUrl = null, string? Swatch = null);
