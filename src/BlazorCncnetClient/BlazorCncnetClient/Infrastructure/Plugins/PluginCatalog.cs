using System.Reflection;

using CnCNetClient.Core;
using CnCNetClient.Plugin.Abstractions;
using CnCNetClient.Tools;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;

using Serilog;

namespace BlazorCncnetClient.Infrastructure.Plugins;

/// <summary>
/// 插件目录（F4）：启动最早期构建的单例，负责「发现 → 校验 → 加载 → 注册 → 初始化」。
/// </summary>
/// <remarks>
/// 关键约束（见 docs/plugin-plan.md）：
/// <list type="bullet">
/// <item>插件用 <see cref="Assembly.LoadFrom(string)"/> 载入**默认 AssemblyLoadContext**，
/// 与宿主共享程序集，故 <c>IComponent</c>/<c>CnCNetManager</c> 等类型身份一致。</item>
/// <item>契约版本在加载前比对（<see cref="PluginApi.CurrentVersion"/>），
/// 从源头堵死"加载第二份同名程序集"导致类型分裂。</item>
/// <item>三阶段（Discover / RegisterServices / Initialize）各自 try-catch，
/// 单个插件失败仅禁用该插件并记日志，绝不阻断启动。</item>
/// </list>
/// </remarks>
public sealed class PluginCatalog
{
    private const string Section = "Plugins";
    private const string PluginIniName = "plugin.ini";
    private const string AbstractionsAssemblyFileName = "CnCNetClient.Plugin.Abstractions.dll";

    private readonly List<LoadedPlugin> _plugins = [];
    private readonly List<string> _directories = [];
    private bool _resolverHooked;

    /// <summary>游戏根目录（Discover 时写入）。</summary>
    public string GameRoot { get; private set; } = string.Empty;

    /// <summary>
    /// 游戏根相对路径 → 客户端内可加载 URL 的解析器（由宿主注入 GameContentServer 实现）。
    /// 未设置时插件上下文降级为相对 URL。
    /// </summary>
    public Func<string, string>? ContentUrlProvider { get; set; }

    /// <summary>全部已发现的插件（含 Disabled/Failed）。</summary>
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;

    /// <summary>可用插件。</summary>
    public IEnumerable<LoadedPlugin> Enabled => _plugins.Where(p => p.State == PluginState.Enabled);

    /// <summary>插件提供的页面程序集（供 Router.AdditionalAssemblies）。</summary>
    public IReadOnlyList<Assembly> PageAssemblies =>
        Enabled.SelectMany(p => p.PageAssemblies).Distinct().ToList();

    /// <summary>插件声明的主菜单项（按插件加载顺序）。</summary>
    public IReadOnlyList<PluginMenuEntry> EnabledMenuEntries =>
        Enabled.SelectMany(p => p.MenuEntries).ToList();

    /// <summary>
    /// 阶段一：发现并加载插件（必须在 DI <c>Build()</c> 之前调用）。
    /// </summary>
    /// <param name="gameRoot">游戏根目录（插件目录位于其 <c>Plugins/</c> 下）。</param>
    public void Discover(string gameRoot)
    {
        // SafePath 对空/空白输入返回 null（游戏根未定位时），此处归一为空串避免下游 NRE
        GameRoot = SafePath.CombineDirectoryPath(gameRoot) ?? string.Empty;
        if (GameRoot.Length == 0)
            return;

        try
        {
            string defsPath = SafePath.CombineFilePath(
                GameRoot, ProgramConstants.BASE_RESOURCE_PATH, ClientConfiguration.CLIENT_DEFS);
            if (!File.Exists(defsPath))
                return;

            var defs = new IniFile(defsPath);

            // 未配置插件（无 [Plugins] 节）→ 静默返回，不视为异常。
            // 注意：GetSectionKeys 对不存在的节返回 null（IniFile.cs:728），直接 foreach 会抛 NRE，
            // 被下方 catch 吞成"插件清单读取失败"告警——无插件用户每次启动都会看到，必须显式判空。
            var keys = defs.GetSectionKeys(Section);
            if (keys is null || keys.Count == 0)
                return;

            foreach (string key in keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                    continue;

                string relative = defs.GetStringValue(Section, key, string.Empty).Trim().Trim('/', '\\');
                if (relative.Length == 0)
                    continue; // 空值 = 不启用

                // 归一为绝对路径（Path.GetFullPath 对空/相对路径的行为依赖 CWD，此处显式拼到游戏根下）
                string dir = Path.GetFullPath(Path.Combine(GameRoot, relative));
                if (!Directory.Exists(dir))
                {
                    Log.Warning("插件 {Key} 目录不存在，已跳过：{Dir}", key, dir);
                    continue;
                }

                _directories.Add(dir);
                Load(key, dir, relative);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "插件清单读取失败（[Plugins] 节），本次不加载任何插件");
        }

        if (_directories.Count > 0 && !_resolverHooked)
        {
            _resolverHooked = true;
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
        }

        if (_plugins.Count > 0)
        {
            Log.Information("插件发现完成：{Enabled} 启用 / {Total} 发现", Enabled.Count(), _plugins.Count);
        }
    }

    /// <summary>
    /// 阶段二：调用各插件的 <see cref="ICncNetPlugin.RegisterServices"/> 与
    /// <see cref="ICncNetPlugin.RegisterUi"/>（必须在 <c>Build()</c> 之前）。
    /// </summary>
    public void RegisterServices(IServiceCollection services)
    {
        foreach (var plugin in _plugins)
        {
            if (plugin.State != PluginState.Enabled || plugin.Instance is null)
                continue;

            try
            {
                var context = new PluginContext(services, plugin, GameRoot, ContentUrlProvider);
                plugin.Instance.RegisterServices(context);
                plugin.Instance.RegisterUi(new PluginUiRegistrar(plugin));
            }
            catch (Exception ex)
            {
                plugin.State = PluginState.Failed;
                plugin.Error = ex.Message;
                Log.Warning(ex, "插件 {Id} 注册失败（已禁用）", plugin.Id);
            }
        }

        DetectRouteConflicts();
    }

    /// <summary>
    /// 阶段三：调用各插件的 <see cref="ICncNetPlugin.Initialize"/>（<c>Build()</c> 之后，
    /// 可解析全部宿主服务）。
    /// </summary>
    public void InitializeAll(IServiceProvider services)
    {
        foreach (var plugin in _plugins)
        {
            if (plugin.State != PluginState.Enabled || plugin.Instance is null)
                continue;

            try
            {
                plugin.Instance.Initialize(services);
            }
            catch (Exception ex)
            {
                plugin.State = PluginState.Failed;
                plugin.Error = ex.Message;
                Log.Warning(ex, "插件 {Id} 初始化失败（已禁用）", plugin.Id);
            }
        }

        foreach (var plugin in _plugins.Where(p => p.State != PluginState.Enabled))
        {
            Log.Warning("插件未启用：{Key}（{Id}）原因：{Reason}", plugin.Key, plugin.Id, plugin.Error);
        }
    }

    /// <summary>替换型槽位解析（后加载的插件优先）。</summary>
    public bool TryGetReplacement(string slotKey, out Type type)
    {
        for (int i = _plugins.Count - 1; i >= 0; i--)
        {
            var plugin = _plugins[i];
            if (plugin.State != PluginState.Enabled)
                continue;
            if (plugin.SlotReplacements.TryGetValue(slotKey, out var found))
            {
                type = found;
                return true;
            }
        }

        type = null!;
        return false;
    }

    /// <summary>追加型槽位解析（多插件/多组件按加载顺序返回）。</summary>
    public IReadOnlyList<Type> GetContributions(string slotKey)
    {
        var result = new List<Type>();
        if (string.IsNullOrWhiteSpace(slotKey))
            return result;

        foreach (var plugin in _plugins)
        {
            if (plugin.State != PluginState.Enabled)
                continue;
            if (plugin.SlotContributions.TryGetValue(slotKey, out var types))
                result.AddRange(types);
        }

        return result;
    }

    // ── 发现/加载 ────────────────────────────────────────────────

    private void Load(string key, string dir, string relativeDir)
    {
        var plugin = new LoadedPlugin(key, dir, relativeDir);
        _plugins.Add(plugin);

        try
        {
            string iniPath = SafePath.CombineFilePath(dir, PluginIniName);
            if (!File.Exists(iniPath))
            {
                Fail(plugin, "缺少 plugin.ini");
                return;
            }

            var ini = new IniFile(iniPath);
            string id = ini.GetStringValue("Plugin", "Id", key).Trim();
            plugin.Id = id.Length > 0 ? id : key;
            plugin.Name = ini.GetStringValue("Plugin", "Name", plugin.Id).Trim();
            plugin.ApiVersion = ini.GetIntValue("Plugin", "ApiVersion", 0);
            plugin.Style = NullIfEmpty(ini.GetStringValue("Plugin", "Style", string.Empty).Trim());
            plugin.Script = NullIfEmpty(ini.GetStringValue("Plugin", "Script", string.Empty).Trim());

            string entryType = ini.GetStringValue("Plugin", "EntryType", string.Empty).Trim();
            string assemblyName = ini.GetStringValue("Plugin", "Assembly", string.Empty).Trim();

            // 纯资源插件（阶段 0）：只声明 Style/Script，无入口类型、无程序集。
            // 不涉及契约程序集，故不做 ApiVersion 校验（无类型身份分裂风险）。
            if (entryType.Length == 0)
            {
                if (plugin.Style is null && plugin.Script is null)
                {
                    Fail(plugin, "未声明任何贡献（EntryType 与 Style/Script 均为空）");
                    return;
                }

                plugin.State = PluginState.Enabled;
                Log.Information("纯资源插件已启用：{Id}（{Name}）样式={Style} 脚本={Script}",
                    plugin.Id, plugin.Name, plugin.Style ?? "-", plugin.Script ?? "-");
                return;
            }

            // 程序集插件：契约版本前置校验（防"同名程序集第二份"导致类型身份分裂）
            if (plugin.ApiVersion != PluginApi.CurrentVersion)
            {
                plugin.State = PluginState.Disabled;
                plugin.Error = $"契约版本不匹配（插件 {plugin.ApiVersion} / 宿主 {PluginApi.CurrentVersion}）";
                Log.Warning("插件 {Id} 契约版本不匹配（插件 {PluginApi} / 宿主 {Host}），已禁用",
                    plugin.Id, plugin.ApiVersion, PluginApi.CurrentVersion);
                return;
            }

            Type? type = FindEntryType(dir, assemblyName, entryType);
            if (type is null)
            {
                Fail(plugin, $"未找到入口类型 {entryType}");
                return;
            }

            if (Activator.CreateInstance(type) is not ICncNetPlugin instance)
            {
                Fail(plugin, $"入口类型 {entryType} 未实现 ICncNetPlugin");
                return;
            }

            plugin.Instance = instance;
            plugin.State = PluginState.Enabled;
            Log.Information("插件已加载：{Id}（{Name}）ApiVersion={ApiVersion} 目录={Dir}",
                plugin.Id, plugin.Name, plugin.ApiVersion, dir);
        }
        catch (Exception ex)
        {
            plugin.State = PluginState.Failed;
            plugin.Error = ex.Message;
            Log.Warning(ex, "插件加载失败：{Key}（{Dir}）", key, dir);
        }
    }

    /// <summary>
    /// 定位插件入口类型：优先 plugin.ini [Plugin] Assembly 指定的 dll；
    /// 未指定时遍历插件目录内 *.dll（跳过契约程序集）。
    /// </summary>
    private static Type? FindEntryType(string dir, string assemblyName, string entryType)
    {
        IEnumerable<string> candidates = assemblyName.Length > 0
            ? [SafePath.CombineFilePath(dir, assemblyName)]
            : Directory.GetFiles(dir, "*.dll");

        foreach (string dll in candidates)
        {
            if (!File.Exists(dll))
                continue;
            if (string.Equals(Path.GetFileName(dll), AbstractionsAssemblyFileName, StringComparison.OrdinalIgnoreCase))
                continue; // 契约程序集只保留宿主一份

            try
            {
                var type = Assembly.LoadFrom(dll).GetType(entryType, throwOnError: false);
                if (type is not null)
                    return type;
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "探测入口类型时跳过：{Dll}", dll);
            }
        }

        return null;
    }

    private static void Fail(LoadedPlugin plugin, string reason)
    {
        plugin.State = PluginState.Failed;
        plugin.Error = reason;
        Log.Warning("插件 {Key} 加载失败，已跳过：{Reason}", plugin.Key, reason);
    }

    private static string? NullIfEmpty(string value) => value.Length == 0 ? null : value;

    /// <summary>
    /// 插件私有依赖兜底：默认解析失败时，从插件目录旁加载
    /// （宿主已有程序集由默认 ALC 直接解析，不会走到这里）。
    /// </summary>
    private Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
    {
        string simpleName = new AssemblyName(args.Name).Name + ".dll";
        foreach (string dir in _directories)
        {
            try
            {
                string candidate = Path.Combine(dir, simpleName);
                if (File.Exists(candidate))
                    return Assembly.LoadFrom(candidate);
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "插件依赖解析失败：{Assembly} ← {Dir}", simpleName, dir);
            }
        }

        return null;
    }

    // ── 路由冲突检测 ─────────────────────────────────────────────

    /// <summary>
    /// 扫描插件页面的 <c>@page</c> 路由与宿主路由比对，冲突时告警
    /// （不摘除路由：Blazor 路由表由 Router 统一构建，重复模板行为不可预期，以日志暴露问题）。
    /// </summary>
    private void DetectRouteConflicts()
    {
        HashSet<string>? hostRoutes = null;
        foreach (var plugin in Enabled)
        {
            foreach (var assembly in plugin.PageAssemblies)
            {
                hostRoutes ??= CollectRoutes(typeof(PluginCatalog).Assembly);
                foreach (string route in CollectRoutes(assembly))
                {
                    if (hostRoutes.Contains(route))
                    {
                        Log.Warning("插件 {Id} 的页面路由与宿主内置路由冲突：{Route}（请改用插件专属前缀，如 /plugin/{Id}/…）",
                            plugin.Id, route);
                    }
                }
            }
        }
    }

    private static HashSet<string> CollectRoutes(Assembly assembly)
    {
        var routes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray();
        }

        foreach (var type in types)
        {
            foreach (var attribute in type.GetCustomAttributes<RouteAttribute>(inherit: false))
            {
                if (!string.IsNullOrWhiteSpace(attribute.Template))
                    routes.Add("/" + attribute.Template.TrimStart('/'));
            }
        }

        return routes;
    }
}
