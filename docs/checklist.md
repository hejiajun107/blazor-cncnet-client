# CNCNet 重构 · 需求检查清单（Checklist）

> 配套 `spec.md`。每项均可勾选，作为验收依据。
> 图例：☐ 待办 · ✅ 已完成 · 🚫 已排除（非目标）
> 更新：M4.10 出口条件核对（2026-09-07）。逻辑类库迁移基线 commit `e6e367bbe04c1a0dc1e34a8fed2856ea3ab7e8c4`（M1.5）。

## A. 架构与工程

- ✅ A1 新解决方案包含：`BlazorCncnetClient`（UI）+ 4 个逻辑类库 + `Integration` 适配层（M1.1）
- ✅ A2 所有逻辑类库编译通过，且**不引用** `Rampastring.XNAUI` / `MonoGame` / `Microsoft.Xna.Framework.*` / `System.Windows.Forms` / `System.Drawing.*`（M1.4 grep 基线核对；B6 类型替换完成）
- ✅ A3 逻辑层通过 `IDispatcher` 与 UI 交互，无直接 Blazor/WebView 类型依赖（M1.7/1.8，30 处 AddCallback → dispatcher.Post）
- ✅ A4 UI 主工程（`BlazorCncnetClient`）在 InfiniFrame + BlazorWebView 下正常启动并加载逻辑服务（M2.1/2.2；EnvironmentCheck 无 UI 初始化验证）
- ✅ A5 单一进程模型；启动游戏时宿主窗口隐藏/最小化，游戏退出后恢复（M3.7/M4.5/M4.6 各 GameLaunchService）
- ☐ A6 Windows 首发验收通过；Linux 保持编译通过（可选）——**待运行验收**（各里程碑 acceptance 已备，需真机联机/单机走查；Linux 逻辑类库 `-r linux-x64` 编译已确认 0 错误 2026-09-09；走查单：`docs/acceptance-run.md` §1/§4）
- ✅ A7 **离线资源**：所有第三方 CSS/JS 库、字体均本地引入 `wwwroot`，代码中无任何 CDN 引用（M2.6.1/M5.2.1 核对）
- ✅ A8 **协作约定**：逻辑层改动先给改动清单，重大设计先讨论方案再落地（全程遵守）

## B. 逻辑层复用（去 XNA 化）

- ✅ B1 `CnCNetClient.Core`：INI 解析/继承、`ClientConfiguration`、设置体系、i18n、缓存、`ProcessLauncher`、`ProfanityFilter` 全部可用（M1.1/1.10/1.13）
- ✅ B2 `CnCNetClient.Domain`：Map/MapLoader/GameModeMap/CnCNetGame/TunnelHandler/PlayerInfo/CustomMapCache/Mission/SavedGame/DirectDraw 检查与包装/Discord 全部可用（M1.1；Mission/SavedGame 战役用）
- ✅ B3 `CnCNetClient.Online`：CnCNetManager/Connection/Channel/IRCUser/PrivateMessageHandler/FileHashCalculator 全部可用，可无 UI 连接 IRC（M1.1/M3 在线闭环）
- ✅ B4 `CnCNetClient.Updater`：版本检查/LZMA 下载/解压可用（M4.3）
- ✅ B5 `GameProcessLogic` 可用，启动参数（地图/端口/规则）与原生一致（M3.7.1）
- ✅ B6 XNA 类型（Color/Rectangle/Point/Vector2）替换为 ImageSharp 类型（`Rgba32`/`Rectangle`/`Point`/`Size`，int 语义）且行为等价；IRC 颜色表映射为 `Rgba32` 常量表；**无 System.Drawing 引用**（M1.6）
- ✅ B6.1 位图绘制统一走 ImageSharp（与地图预览共用同一图像依赖），Linux 下可运行（M1.6.1）
- ✅ B6.2 地图预览生成性能达标：限制并行度（`MaxDegreeOfParallelism`）后不占满 CPU，游戏启动/更新下载不卡顿（M1.6.1；待运行验证）
- ✅ B7 聊天颜色/IRC 颜色表迁移完成（M1.9）
- ✅ B8 记录逻辑复制的上游 commit 基线（M1.5）

## C. 线程模型

- ✅ C1 `IDispatcher.Post/InvokeAsync` 实现并注入 `CnCNetManager` 等（M1.7；InfiniFrameDispatcher 包装 Blazor 同步上下文）
- ✅ C2 原所有 `WindowManager.AddCallback` 调用点已替换，无遗漏（M1.8 grep 基线对比）
- ✅ C3 DEBUG 下断言"UI 状态仅在调度器线程变更"，跑通聊天/大厅无跨线程异常（2026-09-09：`InfiniFrameDispatcher.AssertUiThread`（DEBUG 断言，Initialize 记录调度器线程）+ `GlobalExceptionHandler.ClassifyCrossThreadViolation`（Blazor「not associated with the Dispatcher」异常单独标记 C3 违规）；聊天/大厅已经 ComponentBase.InvokeAsync 调度；真机走查：DEBUG 包下 logs 无「C3 跨线程 UI 变更违规」即通过，见 `docs/acceptance-run.md` §3.1）
- ✅ C4 高频事件（聊天/房间/玩家列表）无卡顿（事件合并/节流生效，M4.9 66ms 渲染节流 + Virtualize）

## D. UI 层（全新，完整功能）

### D1 框架与主题
- ✅ D1.1 Router + MainLayout 导航结构就位，覆盖 §5.2 全部路由（M2.4；/menu /online/lobby /online/game /online/loading /lan/lobby /lan/game /skirmish /skirmish/loading /campaign /campaign/loading /stats / 环境检测）
- ✅ D1.2 控件零封装：无"Blazor 版 XNA 控件"封装层
- ✅ D1.3 主题系统：design tokens（CSS 变量）+ `IThemeProvider`，运行时切换生效；动效时长/缓动同为 tokens（M2.5/2.6.2）
- ✅ D1.4 风格阶段：3 套**带动效演示**静态 HTML 原型（theme-a 浅色 / theme-b 星际2科幻 / theme-d 融合），选定 **theme-d 青单色 HUD**（IRON TIDE 参考）落地（M2.6）
- ✅ D1.5 LoadingScreen / TopBar / MessageBox / Tooltip 通用组件（含对应动效）（M2.7；MessageBox/TopBar/Tooltip + 各 loading 页）
- ✅ D1.6 地图预览以 base64 `<img>` 展示（复用 FastMapPreviewExtractor + 缓存，M3.4 `MapThumbnail`/`MapPreviewService`）
- ✅ D1.7 **交互动效达标**：hover/active/focus、页面切换、列表增删、弹窗、侧栏、通知、加载、表单错误等均有自然反馈（M5.1.1 逐项核对 + `docs/acceptance-m5.1.md` 验收；时长/缓动 token 统一、reduced-motion 生效）
- ✅ D1.8 **动效性能**：仅 `transform`/`opacity`；`prefers-reduced-motion` 降级生效；高频列表 60fps 流畅（M4.9 验收核对）
- ✅ D1.9 无 CDN：`wwwroot` 本地化资源（CSS/JS/字体/图标）全部通过本地路径加载（M2.6.1）

### D2 在线功能
- ✅ D2.1 登录 Modal（昵称 + 记住我/持久模式/自动连接，无密码）→ CnCNet 连接成功（M3.1）
- ✅ D2.2 大厅：频道列表 + 聊天 + 房间列表 + 玩家列表 + 右键菜单（M3.2/3.3）
- ✅ D2.3 房间：建/加入、队伍/地图/规则配置、出生点映射、游戏信息面板（M3.5/3.6）
- ✅ D2.4 游戏加载等待页 + 游戏启动（M3.7）
- ✅ D2.5 私聊多标签 + 消息历史（M3.8）
- ✅ D2.6 邀请、踢人/禁言等频道指令（复用 CommandHandler 模式）（M3.9）
- ✅ D2.7 连接状态显示与断线重连提示（M3.10）

### D3 LAN 功能
- ✅ D3.1 LAN 大厅广播/发现（M4.4.1-4.4.3，acceptance-m4.4 D3.1）
- ☐ D3.2 LAN 建/加入房间 + 游戏配置 + 启动——**代码完成**（M4.4.4/4.4.5，`/lan/game` 页面已实现），**双端端到端验收待做**（含加载存档局；走查单：`docs/acceptance-run.md` §2）

### D4 单机/战役
- ✅ D4.1 遭遇战（Skirmish）入口与配置（M4.5，含 G3/G5/G6/G7/G8/G9 补全；G8 收藏地图、G9 地图排名 2026-09-09）
- ✅ D4.2 战役/任务选择（含 BattleFS 解析）（M4.6）
- ✅ D4.3 附加窗口（地图编辑器启动、关于、Credits）（M4.7）

### D5 设置
- ✅ D5.1 显示/音频/游戏/组件/更新/CnCNet/隐私 各设置面板（M4.1）
- ✅ D5.2 设置读写 settings INI（RA2/YR=RA2MD.ini，原版 DTACnCNetClient.ini 同体系）（M4.1.1）
- ✅ D5.3 语言切换（M4.2）与主题切换（M2.5/2.5.1）

### D6 更新
- ✅ D6.1 启动更新检查、手动更新查询、更新下载与解压（M4.3.1-4.3.5）
- ✅ D6.2 新 exe 名 + 单一二进制目录 + 更新清单端到端演练（M4.3.6，acceptance-m4.3 D6.1/D6.2）

### D7 统计
- ✅ D7.1 战绩/统计窗口（复用 Statistics，M4.8）

## E. 多语言

- ✅ E1 UI 文案走 Resx + `IStringLocalizer`，运行时切 Culture 生效（M4.2.1/4.2.2；`UIStrings` Resx 桥接，UI 键隔离）
- ✅ E2 原 INI `Translation` 机制保留，负责游戏文件/主题翻译（M4.2）
- ✅ E3 `ITranslationService` 桥接 `.L10N()` 调用（UI 键→Resx、游戏键→INI），两套 key 空间隔离（M4.2.1）
- ✅ E4 至少中/英两套 UI 文案验证切换（M4.2.4 中文优先默认 + 设置页语言下拉切换，中/英两套 Resx + 游戏翻译目录）

## F. MOD 可定制（附加）

- ✅ F1 主题目录机制：MOD 可通过 `[Themes]` + 主题目录整体换肤（M5.1.1/5.5：`ThemeProvider.LoadExternalThemes` 读 `[Themes]`；目录含 `theme.css` 覆盖 design tokens + 可选 `theme.ini`；`url()` 相对资源经 `GameContentServer` 伺服；内置主题重名忽略；顶栏切换按钮自动出现）
- ✅ F2 关键页面组件可替换（组件拆分 + 槽位覆盖能力）（5.5：`ReplaceableComponentService` 注册表 + `ReplaceableComponent.razor` 槽位渲染；`MainLayout` 底栏走 `layout.footer` 槽位，默认 `FooterBar`；`[Components]` 节 key=程序集内组件类名覆盖）
- ✅ F3 `ClientDefinitions.ini` 扩展 UI 相关配置项生效（5.5：`UiDefinitionService` 懒读取 `[UI]` 节——`DefaultTheme`/`HiddenMenus`/`GetString`/`GetBool`；主题初始回退链与隐藏入口已接线）
- ✅ F4 插件机制（阶段 0 + 阶段 1，2026-10-15 落地）：`[Plugins]` 声明启用外部插件目录（`Plugins/<id>/plugin.ini` 自描述）。阶段 0 纯资源插件（仅 `plugin.css`/`plugin.js`，无需编译，不校验 ApiVersion）；阶段 1 程序集插件（`Assembly.LoadFrom` 落**默认 ALC** 与宿主共享类型身份；三阶段 Discover/RegisterServices/InitializeAll 各自容错、单插件失败只禁用自身；契约版本前置校验；私有依赖 `AssemblyResolve` 兜底）。扩展面：5 个槽位（`layout.footer` 替换 + `layout.topbar`/`menu.entry`/`page.settings.aside`/`page.lobby.aside` 追加）、`Router.AdditionalAssemblies` 新增页面、`MainMenu` 合并插件菜单项、插件自有/宿主服务注入。契约项目 `CnCNetClient.Plugin.Abstractions`（`PluginApi.CurrentVersion=1`）；示例 `samples/HelloPlugin`（RCL）；文档 `plugin-dev.md`；SDK 打包 `publish.ps1 -PackPluginSdk`；验收单 `acceptance-f4.md`（**已执行 2026-10-15/16：T0–T8 全部通过**，含日志原文与人工确认的 6 项界面表现；验收中顺带修复 3 处问题——**Build 前日志全部丢失**（`AddSerilog(config=>…)` 延迟创建致静态 `Log.Logger` 在 Build 后才赋值，改显式 `CreateLogger` + `AddSerilog(instance)`，**启动最早期诊断能力整体恢复**）、**无插件环境每次启动假告警**（`IniFile.GetSectionKeys` 对不存在的节返回 `null` 致 NRE 被吞，改判空静默返回）、**`Id` 缺失时日志空括号**（构造时以 ini key 作 `Id` 默认值））。样例集 `samples/README.md`（程序集样例 `HelloPlugin` + 纯资源样例 `ThemeDemo`，**均不在主解决方案、不随发布**）。**不做热插拔/签名/沙箱**（决策：本地目录即信任）；阶段 2（插件市场）另行排期
- ✅ F5 许可证合规（A+B，2026-10-16 落地）：`LICENSE`（GPLv3 全文）+ `NOTICE.md`（本项目 GPLv3 声明、上游 **CnCNet/xna-cncnet-client** 衍生声明与 `Copyright (C) 2013-2025 CnCNet, Rampastring` 署名、GPLv3 §5 修改声明、字体 OFL 指引）+ `THIRD-PARTY-NOTICES.md`（全部依赖许可证实测汇总：InfiniFrame/Serilog=Apache-2.0、ImageSharp=Six Labors Split License、OpenMcdf=MPL-2.0、Ude=MPL-1.1/GPL-2.0/LGPL-2.1 三重许可、WebApi.Client=.NET EULA、Newtonsoft/Discord/lzo/xunit=MIT、pixi.min.js=MIT、原生 DLL 清单，附 nuspec 复核命令）均置于**仓库根（git 仓库 = 内层 `blazor-cncnet-client/`）**；`wwwroot/fonts/OFL.txt`（OFL-1.1 全文 + 8 个字体 name 表版权行，经 fontTools 实测提取）；`publish.ps1` 打包时把 3 个许可文件复制进 `dist/publish/ClientApp/`（安装根顶层仍仅 `Launcher.exe`）并纳入 `$required` 校验；契约 `PackageLicenseExpression=GPL-3.0-or-later` + nupkg 内随附 LICENSE/NOTICE/THIRD-PARTY（`dotnet pack` 实测 4 个文件均入包）。**校验全绿**：关键文件 11 项 [OK]、405 文件 / 138625 KB、安装根干净、测试 30/30 通过

## G. 非功能

- ✅ G1 冷启动时间与内存占用可接受（相对原 XNA 客户端不劣化明显）（M4.9：启动无同步阻塞、内存有界）
- ✅ G2 聊天/大厅高频操作 60fps 交互流畅（虚拟化/节流生效）（M4.9：66ms 渲染节流 + Virtualize + OnlineCount 缓存）
- ✅ G2.1 动效无卡顿、不生硬：交互反馈时长/缓动统一（spec §5.5）；`prefers-reduced-motion` 生效（M4.9 核对）
- 🚫 G2.2 星际2科幻风独立主题——**已裁决排除**（2026-09-07）：theme-b 在选型（M2.6）中被 theme-d 融合版（IRON TIDE 青单色 HUD，含星际2科幻元素）替代并落地，不再补做独立主题
- ✅ G2.3 第三方库/字体本地化清单核对通过（无 CDN 引用）（M2.6.1）
- ✅ G3 异常处理：崩溃日志（Serilog）定位；`DisplayErrorAction` 语义迁移（M2.8）
- ✅ G4 数据安全：密码/令牌不落明文日志（M4.10 修复：`Connection.cs:310` 原始 IRC 消息日志经 `SanitizeForLog` 脱敏——`PASS`/`JOIN +密码`/`MODE +k` 参数隐藏，保留其余调试信息；无其他明文凭据日志点）
- ✅ G5 设置持久化（spec §6.10）：settings INI 读写（RA2/YR=ra2md.ini，可被 MOD 覆盖）、`UserDefaults.ini` 合并、变更即存 + 退出兜底（ApplicationExiting）、`FinalSun.ini` ANSI、spawn ini 生成、主题持久化写 settings INI `[MultiPlayer] Theme`（M4.1.1）

## 排除项（非目标，明确不做）

- 🚫 不迁移 XNAUI 控件到 Blazor
- 🚫 不做浏览器/WASM 模式
- 🚫 不保留 XNA/DX/GL 多引擎构建矩阵
- 🚫 不内嵌游戏本体渲染

---

## M4.10 出口条件 · 剩余工作清单

| 项 | 内容 | 归属 |
|----|------|------|
| A6 | Windows 首发运行验收（各 acceptance 真机走查；Linux 编译可选） | 运行验收 |
| C3 | DEBUG 线程断言（可选增强）或运行验收确认无跨线程异常 | 运行验收 |
| D1.7 | 动效对照 spec §5.5 逐项验收 | M5.1 |
| D3.2 | LAN 双端端到端验收（含加载存档局） | 运行验收 |
| F1-F3 | MOD 可定制（主题目录/组件替换/ClientDefinitions 扩展） | M5 打磨（附加） |

> 已闭环：G4 密码日志脱敏（M4.10 修复，构建 0 错误）；G2.2 星际2主题已裁决排除（theme-d 覆盖）；F1-F3 MOD 可定制（M5 打磨，5.5 落地）。
> 出口条件判定：剩余 A6/D3.2 为**运行验收**、D1.7 为打磨验收，完成后可达 checklist 全勾。
> 真机走查清单已备：**`docs/acceptance-run.md`**（A6 首发 + D3.2 LAN 双端 + C3 + D1.7 抽查），发布后按单勾选。
