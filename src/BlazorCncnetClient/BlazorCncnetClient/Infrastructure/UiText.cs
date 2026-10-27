using CnCNetClient.Core.Extensions;
using CnCNetClient.Core.I18N;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// UI 文案查表（Resx + Translation.ini 双轨）。
/// 键省略 <c>Client:UI:</c> 前缀；<paramref name="fallback"/> 为英文原文。
/// </summary>
public static class UiText
{
    /// <summary>按当前 UI 语言查表，缺失时返回 <paramref name="fallback"/>。</summary>
    public static string T(string key, string fallback)
        => fallback.L10N(UIStrings.KeyPrefix + key);
}
