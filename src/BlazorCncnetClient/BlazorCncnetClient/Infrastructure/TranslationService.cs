using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using CnCNetClient.Core;
using CnCNetClient.Core.I18N;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 语言切换服务（M4.2，spec §6.8 双轨制）。
/// - UI 文案：运行时切 <see cref="CultureInfo.CurrentUICulture"/>，UIStrings（Resx，neutral=en / zh-CN 卫星）生效；
/// - 游戏内容：重载 INI <see cref="Translation"/>（游戏目录翻译，含主题翻译叠加，镜像原版 PreStartup）；
/// - 持久化：区域代码写 settings INI <c>[Options] Translation</c>（即改即存）。
/// scoped 注册：与组件共享同一 WebView 电路上下文。
/// </summary>
public sealed class TranslationService : ITranslationService
{
    private readonly SettingsService _settings;
    private string _current = ProgramConstants.HARDCODED_LOCALE_CODE;
    private IReadOnlyList<LanguageInfo>? _languages;

    /// <summary>内置 UI Resx 语言（本客户端至少中/英两套，spec §6.8 / checklist E4）。</summary>
    private static readonly (string Code, string DisplayName)[] BuiltInLanguages =
    [
        ("zh-CN", "简体中文"),
        (ProgramConstants.HARDCODED_LOCALE_CODE, "English"),
    ];

    public TranslationService(SettingsService settings)
    {
        _settings = settings;
        Initialize();
    }

    /// <inheritdoc/>
    public string Current => _current;

    /// <inheritdoc/>
    public IReadOnlyList<LanguageInfo> Languages => _languages ??= BuildLanguages();

    /// <inheritdoc/>
    public event Action? LanguageChanged;

    /// <summary>启动时按已持久化语言（settings INI <c>[Options] Translation</c>）应用 UI 文化与游戏翻译。</summary>
    private void Initialize()
    {
        string saved = _settings.Ini.Translation.Value;
        ApplyLocale(SanitizeLocale(saved));
    }

    /// <inheritdoc/>
    public void SetLanguage(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode) || !IsKnownLocale(localeCode))
            return;

        ApplyLocale(localeCode);
        _settings.SetAndSave(ini => ini.Translation.Value = localeCode);
        LanguageChanged?.Invoke();
    }

    /// <summary>应用语言：切 UI 文化 + 重载游戏 INI 翻译（不触发持久化，由调用方决定）。</summary>
    private void ApplyLocale(string localeCode)
    {
        _current = localeCode;

        try
        {
            CultureInfo.CurrentUICulture = new CultureInfo(localeCode);
        }
        catch (CultureNotFoundException)
        {
            // 无效区域不崩溃；UI Resx 回退 neutral（en）
        }

        LoadGameTranslation(localeCode);
    }

    /// <summary>
    /// 镜像原版 PreStartup 加载游戏内容翻译：优先 <c>[TranslationsFolder]/&lt;locale&gt;/Translation.ini</c>，
    /// 再叠加主题翻译；缺失/异常时回退到空翻译（L10N 走默认值）。
    /// </summary>
    private static void LoadGameTranslation(string localeCode)
    {
        try
        {
            FileInfo translationFile = SafePath.GetFile(
                UserINISettings.Instance.TranslationFolderPath,
                ClientConfiguration.Instance.TranslationIniName);

            if (translationFile.Exists)
            {
                var translation = new Translation(translationFile.FullName, localeCode);

                FileInfo themeFile = SafePath.GetFile(
                    UserINISettings.Instance.TranslationThemeFolderPath,
                    ClientConfiguration.Instance.TranslationIniName);
                if (themeFile.Exists)
                    translation.AppendValuesFromIniFile(themeFile.FullName);

                Translation.Instance = translation;
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load the translation file. " + ex);
        }

        Translation.Instance = new Translation(localeCode);
    }

    /// <summary>组合可用语言：内置 UI 语言 + 游戏翻译目录（<see cref="Translation.GetTranslations"/>），去重保序。</summary>
    private static IReadOnlyList<LanguageInfo> BuildLanguages()
    {
        var list = new List<LanguageInfo>(BuiltInLanguages.Select(l => new LanguageInfo(l.Code, l.DisplayName)));

        try
        {
            foreach (var (code, name) in Translation.GetTranslations())
            {
                if (list.Any(l => string.Equals(l.Code, code, StringComparison.OrdinalIgnoreCase)))
                    continue;

                list.Add(new LanguageInfo(code, name));
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to enumerate game translations. " + ex);
        }

        return list;
    }

    /// <summary>校验区域代码是否为可用语言（列表构建失败时至少覆盖内置 zh-CN/en）。</summary>
    private static bool IsKnownLocale(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
            return false;

        return LanguagesStaticCache.Contains(localeCode, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly HashSet<string> LanguagesStaticCache = new(
        BuiltInLanguages.Select(l => l.Code), StringComparer.OrdinalIgnoreCase);

    /// <summary>将持久化区域代码规范化为可用值；无效时回退系统语言（中文系统 → zh-CN，否则 en）。</summary>
    private static string SanitizeLocale(string saved)
    {
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                _ = new CultureInfo(saved);
                return saved;
            }
            catch (CultureNotFoundException)
            {
                // 已持久化的区域代码无效 → 回退
            }
        }

        string system = CultureInfo.CurrentUICulture.Name;
        return system.StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? "zh-CN"
            : ProgramConstants.HARDCODED_LOCALE_CODE;
    }
}
