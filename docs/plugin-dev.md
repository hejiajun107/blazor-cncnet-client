# 插件开发指南（F4）

> 外部插件机制：**不改客户端代码**即可换肤、加样式、往既有页面追加功能、
> 新增独立页面、追加主菜单项，并可访问客户端的 CnCNet / 游戏服务。
>
> 面向读者：插件作者。面向使用者的配置说明见
> [mod-customization.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/mod-customization.md)；
> 实施细节见 [plugin-plan.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/plugin-plan.md)。

---

## 1. 能力总览

| 扩展面 | 需要编译 | 用到的 API |
|---|---|---|
| 换肤 / 加样式 / 加脚本 | 否（纯文件） | `plugin.ini` 的 `Style` / `Script` |
| 往既有页面追加功能 | 是 | `IPluginUiRegistrar.ContributeSlot` |
| 覆盖既有槽位组件 | 是 | `IPluginUiRegistrar.ReplaceSlot` |
| 替换整个主菜单主体（首页） | 是 | `ReplaceSlot("page.mainmenu", typeof(你的组件))` |
| 新增独立页面 | 是 | `IPluginUiRegistrar.AddPageAssembly` + `@page` |
| 追加主菜单项 | 是 | `IPluginUiRegistrar.AddMenuEntry` |
| 注册自有服务 | 是 | `IPluginContext.Services` |
| 访问 CnCNet / 游戏服务 | 是 | 组件 `@inject` / `Initialize(IServiceProvider)` |

**信任模型**：本地目录即信任（不做签名/沙箱）。**无热插拔**：改动后需重启客户端。

---

## 2. 快速开始（最小可跑插件）

### 2.1 创建插件工程

插件是一个 **Razor 类库（RCL）**，目标框架 `net10.0`。可用仓库内示例
[samples/HelloPlugin](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/samples/HelloPlugin) 作模板。

```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <OutputType>Library</OutputType>
  </PropertyGroup>
</Project>
```

### 2.2 引用插件 SDK（二选一）

插件**唯一**必需的编译期依赖是契约程序集 `CnCNetClient.Plugin.Abstractions`。

**方式 A（推荐）：本地 nupkg**——由发布脚本产出（`tools/publish.ps1 -PackPluginSdk` →
`dist/nuget/CnCNetClient.Plugin.Abstractions.1.0.0.nupkg`）。

```powershell
# 一次性：把输出目录注册为本地包源
dotnet nuget add source "<仓库根>\dist\nuget" -n cncnet-client-local
# 然后
dotnet add package CnCNetClient.Plugin.Abstractions
```

```xml
<PackageReference Include="CnCNetClient.Plugin.Abstractions" Version="1.0.0" />
```

**方式 B：直接引用客户端内的契约 DLL**（零配置，适合调试）——发布版
`ClientApp\CnCNetClient.Plugin.Abstractions.dll` 即为约束面。

```xml
<ItemGroup>
  <Reference Include="CnCNetClient.Plugin.Abstractions">
    <HintPath>$(CncNetClientDir)\CnCNetClient.Plugin.Abstractions.dll</HintPath>
  </Reference>
</ItemGroup>
```

> ⚠️ **关键：不要让契约 DLL 复制进你的插件输出目录。**
> 宿主导入插件时已加载**同一份**契约（共享默认 `AssemblyLoadContext`，按简单名复用），
> 插件目录再放一份不仅多余，还会让"是否加载了第二份类型"难以排查 —— 表现为
> 组件渲染/服务注入莫名失败。两种引用方式的正确写法：
>
> | 引用方式 | 必要的 MSBuild 元数据 |
> |---|---|
> | `PackageReference`（方式 A） | `PrivateAssets="all"` **且** `ExcludeAssets="runtime"` |
> | `Reference` + `HintPath`（方式 B） | `<Private>false</Private>` |
> | `ProjectReference`（仓库内联调） | `Private="false"` |
>
> 注意 `PrivateAssets="all"` **单独不够**：它只阻止"向下游传递"，并不阻止复制到
> **自身**输出目录；必须同时加 `ExcludeAssets="runtime"`。

### 2.3 写入口类型

```csharp
using CnCNetClient.Plugin.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace MyPlugin;

public sealed class MyPluginEntry : ICncNetPlugin
{
    public string Id => "my-plugin";
    public string Name => "我的插件";
    public int ApiVersion => PluginApi.CurrentVersion;   // 必须等于宿主契约版本

    // Build 之前：注册插件自有服务（AddSingleton / AddScoped 均可）
    public void RegisterServices(IPluginContext ctx)
    {
        ctx.Services.AddSingleton<MyState>();
    }

    // 声明 UI 贡献面
    public void RegisterUi(IPluginUiRegistrar ui)
    {
        ui.ContributeSlot("layout.topbar", typeof(Components.MyBadge)); // 追加型槽位
        ui.AddPageAssembly(typeof(MyPluginEntry).Assembly);            // 提供 @page 路由
        ui.AddMenuEntry(new PluginMenuEntry(
            "my-plugin.home", "我的页面", "MY PAGE", "★", "/plugin/my-plugin/home"));
    }

    // Build 之后：可解析全部宿主服务
    public void Initialize(IServiceProvider services) { }
}
```

### 2.4 自描述文件 `plugin.ini`

```ini
[Plugin]
Id=my-plugin
Name=我的插件
ApiVersion=1
Assembly=MyPlugin.dll                          ; 可省略 → 遍历目录内 *.dll 探测 EntryType
EntryType=MyPlugin.MyPluginEntry
Style=plugin.css                               ; 相对插件目录，可空
Script=                                        ; 相对插件目录，可空
```

> **纯资源插件**（不需要编译，见 §1 第一行）只需 `Id` + `Style`：
> `EntryType` / `Assembly` 留空即视为纯资源插件，且**不需要** `ApiVersion`
> （不加载任何程序集，无类型身份风险）。此时 `plugin.ini` 里只写
> `Id` / `Name` / `Style`（可选 `Script`）即可。
> 程序集插件则必须提供 `EntryType` 与正确的 `ApiVersion`。

### 2.5 安装与启用

```
游戏根目录/
├── Resources/ClientDefinitions.ini   ← 追加 [Plugins] 节启用插件
└── Plugins/
    └── my-plugin/
        ├── plugin.ini
        ├── MyPlugin.dll
        ├── plugin.css
        └── assets/…          ; 图片等（css 里 url() 相对路径可解析）
```

```ini
; Resources/ClientDefinitions.ini
[Plugins]
my-plugin=Plugins/my-plugin
```

**重启客户端**后插件生效。是否加载成功看 `logs/client-*.log`：

```
插件已加载：my-plugin（我的插件）ApiVersion=1 目录=…
插件未启用：my-plugin（…）原因：契约版本不匹配（插件 2 / 宿主 1）
插件 my-plugin 注册失败（已禁用）…
```

---

### 2.6 纯资源插件：换肤 / 加样式（无需编译）

`plugin.ini` 的 `EntryType`/`Assembly` 留空即视为纯资源插件：**不加载程序集、不校验 `ApiVersion`**，
只把声明的 `Style`/`Script` 注入 WebView。

```ini
[Plugin]
Id=my-theme
Name=我的样式包
Style=plugin.css
```

启动日志：`纯资源插件已启用：my-theme（我的样式包）样式=plugin.css 脚本=`

**样式来源与路径**：`plugin.css` 由进程内内容伺服以**游戏根目录**为根提供 URL
（`http://127.0.0.1:<端口>/Plugins/<id>/plugin.css`，`Content-Type: text/css`），
所以文件内 `url(assets/x.png)` 按**插件目录**解析，可直接引用自带图片/字体。

**可覆盖的 design tokens**（定义于宿主 `wwwroot/css/app.css`）：

| 类别 | 变量 |
|---|---|
| 背景 | `--bg-0`（页面底） `--bg-1` `--panel`（面板） |
| 主色 | `--cyan` `--cyan-soft` `--cyan-deep` `--blue` `--violet` |
| 文字 | `--text` `--text-2` `--text-3` `--dim` |
| 语义 | `--line`（描边） `--gold` `--red` `--ok` |
| 字体 | `--font-cn` `--font-orbitron` `--font-rajdhani` |

**⚠️ 关键坑：必须同时覆盖内页 / 红警主题选择器。**
宿主的主题分三档：`:root`（主菜单）、`:root[data-theme="inner"]`（内页）、`:root[data-theme="ra"]`（红警）。
后两者带**属性选择器，特异性高于裸 `:root`** —— 只写 `:root{--cyan:…}` 时，
进入设置 / 大厅等内页会被内置规则压掉，表现为"主菜单变了、内页没变"。
（F1 的 `[Themes]` 目录没有这个问题：它的加载逻辑是「注入 `<link>` **并移除 `data-theme` 属性**」。）

```css
/* 正确写法：一次覆盖三档 */
:root,
:root[data-theme="inner"],
:root[data-theme="ra"]{
  --bg-0:#1a0b2e; --bg-1:#231145;
  --cyan:#ffd23f; --cyan-soft:#ffe9a8; --line:rgba(255,210,63,.35);
}
```

**最小"可肉眼验证"示例**（建议第一次就这么试，能一眼判定注入是否生效）：

```css
:root,:root[data-theme="inner"],:root[data-theme="ra"]{--cyan:#ffd23f;--cyan-soft:#ffe9a8}
body::after{content:"PLUGIN CSS ACTIVE";position:fixed;top:8px;right:8px;z-index:99999;
  padding:6px 10px;background:#ffd23f;color:#201a06;font:700 12px/1 monospace;border-radius:4px}
```

重启后应看到：右上角出现金黄标签（证明 CSS 被注入）+ 主色变金（证明优先级压过 `app.css`）；
再进设置页确认内页同样变金（证明三档覆盖写对了）。

**可直接复制运行的完整样例**：[samples/ThemeDemo/](../samples/ThemeDemo)（纯资源、免编译）。

**两个时序注意点**：
- 注入发生在 `MainLayout` **首帧之后**，启动瞬间可能闪一下未换肤的样式（与 F1 主题同一时机）。
- `Script` 以 `<script defer>` 注入，且注入时 `DOMContentLoaded` 已过 —— 只适合挂后续交互或幂等初始化，
  不要指望在启动期钩事件。

### 2.7 脚本与内容资源（JS / 图片 / 3D 贴图）

插件脚本与静态资源都从**插件目录**经内容伺服读取，不经过宿主的静态资源管道，因此：

- **页面源与内容源不同源**：Blazor 页面来自 `app://localhost`，资源来自
  `http://127.0.0.1:<动态端口>/Plugins/<id>/…`。宿主响应已带
  `Access-Control-Allow-Origin: *`，所以 `<script src>`、`<img>`、`fetch`、ESM `import`、
  WebGL `texImage2D` 都能直接跨源读取，不需要内联 base64 绕过。
- **路径基准**：CSS 的 `url(...)` 以插件目录为基准；JS 里不要写死路径，用脚本自身地址推导：

  ```js
  var baseUrl = new URL('.', document.currentScript.src).href;
  new THREE.TextureLoader().load(baseUrl + 'assets/textures/earth_day.jpg');
  ```

- **注入时序**：`Script` 是 `defer`，可能晚于组件首帧。组件在 `OnAfterRenderAsync` 里
  调用 `window.xxx.mount()` 时要短暂轮询等待命名空间就绪（`JSException` → 延迟重试），
  或让脚本自己监听 DOM 后就绪；初始化必须**幂等**，卸载时释放 GPU/定时器/监听器。
- **MIME 白名单**：`.css/.js/.mjs/.json/.woff/.woff2/.ttf` 与常见图片按正确类型返回；
  `.wasm`、`.glb` 等按 `application/octet-stream` 返回 —— `fetch` + `arrayBuffer()`
  （含 `GLTFLoader`）可用，但 `WebAssembly.instantiateStreaming` 会因 MIME 报错，
  需先取 `ArrayBuffer` 再 `WebAssembly.instantiate`。
- **禁止 CDN**：three.js / Babylon / 贴图 / 字体一律随插件分发；注意包体与素材许可证，
  3D 场景需有 WebGL 不可用与 `prefers-reduced-motion` 的静态降级。

完整实现见 [samples/Cnc3Home](../samples/Cnc3Home)：本地 three.js r149（UMD）、
NASA 公有领域地球贴图、`assets/` 随构建复制、`cnc3-home.js` 负责挂载/释放。

---

## 3. 契约 API 参考
契约程序集 `CnCNetClient.Plugin.Abstractions`（命名空间同名）。

### `ICncNetPlugin`

| 成员 | 调用时机 | 说明 |
|---|---|---|
| `string Id` | 加载时读取 | 唯一标识；建议与 `[Plugins]` 键、目录名一致 |
| `string Name` | 加载时读取 | 展示名 |
| `int ApiVersion` | 加载时读取 | 必须 `== PluginApi.CurrentVersion`，否则禁用 |
| `void RegisterServices(IPluginContext)` | `Build()` **之前** | DI 注册（此后集合不可变） |
| `void RegisterUi(IPluginUiRegistrar)` | `Build()` **之前** | 声明槽位/页面/菜单 |
| `void Initialize(IServiceProvider)` | `Build()` **之后** | 可解析全部宿主服务；默认空实现 |

### `IPluginContext`

| 成员 | 说明 |
|---|---|
| `IServiceCollection Services` | 注册自有服务（仅 `Build()` 前可用） |
| `string PluginDirectory` | 插件目录绝对路径（读自带配置/资源） |
| `string GameRoot` | 游戏根目录绝对路径 |
| `string ContentUrl(string relativePath)` | 游戏根相对路径 → 客户端内可加载的 http URL |

### `IPluginUiRegistrar`

| 方法 | 语义 |
|---|---|
| `ContributeSlot(slotKey, Type)` | **追加**：多插件/多组件共存，按加载顺序渲染 |
| `ReplaceSlot(slotKey, Type)` | **覆盖**：同槽位后注册者胜（会写日志），谨慎使用 |
| `AddMenuEntry(PluginMenuEntry)` | 追加主菜单项 |
| `AddPageAssembly(Assembly)` | 声明提供 `@page` 的程序集，由宿主 `Router` 发现 |

### `PluginMenuEntry`

```csharp
new PluginMenuEntry(
    Key: "my-plugin.home",   // 唯一键（加载时校验非空）
    Label: "我的页面",        // 中文主文本
    LabelEn: "MY PAGE",      // 英文副文本（Orbitron 小字）
    Icon: "★",               // 图标字符
    Route: "/plugin/my-plugin/home",
    Order: 100);             // 排序：默认 100，居内置项之后、「退出」之前
```

### 当前生效的槽位

| slotKey | 类型 | 宿主渲染位置 |
|---|---|---|
| `layout.footer` | 替换 | 全局底栏（`ReplaceableComponent`）；默认 `FooterBar` |
| `page.mainmenu` | 替换 | 主菜单主体（`MainMenu.razor` 的 `DynamicComponent`）；宿主自动连接 / ESC / 更新检查 / 隐私声明仍生效，示例见 `samples/Cnc3Home` |
| `layout.topbar` | 追加 | 全局悬浮顶栏的菜单区（`PluginSlot`） |
| `menu.entry` | 追加 | 主菜单按钮列表之后、页脚之前（自定义渲染的菜单入口） |
| `page.settings.aside` | 追加 | 设置页左侧栏（`PluginSlot`） |
| `page.lobby.aside` | 追加 | 在线大厅 `.lobby-grid`（三列网格；`grid-column:1/-1` 可占满整行） |

> 追加型槽位的外层容器 `.plugin-slot` 为 `display:contents`，其子元素**直接参与父容器布局**，
> 因此你的组件根元素应是一个（或一组）与所在容器风格一致的块/按钮 —— 顶栏与主菜单建议用宿主的
> `.hm` / `.mbtn` 类，设置侧栏与大厅面板建议用普通卡片 / `.lb-panel`（示例见 `samples/HelloPlugin`）。
>
> 新增槽位 key 需要宿主侧加一行 `<PluginSlot Key="…"/>`；如需更多挂载点请提需求。

**替换型槽位的约定**（`page.mainmenu` / `layout.footer`）：

- 由宿主的 `DynamicComponent` 渲染，**不向组件传参**；所需数据用 `@inject` 取宿主服务。
- 同一 key 只有一个组件生效：**后加载的插件胜出**，宿主按 `TryGetReplacement` 倒序解析；
  多个插件争抢会在日志里体现，不建议。
- 只替换**视觉主体**，宿主页面自身的逻辑仍在跑：`page.mainmenu` 的自动连接、ESC 返回、
  更新检查、隐私声明都不受覆盖影响（隐私弹窗会盖在你的页面上，属正常）。
- 接管整页时，全局顶栏/底栏仍由 `MainLayout` 渲染，可用
  `body:has(.your-root) .hud, body:has(.your-root) .foot{display:none}` 临时隐藏，
  离开该页后自动恢复；不要直接改宿主 CSS 文件。
- 退出必须 `AppExitNotifier.NotifyExiting()` 后再 `Environment.Exit(0)`，
  确认弹窗复用 `MessageBoxService`，否则退出码/重启流程会异常。

### 页面路由

- 插件 `@page` 路由由宿主 `Router.AdditionalAssemblies` 自动发现，**默认套用宿主 `MainLayout`**
  （顶栏/底栏/背景/弹窗均可用）。
- 请使用专属前缀（如 `/plugin/<id>/…`）。与内置路由重复时宿主只在日志告警，行为不可预期。
- 页面内可直接 `@inject` 宿主服务（见下节）。

---

## 4. 访问宿主服务

插件与宿主共享**默认 `AssemblyLoadContext`**，因此宿主程序集的类型身份与宿主自身完全一致：
`@inject CnCNetManager`、`@inject GameRoomService` 等均可用，组件也能被宿主的
`DynamicComponent` 正常渲染。

```razor
@* 插件页面/槽位组件内 *@
@inject CnCNetManager CncNet
@inject IJSRuntime JS
```

要**编译**这类代码，插件工程还需引用宿主对应程序集（发布目录
`ClientApp\CnCNetClient.Online.dll` / `CnCNetClient.Domain.dll` / `CnCNetClient.Core.dll` …），
同样用 `<Private>false</Private>`（或 nupkg 引用时 `ExcludeAssets="runtime"`）避免复制。
这些程序集宿主都已加载，因此不要把它们放进插件目录。

> ⚠️ `Initialize(IServiceProvider)` 收到的根容器**不能解析 Scoped 服务**
> （如 `GameRoomService`）；需要 scoped 服务请在 `Build()` 之后的自有 `AddSingleton` 里
> 通过 `IServiceScopeFactory` 建 scope，或直接在组件里 `@inject`（组件运行在电路 scope 内，推荐）。

---

## 5. 调试与排障

| 现象 | 排查方向 |
|---|---|
| 插件完全没反应 | `[Plugins]` 键值拼写、目录是否存在、`Resources/ClientDefinitions.ini` 是否在 `Resources\` 下 |
| 日志出现"契约版本不匹配" | `plugin.ini` 的 `ApiVersion` 未跟随宿主契约升级 → 更新插件并重新编译 |
| 日志出现"注册失败/初始化失败" | 见日志异常栈；常见于 `RegisterUi` 传了非 `IComponent` 类型 |
| 槽位组件不渲染 | slotKey 拼写、是否用了宿主实际挂载的 key（见 §3 表）、`ContributeSlot` 是否被调用 |
| 页面 404 | `AddPageAssembly` 未调用、`@page` 模板写错、路由与内置冲突（看日志告警） |
| 样式没生效 | `plugin.ini` 的 `Style` 路径（相对插件目录）、文件是否被内容伺服到、浏览器 DevTools 看 404 |
| 组件注入/渲染莫名失败 | 检查插件目录里是否混入了契约 DLL（见 §2.2 警告） |

插件开发时可用客户端内建的 DevTools（`EnableDevTools(true)`）观察 DOM/CSS/JS 报错。

---

## 6. 打包与分发（清单）

发布一个程序集插件：

1. `dotnet build -c Release`（或 `dotnet publish`）；
2. 新建 `Plugins/<id>/` 目录，放入：
   - `plugin.ini`（`Id`/`Name`/`ApiVersion`/`EntryType` 与代码一致）
   - 插件主 DLL（`Assembly` 指向它）
   - 插件**私有**依赖 DLL（宿主没有的第三方库；**不要**放宿主程序集与契约 DLL）
   - `plugin.css` / `plugin.js` / `assets/`（如有）
3. 使用者在其 `Resources/ClientDefinitions.ini` 加 `[Plugins] <id>=Plugins/<id>`；
4. 重启客户端，查日志确认 `插件已加载：…`。

纯资源插件只需第 2 步的 `plugin.ini` + `plugin.css`（可无 DLL），使用者加一行 `[Plugins]` 即完成换肤。

---

## 7. 限制

- **不支持热插拔**：无卸载、无运行时重载，改动后必须重启客户端。
- **软件包版本分级粗**：`ApiVersion` 为整数，契约不兼容变更时宿主递增 `PluginApi.CurrentVersion`，
  插件需随之重新编译并发布新版本。
- **不做沙箱/签名**：插件拥有与宿主同等的权限（可读写游戏目录、访问网络）。请只安装可信来源的插件。
- **不做依赖隔离**：插件私有依赖由宿主的解析兜底按**文件名**从插件目录加载；两个插件若带同名但
  不同版本的第三方库，会以后加载者为准（默认 ALC 复用已加载程序集）。
- **RCL 的 `wwwroot` 静态资源不可用**：宿主**没有**把插件程序集注册进静态资源管道，因此
  RCL 惯用的 `_content/<AssemblyName>/…` 路径（如 `_content/MyPlugin/css/x.css`）**不生效**。
  插件样式/脚本请作为**松散文件**放进插件目录（`plugin.css`/`plugin.js`/`assets/`），
  由 `plugin.ini` 的 `Style`/`Script` 声明（宿主经内容伺服加载）；
  组件内引用图片等资源用 `IPluginContext.ContentUrl("Plugins/<id>/assets/x.png")` 或
  `<img src="@Url">`（`Microsoft.AspNetCore.Components.WebAssembly.Services` 之外无特殊要求）。
  同理，`@import`/`url()` 的相对路径以**插件目录**为基准（内容伺服以游戏根为根，插件目录是它的一段）。
- **槽位数量有限**：当前为 `page.mainmenu` / `layout.footer`（替换）+ `layout.topbar` /
  `menu.entry` / `page.settings.aside` / `page.lobby.aside`（追加），共 6 个挂载点。
  需要更多挂载点在宿主侧加一行 `<PluginSlot Key="…"/>` 即可。
- **内容伺服的 MIME 是白名单制**：非常见扩展名（`.wasm`、`.glb`、`.hdr` 等）按
  `application/octet-stream` 返回，只影响流式实例化一类的用法，详见 §2.7。

---

## 8. 参考实现

- 完整可跑示例：[samples/HelloPlugin](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/samples/HelloPlugin)
  —— 覆盖 4 个追加型槽位（顶栏徽标、主菜单自定义入口、设置侧栏卡片、大厅面板）、
  新增页面 `/plugin/hello-plugin/home`、主菜单项、自有 DI 服务、`plugin.css` 注入。
- 替换主菜单示例：[samples/Cnc3Home](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/samples/Cnc3Home)
  —— `ReplaceSlot("page.mainmenu", …)` 接管首页主体、three.js 昼夜地球（本地贴图/UMD）、
  斜切菜单复用 `LoginService` / `MessageBoxService` / `AppExitNotifier`、
  `body:has()` 隐藏全局顶栏底栏、`IAsyncDisposable` 释放 WebGL。
- 宿主侧实现：
  [PluginCatalog.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginCatalog.cs)（发现/校验/加载/容错）、
  [PluginAssetInjector.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Plugins/PluginAssetInjector.cs)（样式/脚本注入）、
  [PluginSlot.razor](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Shared/PluginSlot.razor)（追加型槽位）、
  [Program.cs](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs)（三阶段接入）。
