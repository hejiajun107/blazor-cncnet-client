# MOD 定制 UI 说明

> 对应 tasks 5.5 / checklist F1-F4。全部扩展经游戏目录内
> `Resources/ClientDefinitions.ini` 一处声明，**不改代码**即可定制 UI（F1-F3），
> 或经 `Plugins/` 引入外部程序集插件（F4）。
> 配套：`docs/plugin-dev.md`（插件开发/发布）、`docs/acceptance-f4.md`（插件验收单）、
> `docs/splash-screen.md`（启动闪屏）、`docs/build-guide.md`（构建/部署）。

## 配置入口

`游戏根目录/Resources/ClientDefinitions.ini`，客户端启动后惰性读取并缓存
（`Infrastructure/UiDefinitionService.cs`）。环境未就绪或文件缺失时全部回退默认值，
不影响启动。

## F3 · `[UI]` 节 — UI 扩展配置

通用 UI 行为，供组件注入读取（`UiDefinitionService` 提供 `GetString/GetBool`）。

```ini
[UI]
DefaultTheme=menu        ; 默认主题（settings 未持久化主题时使用；空 = 内置「menu」）
HiddenMenus=campaign,lan ; 隐藏的主菜单入口键（逗号分隔，不区分大小写）
                         ; 可选：campaign,multiplayer,skirmish,lan,load,lore,stats,settings,mapeditor,credits,extras,exit
                         ; 未定义该键时默认隐藏 lore（原版客户端无资料库入口）；
                         ; 显式写 HiddenMenus=（空）或省略 lore 可重新显示。
```

- 组件可注入 `UiDefinitionService` 读 `[UI]` 任意键（`GetString`/`GetBool`），
  实现"一处配置、多处生效"。
- 主题持久化权威来源为 `settings.ini [MultiPlayer] Theme`；未持久化时回退 `DefaultTheme`。
- 主菜单入口为**功能对齐**原版客户端：默认含战役/多人/遭遇战/局域网/载入/统计/设置/地图编辑器/鸣谢/附加功能/退出；
  「资料库(lore)」为占位项，默认隐藏，MOD 可按需开启。

## F1 · `[Themes]` 节 — 主题目录整体换肤

```ini
[Themes]
iron=Themes/iron        ; key=主题标识（UI 切换按钮显示），值=游戏根目录相对主题目录
```

主题目录约定（示例：`游戏根目录/Themes/iron/`）：

```
Themes/iron/
├── theme.css    ; 覆盖 design tokens，整体换肤（必选）
└── theme.ini    ; 元信息（可选）
```

`theme.ini`：

```ini
[Theme]
Name=钢铁纪元        ; 切换按钮主文本（缺省 = 节 key）
Hint=IRON THEME     ; Tooltip 说明（缺省 = 相对目录）
```

规则：

- **覆盖 design tokens**：`theme.css` 里用与 `app.css` `:root` 相同的 CSS 变量覆盖即可
  （主色/背景/按钮/动效时长等，变量全集见 `wwwroot/css/app.css` 顶部 `:root`）。
- `url()` 相对资源（如 `url(bg.png)`）经进程内 `GameContentServer` 以游戏根目录为根伺服，
  相对路径解析为游戏根目录下同相对路径——资源直接放主题目录旁即可，无需打包进 wwwroot。
- **与内置主题重名（menu/inner/ra）的 key 被忽略**（保留内置）。
- 主题会出现在 HUD 顶栏切换按钮中；主题间仅 opacity 过渡。
- 切换/持久化与内置主题一致（settings INI 为权威来源）。

## F1.5 · 控件身份钩子 — CSS 精准定位单个控件

除 design tokens 与槽位外，宿主还给**特定控件**暴露了稳定的 `data-*` 身份属性，供主题 / 插件的
`plugin.css` 用属性选择器**精确定位某一个控件**（无需依赖 `id`——实例化 `id` 仅归宿主 JS / 锚点使用，
且同一控件可能多处渲染）：

| 钩子 | 出现位置 | 取值 | 用途示例 |
|---|---|---|---|
| `data-option` | 游戏选项的复选框容器（`gl-check`）与下拉容器（`go-dd`） | 选项名（如 `chkSuperWeapons` / `cmbCredits`） | `.go-check[data-option="chkSuperWeapons"]{…}` |
| `data-setting` | 设置页每一行（`set-row`）：内置控件 + ini 注册的额外控件 | `<Section>:<Key>`（如 `Video:ClientFPS` / `Options:ScrollRate`） | `.set-row[data-setting="Audio:VoiceVolume"]{…}` |

规则与约束：

- 取值即 **ini 的节:键**（内置控件按 `UserINISettings` 的真实节/键；RA 下少数键落在 `[Options]`，
  钩子按游戏类型解析，故同一控件在 RA / 其它客户端下取值可能不同）。
- 游戏选项出现在遭遇战、在线房间、LAN 房间三处页面，**同一选项名在三处取值一致**，一条属性选择器即可覆盖。
- 这些钩子**只增不减、语义稳定**，可安全用于主题与插件样式；但**不**替代 design tokens——
  能改颜色/尺寸时优先覆盖 token（见上）。
- 宿主不保证其它任意元素的 `data-*`／`id` 稳定，除上表两种外请勿依赖。

## F2 · `[Components]` 节 — 关键页面组件可替换

页面内「槽位」组件通过 key 解析渲染类型（`Infrastructure/ReplaceableComponentService.cs`
+ `Components/Shared/ReplaceableComponent.razor`）：

```ini
[Components]
layout.footer=BlazorCncnetClient.Components.Shared.FooterBar
```

- 键：槽位标识；当前已注册槽位 `layout.footer`（全局底栏），默认实现
  `BlazorCncnetClient.Components.Shared.FooterBar`。
- 值：**主程序集内** Blazor 组件类名（全名或类名均可）。MOD 可在不改代码的情况下
  切到程序集内其他组件变体。
- 解析优先级：`[Components]` 覆盖 → 插件 `ReplaceSlot`（F4）→ 内置默认；未知键/非组件/未知类型
  会抛异常（配置错误尽早暴露，请以 logs 排查）。

> 追加组件（不在主程序集内的组件）自 F4 起由插件机制提供：插件用
> `ContributeSlot` 追加到 `PluginSlot` 槽位、用 `AddPageAssembly` 提供整页、用
> `ReplaceSlot` 覆盖 `ReplaceableComponent` 槽位。见下节与 `docs/plugin-dev.md`。

## F4 · `[Plugins]` 节 — 外部插件（换肤 / 加组件 / 加页面）

```ini
[Plugins]
hello-plugin=Plugins/hello-plugin   ; key = 插件标识，值 = 游戏根目录相对插件目录（空值 = 不启用）
```

插件目录自带自描述文件 `plugin.ini`：

```ini
[Plugin]
Id=hello-plugin                        ; 唯一标识
Name=示例：你好插件                     ; 展示名
ApiVersion=1                           ; 编译时针对的契约版本（须等于宿主 PluginApi.CurrentVersion）
Assembly=HelloPlugin.dll               ; 入口程序集（可省略 → 遍历目录内 *.dll 探测 EntryType）
EntryType=HelloPlugin.HelloPluginEntry ; 入口类型（须实现 ICncNetPlugin）
Style=plugin.css                       ; 自带样式（相对插件目录，空 = 不注入）
Script=                                ; 自带脚本（相对插件目录，空 = 不注入）
```

**纯资源插件（不编译）** 的最小 `plugin.ini` —— `EntryType`/`Assembly` 留空即视为纯资源插件，
不加载程序集、**不校验 `ApiVersion`**：

```ini
[Plugin]
Id=my-theme
Name=我的样式包
Style=plugin.css
```

> 可覆盖的主题变量（design tokens）清单、**内页/红警主题选择器的特异性坑**、
> 以及"一眼可验证"的最小示例，见 [plugin-dev.md](file:///f:/work/PhotinoCLient/blazor-cncnet-client/docs/plugin-dev.md) §2.6；
> 可直接复制的完整样例见 [samples/ThemeDemo/](file:///f:/work/PhotinoCLient/blazor-cncnet-client/blazor-cncnet-client/samples/ThemeDemo)（免编译）。

两档用法：

| 档位 | 需要编译 | 能力 |
|---|---|---|
| **纯资源插件** | 否 | 只放 `plugin.ini` + `plugin.css`（可选 `plugin.js`）→ 换肤/加样式/加脚本 |
| **程序集插件** | 是 | 追加槽位组件、提供新页面与主菜单项、注册自有服务、访问宿主服务 |

规则与约束：

- **本地目录即信任**：插件不做签名/沙箱校验，属用户本地自装扩展。
- **不热插拔**：插件在客户端启动时一次性加载（Build 前发现/注册、Build 后初始化），
  改动后需**重启客户端**；无运行时卸载。
- **程序集插件的契约版本必须匹配** `PluginApi.CurrentVersion`（当前 `1`）：不匹配 → 该插件禁用并记
  `logs/client-*.log`，不影响其它插件与客户端启动（纯资源插件不校验此值）。
- 插件**自带依赖**（宿主没有的第三方库）可放在插件目录内，由宿主的解析兜底从同目录加载；
  **不要**在插件目录放 `CnCNetClient.Plugin.Abstractions.dll`（宿主已加载同一份）。
- 插件的 `plugin.css` / `plugin.js` / 图片等经进程内 `GameContentServer` 以**游戏根目录**为根
  伺服，因此 `plugin.ini` 中的 `Style`/`Script` 写相对**插件目录**的路径即可。
- 插件样式/脚本必须是插件目录内的**松散文件**；RCL 的 `wwwroot` 静态资源（`_content/<程序集>/…`）
  在插件场景下**不可用**（宿主未把插件程序集注册进静态资源管道）。
- 单个插件加载/注册/初始化失败仅禁用该插件；失败原因见 `logs/client-*.log`。
- 插件页面路由请使用专属前缀（如 `/plugin/<id>/…`）避免与内置页面冲突；重复模板会在日志中告警。


## 当前生效的槽位

| 槽位 key | 类型 | 默认组件 | 说明 |
|---|---|---|---|
| `layout.footer` | 替换 | `FooterBar` | 全局底栏（SYSTEM STATUS / 分辨率等） |
| `layout.topbar` | 追加 | — | 全局顶栏菜单区（插件按钮/指示器），经 `PluginSlot` 渲染 |
| `menu.entry` | 追加 | — | 主菜单按钮列表之后、页脚之前（自定义渲染的菜单入口） |
| `page.settings.aside` | 追加 | — | 设置页左侧栏（插件内容卡片），经 `PluginSlot` 渲染 |
| `page.lobby.aside` | 追加 | — | 在线大厅 `.lobby-grid` 内（三列网格；用 `grid-column:1/-1` 可占满整行） |

- **替换型**（`layout.footer`）：宿主用 `<ReplaceableComponent Key="…"/>` 渲染，`Resolve` 只取一个类型，
  来源优先级 = `[Components]` ini → 插件 `ReplaceSlot` → 内置默认。
- **追加型**（其余）：宿主用 `<PluginSlot Key="…"/>` 渲染，
  按插件加载顺序渲染**全部** `ContributeSlot` 贡献（无贡献时输出为空，不影响布局）。
  槽位容器 `.plugin-slot` 为 `display:contents`，插件组件根元素直接参与父容器布局。

## 相关：启动闪屏

`[LoadingScreen]` 节可配置启动闪屏（图片/视频/品牌画面/进度条/版本号），详见
`docs/splash-screen.md`。放同一 ini 的原因：F3 统一一处维护 UI 外观。

## 完整示例

```ini
[UI]
DefaultTheme=iron
HiddenMenus=stats,credits

[Themes]
iron=Themes/iron

[Components]
layout.footer=BlazorCncnetClient.Components.Shared.FooterBar

[Plugins]
hello-plugin=Plugins/hello-plugin

[LoadingScreen]
ShowSplash=true
SplashImage=Resources/splash.png
SplashDuration=2.0
```

> 修改 `ClientDefinitions.ini` 后**重启客户端**生效（配置惰性缓存，运行期不热更新）。
