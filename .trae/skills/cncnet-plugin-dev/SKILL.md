---
name: cncnet-plugin-dev
description: Scaffold, debug, and package CnCNet client plugins (theme packs and compiled RCL plugins). Use when the user asks to 做插件/写插件/换肤/加槽位, or mentions plugin.ini, ICncNetPlugin, or the Plugins directory.
---

# CnCNet 插件开发

按本流程创建、调试、打包本客户端的插件。契约与宿主实现以仓库代码为准；
权威长文档见 `docs/plugin-dev.md`（细节与本 skill 冲突时以代码为准，并顺手修正文档）。

## 1. 先判定插件类型

| 需求 | 类型 | 是否编译 |
|---|---|---|
| 换肤、改 design tokens、加 CSS/JS | 纯资源插件 | 否 |
| 往既有页面/顶栏/主菜单追加组件 | 编译型（RCL） | 是 |
| 新增独立页面 + 主菜单入口 | 编译型（RCL） | 是 |
| 覆盖页脚等替换型槽位 | 编译型（RCL） | 是 |
| 替换整个主菜单主体（首页） | 编译型（RCL），槽位 `page.mainmenu` | 是 |

拿不准时先问用户要哪一档；纯资源能解决就不要建工程。

## 2. 不可违反的硬约束

1. **安装位置**：`<游戏根>/Plugins/<id>/`；在 `Resources/ClientDefinitions.ini` 写
   `[Plugins]` 节，`<id>=Plugins/<id>`。键值写错=插件完全不加载。
2. **契约 DLL 绝不能复制进插件目录**。宿主已加载同一份，第二份会造成类型身份分裂，
   表现为组件渲染/注入莫名失败。引用写法：
   - `PackageReference`：`PrivateAssets="all"` **且** `ExcludeAssets="runtime"`
   - `Reference` + `HintPath` / `ProjectReference`：`Private="false"`
3. **`ApiVersion` 必须等于 `PluginApi.CurrentVersion`**（当前为 `1`）。纯资源插件不校验。
4. **目标框架 `net10.0`**，插件输出 `OutputType=Library`。
5. **RCL 的 `wwwroot` / `_content/<AssemblyName>/` 不可用**。样式、脚本、图片必须是插件目录里的
   松散文件，由 `plugin.ini` 的 `Style`/`Script` 声明。
6. **无热插拔**：任何改动都要重启客户端。
7. 不要往插件目录放宿主程序集（`CnCNetClient.*.dll` 等）；只放插件自己的 DLL 和宿主没有的第三方依赖。

## 3. 纯资源插件（免编译）

1. 复制 `assets/resource-plugin/` 到 `<游戏根>/Plugins/<id>/`。
2. 改 `plugin.ini` 的 `Id` / `Name`；`Style` 保持相对插件目录。
3. CSS 必须**同时覆盖三档主题**，否则内页不生效：

```css
:root,
:root[data-theme="inner"],
:root[data-theme="ra"]{ --cyan:#ffd23f; --cyan-soft:#ffe9a8; }
```

4. 加一行 `[Plugins]`，重启。
5. 验证：右上角出现示例标签 + 主菜单主色变化 + 进入设置页仍是新色（三项都过才算成功）。

可覆盖的 token：`--bg-0/--bg-1/--panel`、`--cyan/--cyan-soft/--cyan-deep/--blue/--violet`、
`--text/--text-2/--text-3/--dim`、`--line/--gold/--red/--ok`、`--font-cn/--font-orbitron/--font-rajdhani`。

## 4. 编译型插件（RCL）

1. 复制 `assets/assembly-plugin/` 新建工程，替换所有 `{{...}}` 占位符。
2. 契约引用二选一：
   - 发布产物：`dist/nuget` 下的 `CnCNetClient.Plugin.Abstractions.1.0.0.nupkg`
     （在仓库根执行 `tools/publish.ps1 -PackPluginSdk` 生成）；
   - 仓库内联调：`ProjectReference`（必须 `Private="false"`）。
3. 入口类实现 `ICncNetPlugin`，按需调用：
   - `RegisterServices`：Build 前注册自有服务（`AddSingleton/AddScoped`）
   - `RegisterUi`：`ContributeSlot` / `ReplaceSlot` / `AddMenuEntry` / `AddPageAssembly`
   - `Initialize`：Build 后可解析宿主服务
4. 页面用专属前缀 `/plugin/<id>/…`，避免与内置路由冲突。
5. `dotnet build -c Release`，把 DLL + `plugin.ini` + 松散资源复制到 `Plugins/<id>/`。
6. 注册 `[Plugins]`，重启，查 `logs/client-*.log` 是否出现 `插件已加载：…`。

### 可用槽位（宿主实际挂载）

| slotKey | 语义 | 位置 |
|---|---|---|
| `page.mainmenu` | 替换 | 主菜单主体（`MainMenu.razor` 的 `DynamicComponent`） |
| `layout.footer` | 替换（默认 `FooterBar`） | 全局底栏 |
| `layout.topbar` | 追加 | 全局悬浮顶栏菜单区 |
| `menu.entry` | 追加 | 主菜单按钮列表之后 |
| `page.settings.aside` | 追加 | 设置页左侧栏 |
| `page.lobby.aside` | 追加 | 在线大厅网格（`grid-column:1/-1` 可占整行） |

追加型槽位外层是 `display:contents`，组件根元素直接参与父容器布局；顶栏/主菜单建议复用
宿主 `.hm` / `.mbtn` 类。需要新挂载点时必须改宿主（加一行 `<PluginSlot Key="…"/>`），不要假装存在。

### 替换主菜单主体（`page.mainmenu`）

定义方式就一行，在 `RegisterUi` 里把槽位指向自己的组件：

```csharp
public void RegisterUi(IPluginUiRegistrar ui)
{
    ui.ReplaceSlot("page.mainmenu", typeof(Components.MyHome));
    ui.AddPageAssembly(typeof(MyPluginEntry).Assembly);   // 组件内有 @page 时才需要
}
```

```razor
@page "/plugin/<id>/home"
@inject NavigationManager Nav
@inject LoginService Login

<div class="my-home">…</div>
```

规则（宿主 `MainMenu.razor` 的实现决定）：

1. **替换的是 `<main class="stage">` 主体**，`DynamicComponent` 渲染，**不传任何参数**。
2. **宿主逻辑不受影响**：自动连接、ESC 返回、更新检查、隐私声明仍在 MainMenu 里执行；
   隐私弹窗会盖在你的页面之上，属于正常现象。
3. **同一槽位后加载的插件胜出**（`TryGetReplacement` 倒序查找），写日志；不要多个插件抢。
4. **接管整页要自己处理视觉与交互**：全局顶栏/底栏由 `MainLayout` 渲染，可用
   `body:has(.my-home) .hud, body:has(.my-home) .foot { display:none }` 临时隐藏，
   导航到内页后 `.my-home` 消失即自动恢复。
5. **动作复用宿主服务**，不要自己造连接/设置逻辑：路由跳转用 `NavigationManager`，
   登录用 `LoginService.ShowAsync()`，弹窗用 `MessageBoxService`，退出前必须
   `AppExitNotifier.NotifyExiting()` 再 `Environment.Exit(0)`。
6. 组件卸载时释放 JS/WebGL/定时器（`IAsyncDisposable`），否则返回主菜单会叠加实例。

### 插件脚本与内容资源

- `plugin.ini` 的 `Script` 在 `MainLayout` 首帧注入（`<script defer>`），可能晚于组件首帧。
  组件里挂载 JS 场景时要**短暂轮询等待全局命名空间就绪**（`JSException` → 延迟重试），
  或让脚本自己监听 DOM 变化后自挂载。示例见 `samples/Cnc3Home`。
- 脚本内定位自带资源用自身 URL，不要写死路径：
  `var baseUrl = new URL('.', document.currentScript.src).href;` → `baseUrl + 'assets/x.png'`。
  CSS 里 `url("assets/x.png")` 天然按**插件目录**解析。
- 页面源是 `app://localhost`，内容伺服是 `http://127.0.0.1:<动态端口>`：宿主已返回
  `Access-Control-Allow-Origin: *`，`<script src>`、`<img>`、`fetch`、ESM import、
  WebGL 纹理都可直接用。
- MIME 白名单：`.css/.js/.mjs/.json/.woff2/.woff/.ttf` + 常见图片。`.wasm`/`.glb`
  会按 `application/octet-stream` 返回：`fetch`+`ArrayBuffer` 可用，但
  `WebAssembly.instantiateStreaming` 会失败，需先转 `ArrayBuffer`。
- **禁止 CDN**：three.js/Babylon、贴图、字体全部放插件目录；注意体积与素材许可证。
- 3D 场景必须做降级：WebGL 不可用 / `prefers-reduced-motion` 时给静态画面，
  并尊重系统的减弱动态效果设置。

### 访问宿主服务

插件与宿主共享默认 `AssemblyLoadContext`，组件里可直接：

```razor
@inject CnCNetManager CncNet
@inject IJSRuntime JS
```

但 `Initialize(IServiceProvider)` 收到的是根容器，**不能解析 Scoped 服务**（如 `GameRoomService`）；
组件运行在电路 scope 内，优先在组件里 `@inject`。

## 5. 排障

| 现象 | 优先检查 |
|---|---|
| 完全没反应 | `[Plugins]` 拼写、目录存在、`ClientDefinitions.ini` 在 `Resources/` 下 |
| 日志"契约版本不匹配" | `plugin.ini` 的 `ApiVersion` 是否为 `1`，插件是否用同一份契约重编译 |
| 日志"未找到入口类型" | `EntryType` 全名（含命名空间）与 DLL 是否一致；`Assembly` 文件名 |
| 组件不渲染 | slotKey 拼写、是否在宿主实际挂载表里、`ContributeSlot` 是否被调用 |
| 页面 404 | 是否调用 `AddPageAssembly`、`@page` 模板、路由是否与内置冲突（看日志告警） |
| 样式不生效 | `Style` 路径、DevTools 看 404；内页不生效多半是漏了 `data-theme` 两档 |
| 注入/渲染莫名失败 | 插件目录里是否混进了契约 DLL 或宿主程序集 |
| 主菜单没被替换 | 日志有无 `DynamicComponent → 你的组件`；槽位是否 `page.mainmenu`；是否被后加载插件抢占 |
| JS 未生效 / `undefined` | `Script` 注入是 `defer`，首帧可能早于脚本；组件内轮询等待全局对象就绪 |
| 3D 贴图或 fetch 失败 | DevTools 看 `127.0.0.1` 请求的 CORS/MIME；`.wasm` 需先取 `ArrayBuffer` 再实例化 |

客户端内建 DevTools（`EnableDevTools(true)`）可直接看 DOM/CSS/JS 报错。

## 6. 完成前自检

- [ ] 插件目录里没有契约 DLL 和宿主程序集
- [ ] `plugin.ini` 的 `Id` 与 `[Plugins]` 键、目录名一致
- [ ] 程序集插件 `ApiVersion=1`，`EntryType` 能被解析
- [ ] 资源来自插件目录松散文件，未使用 `_content/`
- [ ] 重启后日志显示 `插件已加载` / `纯资源插件已启用`
- [ ] 目标页面/槽位肉眼可见生效，且内页主题未被覆盖失效
- [ ] 若接管 `page.mainmenu`：菜单动作走宿主服务、退出走 `NotifyExiting()`、卸载时释放 JS/WebGL
- [ ] 若带 3D/脚本：全部资源本地化、无 CDN、有 `prefers-reduced-motion` 与 WebGL 降级

## 7. 参考

- 完整示例：`samples/HelloPlugin`（编译型）、`samples/Cnc3Home`（替换主菜单 + three.js 本地贴图）、`samples/ThemeDemo`（纯资源）
- 作者指南：`docs/plugin-dev.md`
- 宿主实现：`Infrastructure/Plugins/PluginCatalog.cs`、`PluginAssetInjector.cs`、`Components/Shared/PluginSlot.razor`
- 契约：`src/BlazorCncnetClient/CnCNetClient.Plugin.Abstractions/`
