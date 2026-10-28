#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

using CnCNetClient.Core.Extensions;
using CnCNetClient.Core.PlatformShim;

using CnCNetClient.Tools;

namespace CnCNetClient.Core.I18N;

public class Translation : ICloneable
{
    public static Translation Instance { get; set; } = new Translation(ProgramConstants.HARDCODED_LOCALE_CODE);

    /// <summary>The translation metadata section name.</summary>
    public const string METADATA_SECTION = "General";

    private static CultureInfo? _initialUICulture;
    /// <summary>The UI culture that the application was started with. Must be initialized as early as possible.</summary>
    public static CultureInfo? InitialUICulture
    {
        get => _initialUICulture;
        set => _initialUICulture = _initialUICulture is null ? value
            : throw new InvalidOperationException($"{nameof(InitialUICulture)} is already set!");
    }

    /// <summary>AKA name of the folder, used to look up <see cref="Culture"/> and select a language</summary>
    public string LocaleCode { get; private set; } = string.Empty;

    /// <summary>The explicitly set UI name for the translation.</summary>
    private string _name = string.Empty;
    /// <summary>The UI name for the translation.</summary>
    public string Name
    {
        get => string.IsNullOrWhiteSpace(_name) ? GetLanguageName(LocaleCode) : _name;
        private set => _name = value;
    }

    /// <summary>The explicitly set UI culture for the translation.</summary>
    /// <remarks>Not accounted when selecting the translation automatically.</remarks>
    private CultureInfo? _culture;
    /// <summary>The UI culture for the translation.</summary>
    public CultureInfo Culture
    {
        get => _culture ?? new CultureInfo(LocaleCode);
        private set => _culture = value;
    }

    /// <summary>The author(s) of the translation.</summary>
    public string Author { get; private set; } = string.Empty;

    /// <summary>Override the default encoding used for reading/writing map files. Null ("Auto") means detecting the encoding from each file (sometimes unreliable). </summary>
    public Encoding? MapEncoding = EncodingExt.UTF8NoBOM;

    /// <summary>Stores the translation values (including default values for missing strings).</summary>
    private ConcurrentDictionary<string, string> Values { get; } = new();

    // public bool IsRightToLeft { get; set; } // TODO

    /// <summary>Contains all keys within <see cref="Values"/> with missing translations.</summary>
    private readonly ConcurrentDictionary<string, byte> MissingKeys = new();

    /// <summary>Used to write missing translation table entries to a file.</summary>
    public const string MISSING_KEY_PREFIX = "; ";  // a hack but hey it works

    /// <summary>
    /// Initializes a new instance of the <see cref="Translation"/> class.
    /// </summary>
    /// <param name="localeCode">A locale code for this translation.</param>
    public Translation(string localeCode)
    {
        LocaleCode = localeCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Translation"/> class
    /// that loads the translation from an INI file.
    /// </summary>
    /// <param name="ini">An INI file to read from.</param>
    /// <param name="localeCode">A locale code for this translation.</param>
    public Translation(IniFile ini, string localeCode)
        : this(localeCode)
    {
        if (ini is null)
            throw new ArgumentNullException(nameof(ini));

        IniSection? metadataSection = ini.GetSection(METADATA_SECTION);
        Name = metadataSection?.GetStringValue(nameof(Name), string.Empty) ?? string.Empty;
        Author = metadataSection?.GetStringValue(nameof(Author), string.Empty) ?? string.Empty;

        MapEncoding = EncodingExt.GetEncodingWithAuto(metadataSection?.GetStringValue(nameof(MapEncoding), null));

        string? cultureName = metadataSection?.GetStringValue(nameof(Culture), null);
        if (cultureName is not null)
            Culture = new(cultureName);

        AppendValuesFromIniFile(ini);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Translation"/> class
    /// that loads the translation from an INI file.
    /// </summary>
    /// <param name="iniPath">A path to an INI file to read from.</param>
    /// <param name="localeCode">A locale code for this translation.</param>
    public Translation(string iniPath, string localeCode)
        : this(new CCIniFile(iniPath), localeCode) { }

    /// <summary>
    /// Initializes a new instance of the <see cref="Translation"/> class
    /// that is a copy of the given instance.
    /// </summary>
    /// <param name="other">An object to copy from.</param>
    public Translation(Translation other)
    {
        LocaleCode = other.LocaleCode;
        _name = other._name;
        _culture = other._culture;
        Author = other.Author;
        MapEncoding = other.MapEncoding;

        foreach (var (key, value) in other.Values)
            Values.TryAdd(key, value);
    }

    public Translation Clone() => new Translation(this);
    object ICloneable.Clone() => Clone();

    /// <summary>
    /// Reads <see cref="Values"/> from an INI file, overriding possibly existing ones.
    /// </summary>
    /// <param name="iniPath">A path to an INI file to read from.</param>
    public void AppendValuesFromIniFile(string iniPath)
        => AppendValuesFromIniFile(new CCIniFile(iniPath));

    /// <summary>
    /// Reads <see cref="Values"/> from an INI file, overriding possibly existing ones.
    /// </summary>
    /// <param name="ini">An INI file to read from.</param>
    public void AppendValuesFromIniFile(IniFile ini)
    {
        IniSection? valuesSection = ini.GetSection(nameof(Values));
        if (valuesSection is null)
            return;

        foreach (var (key, value) in valuesSection.Keys)
            Values[key] = value.FromIniString();
    }

    /// <param name="localeCode">The locale code to look up the language name for.</param>
    /// <returns>The language name for the given locale code.</returns>
    public static string GetLanguageName(string localeCode)
    {
        string? result = null;

        string iniPath = SafePath.CombineFilePath(
            ClientConfiguration.Instance.TranslationsFolderPath, localeCode, ClientConfiguration.Instance.TranslationIniName);

        if (SafePath.GetFile(iniPath).Exists)
        {
            // This parses only the metadata section content so that we don't parse
            // the bazillion of localized values just to read the translation name.
            // The only issue is that inheritance would break.
            // FIXME AllowNewSections is ignored with inheritance
            IniFile ini = new();
            ini.AddSection(METADATA_SECTION);
            ini.FileName = iniPath;
            ini.AllowNewSections = false;

            ini.Parse();

            // Overridden name first
            IniSection? metadataSection = ini.GetSection(METADATA_SECTION);
            result = metadataSection?.GetStringValue(nameof(Name), null);
        }

        if (string.IsNullOrWhiteSpace(result))
        {
            try
            {
                result = new CultureInfo(localeCode).NativeName;
            }
            catch (CultureNotFoundException)
            {
                // globalization-invariant 模式（InvariantGlobalization=true）下仅支持 InvariantCulture，
                // 无法为指定区域代码构造 CultureInfo，回退到区域代码本身。
                result = localeCode;
            }
        }

        if (string.IsNullOrWhiteSpace(result))
            result = localeCode;

        return result!;
    }

    /// <summary>
    /// Applies (hard-links or copies) the translation game files for a given locale to the game directory,
    /// and removes any destination files whose source no longer exists.
    /// </summary>
    public void ApplyTranslationGameFiles() => ApplyTranslationGameFiles(LocaleCode);

    /// <inheritdoc cref="ApplyTranslationGameFiles()"/>
    /// <param name="localeCode">The locale code identifying the translation whose game files should be applied.</param>
    public static void ApplyTranslationGameFiles(string localeCode)
    {
        ClientConfiguration.Instance.RefreshTranslationGameFiles();

        string translationFolderPath = SafePath.CombineDirectoryPath(
            ClientConfiguration.Instance.TranslationsFolderPath, localeCode);

        foreach (var tgf in ClientConfiguration.Instance.TranslationGameFiles)
        {
            string sourcePath = SafePath.CombineFilePath(translationFolderPath, tgf.Source);
            string targetPath = SafePath.CombineFilePath(ProgramConstants.GamePath, tgf.Target);

            if (File.Exists(sourcePath))
            {
                string sourceHash = Utilities.CalculateSHA1ForFile(sourcePath);
                string targetHash = Utilities.CalculateSHA1ForFile(targetPath);

                if (sourceHash != targetHash)
                {
                    FileExtensions.CreateHardLinkFromSource(sourcePath, targetPath);
                    new FileInfo(targetPath).IsReadOnly = true;
                }
            }
            else
            {
                if (File.Exists(targetPath))
                {
                    new FileInfo(targetPath).IsReadOnly = false;
                    File.Delete(targetPath);
                }
            }
        }
    }

    /// <summary>
    /// Lists valid available translations from the <see cref="TranslationsFolderPath"/> along with their UI names.
    /// A localization is valid if it has a corresponding <see cref="TranslationIniName"/> file in the <see cref="TranslationsFolderPath"/>.
    /// </summary>
    /// <returns>Locale code -> display name pairs.</returns>
    public static Dictionary<string, string> GetTranslations()
    {
        var translations = new Dictionary<string, string>(StringComparer.InvariantCultureIgnoreCase)
        {
            // Add default localization so that we always have it in the list even if the localization does not exist
            [ProgramConstants.HARDCODED_LOCALE_CODE] = GetLanguageName(ProgramConstants.HARDCODED_LOCALE_CODE)
        };

        if (!Directory.Exists(ClientConfiguration.Instance.TranslationsFolderPath))
            return translations;

        foreach (var localizationFolder in Directory.GetDirectories(ClientConfiguration.Instance.TranslationsFolderPath))
        {
            string localizationCode = Path.GetFileName(localizationFolder);
            translations[localizationCode] = GetLanguageName(localizationCode);
        }

        return translations;
    }

    /// <summary>
    /// Checks the current UI culture and finds the closest match from supported translations.
    /// </summary>
    /// <returns>Available translation locale code.</returns>
    public static string GetDefaultTranslationLocaleCode()
    {
        // we don't need names here pretty much
        Dictionary<string, string> translations = GetTranslations();

        CultureInfo? initialUICulture = InitialUICulture;
        if (initialUICulture is null)
            return ProgramConstants.HARDCODED_LOCALE_CODE;

        for (var culture = initialUICulture;
            culture != CultureInfo.InvariantCulture;
            culture = culture.Parent)
        {
            string translation = culture.Name;

            // the keys in 'translations' are case-insensitive
            if (translations.ContainsKey(translation))
                return translation;
        }

        return ProgramConstants.HARDCODED_LOCALE_CODE;
    }

    /// <summary>
    /// 中文客户端的默认 UI 语言（M4.2 移植适配）：系统 UI 语言为中文时默认 <c>zh-CN</c>，
    /// 否则沿用 <see cref="GetDefaultTranslationLocaleCode"/>（跟随系统匹配的游戏翻译目录）。
    /// </summary>
    /// <remarks>
    /// 原版为英文优先（<see cref="ProgramConstants.HARDCODED_LOCALE_CODE"/>）；本移植版以中文为默认 UI。
    /// 该值仅决定「首次启动」默认；用户一旦在设置页切换语言即持久化到 <c>[Options] Translation</c>，此后不再改写。
    /// </remarks>
    public static string GetDefaultUiLocaleCode()
    {
        string system = CultureInfo.CurrentUICulture.Name;
        if (system.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
            return "zh-CN";

        return GetDefaultTranslationLocaleCode();
    }

    /// <summary>
    /// Dump the translation table to an ini file.
    /// </summary>
    /// <returns>An ini file that contains the translation table.</returns>
    public IniFile DumpIni(bool saveOnlyMissingValues = false)
    {
        IniFile ini = new IniFile();

        ini.AddSection(METADATA_SECTION);
        IniSection general = ini.GetSection(METADATA_SECTION)!;

        if (!string.IsNullOrWhiteSpace(_name))
            general.AddKey(nameof(Name), _name);

        if (_culture is not null)
            general.AddKey(nameof(Culture), _culture.Name);

        general.AddKey(nameof(Author), Author);

        general.AddKey(nameof(MapEncoding), EncodingExt.EncodingWithAutoToString(MapEncoding));

        ini.AddSection(nameof(Values));
        IniSection translation = ini.GetSection(nameof(Values))!;

        foreach (var (key, value) in Values.OrderBy(kvp => kvp.Key))
        {
            bool valueMissing = MissingKeys.ContainsKey(key);
            if (!saveOnlyMissingValues || valueMissing)
            {
                translation.AddKey(valueMissing
                        ? MISSING_KEY_PREFIX + key
                        : key,
                    value.ToIniString());
            }
        }

        return ini;
    }

    private bool HandleMissing(string key, string defaultValue)
    {
        if (MissingKeys.TryAdd(key, 0))
        {
            Values.TryAdd(key, defaultValue);
            return true;
        }

        return false;
    }

    /// <summary>
    /// 地图文本翻译（Description / Briefing）。
    /// 先按完整相对路径查 <c>INI:Maps:&lt;路径&gt;:&lt;字段&gt;</c>（兼容 / 与 \ 两种分隔符）；
    /// 未命中时按地图文件名词干回退：翻译文件里的路径是按某个地图包目录结构编写的，
    /// 而实际安装的地图可能被重新归类到别的目录（如 Maps\Yuri's Revenge → Maps\Standard），
    /// 只用完整路径会导致所有官方地图名都显示英文。
    /// </summary>
    /// <param name="baseFilePath">地图相对路径（不含扩展名），如 Maps/Standard/xmp29u2。</param>
    /// <param name="field">字段名，如 Description、Briefing。</param>
    /// <param name="defaultValue">无译文时的回退值。</param>
    public string LookUpMap(string baseFilePath, string field, string defaultValue)
    {
        if (string.IsNullOrWhiteSpace(baseFilePath))
            return defaultValue;

        string normalized = baseFilePath.Replace('/', '\\').Trim('\\');

        if (Values.TryGetValue(MAP_KEY_PREFIX + normalized + ":" + field, out string? value))
            return value;

        string forwardSlashKey = MAP_KEY_PREFIX + normalized.Replace('\\', '/') + ":" + field;
        if (Values.TryGetValue(forwardSlashKey, out value))
            return value;

        int separator = normalized.LastIndexOf('\\');
        string stem = separator >= 0 ? normalized.Substring(separator + 1) : normalized;

        if (MapTextIndex.TryGetValue(stem + "|" + field, out string? fallback))
            return fallback;

        return defaultValue;
    }

    /// <summary>按“文件名词干|字段”索引地图翻译条目，供 <see cref="LookUpMap"/> 的路径回退使用。</summary>
    private Dictionary<string, string>? _mapTextIndex;

    private Dictionary<string, string> MapTextIndex
    {
        get
        {
            if (_mapTextIndex is not null)
                return _mapTextIndex;

            var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var (key, value) in Values)
            {
                if (!key.StartsWith(MAP_KEY_PREFIX, StringComparison.OrdinalIgnoreCase))
                    continue;

                int fieldSeparator = key.LastIndexOf(':');
                if (fieldSeparator <= MAP_KEY_PREFIX.Length)
                    continue;

                string path = key.Substring(MAP_KEY_PREFIX.Length, fieldSeparator - MAP_KEY_PREFIX.Length);
                string fieldName = key.Substring(fieldSeparator + 1);

                int separator = path.LastIndexOfAny(['\\', '/']);
                string stem = separator >= 0 ? path.Substring(separator + 1) : path;

                index.TryAdd(stem + "|" + fieldName, value);
            }

            return _mapTextIndex = index;
        }
    }

    private const string MAP_KEY_PREFIX = "INI:Maps:";

    /// <summary>
    /// Looks up the translated value that corresponds to the given key.
    /// </summary>
    /// <param name="key">The translation key (identifier).</param>
    /// <param name="defaultValue">The value to fall back to in case there's no translated value.</param>
    /// <param name="notify">Whether to add this key and value to the list of missing key-values.</param>
    /// <returns>The translated value or a default value.</returns>
    public string LookUp(string key, string defaultValue, bool notify = true)
    {
        if (Values.TryGetValue(key, out string? value))
            return value;

        if (notify)
            _ = HandleMissing(key, defaultValue);

        return defaultValue;
    }

    /// <summary>
    /// Looks up the translated value that corresponds to the given key with a fallback to the value of global key.
    /// </summary>
    /// <param name="key">The translation key (identifier).</param>
    /// <param name="fallbackKey">The fallback translation key (identifier).</param>
    /// <param name="defaultValue">The value to fall back to in case there's no translated value.</param>
    /// <param name="notify">Whether to add this key and value to the list of missing key-values. Doesn't include the fallback key.</param>
    /// <returns>The translated value or a default value.</returns>
    public string LookUp(string key, string fallbackKey, string defaultValue, bool notify = true)
    {
        if (Values.TryGetValue(key, out string? value))
            return value;

        if (key != fallbackKey && Values.TryGetValue(fallbackKey, out string? fallbackValue))
            return fallbackValue;

        if (notify)
            _ = HandleMissing(key, defaultValue);

        return defaultValue;
    }
}
