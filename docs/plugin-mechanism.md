# 插件机制调研（运行时扩展，无需重编译客户端）

> 调研目标：用户在不重新编译客户端的前提下，放入自行编译的插件，实现
> **换主题 / 改样式 / 给特定页面增加功能**。
> 结论：**技术上可行**，且当前代码已具备约三分之一基础（主题侧已可用）。
> 本文只做调研与方案评估，未改动任何代码。

---

## 1. 现状盘点（宿主架构事实）

| 项 | 事实 | 出处 |
|---|---|---|
| 宿主形态 | InfiniFrame BlazorWebView，**进程内 Blazor**（非 WASM），渲染到 WebView2 | [Program.cs L27](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs#L27)、根组件注册 L249 |
| 目标框架 | net10.0，`Microsoft.NET.Sdk.Razor`，`WinExe` | [BlazorCncnetClient.csproj L1-L10](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/BlazorCncnetClient.csproj#L1-L10) |
| AOT/裁剪 | 客户端 `PublishAot=false`；发布脚本**无 `PublishTrimmed`、无单文件** | [csproj L8](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/BlazorCncnetClient.csproj#L8)、[publish.ps1 L53](file:///f:/work/PhotinoCLient/blazor-cncnet-client/tools/publish.ps1#L53) |
| 路由 | `Router AppAssembly="@typeof(Program).Assembly"`，**单一程序集**，无 AdditionalAssemblies | [App.razor L9](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/App.razor#L9) |
| DI | 全部注册集中在 Program.cs，**在 `Build()` 之前**（L92-L282） | [Program.cs L92-L132](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs#L92-L132) |
| 主题 | 内置 3 主题（menu/inner/ra）+ **外部主题目录**（ini 声明 → `theme.css`） | [ThemeProvider.cs L127-L162](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Theme/ThemeProvider.cs#L127-L162) |
| 槽位组件 | `ReplaceableComponentService` 支持覆盖，但**只解析主程序集内类型** | [ReplaceableComponentService.cs L51-L53](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/ReplaceableComponentService.cs#L51-L53) |
| UI 配置 | `UiDefinitionService` 读 `ClientDefinitions.ini [UI]`，提供 `GetString/GetBool` | [UiDefinitionService.cs L39-L50](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/UiDefinitionService.cs#L39-L50) |
| 前端注入 | `app.js` 已有**运行时注入 `<link>`** 的实现（外部主题用） | [app.js L26-L51](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/wwwroot/js/app.js#L26-L51) |
| 内容伺服 | `GameContentServer` 进程内 HTTP 伺服游戏根目录相对路径（css/png/svg/视频），**MIME 缺 .js/.woff2/.json** | [GameContentServer.cs L268-L284](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/GameContentServer.cs#L268-L284) |

仓库已有相关文档 [mod-customization.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/mod-customization.md)（F1 主题 / F2 槽位 / F3 UI 配置），其中 L82-83 明确写着：
> 「目前组件须位于主程序集。追加组件（RCL/附加程序集）为规划中的扩展路径——待 `ReplaceableComponentService`
> 增加外部程序集扫描后即可实现"整组件由 MOD 提供"。」

即：**本次调研的需求正好是既有规划的下一步。**

---

## 2. 需求拆解 → 三档能力

| 档位 | 需求描述 | 现状 |
|---|---|---|
| **A. 主题 / 样式插件** | 放文件即换肤、加 CSS，无需编译 | **已实现**（F1：`[Themes]` + `theme.css` + `theme.ini`，资源走 GameContentServer） |
| **B. 组件/功能插件** | 自行编译 DLL，替换或新增页面内功能块（如页脚、顶栏、侧栏、页面局部） | **缺口**：只能替换主程序集内类型 |
| **C. 页面级插件** | 增加全新页面（自定义路由）、注入 JS、注册自有服务 | **缺口**：Router 单程序集、DI 在 Build 前固定 |

---

## 3. 可行性判定（关键技术点）

### 3.1 动态加载程序集：可行
客户端不是 AOT、发布不裁剪、非单文件 → 反射与 `Assembly.Load*` 都能工作。
Launcher.exe 虽是 NativeAOT，但它只负责启动转发，与插件无关。

### 3.2 类型身份问题（**最高风险点**）
Blazor 组件必须实现 `Microsoft.AspNetCore.Components.IComponent`。
若插件用自己的 `AssemblyLoadContext` 把 `Microsoft.AspNetCore.Components.dll` 也加载一份，
会与宿主那份产生**类型身份不一致** → `DynamicComponent` 渲染时报类型转换失败。

结论：**插件程序集必须与宿主共享框架程序集**。落地方式二选一：
- 方案甲（简单）：`Assembly.LoadFrom(pluginDll)` → 落入默认 ALC，宿主已加载的程序集可直接解析，无类型割裂；
  缺点：不可卸载、同名程序集版本无法共存。
- 方案乙（推荐）：自定义 `AssemblyLoadContext`，但对 `Microsoft.*` / `System.*` / `CnCNetClient.*`（契约）
  **回退到默认 ALC**（`Resolving` 或 `AssemblyDependencyResolver` + 白名单），仅插件私有依赖走插件 ALC。
  可支持卸载与私有依赖隔离。

桌面客户端无热插拔硬需求，**方案甲足够**；若后续要"重启即卸载/升级"，选方案乙。

### 3.3 组件渲染：已具备
[ReplaceableComponent.razor L5-L8](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Components/Shared/ReplaceableComponent.razor#L5-L8)
已用 `<DynamicComponent Type="_resolved" />`。只要 `Resolve()` 能返回外部程序集类型，**渲染链路无需改动**。

### 3.4 静态资源（CSS/JS/图片）：已具备类似能力，需小幅补齐
F1 主题已证明"游戏根目录资源 → GameContentServer → 前端 `<link>` 注入"这条链路可行
（[ThemeProvider.cs L159](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/Theme/ThemeProvider.cs#L159) `MapUrl`）。
插件资源放 `Plugins/<name>/`，同法伺服；**需补 MIME：`.js` / `.woff2` / `.json`**（[GameContentServer L268-284](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/GameContentServer.cs#L268-L284) 现无这三项）。
注意：RCL 编译产物里的 `_content/<包名>/` 资源路径**在运行时加载的程序集上不会自动映射**，
因此插件资源**不要依赖 `_content/`**，统一走 `Plugins/<name>/` 目录更稳。

### 3.5 插件形态：预编译 Razor Class Library（RCL）
插件作者 `dotnet build` 一个 RCL → 产出 `<Plugin>.dll`（内含编译后的 Razor 组件），
放入 `Plugins/`。**不需要 RazorRuntimeCompilation**、不需要宿主源码，只需要引用"插件契约程序集"。

### 3.6 DI 与注册时机：可行
[Program.cs L92-L282](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Program.cs#L92-L282) 的服务注册与
`appBuilder.Build()`（L282）之间是纯代码区间 → **插件加载与 `RegisterServices` 必须在 Build 之前完成**。
这是硬约束：插件无法在运行中途往容器里加服务。

### 3.7 路由页面插件：可行但需改 App.razor
`Router` 支持 `AdditionalAssemblies`；把插件程序集列表传进去即可让 `@page` 路由被发现。
建议路由命名空间加前缀（如 `/plugin/<id>/xxx`）避免与内置页面冲突。

---

## 4. 推荐方案

### 插件契约（新增程序集 `CnCNetClient.Plugin.Abstractions`）
只依赖 ASP.NET Core Components，**不依赖宿主实现**，以稳定 API 面：

```csharp
public interface ICncNetPlugin
{
    string Id { get; }              // 唯一标识
    string Name { get; }
    Version ApiVersion { get; }     // 与宿主契约版本协商
    void RegisterServices(IPluginContext ctx);          // 注册自有服务（Build 前）
    void RegisterUi(IPluginUiRegistrar ui);              // 注册槽位/路由/样式
    void Initialize(IServiceProvider services);           // Build 后初始化（可选）
}
```

宿主侧新增：
- `PluginLoader`：扫 `Plugins/*/plugin.ini`（或目录约定）→ 加载 DLL → 实例化 `ICncNetPlugin`
- `PluginLoader` 在 `Build()` 前调用 `RegisterServices`，在 Build 后调用 `Initialize`
- `ReplaceableComponentService` 扩展：`Resolve()` 增加**插件程序集扫描**（当前 [L51-L53](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/src/BlazorCncnetClient/BlazorCncnetClient/Infrastructure/ReplaceableComponentService.cs#L51-L53) 仅 `AppAssembly`）
- 新增更多槽位：`layout.topbar`、`menu.entry`（主菜单入口追加）、`page.<name>.aside` 等
- `App.razor` 的 Router 加 `AdditionalAssemblies`
- `GameContentServer` 补 `.js/.woff2/.json` MIME
- `app.js` 增加通用 `injectCss(url)` / `loadScript(url)`（复用现有链接注入逻辑）
- 插件开关：`ClientDefinitions.ini [Plugins]` 节（`<id>=true/false`），复用 `UiDefinitionService` 读取风格

### 目录约定（建议，与现有 F1 主题目录风格一致）

```
游戏根目录/Plugins/
└── my-plugin/
    ├── plugin.ini        ; [Plugin] Id/Name/Enabled/ApiVersion
    ├── MyPlugin.dll      ; 预编译 RCL（含 Blazor 组件）
    ├── plugin.css        ; 页面样式（GameContentServer 伺服）
    ├── plugin.js         ; 可选脚本
    └── assets/…          ; 图片等（url() 相对路径可解析）
```

配置入口统一放 `Resources/ClientDefinitions.ini`（与 F1/F2/F3 一致）：

```ini
[Plugins]
my-plugin=Plugins/my-plugin      ; key=插件 id，值=游戏根目录相对目录
```

---

## 5. 风险与约束

| 风险 | 说明 | 应对 |
|---|---|---|
| 类型身份割裂 | 插件自加载框架程序集 → 组件类型不匹配 | 共享默认 ALC（方案甲）或白名单回退（方案乙） |
| DI 时机 | Build 后不可增服务 | 插件加载强制在 Build 前；文档写明 |
| 插件崩溃拖垮主进程 | 第三方代码不可信 | 加载/注册/初始化全 try-catch 隔离 + 失败降级（跳过该插件并记日志） |
| API 版本漂移 | 客户端升级后插件失效 | `ApiVersion` 协商；不匹配则禁用并提示，不崩溃 |
| 前端资源 MIME | 缺 `.js/.woff2/.json` | 补 `ContentTypeFor` 三行 |
| RCL `_content/` 不可用 | 运行时程序集的静态资源无自动映射 | 约定插件资源走 `Plugins/<name>/` + MapUrl |
| 自更新链路 | publish.ps1 校验"安装根顶层仅 Launcher.exe"、wwwroot 无外部 URL | 插件目录位于游戏根 `Plugins/`（ClientApp 之外），**不影响校验**；但更新需**不覆盖用户插件目录** |
| 杀软误报 | 外部 DLL + 未签名 | 沿用现有"签名/白名单"策略；文档提示 |
| 安全边界 | 插件可执行任意 .NET 代码 | 明确"本地目录即信任"模型；如需限制需进程隔离（成本高，不建议首版做） |

---

## 6. 建议实施路线（分期，按性价比排序）

**阶段 0 — 样式/主题插件"正式化"（成本低，风险极低）**
- 补齐 GameContentServer 的 `.js/.woff2/.json` MIME
- 在 app.js 抽象通用 `injectCss/injectJs`（脱离"仅主题"语义）
- 在 ClientDefinitions.ini 增 `[Plugins]` 节的 **CSS/JS 注入类**条目（纯文件插件，零编译）
- 更新 `docs/mod-customization.md`
- 收益：**满足"改主题/加样式不编译"这一档**，风险几乎为零。

**阶段 1 — 外部程序集组件插件（核心诉求）**
- 新增 `CnCNetClient.Plugin.Abstractions`
- `PluginLoader`（加载 + 隔离 + 版本校验）
- `ReplaceableComponentService` 支持外部程序集
- 新增若干槽位；`Router.AdditionalAssemblies`
- 示例插件 + `docs/plugin-dev.md`（插件作者指南）
- 收益：满足"自行编译 DLL 给页面加功能"。

**阶段 2 — 插件 SDK 与生态（可选）**
- 插件模板项目（`dotnet new` 模板）、示例仓库
- 插件权限/信任策略、禁用清单、诊断页（列出已加载插件与状态）
- 收益：让第三方好上手；非必需。

---

## 7. 需要你决策的开放问题

1. **插件能否新增独立页面（自定义路由）**？涉及导航一致性（顶栏/主菜单是否列出插件页面）。
2. **插件能否访问 CnCNet 网络/游戏服务**（权限边界到哪）？
3. **是否要求热插拔**（不重启生效）？建议**不做**：Blazor 组件树 + DI 容器在 Build 后不可变，成本高收益低。
4. **信任模型**：本地目录即信任，还是需要签名校验？
5. 首版是否**只做阶段 0 + 阶段 1**（推荐），阶段 2 延后？

---

## 8. 一句话总结

- **换主题/加样式**：现在就能做，放文件即可（F1 已实现），只需小幅补齐 MIME 与通用注入 API。
- **自行编译 DLL 给页面加功能**：可行，核心工作量是"外部程序集加载 + 槽位解析放开 + 契约程序集"，
  且 `DynamicComponent` 渲染链路已就绪，仓库文档也已把它列为规划路径。
- **根本约束**：插件必须在 `Build()` 前加载（DI 不可变），且必须与宿主共享框架程序集（类型身份）。
