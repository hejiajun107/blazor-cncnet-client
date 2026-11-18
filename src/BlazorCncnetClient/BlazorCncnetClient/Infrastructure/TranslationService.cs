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

    /// <summary>stub 生成只注册一次（ProcessExit 事件可能因多次调用而重复订阅）。</summary>
    private static bool _translationStubEnabled;

    /// <summary>
    /// 翻译 stub 生成（镜像原版 PreStartup）：settings INI <c>[Options] GenerateTranslationStub</c>
    /// 为 true 时，在进程退出前把本次运行中查不到译文的键写入
    /// <c>Client\Translation.ini</c>（文件名取 <see cref="ClientConfiguration.TranslationIniName"/>）。
    /// 缺失键以 <c>; 键=默认值</c> 注释形式输出，翻译者取消注释并填入译文即可；
    /// <c>GenerateOnlyNewValuesInTranslationStub</c> 为 true 时只输出缺失项，否则输出整张表。
    /// </summary>
    /// <remarks>
    /// 与原版的差异：原版还会通过生成代码（TranslationNotifier）登记全部编译期字符串，
    /// 本移植版没有该生成器，stub 只包含运行时实际查询过的键（即真正缺失的那些）。
    /// </remarks>
    public static void EnableStubGenerationIfConfigured()
    {
        if (_translationStubEnabled)
            return;

        if (!UserINISettings.Instance.GenerateTranslationStub.Value)
            return;

        _translationStubEnabled = true;

        string stubPath = SafePath.CombineFilePath(
            ProgramConstants.ClientUserFilesPath,
            ClientConfiguration.Instance.TranslationIniName);

        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                IniFile ini = Translation.Instance.DumpIni(
                    UserINISettings.Instance.GenerateOnlyNewValuesInTranslationStub.Value);

                string? directory = Path.GetDirectoryName(stubPath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                ini.WriteIniFile(stubPath);

                Logger.Log("翻译 stub 已写入：" + stubPath);
            }
            catch (Exception ex)
            {
                Logger.Log("写入翻译 stub 失败：" + ex);
            }
        };

        Logger.Log("已启用翻译 stub 生成，退出时写入：" + stubPath);
    }

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
            var culture = new CultureInfo(localeCode);

            CultureInfo.CurrentUICulture = culture;

            // Blazor 的渲染批次可能落在不同线程上，线程文化不保证一致（表现为「设置页切了，
            // 回首页/弹窗仍是旧语言」）。Resx 查表改用进程级的 UI 文化，任何线程结果一致。
            UIStrings.Culture = culture;
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
    /// 额外支持父区域与脚本后缀回退（zh-CN → zh → zh-Hans），以兼容原版翻译目录命名。
    /// </summary>
    private static void LoadGameTranslation(string localeCode)
    {
        try
        {
            foreach (string candidate in GetLocaleCandidates(localeCode))
            {
                string translationFolder = SafePath.CombineDirectoryPath(
                    ClientConfiguration.Instance.TranslationsFolderPath, candidate);
                FileInfo translationFile = SafePath.GetFile(
                    translationFolder, ClientConfiguration.Instance.TranslationIniName);

                if (!translationFile.Exists)
                    continue;

                var translation = new Translation(translationFile.FullName, localeCode);

                AppendThemeTranslation(translation, candidate);

                Translation.Instance = translation;

                Logger.Log($"游戏翻译已加载：locale={localeCode}, 目录={candidate}, 文件={translationFile.FullName}, " +
                    $"样例地图名=\"{translation.LookUpMap("Maps/Standard/xmp29u2", "Description", "<无译文>")}\"");
                return;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to load the translation file. " + ex);
        }

        Logger.Log($"未找到游戏翻译文件：locale={localeCode}, 根目录={ClientConfiguration.Instance.TranslationsFolderPath}, " +
            $"候选={string.Join("/", GetLocaleCandidates(localeCode))}（游戏内文本将显示默认语言）");

        Translation.Instance = new Translation(localeCode);
    }

    /// <summary>
    /// 叠加当前主题的翻译（可选）：主题名不存在时 <see cref="ClientConfiguration.GetThemePath"/> 返回 null，
    /// 此时跳过即可。此处必须独立 try，不能让主题翻译的异常把已加载的主翻译整表丢弃——
    /// 否则异常会落到外层 catch，最终 <see cref="Translation.Instance"/> 变成空表，游戏内文本全为英文。
    /// </summary>
    private static void AppendThemeTranslation(Translation translation, string candidate)
    {
        try
        {
            string? themePath = ClientConfiguration.Instance.GetThemePath(UserINISettings.Instance.ClientTheme.Value);
            if (string.IsNullOrWhiteSpace(themePath))
                return;

            string themeFolder = SafePath.CombineDirectoryPath(
                ClientConfiguration.Instance.TranslationsFolderPath, candidate, themePath);
            FileInfo themeFile = SafePath.GetFile(
                themeFolder, ClientConfiguration.Instance.TranslationIniName);

            if (themeFile.Exists)
                translation.AppendValuesFromIniFile(themeFile.FullName);
        }
        catch (Exception ex)
        {
            Logger.Log("加载主题翻译失败（不影响主翻译）：" + ex.Message);
        }
    }

    /// <summary>
    /// 生成区域候选列表：exact → 脚本后缀别名（zh-CN → zh-Hans）→ 父区域（zh），逐级回退。
    /// 原版翻译目录按脚本命名（zh-Hans），而 Windows/设置里的语言代码是 zh-CN；
    /// 不做别名映射时中文翻译目录根本找不到，地图名、遭遇战选项等游戏文本全部回退英文。
    /// </summary>
    private static IReadOnlyList<string> GetLocaleCandidates(string localeCode)
    {
        var candidates = new List<string>();

        void Add(string? code)
        {
            if (!string.IsNullOrWhiteSpace(code) &&
                !candidates.Any(c => string.Equals(c, code, StringComparison.OrdinalIgnoreCase)))
            {
                candidates.Add(code);
            }
        }

        string current = localeCode;
        while (true)
        {
            Add(current);
            Add(GetChineseScriptAlias(current));

            int separator = current.LastIndexOf('-');
            if (separator <= 0)
                break;

            current = current.Substring(0, separator);
        }

        return candidates;
    }

    /// <summary>中文区域代码 → 原版翻译目录的脚本后缀名（zh-CN/zh → zh-Hans，zh-TW/zh-HK → zh-Hant）。</summary>
    private static string? GetChineseScriptAlias(string localeCode)
    {
        if (!localeCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return null;

        return localeCode.ToLowerInvariant() switch
        {
            "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" => "zh-Hant",
            _ => "zh-Hans",
        };
    }

    /// <summary>
    /// 翻译目录名 → 设置里使用的语言代码：zh-Hans/zh → zh-CN、zh-Hant → zh-TW。
    /// 避免同一门语言在语言列表里出现两个条目（且选到 zh-Hans 时 UI Resx 卫星加载不到，变成英文界面）。
    /// </summary>
    private static string NormalizeLocaleCode(string localeCode)
    {
        if (!localeCode.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return localeCode;

        return localeCode.ToLowerInvariant() switch
        {
            "zh-tw" or "zh-hk" or "zh-mo" or "zh-hant" => "zh-TW",
            _ => "zh-CN",
        };
    }

    /// <summary>组合可用语言：内置 UI 语言 + 游戏翻译目录（<see cref="Translation.GetTranslations"/>），去重保序。</summary>
    private static IReadOnlyList<LanguageInfo> BuildLanguages()
    {
        var list = new List<LanguageInfo>(BuiltInLanguages.Select(l => new LanguageInfo(l.Code, l.DisplayName)));

        try
        {
            foreach (var (code, name) in Translation.GetTranslations())
            {
                string normalized = NormalizeLocaleCode(code);
                if (list.Any(l => string.Equals(l.Code, normalized, StringComparison.OrdinalIgnoreCase)))
                    continue;

                list.Add(new LanguageInfo(normalized, name));
            }
        }
        catch (Exception ex)
        {
            Logger.Log("Failed to enumerate game translations. " + ex);
        }

        return list;
    }

    /// <summary>校验区域代码是否为可用语言（内置 UI 语言或翻译目录中列出的语言）。</summary>
    private bool IsKnownLocale(string localeCode)
    {
        if (string.IsNullOrWhiteSpace(localeCode))
            return false;

        return Languages.Any(l => string.Equals(l.Code, localeCode, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>将持久化区域代码规范化为可用值；无效时回退系统语言（中文系统 → zh-CN，否则 en）。</summary>
    private static string SanitizeLocale(string saved)
    {
        if (!string.IsNullOrWhiteSpace(saved))
        {
            try
            {
                _ = new CultureInfo(saved);
                // 旧版本可能存了脚本后缀目录名（zh-Hans），统一回 zh-CN 以便 UI Resx 卫星也加载。
                return NormalizeLocaleCode(saved);
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
