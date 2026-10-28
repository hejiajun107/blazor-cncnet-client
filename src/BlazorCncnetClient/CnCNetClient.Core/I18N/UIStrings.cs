#nullable enable
using System.Globalization;
using System.Resources;

namespace CnCNetClient.Core.I18N;

/// <summary>
/// UI 文案 Resx 桥接（M4.2，spec §6.8 双轨制：UI 文案 → Resx）。
/// 资源：<c>CnCNetClient.Core.Resources.UI</c>（默认=en）与卫星 <c>UI.zh-CN.resx</c>。
/// 键空间前缀隔离：仅 <c>Client:UI:</c> 前缀的 <see cref="L10N"/> 键走 Resx；
/// 其余（<c>Client:Main:</c> 等游戏/内容键）仍走 INI <see cref="Translation"/>。
/// 语言切换通过 <see cref="CultureInfo.CurrentUICulture"/> 生效（由 TranslationService 设置）。
/// </summary>
public static class UIStrings
{
    /// <summary>UI 文案键前缀（与 INI 游戏翻译键空间隔离，spec §6.8）。</summary>
    public const string KeyPrefix = "Client:UI:";

    private static readonly ResourceManager Resources =
        new("CnCNetClient.Core.Resources.UI", typeof(UIStrings).Assembly);

    /// <summary>
    /// 当前 UI 区域文化（由 TranslationService 在切换语言时设置）。为 null 时退回
    /// <see cref="CultureInfo.CurrentUICulture"/>。
    /// Blazor 的渲染批次可能跑在不同的线程上，线程文化会随线程变化（切语言后部分页面/弹窗仍用旧语言）；
    /// 这里改用进程级显式文化，保证任何线程查表结果一致。
    /// </summary>
    public static CultureInfo? Culture { get; set; }

    /// <summary>
    /// 按当前 UI 区域性查找 Resx 文案；缺失或异常时回退到 <paramref name="defaultValue"/>。
    /// </summary>
    public static string Lookup(string key, string defaultValue)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(defaultValue))
            return defaultValue;

        try
        {
            string? value = Resources.GetString(key, Culture ?? CultureInfo.CurrentUICulture);
            return string.IsNullOrEmpty(value) ? defaultValue : value;
        }
        catch (MissingManifestResourceException)
        {
            return defaultValue;
        }
    }
}
