using System;
using System.Collections.Generic;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 一种可用的语言（spec §6.8）：内置 UI Resx 语言 + 游戏 INI 翻译目录。
/// </summary>
/// <param name="Code">区域代码（如 zh-CN / en），同时是 settings INI <c>[Options] Translation</c> 的持久化值。</param>
/// <param name="DisplayName">界面显示名（如「简体中文」/「English」）。</param>
public sealed record LanguageInfo(string Code, string DisplayName);

/// <summary>
/// 语言切换服务（M4.2，spec §6.8 双轨制）。
/// 负责：运行时切换 <see cref="System.Globalization.CultureInfo.CurrentUICulture"/>（UI Resx）、
/// 重新加载 INI <see cref="CnCNetClient.Core.I18N.Translation"/>（游戏内容）、持久化到
/// settings INI <c>[Options] Translation</c>，并通知 UI 重渲染。
/// </summary>
public interface ITranslationService
{
    /// <summary>当前语言区域代码（如 zh-CN / en）。</summary>
    string Current { get; }

    /// <summary>可用语言列表（内置 UI 语言 + 游戏翻译目录）。</summary>
    IReadOnlyList<LanguageInfo> Languages { get; }

    /// <summary>语言切换（已持久化）后触发，供 UI 订阅重渲染。</summary>
    event Action? LanguageChanged;

    /// <summary>按区域代码切换语言（校验通过后生效 + 持久化）。</summary>
    void SetLanguage(string localeCode);
}
