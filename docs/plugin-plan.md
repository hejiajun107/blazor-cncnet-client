# 插件机制 · 实施计划（阶段 0 + 阶段 1）

> 依据调研 [plugin-mechanism.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/plugin-mechanism.md)，并结合已确认的决策：
> **① 允许新增独立页面  ② 允许访问 CnCNet/游戏服务  ③ 不要求热插拔  ④ 本地目录即信任**
> 本阶段交付阶段 0 + 阶段 1；文档与示例插件为阶段 1 收尾项。
>
> **状态：已实施（阶段 0 + 阶段 1 完成）**。§9 剩余两项已按建议默认值落地
> （首批槽位 = `layout.topbar` + `page.settings.aside`；插件菜单项 Order 默认 100，排在「退出」之前）。
> 实施差异与交付物见文末「实施记录」。

---

## 0. 实施记录（完成的落地位置）

| 步骤 | 落地位置 |
|---|---|
| 0-A MIME | [GameContentServer.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/GameContentServer.cs)（`.js/.mjs/.json/.woff2/.woff/.ttf`） |
| 0-B 注入 JS | [app.js](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/wwwroot/js/app.js) `cncnet.injectCss(key,url)` / `cncnet.injectJs(url)`（幂等 + onerror 告警） |
| 0-C/0.4 装配 | `Infrastructure/Plugins/`：[PluginCatalog.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginCatalog.cs)、[LoadedPlugin.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/LoadedPlugin.cs)、[PluginContext.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginContext.cs)、[PluginAssetInjector.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginAssetInjector.cs)；MainLayout 首帧挂载 |
| 0-D 文档 | [mod-customization.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/mod-customization.md) 增 F4 节与槽位表 |
| 1-A 契约项目 | `src/BlazorCncnetClient/CnCNetClient.Plugin.Abstractions/`（`PluginApi.cs` + `ICncNetPlugin.cs`），已入 `.sln`/`.slnx`，主项目 ProjectReference |
| 1-B 加载器 | `PluginCatalog.Discover/RegisterServices/InitializeAll` + `AppDomain.AssemblyResolve` 兜底（仅解析插件私有依赖） |
| 1-C Program 接入 | [Program.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs)：Discover（内容伺服启动之后）→ RegisterServices（Build 前）→ InitializeAll（Build 后） |
| 1-D 多源槽位 | [ReplaceableComponentService.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/ReplaceableComponentService.cs) `Resolve`/`ResolveSlot` + [PluginSlot.razor](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Shared/PluginSlot.razor) |
| 1-E 路由扩展 | [App.razor](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/App.razor) `AdditionalAssemblies`；冲突检测在 `PluginCatalog.DetectRouteConflicts`（**只告警**：Blazor 路由表由 Router 统一构建，摘除单条模板不可行） |
| 1-F 菜单合并 | [MainMenu.razor](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Pages/MainMenu.razor) `BuildMenu()`（内置 → 插件项按 Order → 「退出」）+ `OnMenuClick` 顶部路由分流 |
| 1-G 首批槽位 | `TopBar.razor`（`layout.topbar`，菜单区）、`Settings.razor`（`page.settings.aside`，左侧栏）、`MainMenu.razor`（`menu.entry`，按钮列表之后）、`Lobby.razor`（`page.lobby.aside`，`.lobby-grid` 内） |
| 1-H 示例 + 文档 | `samples/HelloPlugin/`（RCL：入口 + 4 槽位组件 + 1 页面 + `plugin.css` + `plugin.ini`）；[plugin-dev.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/plugin-dev.md)；[acceptance-f4.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/acceptance-f4.md)（验收单） |
| 1-I 打包 SDK | `tools/publish.ps1 -PackPluginSdk`（`dotnet pack` → `<OutputDir>/nuget`，位于 `publish/` 之外不影响既有校验） |

**与计划的差异（均为收敛性调整）**：

1. **内容伺服改为 Build 前显式实例化**（原 `AddSingleton<GameContentServer>()` 类型注册）。
   原因：`RegisterServices` 阶段 `IPluginContext.ContentUrl` 就需要可用 URL，而该阶段在 `Build()` 之前
   无法解析服务；改为先 `new` + `Start(GamePath)` 再 `AddSingleton(instance)`（幂等），
   启动闪屏注入同一实例时 `IsRunning` 短路，无行为变化。
2. **契约 csproj 用 `Microsoft.NET.Sdk` + `FrameworkReference Microsoft.AspNetCore.App`**
   （而非 `Microsoft.NET.Sdk.Razor`）：契约只是一组接口/记录，无 Razor 资源，无需 Razor SDK。
3. **`AssemblyResolve` 仅在存在插件目录时挂载**，且只从插件目录解析（宿主程序集由默认 ALC 直接命中，不走兜底）。
4. **路由冲突只告警**，不摘除路由。
5. 槽位最终为 5 个：`layout.footer`（替换）+ `layout.topbar` / `menu.entry` /
   `page.settings.aside` / `page.lobby.aside`（追加）—— 在已确认的两个之外补了
   `menu.entry`（§9 曾列为候选）与 `page.lobby.aside`，使"主菜单/大厅"两个常用扩展面开箱即用。

## 验收结论与顺带修复（2026-10-15）

阶段 0 + 阶段 1 **T0–T8 全部验收通过**（2026-10-15 运行时 T0–T5；2026-10-16 T6 路由冲突 / T7 发布链路 / T8 SDK 编译链路），
验收单见 [acceptance-f4.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/acceptance-f4.md)
（含日志原文证据与人工确认的 6 项界面表现）。

验收过程发现并修复 3 个问题（2 个为真 bug）：

1. **Build 之前的日志全部丢失** —— 原 `appBuilder.Services.AddSerilog(config => …)` 把 logger 创建
   推迟到 `Build()`，静态 `Log.Logger` 那时才赋值，导致 Build 前（插件发现、环境检测等启动最早期）
   的 `Log.*` 写入默认静默 logger 被丢弃（日志首行恒为 `Started WebView message pump`）。
   改为显式 `Log.Logger = new LoggerConfiguration()…CreateLogger()` + `AddSerilog(Log.Logger, dispose:false)`。
   **该修复不限于插件：启动最早期诊断能力整体恢复。**
2. **无插件的干净环境每次启动都有一条假告警** —— `IniFile.GetSectionKeys` 对不存在的节返回 `null`
   （`CnCNetClient.Tools/IniFile.cs:728`），`foreach (… in null)` 抛 NRE 被 catch 吞成
   `插件清单读取失败（[Plugins] 节）`。改为显式判空后静默返回。
3. **`Id` 缺失时「未启用」日志出现空括号** —— `LoadedPlugin.Id` 默认 `string.Empty` 且仅在读到
   `plugin.ini` 后赋值，而 `PluginCatalog.cs:179` 输出 `插件未启用：{Key}（{Id}）…`。
   改为构造时以 ini key 作 `Id` 默认值。

---

## 1. 决策带来的两个关键简化

| 决策 | 直接后果 |
|---|---|
| **不要求热插拔** | 不需要卸载 → **采用"共享默认 ALC"方案**：插件用 `Assembly.LoadFrom` 落入默认 `AssemblyLoadContext`。彻底规避类型身份割裂（`IComponent` 与宿主同一份），实现最简。 |
| **可访问宿主服务** | 插件必须能解析到 `CnCNetClient.Domain/Online/Core` 等宿主程序集 → 更确认"共享默认 ALC"是正解（自定义 ALC 会导致 `CnCNetManager` 等类型双份）。 |

即：本设计**不使用自定义 ALC**，只用一个被隔离的加载器做"发现 + 容错"，加载动作交给默认 ALC。

---

## 2. 总体架构

```
游戏根目录/
├── Resources/ClientDefinitions.ini      ; [Plugins] 声明启用哪些插件目录（统一入口，沿用 F1 风格）
└── Plugins/
    └── my-plugin/                        ; 插件目录（自带自描述）
        ├── plugin.ini                    ; [Plugin] Id/Name/EntryType/ApiVersion/Style/Script
        ├── MyPlugin.dll                  ; 预编译 RCL（含 Blazor 组件）
        ├── plugin.css                    ; 页面样式（GameContentServer 伺服）
        ├── plugin.js                     ; 可选脚本
        └── assets/…                      ; 图片等（url() 相对路径可解析）
```

### 加载时序（硬约束：DI 在 `Build()` 后不可变）

```
Program.Main
  ├─ 1. UserINISettings.Initialize（既有）
  ├─ 2. PluginCatalog.Discover()                     ← 扫插件目录、读 plugin.ini、校验 ApiVersion、LoadFrom DLL、实例化 ICncNetPlugin
  ├─ 3. appBuilder.Services.AddSingleton(catalog) + 宿主服务注册（既有）
  ├─ 4. foreach plugin → plugin.RegisterServices(PluginContext)   ← 插件注册自有服务（Build 前）
  ├─ 5. app = appBuilder.Build()
  ├─ 6. foreach plugin → plugin.Initialize(services) ← Build 后初始化（可访问全部宿主服务）
  └─ 7. app.Run()
```

任一步骤的插件异常**只影响该插件**：记为 `Disabled` 并写日志，绝不阻断启动。

### 三类扩展面的落地机制

| 扩展面 | 机制 | 复用既有设施 |
|---|---|---|
| **换肤 / 加样式** | 插件 `plugin.css` → `GameContentServer.MapUrl` → `app.js` 注入 `<link>` | 已有（F1 主题同款链路） |
| **页面内加功能** | 槽位：`ReplaceableComponent`（替换）+ 新增 `PluginSlot`（追加，渲染多个组件） | 已有 `DynamicComponent` |
| **新增页面** | `Router.AdditionalAssemblies` 挂插件程序集，`@page` 路由自动被发现 | 需改 `App.razor` |
| **追加主菜单项** | `PluginCatalog.MenuEntries` 与 `MainMenu.razor` 硬编码列表合并 | 需改 `MainMenu.razor` L120-126 |
| **访问网络/游戏服务** | 插件组件 `@inject CnCNetManager` / `GameRoomService` 等（共享 ALC 下类型一致） | 无需改宿主 |

---

## 3. 阶段 0 · 样式/资源插件正式化（低成本，低风险）

目标：**纯文件插件**（不编译）即可换肤、加 CSS/JS。这一档风险接近零、可独立交付。

### 0.1 `GameContentServer` 补 MIME

```csharp
// GameContentServer.cs ContentTypeFor 追加
".js" or ".mjs" => "text/javascript; charset=utf-8",
".json"         => "application/json; charset=utf-8",
".woff2"        => "font/woff2",
".woff"         => "font/woff",
".ttf"          => "font/ttf",
```

### 0.2 `app.js` 抽象通用资源注入（脱离"仅主题"语义）

```javascript
// wwwroot/js/app.js —— 复用 applyTheme 里成熟的 <link> 注入方式
const pluginLinks = new Map();   // key → <link> 元素
function injectCss(key, url) {   // 幂等：同 key 重复调用只更新 href
    let el = pluginLinks.get(key) || (() => {
        const l = document.createElement('link');
        l.rel = 'stylesheet'; l.dataset.plugin = key;
        document.head.appendChild(l); pluginLinks.set(key, l); return l;
    })();
    el.href = url;
}
function injectJs(url) {          // 可选脚本：插入 <script>（注意顺序，defer）
    const s = document.createElement('script');
    s.src = url; s.defer = true; s.dataset.plugin = '1';
    document.head.appendChild(s);
}
```

### 0.3 宿主服务 `PluginAssetInjector`

```csharp
// Infrastructure/Plugins/PluginAssetInjector.cs
// 在 MainLayout 首帧后调用，把插件声明的 css/js 注入 WebView
public sealed class PluginAssetInjector(IJSRuntime js, PluginCatalog catalog, GameContentServer content)
{
    public async Task InjectAllAsync()
    {
        foreach (var p in catalog.Enabled)
        {
            if (p.Style is { Length: > 0 } css)
                await js.InvokeVoidAsync("cncnet.injectCss", p.Id, content.MapUrl(SafePath.CombineFilePath(p.Dir, css)));
            if (p.Script is { Length: > 0 } js_)
                await js.InvokeVoidAsync("cncnet.injectJs", content.MapUrl(SafePath.CombineFilePath(p.Dir, js_)));
        }
    }
}
```

挂载点：`MainLayout.OnAfterRenderAsync(firstRender)`（既有 `ThemeProvider.InitializeAsync()` 旁）。

### 0.4 配置入口（`ClientDefinitions.ini` 增节，与 F1/F2/F3 风格一致）

```ini
[Plugins]
my-plugin=Plugins/my-plugin      ; key=插件 id，值=游戏根目录相对目录（空 = 不启用）
```

### 0.5 文档
更新 [mod-customization.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/mod-customization.md)：新增 `[Plugins]` 与"纯资源插件"章节。

**阶段 0 交付物**：文件级换肤/加样式可用；无 DLL 加载，风险可控。

---

## 4. 阶段 1 · 外部程序集插件（核心）

### 4.1 新增契约程序集 `CnCNetClient.Plugin.Abstractions`

位置：`src/BlazorCncnetClient/CnCNetClient.Plugin.Abstractions/`（与 Core/Domain 等同层）。
只引用 ASP.NET Core Components；**不引用宿主实现**（避免循环）。

```csharp
namespace CnCNetClient.Plugin.Abstractions;

public static class PluginApi
{
    public const int CurrentVersion = 1;   // 契约版本，宿主升级时递增
}

public interface ICncNetPlugin
{
    string Id { get; }            // 唯一标识（须与目录名/ini key 一致）
    string Name { get; }          // 展示名
    int ApiVersion { get; }       // 插件编译时针对的契约版本

    /// <summary>DI 注册（仅在 appBuilder.Build() 之前调用，可 AddSingleton/AddScoped）。</summary>
    void RegisterServices(IPluginContext ctx);

    /// <summary>UI 贡献：槽位 / 页面程序集 / 菜单项 / 样式资源。</summary>
    void RegisterUi(IPluginUiRegistrar ui);

    /// <summary>Build 之后调用，可解析全部宿主服务（含 CnCNetManager）。</summary>
    void Initialize(IServiceProvider services) { }
}

public interface IPluginContext
{
    IServiceCollection Services { get; }       // 插件注册自有服务
    string PluginDirectory { get; }            // 插件自身目录（读自带配置/资源）
    string GameRoot { get; }                   // 游戏根目录
    string ContentUrl(string relativePath);    // → GameContentServer 的 http URL
}

public interface IPluginUiRegistrar
{
    void ContributeSlot(string slotKey, Type componentType);   // 追加型槽位
    void ReplaceSlot(string slotKey, Type componentType);      // 覆盖既有槽位（谨慎）
    void AddMenuEntry(PluginMenuEntry entry);                  // 主菜单项
    void AddPageAssembly(Assembly assembly);                   // 提供 @page 路由
}

public sealed record PluginMenuEntry(
    string Key, string Label, string LabelEn, string Icon,
    string Route,                    // 点击跳转的路由
    int Order = 100,                 // 排序（内置项之后）
    string? VisibleWhen = null);     // 可选可见性条件（如 "connected"）
```

### 4.2 `PluginCatalog` + `PluginLoader`（发现 / 校验 / 容错）

```csharp
// Infrastructure/Plugins/PluginCatalog.cs —— 单例，启动最早期构建
public sealed class PluginCatalog
{
    private readonly List<LoadedPlugin> _plugins = [];
    public IReadOnlyList<LoadedPlugin> Plugins => _plugins;
    public IEnumerable<LoadedPlugin> Enabled => _plugins.Where(p => p.State == PluginState.Enabled);
    public IReadOnlyList<Assembly> PageAssemblies => ...;     // 供 Router.AdditionalAssemblies
    public IReadOnlyDictionary<string, List<Type>> SlotContribs => ...;

    public void Discover(string gameRoot)
    {
        // 1) 读 Resources/ClientDefinitions.ini [Plugins] → 得到启用的插件目录
        // 2) 每目录读 plugin.ini（缺失 → 跳过并警告）
        // 3) ApiVersion 校验：!= PluginApi.CurrentVersion → Disabled + 日志（不崩溃）
        // 4) Assembly.LoadFrom(dir/xxx.dll)   ← 关键：默认 ALC，与宿主共享程序集
        // 5) 找 ICncNetPlugin 实现 → Activator.CreateInstance
        // 6) 全程 try/catch：任何异常 → State=Failed，记录原因，继续下一个
    }
}

// 依赖解析兜底：插件私有依赖（宿主没有的第三方库）从插件目录旁加载
AppDomain.CurrentDomain.AssemblyResolve += (_, args) =>
{
    var simple = new AssemblyName(args.Name).Name + ".dll";
    foreach (var dir in _pluginDirs)
    {
        var candidate = Path.Combine(dir, simple);
        if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
    }
    return null;
};
```

> ⚠️ **必须 `LoadFrom`（默认 ALC）**，不可用自定义 `AssemblyLoadContext` 加载插件本体——
> 否则 `IComponent`、`CnCNetManager` 等类型与宿主不同源，`DynamicComponent` 渲染与注入会失败。

### 4.3 `ReplaceableComponentService` 支持插件程序集

```csharp
// 既有：只查 AppAssembly（L51-53）→ 扩展为多源解析（顺序：ini 覆盖 → 插件贡献 → 内置默认）
public Type Resolve(string key)
{
    if (TryResolveOverride(key, out var t)) return t;      // [Components] ini（既有）
    if (_catalog.TryGetSlot(key, out t)) return t;          // 插件 ReplaceSlot/ContributeSlot
    if (_defaults.TryGetValue(key, out var d)) return d;    // 内置默认（既有）
    throw new InvalidOperationException($"ReplaceableComponent 未注册: {key}");
}
```

> `ReplaceableComponentService` 现为 **Scoped**（[Program.cs L132](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs#L132)），
> 注入单例 `PluginCatalog` 即可（单例可被 scoped 依赖）。

### 4.4 新增"追加型"槽位组件 `PluginSlot.razor`

替换语义（`ReplaceableComponent`）与追加语义（多插件共存）必须分开：

```razor
@* Components/Shared/PluginSlot.razor *@
@if (_types.Count > 0)
{
    @foreach (var t in _types)
    {
        <DynamicComponent Type="t" />
    }
}

@code {
    [Parameter, EditorRequired] public string Key { get; set; } = "";
    private IReadOnlyList<Type> _types = [];
    protected override void OnParametersSet() => _types = Replacer.ResolveSlot(key);  // 追加型：返回多类型
}
```

### 4.5 页面路由扩展（`App.razor`）

```razor
@* 既有：<Router AppAssembly="@typeof(Program).Assembly" ...> *@
<Router AppAssembly="@typeof(Program).Assembly"
        AdditionalAssemblies="@PluginCatalog.PageAssemblies"   ← 新增
        NotFoundPage="@typeof(PageNotFound)">
```

### 4.6 主菜单追加项（`MainMenu.razor`）

```csharp
// 既有 L120-126：硬编码 MenuItems + HiddenMenus 过滤
private MenuItem[] VisibleMenuItems =>
    _visible ??= MenuItems
        .Where(m => !UiDefs.HiddenMenus.Contains(m.Key))
        .Concat(PluginCatalog.MenuEntries.Select(e => new MenuItem(e.Key, e.Label, e.LabelEn, e.Icon)))
        .ToArray();

// 点击分发：既有 switch 顶部分流插件项（按 Route 直接导航）
// case 内：if (PluginCatalog.TryGetMenuRoute(key, out var route)) { Nav.NavigateTo(route); break; }
```

排序：插件项 Order 默认 100 → 排在"退出"之前、内置项之后（可调）。

### 4.7 需要预留的新槽位（阶段 1 建议最小集）

| 槽位 key | 位置 | 语义 |
|---|---|---|
| `layout.footer`（既有） | MainLayout | 替换 |
| `layout.topbar` | [MainLayout L11](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Layouts/MainLayout.razor#L11) | 替换/追加 |
| `menu.entry` | MainMenu | 追加（与 4.6 互补，供需要自定义渲染的项） |
| `page.settings.aside` | Settings.razor | 追加 |
| `page.lobby.aside` | Lobby.razor | 追加 |

> 首批建议只加 `layout.topbar` + `page.settings.aside` 两个，把链路跑通，其余按需再加。

---

## 5. 插件作者视角（示例，阶段 1 收尾交付）

`MyPlugin/MyPlugin.csproj`（RCL）：

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>
  <ItemGroup>
    <!-- 方式一（推荐）：本地 nupkg 源引用；版本明确、跨机器/跨项目通用 -->
    <!-- ExcludeAssets="runtime" 为关键：保证契约 DLL 不被复制到插件输出目录（原因见 §5.2） -->
    <PackageReference Include="CnCNetClient.Plugin.Abstractions" Version="1.0.0"
                      ExcludeAssets="runtime" PrivateAssets="all" />

    <!-- 方式二（零配置/调试）：直接引用已发布客户端内的契约 DLL，免配 nuget 源 -->
    <!--
    <Reference Include="CnCNetClient.Plugin.Abstractions">
      <HintPath>..\..\ClientApp\CnCNetClient.Plugin.Abstractions.dll</HintPath>
      <Private>false</Private>
    </Reference>
    -->
  </ItemGroup>
</Project>
```

### 5.1 SDK 分发（两种方式并存，成本评估）

| 方式 | 产出 | 增量成本 | 定位 |
|---|---|---|---|
| **nupkg（推荐）** | `<dist>/nuget/CnCNetClient.Plugin.Abstractions.<ver>.nupkg` | publish.ps1 增 `-PackPluginSdk` 开关，内约 5 行：`dotnet pack` + 拷入 `dist/nuget`；契约 csproj 增 `<Version>` 属性 | 插件作者的正式方式 |
| **ClientApp 内契约 DLL** | 随 `dotnet publish` **自动**落在 `ClientApp\`（ProjectReference 的运行时依赖会被复制）| **零步骤**（发布链路本就产出） | 快速试玩 / 免配 nuget 源 |

**结论：两种并存几乎不增加负担**——方式二是发布流程的"免费副产品"，方式一只需发布脚本加一小步。
真正的必要成本只有一处**版本对应约定**（与 §4.2 的 `ApiVersion` 协商同源，本来就要维护）：

- `nupkg` 主版本号 = `PluginApi.CurrentVersion`（`1.x.y` ↔ `ApiVersion=1`）；
  契约发生不兼容变更时递增 `CurrentVersion` 并发布新主版本 nupkg。
- ⚠️ **契约程序集不做强命名**（不要 `SignAssembly`）：.NET Core 下同简单名程序集在默认 ALC 中已加载即复用，
  宿主/插件版本差异不会导致加载失败；强命名反而引入绑定重定向麻烦。
- ⚠️ **契约 DLL 必须只留宿主一份**（手段与原理见 §5.2）：HintPath 方式用 `<Private>false</Private>`；
  nupkg 方式用 `ExcludeAssets="runtime"`。注意 **`PrivateAssets="all"` 单独使用并不足够**——
  它只表示"不向下游项目传递"，并不阻止程序集被复制到本插件项目自己的输出目录。
- `dist/nuget/` 位于 `publish/` 之外，**不影响** publish.ps1 既有校验（根目录仅 Launcher.exe、wwwroot 无外部 URL）。

### 5.2 程序集数量与去重（"会不会出现多份 SDK DLL？"）

**核心结论：磁盘上可能有几份 ≠ 运行时内存里有几份，后者恒为一份。**

| 位置 | 应有份数 | 说明 |
|---|---|---|
| 宿主 `ClientApp\` | **1**（必需） | publish 自动产出，宿主唯一权威份 |
| 插件项目 `bin\`（编译输出） | **0**（正确配置时） | 由 `ExcludeAssets="runtime"` / `<Private>false</Private>` 保证 |
| 插件目录 `Plugins/<name>/` | **0**（正确部署时） | 部署只挑插件自身 DLL + 私有依赖，不整目录拷贝 |
| 运行时内存 | **恒 1** | 默认 ALC 按**简单程序集名**去重，同名已加载即复用 |

**为什么运行时只有一份**：`AssemblyLoadContext.Default` 对同一简单名的程序集只加载一次，
后续一切请求（含插件 `LoadFrom` 后的依赖解析）都复用已加载的那份。
因此即便磁盘上误留了第二份契约 DLL，运行时也不会去用它 —— **类型身份保持一致，不会因此崩溃**。

⚠️ **唯一的真实风险：插件目录里出现"版本更高"的同名程序集副本。**
此时默认 ALC 判定"已加载版本不满足请求"，转而从插件目录加载那一份 →
内存中出现第二份 → `ICncNetPlugin` / `IComponent` / `CnCNetManager` 类型身份割裂 → 渲染或注入失败。
（默认 ALC 规则：已加载版本 **≥** 请求版本 → 复用；**<** 请求版本 → 另行加载。）

这正是 **`ApiVersion` 前置校验的真正价值**：在加载插件之前先比对契约版本，不匹配直接禁用该插件，
从源头堵死"加载第二份"这条死路。它不是礼貌提示，而是 **ALC 不分裂的硬防线**。

**同一规则适用于所有宿主程序集**：插件若要 `@inject CnCNetManager`，编译期需引用 `CnCNetClient.Domain.dll`，
但**不得**把该 DLL 复制进插件目录。原则：**插件目录只放"插件自己的代码 + 宿主没有的私有依赖"**，
宿主已有程序集（Core/Domain/Online/Updater/契约）一律不带副本。

**工程保障清单**：
1. 契约与宿主程序集**不做强命名**（`SignAssembly=false`）→ 无绑定重定向；
2. 插件 csproj：nupkg 用 `ExcludeAssets="runtime"`，HintPath 用 `<Private>false</Private>`（**切勿只写 `PrivateAssets`**）；
3. 部署以"只挑自身 DLL"为准，不整目录拷贝 `bin\`（`bin\` 里还有 pdb、宿主 DLL 残留）；
4. 插件私有第三方依赖**避免与宿主/其他插件版本冲突**（默认 ALC 下先加载者生效）——优先复用宿主已有库；
5. 加载器侧落实 `ApiVersion` 校验 + 失败禁用 + 明确日志（§4.2）。

`plugin.ini`：

```ini
[Plugin]
Id=my-plugin
Name=我的插件
EntryType=MyPlugin.PluginMain
ApiVersion=1
Style=plugin.css
Script=plugin.js
```

插件实现（含"访问 CnCNet 服务"与"新增页面"）：

```csharp
public sealed class PluginMain : ICncNetPlugin
{
    public string Id => "my-plugin";
    public string Name => "我的插件";
    public int ApiVersion => 1;

    public void RegisterServices(IPluginContext ctx)
        => ctx.Services.AddSingleton<MyFeatureService>();

    public void RegisterUi(IPluginUiRegistrar ui)
    {
        ui.ContributeSlot("page.settings.aside", typeof(MySettingsAside));   // 设置页加一块
        ui.AddMenuEntry(new("my-page", "我的页面", "MY PAGE", "◆", "/plugin/my-plugin/home"));
        ui.AddPageAssembly(typeof(PluginMain).Assembly);                     // 提供 @page 路由
    }

    public void Initialize(IServiceProvider sp) { /* 可用 sp.GetService<CnCNetManager>() 等 */ }
}
```

插件页面：

```razor
@page "/plugin/my-plugin/home"
@inject CnCNetManager CncNet        @* 共享 ALC → 类型与宿主一致，可直接注入 *@
<h2>我的插件页面</h2>
<p>连接状态：@(CncNet.IsConnected ? "在线" : "离线")</p>
```

---

## 6. 风险与降级

| 风险 | 应对 |
|---|---|
| 插件抛异常阻断启动 | 发现/注册/初始化三阶段各自 try-catch；失败 → `State=Failed` + 日志，跳过该插件 |
| 插件私有依赖缺失 | `AssemblyResolve` 从插件目录兜底（§4.2） |
| 契约版本漂移 | `ApiVersion != CurrentVersion` → 禁用 + 明确日志（不崩溃） |
| 路由与内置页面冲突 | 启动扫描插件 `RouteAttribute`，与内置路由集合比对，冲突则**告警**（不摘除：Blazor 路由表由 Router 统一构建，且冲突模板仅在插件路径书写不当时出现） |
| `[Components]` ini 与插件争抢槽位 | 解析顺序固定（ini 覆盖 > 插件 > 内置），并在日志中提示覆盖来源 |
| 样式/脚本加载失败 | `injectCss/injectJs` 幂等 + JS 侧 `.onerror` 记 toast/console，不影响页面 |
| 自更新覆盖插件 | 插件目录位于**游戏根 `Plugins/`**（ClientApp 之外），更新只作用于 ClientApp，天然不冲突 |
| 杀软误报外部 DLL | 沿用既有策略（签名/白名单）；文档提示 |
| 崩在 UI 线程污染全局 | `GlobalExceptionHandler`（既有）已兜底；插件页面异常按既有链路处理 |

---

## 7. 实施顺序（建议）

| 步骤 | 内容 | 依赖 | 可独立验证 |
|---|---|---|---|
| 0-A | GameContentServer 补 MIME（§0.1） | — | 编译 + 手工访问 .js |
| 0-B | app.js `injectCss/injectJs`（§0.2） | 0-A | 控制台调用验证 |
| 0-C | `[Plugins]` 解析 + `PluginAssetInjector`（§0.3/0.4）+ MainLayout 挂载 | 0-B | 放一个纯 css 插件 → 样式生效 |
| 0-D | 文档增补（§0.5） | 0-C | 审阅 |
| 1-A | 契约程序集 `CnCNetClient.Plugin.Abstractions`（§4.1） | — | 编译 |
| 1-B | `PluginCatalog` + `PluginLoader` + AssemblyResolve（§4.2） | 1-A | 空插件加载成败日志 |
| 1-C | Program.cs 接入（Discover / RegisterServices / Initialize） | 1-B | 启动日志显示插件清单 |
| 1-D | `ReplaceableComponentService` 多源解析 + `PluginSlot.razor`（§4.3/4.4） | 1-B | 插件槽位渲染 |
| 1-E | `App.razor` AdditionalAssemblies + 路由冲突检测（§4.5） | 1-B | 插件页面可访问 |
| 1-F | MainMenu 合并插件菜单项（§4.6） | 1-B | 菜单出现插件项 |
| 1-G | 首批槽位 `layout.topbar` / `page.settings.aside`（§4.7） | 1-D | 页面出现插件块 |
| 1-H | **示例插件 + 插件开发文档**（§5） | 1-A…1-G | 端到端跑通 |
| 1-I | publish.ps1 增 `-PackPluginSdk` + 产出契约 nupkg（§5.1） | 1-A | 产出 `.nupkg`，示例插件以 `PackageReference` 引用并编译成功 |
| 1-J | 编译验证 + 回归（内置功能不受影响） | 全部 | 编译 0 错误、既有页面正常 |

建议按 **0-A → 0-D** 先交付一档（纯文件插件），再进 1-A 起。

---

## 8. 验收标准

1. 放一个**仅含 `plugin.css` 的插件**（不编译）→ 重启后样式生效（阶段 0）。
2. 放一个**编译好的插件 DLL** → 重启后：设置页出现插件区块（槽位）、主菜单出现插件入口、插件页面可通过路由访问（阶段 1）。
3. 插件内 `@inject CnCNetManager` 可读到真实连接状态（阶段 1，共享 ALC 验证点）。
4. 插件 API 版本不匹配 / DLL 损坏 / 初始化抛异常 → **客户端照常启动**，日志有明确原因（容错验证点）。
5. 既有功能回归：无插件时行为与当前完全一致（不引入回归）。
6. `tools/publish.ps1` 打包校验仍通过（根目录干净、无外部 URL）。

---

## 9. 待你确认（仅 3 项，不影响起工）

1. ~~插件 SDK 的提供方式~~ → **已定：两种并存**（nupkg 推荐 + ClientApp 内契约 DLL 备用，成本评估见 §5.1）。
2. **首批槽位数量**：按建议只做 `layout.topbar` + `page.settings.aside`，还是再多加（`page.lobby.aside` / `menu.entry`）？
3. **插件菜单项排序位置**：排在"退出"之前（建议），还是其他位置？

---

## 10. 明确不做（本阶段）

- 插件热插拔（运行时装卸载）—— 已确认不做。
- 插件签名校验 / 沙箱隔离 —— 本地信任模型，不做。
- 插件市场 / 自动下载 —— 阶段 2。
- Razor 运行时编译（插件须预编译）—— 不引入 `RazorRuntimeCompilation`。
