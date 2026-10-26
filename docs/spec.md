# CNCNet 客户端 → InfiniFrame + Blazor 重构 · 规格说明书（Spec）

> 版本：v0.8（草案，按评审反馈打磨）
> 状态：待评审
> 变更记录：v0.3 — 主题定稿「幽能星港（theme-d）」并固化 design tokens / 字体 / 内页蓝色系规范（§5.4）；补充科幻风动效清单（§5.5）。
> v0.4 — 主菜单按 IRON TIDE 参考重构：配色收敛为**青色单色调 HUD**、视觉核心改为 **canvas 全息地球**、新增 **HUD 读数面板**、按钮改**斜切卡片式**（§5.4/§5.5 同步更新）。
> v0.5 — 补充持久化体系（§6.10）：settings INI（RA2/YR=RA2MD.ini）读写、UserDefaults 合并、变更即存 + 退出兜底（ApplicationExiting）、主题持久化决策（写 settings INI）；修订 §5.3 设置数据源、§5.4.6 IThemeProvider 持久化；配套 tasks（4.1.1）/ checklist（G5）。调研依据见 `docs/persistence.md`。
> v0.6 — 登录页定稿（§5.3.1）：**浮窗 Modal**（非独立路由）、**仅昵称无密码**（CnCNet 登录=IRC 昵称连接，注册/密码走论坛侧）、登录设置**等 M4 4.1.1** 接线 settings INI（M3 期间内存+localStorage 过渡）；修订 §5.2 导航树（移除 `/online/login` 路由）与 §5.3 `Login` 数据源（`CnCNetManager.Connect` + `NameValidator`）。调研依据 `docs/login-page.md`。
> v0.7 — 新增**附录 C：从原 CNCNet 客户端迁移指南**（§C.1–C.6）：面向用户的迁移数据、首次启动流程、FAQ 与回退方案；最终文档随 M5 发布为 `docs/migration-guide.md`。
> v0.8 — 扩展 §6.9 为**可定制性与个性化 UI 总纲**：分层定制模型（L1 令牌覆盖 / L2 主题目录 / L3 组件槽位 / L4 外部组件）、扩展性设计原则（§6.9.2）、用户个性化目录（L1，§6.9.3）；新增**附录 D：INI → HTML/CSS 迁移 Skill**（MOD/主题平滑过渡的辅助技能规格，§D.1–D.4）。评审结论：**迁移 Skill 确认落地；L1 用户个性化（§6.9.3）待定**——仅保留设计预留，不进入当前里程碑。落地文档见 `docs/mod-customization.md`。
> 关联代码：原逻辑源 `f:\work\PhotinoCLient\CncnetClient\xna-cncnet-client`；新工程 `f:\work\PhotinoCLient\blazor-cncnet-client`

---

## 0. 协作约定

- 双方均具备技术背景：**方案先行，讨论后落地**；重大结构/设计决策先列方案再动手。
- **不擅自大规模重构**：单次改动聚焦单一主题；逻辑层改动前先给改动清单。
- 涉及原逻辑代码的取舍（复用/改写/删除）先标注再执行，保持可追溯。
- 版本锁定：InfiniFrame 0.60、net10.0（备用 net8.0），升级需评估影响。

---

## 1. 项目背景与目标

### 1.1 背景

原 CNCNet 客户端（xna-cncnet-client）基于 XNA/MonoGame 渲染 + 自研 Rampastring.XNAUI 控件框架：UI 层与渲染框架深度耦合（约 4.4 万行 UI/框架代码），多引擎条件编译（XNA/DX/GL）与 net48/net8 双目标造成构建复杂。新方案采用 **InfiniFrame（Photino 现代重制版，Blazor Hybrid）+ Blazor**：.NET 逻辑在宿主进程内直接运行，UI 层用 Web 技术栈。

### 1.2 目标

1. **功能完整复刻**：覆盖原 CNCNet 客户端全部功能（在线大厅、LAN、游戏启动、设置、自动更新、私聊、战役/单机、统计），不改动游戏本体。
2. **全新现代化 UI + 强动效**：UI 全新设计，**交互动效丰富、反馈不生硬**（见 §5.5）。
3. **多主题切换**：至少包含一套**星际2风格科幻主题**（见 §5.4）。
4. **逻辑层复用**：原 ClientCore/Domain/Online/ClientUpdater 源码**复制到新类库独立维护**，去除 XNA/WinForms 依赖。
5. **控件零封装**：直接使用 Web 技术栈。
6. **可扩展性（附加）**：其他 MOD 可定制 UI / 多语言。

### 1.3 非目标

- 浏览器/WASM 模式（本方案为桌面 Blazor Hybrid）。
- 游戏内渲染（游戏本体外部进程启动）。
- 保留 XNA 多引擎构建矩阵。
- 迁移 XNAUI 控件到 Blazor。

### 1.4 验收基线

首个版本以 **CnCNet 在线大厅闭环 + 完整功能**为验收：登录 → 频道聊天 → 房间列表 → 建/加入 → 配置 → 启动游戏；覆盖设置、自动更新、私聊、LAN、单机/战役入口。

---

## 2. 技术选型与约束

| 项 | 选型 | 说明 |
|---|---|---|
| 宿主框架 | `InfiniLore.InfiniFrame` 0.60 + `InfiniFrame.BlazorWebView` | Blazor Hybrid，进程内，无 IPC 开销 |
| 目标框架 | `net10.0`（现骨架） | 兼容问题回退 `net8.0` |
| 日志 | Serilog | 现骨架已接入 |
| 设置持久化 | Rampastring.Tools `IniFile`（settings INI）+ localStorage（UI 外观回显）+ JSON/文本（好友列表等） | 见 §6.10 |
| 控件 | 不封装，直接用 Web 技术栈 | CSS 框架按主题阶段定 |
| 动效 | 优先 CSS transitions/keyframes + Web Animations API | 交互动效见 §5.5；若引入动画库须本地引入 |
| 多语言 | UI 用 Resx + `IStringLocalizer`；游戏内容沿用原 INI 翻译 | 见 §6.8 |
| 逻辑复用 | 复制源码到新类库独立维护 | 见 §4 |
| 平台 | 首发 Windows（WebView2）；Linux 可选 | 游戏本体外部进程/wine |

**硬性约束**

- **离线资源**：所有第三方 CSS/JS 库、字体**必须下载到本地 `wwwroot` 引用，禁止 CDN**（BlazorWebView 为离线 `app://` 环境）。字体注意授权（尤其星际2科幻风如需特殊字体）。
- 逻辑层类库**不得引用** `Rampastring.XNAUI` / `MonoGame` / `Microsoft.Xna.Framework.*` / `System.Windows.Forms`。
- **禁用 `System.Drawing.*`**（Linux 已停止维护、Windows-only）：XNA 类型替身统一用 `ImageSharp` 基础类型（`Rgba32`/`Point`/`Rectangle`/`Size`）+ IRC 颜色常量表，绘制亦统一 ImageSharp（本地 vendoring）。若未来出现矢量/canvas 绘制需求再评估引入 `SkiaSharp`（当前不引）。
- 跨层线程交互统一走 `IDispatcher`；逻辑层不持有 Blazor/WebView 类型。
- **动效性能**：只动 `transform`/`opacity`，尊重 `prefers-reduced-motion`。

---

## 3. 架构总览

```
┌─────────────────────────────────────────────────────┐
│ InfiniFrame 原生窗口（WebView2 / WKWebView / WebKit2GTK）│
│  ┌───────────────────────────┐ ┌─────────────────┐  │
│  │ Blazor UI（Razor，全新重写）│ │ Dispatcher/JS桥 │  │
│  │  + 主题系统 + 动效          │ │                 │  │
│  └───────────────────────────┘ └─────────────────┘  │
└──────────────────────┬──────────────────────────────┘
                       │ DI 注入 / 事件
┌──────────────────────▼──────────────────────────────┐
│ 逻辑层（复用类库，去 XNA 化）                            │
│  CnCNetClient.Core │ Domain │ Online │ Updater      │
└───────┬────────────────────────────┬────────────────┘
        │ Process.Start               │ TCP IRC
┌───────▼──────────┐       ┌──────────▼──────────┐
│ 游戏进程（外部）  │       │ CnCNet 服务器（IRC/隧道）│
└──────────────────┘       └─────────────────────┘
```

### 3.1 进程模型

单进程桌面应用。启动游戏时宿主窗口最小化/隐藏，游戏退出后恢复前台。

### 3.2 目标解决方案目录结构

```
blazor-cncnet-client/
├─ blazor-cncnet-client/
│  └─ src/
│     ├─ BlazorCncnetClient/            # UI 主工程（已建）
│     │  └─ wwwroot/                    # 全部本地静态资源（CSS/JS/字体/图标，无 CDN）
│     ├─ CnCNetClient.Core/             # 新类库：ClientCore 复制 + 去 XNA 化
│     ├─ CnCNetClient.Domain/           # 新类库：DXMainClient/Domain 复制 + 去 XNA 化
│     ├─ CnCNetClient.Online/           # 新类库：DXMainClient/Online 复制 + 去 XNA 化
│     ├─ CnCNetClient.Updater/          # 新类库：ClientUpdater 复制 + 去 XNA 化
│     └─ CnCNetClient.Integration/      # 适配层：IDispatcher、XNA 替身、路径、翻译桥
└─ docs/
   ├─ spec.md / checklist.md / tasks.md
```

---

## 4. 逻辑层复用清单（文件级，细化到可执行）

### 4.1 模块归属

| 原工程/目录 | 新类库 | 处理方式 |
|---|---|---|
| `ClientCore/` | `CnCNetClient.Core` | 直接复制；`Rampastring.Tools` 改为 NuGet 等价物（`IniFileParser` 等）或复制其 IniFile 源码 |
| `DXMainClient/Domain/**`（非 DXGUI） | `CnCNetClient.Domain` | 复制；替换 XNA 类型；Discord 用 `DiscordRichPresence` |
| `DXMainClient/Online/**` | `CnCNetClient.Online` | 复制；`CnCNetManager` 构造器 `WindowManager`→`IDispatcher` |
| `ClientUpdater/` | `CnCNetClient.Updater` | 复制；适配新 exe 名与单目录结构 |
| `ClientGUI/GameProcessLogic.cs` | `CnCNetClient.Integration`（或 Domain） | 复制，纯 .NET 直接复用 |
| `DXMainClient/DXGUI/**` | 不迁移 | Blazor 页面替代；**夹带的业务逻辑**先抽取到 Domain/Online |

### 4.2 去 XNA 化处理清单（逐项）

**A. 类型替身（`CnCNetClient.Integration/XnaCompat`）**

> 决策：**禁用 `System.Drawing.*`**（Linux 已停止维护）。XNA 类型替身统一用 **ImageSharp** 基础类型（单一图像依赖：类型替身与地图预览共用；纯托管无原生依赖、SIMD 加速）。颜色用 `Rgba32` + 一张 IRC 颜色常量表；未来若出现矢量绘制需求再评估 `SkiaSharp`。

- [x] `Color` → `ImageSharp` `Rgba32`（byte 分量）+ 颜色常量表；原 XNA 预定义命名色/IRC 颜色表映射为常量表（约 15 条，一次性）（M1.6，ColorConstants）
- [x] `Rectangle` / `Point` / `Size` → `ImageSharp` `Rectangle`/`Point`/`Size`（int 语义，含 X/Y/Width/Height、`Contains` 等，与 XNA 行为等价）（M1.6）
- [x] 聊天/IRC 颜色表 → `Rgba32` 常量表（`#AARRGGBB` → `Rgba32` 一次转换，集中一处）（M1.6/1.9，IRCColor + ColorConstants）
- [x] 位图绘制/地图预览 → ImageSharp 输出 PNG 字节 → `<img>`（本地 vendoring）（M1.6.1，MapPreviewExtractor/MapPreviewService）

**B. 线程桥**
- [x] `IDispatcher` 接口（`Post` / `InvokeAsync`）（M1.7）
- [x] `CnCNetManager`、`PrivateMessageHandler` 等构造器注入 `IDispatcher`（M1.7）
- [x] 全部 `WindowManager.AddCallback` 调用点替换（以 grep 基线核对零遗漏）（M1.8）

**C. 资源/路径**
- [x] `ProgramConstants`（GetResourcePath/GetBaseResourcePath/GamePath）、`SafePath` 统一到 Core 或 Integration（M1.11）
- [x] 资源搜索路径逻辑（原 AssetSearchPaths）改为 Integration 提供的 `IAssetPathProvider`（M1.11）

**D. UI 类型引用清除（编译验证）**
- [x] 每个类库编译通过且 `grep` 无 `Microsoft.Xna` / `Rampastring.XNAUI` / `System.Windows.Forms` 引用（M1.4/1.13）
- [x] `ClientConfiguration`/`MainClientConstants` 等初始化在无 UI 下可执行（M1.13，EnvironmentCheck 无 UI 初始化验证）

### 4.3 从 DXGUI 抽取业务逻辑（需逐窗口盘点）

> 原则：先盘点每文件"纯 UI / 业务"占比，业务逻辑下沉到 Domain/Online 服务 + 单测，再写 Blazor 页面。

| 原文件 | 需抽取的业务逻辑 | 落点 |
|---|---|---|
| `CnCNetLobby.cs` | 游戏列表过滤/排序、房间创建参数、CTCP 命令（邀请）处理 | `CnCNetGameManager` 服务 |
| `CnCNetGameLobby.cs` | 房间配置（地图/规则/队伍）、Ready/开始逻辑、Spawn 参数生成 | `CnCNetGameManager` 服务 |
| `GameInformationPanel.cs` | 地图信息聚合、预览纹理请求 | `MapPreviewService` |
| `ChatListBox` 命令 | `/help /me /color` 等聊天命令解析 | `ChatCommandService` |
| `GameLobbyBase` 等 | 玩家位管理、地图切换、规则校验 | `GameLobbyService` |
| `OptionsWindow` 面板 | 设置读写、应用逻辑 | `SettingsService`（复用 Settings 体系；`UserINISettings.SaveSettings()` 变更即存，见 §6.10） |

---

## 5. UI 层设计（全新）

### 5.1 设计原则

- **控件零封装**：按钮/输入/下拉/列表直接用 HTML/CSS/组件库，不建"Blazor 版 XNA 控件"。
- **动效优先**：交互反馈必须自然、有动感（见 §5.5）。
- **全新视觉**：不沿用原 C&C 皮肤；**科幻主题已定稿「幽能星港（theme-d）」**（§5.4）。
- **配色分层**：主菜单偏「青单色 HUD 终端」（青×近黑，金仅点缀）；**内页偏蓝色系**（蓝×青为主，紫/金仅点缀，§5.4.3）。
- **主题可切换**：design tokens（CSS 变量）+ `IThemeProvider`，运行时切换。
- **静态原型先行**：风格阶段产出带动效演示的静态 HTML，用户选定后固化（已定稿 `docs/mockups/theme-d-fusion.html`）。

### 5.2 导航结构

```
App.razor (Router) ── MainLayout（侧栏/顶栏 + 内容区）
├─ /                     主菜单（单机/战役、联机、设置、更新、统计、退出）
├─ /online/login         登录
├─ /online/lobby         CnCNet 大厅（频道聊天 + 房间列表 + 玩家列表）
├─ /online/game          CnCNet 游戏房间（配置/加入）
├─ /online/loading       游戏加载等待
├─ /lan/lobby            LAN 大厅
├─ /lan/game             LAN 房间
├─ /skirmish             遭遇战
├─ /campaign             战役/任务
├─ /settings             设置中心（多 Tab）
├─ /private-messages     私聊
└─ /extras               附加（地图编辑器、更新、关于）
```

### 5.3 页面规格（可执行级：状态 / 交互与动效 / 数据源）

> 约定：每页组件在 `OnInitializedAsync` 订阅服务事件、`IDisposable` 退订；状态更新统一 `IDispatcher` 回 UI 线程。动效标注缩写：T=transition、K=keyframes、WA=Web Animations API（实现见 §5.5）。

#### 通用组件
| 组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `LoadingScreen` | 启动中→就绪 | 启动屏渐隐（K fade-out 400ms）；进度条计数动画 | `ClientConfiguration` |
| `TopBar` | 连接状态/玩家/时间 | 连接状态脉冲呼吸灯（K）；时间滚动更新 | `CnCNetManager` |
| `MessageBox` | 打开/关闭 | 出现 scale+fade（K 240ms ease-out-back）；关闭 fade（120ms） | 全局弹窗服务 |
| `Tooltip` | 显示/隐藏 | 淡入+微上移（T 120ms）；延迟 400ms | 无 |

#### 主菜单
| 组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `MainMenu` | 主菜单项列表 | 菜单项 hover 微缩放+描边发光（T≤120ms）；进入子页 fade-slide（T 240ms） | `ClientConfiguration`、`Updater` |
| `NewsPanel`（可选） | 加载/展示 | 卡片入场 stagger fade-up（K） | 网络新闻（可选） |

#### 在线（CnCNet）
| 页面/组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `LoginModal`（浮窗） | 未连接/连接中/失败 | 输入框 focus 高亮描边（T）；Connect loading 态（spinner+禁用）；昵称非法 shake 提示（K 300ms）；失败 toast + 关 Modal 回主菜单 | `CnCNetManager.Connect`、`NameValidator`（详情见 §5.3.1） |
| `Lobby`（频道聊天+房间+玩家） | 连接中/在线 | 频道切换淡入（T 200ms）；新聊天消息滑入（K slide-in-up 200ms）；房间/玩家进出动画（入场 fade-in、出场 fade+高度折叠）；右键菜单 scale+fade | `CnCNetManager`、`GameCollection`、`TunnelHandler`、`CnCNetUserData` |
| `ChatInput` | 输入/发送 | 发送按钮 press 微缩（T 80ms）；`/` 命令自动补全浮层 | `ChatCommandService` |
| `GameLobby` | 配置/就绪/开始 | 玩家位变化动画（进出场）；地图切换预览 fade；Ready 状态徽章脉冲 | `CnCNetGameManager`、`MapLoader`、`GameModeMap` |
| `GameLobby.MapPreview` | 加载/就绪 | 预览图加载占位 shimmer → 淡入（T 200ms）；出生点 hover 高亮（T） | `MapPreviewService`（base64 `<img>`） |
| `GameLobby.TeamStartMappingPanel` | 编辑中 | 槽位拖拽/点击反馈（T）；锁定状态动画 | `CnCNetGameManager` |
| `GameLoadingLobby` | 等待开始 | 加载进度条计数动画；进场成员列表更新 | `CnCNetManager` |
| `PrivateMessaging` | 会话列表/聊天 | 标签切换 slide（T 200ms）；新消息提醒徽章跳动（K） | `PrivateMessageHandler` |

#### 在线登录 Modal（M3.1）

> 依据调研 `docs/login-page.md`。**无密码认证** —— CnCNet 登录 = 用昵称连接 IRC（`CnCNetManager.Connect`），昵称即身份；昵称注册/密码保护走 CnCNet 论坛侧，客户端不处理。

| 项 | 规格 |
|---|---|
| 形态 | **浮窗 Modal**（复用 `MessageBox` 宿主机制扩展，非独立路由）；主菜单「多人游戏」触发 |
| 字段 | 玩家名输入框（最大长度=`ClientConfiguration.MaxNameLength`，禁 IME）；复选框：**Remember me** / **Stay connected outside of the CnCNet lobby** / **Auto connect**（后者仅当前两者同勾选时可选中） |
| 流程 | 校验 `NameValidator.IsNameValid` → 设 `ProgramConstants.PLAYERNAME` → 保存登录设置（settings INI `[MultiPlayer]`：`Handle`/`SkipConnectDialog`/`PersistentMode`/`AutomaticCnCNetLogin`，**依赖 M4 4.1.1**）→ `CnCNetManager.Connect()` |
| 自动连接 | 进主菜单时若 `SkipConnectDialog` 已勾 → 跳过 Modal 直接 Connect；`AutomaticCnCNetLogin` 启动自动连接 |
| 取消/失败 | Cancel → 关 Modal 回主菜单；连接失败/断开 → toast + 关 Modal 回主菜单（`CnCNetManager.OnDisconnected`/`OnErrorReceived`） |
| 成功 | 关 Modal → 进大厅 `/online/lobby`（M3.2） |
| 动效 | 打开 pop-in（240ms ease-out-back）/ 关闭 fade（120ms）；Connect loading spinner；昵称非法 shake（300ms） |
| 持久化 | **等 M4 4.1.1** 接线 settings INI；M3 期间内存 + localStorage 过渡（`ProgramConstants.PLAYERNAME` 为进程级） |

#### LAN
| 页面/组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `LANLobby` | 广播发现/空闲 | 房间刷新淡入（T）；进出场动画同 Lobby | `LANLobby` 服务 |
| `LANGameLobby` | 配置/开始 | 同在线 GameLobby | `LANGameManager` |

#### 单机/战役
| 页面/组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `SkirmishLobby` | 配置 | 同 GameLobby 交互 | `SkirmishService` |
| `CampaignSelector` | 任务列表 | 卡片 hover 上浮+发光（T）；选择态描边 | `Mission`、`BattleFS` |

#### 设置
| 页面/组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `SettingsWindow`（多 Tab） | 加载/编辑/保存 | Tab 切换滑动条（T 200ms）；滑块/开关即时反馈（T）；保存成功 toast | `SettingsService`（`UserINISettings` → settings INI，RA2/YR 即 `RA2MD.ini`；合并 `UserDefaults.ini`，见 §6.10） |

#### 更新
| 页面/组件 | 状态 | 关键交互与动效 | 数据源 |
|---|---|---|---|
| `UpdateQuery`/`UpdateWindow` | 检查/下载/解压 | 进度条动画；完成淡入新内容 | `Updater` |

### 5.4 主题系统与星际2科幻风（已定稿：幽能星港 theme-d）

> 定稿依据：`docs/mockups/theme-d-fusion.html`（主菜单 16:9 横屏原型，已通过浏览器渲染验证）。
> 风格参考：`RED ALERT · IRON TIDE`（青单色 HUD 主菜单，用户选定）。
> 当前主题定位：**星际2风格高能科幻 HUD**。**主菜单偏"青单色指挥终端"（青 × 近黑，金色极少量点缀），内页偏"蓝色系指挥台"（蓝 × 青为主，紫/金仅点缀）**——两层共用同一套动效与字体体系，仅配色侧重不同。

#### 5.4.1 主题总览

| 维度 | 决策 |
|---|---|
| 主题名 | 幽能星港（theme-d） |
| 氛围 | 深蓝近黑底 + 极光渐变 + 星粒 canvas + 网格 + CRT 扫描线 + 暗角；五层背景叠加 |
| 主菜单布局 | 左：指挥中心视口（canvas 全息地球 + 雷达扫掠 + 四角支架 + HUD 读数面板）；右：斜切角菜单面板（9 个主按钮） |
| 内页布局 | 顶部栏（logo+导航标签+连接状态+玩家+窗口控制）＋内容区（可左导航/中央/右信息三栏）＋底部状态栏 |
| 风格元素 | 斜切角（clip-path）/ 面板扫光线 / 四角支架 / HUD 读数 / 等宽数字（Orbitron）/ 青单色发光；交互动效带"扫掠/扫描/脉冲"反馈 |
| 原型文件 | `docs/mockups/theme-d-fusion.html` + `docs/mockups/fonts/` |

#### 5.4.2 Design Tokens（CSS 变量，`:root` 定义）

**A. 主菜单配色（青单色 HUD）**

> 已收敛为**青色单色调**（原青×紫×金三色已废弃）。金色仅保留两处极点缀：全息地球轨道卫星点、威胁等级读数。

| Token | 值 | 用途 |
|---|---|---|
| `--bg-0` / `--bg-1` | `#02060b` / `#04111c` | 背景基色（深蓝近黑） |
| `--cyan` | `#21e6ff` | 主高亮 / 能量色 / 发光 |
| `--cyan-soft` | `#7af3ff` | 亮青（点云高亮侧 / 文字辉光） |
| `--cyan-deep` | `#0a3a4a` | 深青（极光 / 渐变暗侧） |
| `--gold` | `#ffd34d` | 仅轨道卫星点 / 威胁等级读数（≤2 处） |
| `--text` / `--dim` | `#dff6ff` / `#5f8aa0` | 主文字 / 弱文字（HUD 读数、标签） |
| `--line` | `rgba(33,230,255,.30)` | 分隔线 / 面板边框 / 图标边框 |

**B. 内页配色（蓝色系偏，新增规范）**

> 内页统一以**蓝色为主基调**，与主菜单的青单色 HUD 保持同源（青 × 蓝同一色系），紫色仅作极少量点缀（品牌小标、一次性装饰），金色仅用于警示/编号，占比均 ≤5%。

| Token | 值 | 用途 |
|---|---|---|
| `--bg-0` / `--bg-1` | `#060b1c` / `#0a1428` | 内页背景基色（深蓝） |
| `--panel` | `rgba(10,20,40,.72)` | 面板底色（半透明深蓝） |
| `--blue` | `#3b82f6` | 主色（蓝） |
| `--cyan` | `#2fe0ff` | 高亮 / 链接 / 能量线 |
| `--violet` | `#7c9cf5` | 点缀（≤5%，选中态编号等） |
| `--gold` | `#ffd97a` | 警示 / 编号（≤5%） |
| `--line` | `rgba(80,160,255,.22)` | 面板边框 |
| `--text` / `--text-2` / `--text-3` | `#eaf4ff` / `#9db8e8` / `#5b72a6` | 主 / 次 / 弱文字 |

**C. 通用 tokens**

| Token | 值 | 用途 |
|---|---|---|
| `--ease-out` | `cubic-bezier(.2,.7,.3,1)` | 统一缓动（参考 IRON TIDE 原型值） |
| `--t-fast` | `150ms` | hover / active / focus |
| `--t-page` | `240ms` | 页面/视图切换 |
| `--t-toast` | `240ms` | 通知 |
| 圆角/斜切 | 主要用 `clip-path` 斜切角（面板/按钮/视口统一），面板留 1px 顶扫光线 | 强化棱角感 |

#### 5.4.3 内页蓝色系落地要点

- 内页背景：深蓝渐变 + 细网格（蓝）＋星点（弱化紫色，仅保留极淡蓝紫星云）。
- 面板：深蓝半透明底 + 青→蓝渐变顶部扫光线；沿用主菜单斜切角语言。
- 按钮/列表/输入 focus：蓝/青发光描边（`box-shadow` + `border-color`）。
- 选中态：左能量条蓝→青渐变；编号金（唯一金色常驻点）。
- 进度条/充能条：蓝→青渐变。
- 雷达/装饰：青为主，目标点可金/青。
- **禁止**在内页大面积使用紫或金（避免喧宾夺主）；紫色仅用于品牌小标、一次性装饰图标。

#### 5.4.4 字体规范（本地引入，无 CDN）

| 角色 | 字体 | 本地文件 | 备注 |
|---|---|---|---|
| 标题 `CNCNet` / 编号 / Logo / 数字 | Orbitron（400/500/700/900） | `Orbitron-{400,500,700,900}.woff2` | 几何科技感；可变字重 |
| 英文副标题 / 标签 / 菜单组名 / 状态栏 | Rajdhani（400/500/700） | `Rajdhani-{Regular,Medium,Bold}.ttf` | 硬朗窄体 |
| 中文正文 / 按钮主字 | Noto Sans SC（可变 100–900） | `NotoSansSC.ttf` | 中文；细字重（100–300）+ 宽字距（2–8px）匹配科幻感 |

- 字体统一放 `wwwroot/fonts/`（原型在 `docs/mockups/fonts/`，落地时拷贝）。
- 授权：Orbitron/Rajdhani/Noto Sans SC 均为 OFL（SIL Open Font License 1.1）—— 可打包分发。OFL-1.1 全文与各字体 name 表版权行随字体置于 `wwwroot/fonts/OFL.txt`（见 `checklist.md` F5）。
- 中文**不得**直接指定 Rajdhani（不含中文字形，会静默回退系统字体）；统一走 Noto Sans SC。

#### 5.4.5 组件样式规范（科幻风）

| 组件 | 规范 |
|---|---|
| 主菜单按钮 | 斜切角卡片（clip-path）：左侧图标方块（青色描边，hover 点亮为青底）＋中文主字（Noto Sans SC 700）＋Orbitron 英文小字；右侧 `›` 箭头 hover 浮现；hover 左移 6px + 背后扫光 + 外发光；首个按钮为 primary（青底发光） |
| 全息地球视口 | 左视口：canvas 旋转经纬点云球体 + 金色轨道卫星 + 中心发光徽记（Orbitron）；叠雷达 conic 扫掠 + 四角支架 + 顶部/底部标签；斜切角容器 |
| HUD 读数卡 | 4 列网格：左侧 3px 青色竖条 + Orbitron 大数字（发光）+ 顶部小字标签（Orbitron 大写） |
| 面板 | 深色半透明底（backdrop blur）+ 1px 顶扫光线（sweep 4s）+ 斜切角；菜单面板位于右侧 |
| 导航标签（顶部） | 小字距大写 + 底部渐变发光下划线（hover/active） |
| 状态徽章 | 斜切裁剪 + 呼吸绿点（连接状态） |
| 雷达 | 圆形网格 + 旋转扫光 + 中心波纹 + 闪烁目标点 |
| Toast | 斜切裁剪 + 青色发光边框 + 底部滑入 |
| 顶栏/底栏 | Orbitron 小字 + 分隔点 + 青色高亮；底栏固定 SYSTEM STATUS |

#### 5.4.6 实现

- design tokens 全走 CSS 变量；主题切换 = 替换 `:root` 变量集 + 可选组件样式覆盖。
- `IThemeProvider`：当前主题、加载/切换（含过渡动画）、持久化 —— **主题写 settings INI `[MultiPlayer] Theme`**（原版语义，跨客户端一致），localStorage 仅作启动前即时回显缓存（见 §6.10）。
- MOD 主题：保留原 `[Themes]` 配置（ClientDefinitions.ini），MOD 主题 = 主题目录（CSS 变量 + 可选样式）。
- 动效统一见 §5.5；科幻风专属动效（全息地球/雷达扫掠/扫光线/脉冲）见 §5.5 清单。

### 5.5 UI 动效规范（重点）

**原则**
- 动效服务于反馈与方向感，不炫技；所有交互反馈 ≤300ms，需"有弹性但克制"。
- 只用 `transform`/`opacity`（避免 layout/paint），保证 60fps；尊重 `prefers-reduced-motion`（全局降级为仅透明度或关闭）。
- 统一缓动曲线与时长 tokens（§5.4），不散落硬编码值。

**动效清单（落地要求）**
| 场景 | 动效 | 实现 |
|---|---|---|
| hover / active / focus | 微缩放、阴影、描边/发光过渡 | T ≤120ms，ease-out |
| 页面/视图切换 | 淡入+微上移（fade-slide） | T 240ms，ease-out |
| 列表增删（聊天/房间/玩家） | 入场滑入/淡入；出场淡出+高度折叠 | K/T 200ms，合并同帧批量更新 |
| 弹窗/面板/右键菜单 | scale+fade 出现（微 overshoot，不夸张）、关闭淡出 | K 240ms ease-out-back |
| 侧栏/抽屉 | translateX 滑动 | T 200ms |
| 连接状态 | 脉冲呼吸灯 | K（无限） |
| 加载/进度 | 计数动画、spinner、启动屏渐隐 | T/K |
| 通知/Toast | 滑入 + 自动消失 | T 240ms |
| 数字/进度增量 | 计数过渡 | WA |
| 表单错误 | 轻微 shake + 描边变红 | K 300ms |

**科幻风专属动效（theme-d，落地要求）**

> 这些动效是"幽能星港"的风格基底，性能同样只动 `transform`/`opacity`，且整体尊重 `prefers-reduced-motion`（关闭装饰性无限循环动画）。

| 场景 | 动效 | 实现 |
|---|---|---|
| 全息地球（左视口） | canvas 经纬点云旋转 + 金色轨道卫星公转 + 中心徽记辉光；雷达 conic 扫掠旋转（5s） | Canvas rAF（主）＋CSS（扫掠） |
| 背景氛围 | 星粒 canvas 上浮；极光渐变缓慢漂移缩放（22s alternate）；网格 + CRT 扫描线 + 暗角（静态叠加） | Canvas + CSS K |
| 面板扫光线 | 菜单面板/内页面板顶部 1px 青线左右循环扫过（sweep 4s） | K（无限） |
| 扫描/扫光 | 按钮 hover 背后流光扫过（transform 平移）；雷达旋转扫光 + 目标点闪烁 | K/T |
| 脉冲/呼吸 | 连接状态点、顶栏状态点呼吸（blink 1.4–1.8s） | K（无限） |
| 按钮 hover | 左移 6px + 外发光 + 图标点亮为青底 + `›` 箭头浮现 + 背后扫光 | T ≤200ms |
| HUD 读数 | 数值周期性跳动（原型 2.4s 模拟）；正式接入时用计数过渡（WA） | T/WA |

**实现策略**
- 优先纯 CSS（transitions/keyframes）；需要脚本控制的用 Web Animations API（本地实现，无需库）。
- 如需动画库（如 Motion、GSAP）→ **本地引入 `wwwroot/lib/`，禁 CDN**，且仅用于个别复杂场景。
- 动效与主题解耦：时长/缓动为 tokens，主题可覆盖。
- 装饰性无限循环动效（全息地球/星粒/扫光线/极光）仅在无 `prefers-reduced-motion` 时启用。

---

## 6. 关键机制设计

### 6.1 线程模型（WindowManager → Dispatcher）

```csharp
public interface IDispatcher
{
    void Post(Action action);               // 等同原 AddCallback（异步）
    Task InvokeAsync(Action action);        // 包装 Blazor Dispatcher
}
```
- `CnCNetManager` 等业务服务构造器注入 `IDispatcher`（注册为单例，内部委托 InfiniFrame 的 `Dispatcher`）。
- 组件订阅业务事件，回调内 `await InvokeAsync(StateHasChanged)`。
- DEBUG 断言"UI 状态仅在调度器线程变更"，覆盖聊天/大厅压测。

### 6.2 XNA 类型替换映射

| 原类型 | 替换 |
|---|---|
| `Color` | `ImageSharp` `Rgba32` + 颜色常量表；**禁用 System.Drawing**（Linux 停维护） |
| `Rectangle/Point/Vector2` | `ImageSharp` `Rectangle`/`Point`/`Size`（int 语义） |
| `Texture2D`（UI 贴图） | 不迁移（CSS 替代） |
| 位图/2D 绘制 | `ImageSharp` 输出 PNG 字节 → `<img>`（单一图像依赖） |
| `SpriteFont/位图字体` | Web 字体（本地 `@font-face`） |
| 音频 | `<audio>`/Web Audio（本地转码） |

### 6.3 事件驱动 UI 模式

- 业务服务"事件 + 状态快照"驱动，不依赖 UI 类型；组件订阅/退订；服务暴露可绑定属性或事件 DTO。

### 6.4 地图预览

- 复用 `FastMapPreviewExtractor`（ImageSharp）→ PNG 字节/base64 缓存（复用 `MapPreviewCacheManager`）→ `<img>`；文件变更监听（MapFileWatcher）复用。
- 职责边界：**HTML canvas / SVG 负责前端渲染**（地图叠加标记、出生点、队伍着色、UI 装饰动效）；**ImageSharp 仅负责后端数据解码/编码**。前端叠加标记在 `<img>` 上层绘制，不重新编码后端图像。
- 性能：预览图生成属低频（进房间/换地图），ImageSharp 纯托管 + SIMD 已满足；注意其默认多核并行解码会占满 CPU，游戏启动/更新下载等场景限制 `MaxDegreeOfParallelism`。

### 6.5 资源与主题

- `wwwroot/` 承载全部静态资源（**本地，无 CDN**）。
- 资源路径（GamePath/BaseResourcePath）由 Integration 的 `IAssetPathProvider` 提供。
- 原 `[Themes]` 配置保留以支持 MOD 主题目录。

### 6.6 游戏启动与窗口切换

- 复用 `GameProcessLogic`；新增：启动游戏→`IInfiniFrameWindow` 最小化/隐藏；退出→恢复回大厅。
- `.dxfail` DX11 降级逻辑迁移时评估是否保留（新宿主不依赖 DirectX 渲染客户端 UI，通常可去）。

### 6.7 自更新

- 复用 `ClientUpdater`；适配新 exe 名、单一二进制目录；废弃 `Binaries/BinariesNET8/XNA/GL` 多目录。

### 6.8 多语言（决策）

原方案（INI，`Translation.cs`）：翻译存游戏目录 INI，支持缺失回退、缺失键导出、MOD 自带翻译、翻译文件复制到游戏目录（`TranslationGameFile`）。
Resx：.NET 标准、类型安全、IDE/工具支持、`IStringLocalizer`。

**结论：双轨制。**
- UI 文案 → Resx + `IStringLocalizer`（运行时切 Culture）。
- 游戏内容/资源翻译 → 保留原 INI `Translation`（文件复制 + MOD 自带）。
- 桥接：`ITranslationService` 把 `.L10N("key")` 重定向到 Resx（UI 键）或 INI（游戏键），key 空间前缀隔离。
- 至少中/英两套验证。

### 6.9 可定制性与个性化 UI（附加）

> 目标：**不改逻辑层、不重编译核心**即可定制外观/文案。面向两类受众：
> **普通用户**（个性化微调，只动 CSS）与 **MOD 作者**（整体换肤/换组件/换配置）。
> 已落地 F1–F3（见 `docs/mod-customization.md`），本节为扩展性设计总纲（v0.8 扩展）。

#### 6.9.1 分层定制模型（Customization Ladder）

| 层 | 能力 | 受众 | 载体 | 门槛 |
|---|---|---|---|---|
| L1 令牌覆盖（**待定**） | 换色/字体/时长/间距 | 普通用户 | 用户个性化 CSS（`UserCustomizations/`） | 只改 CSS 变量 |
| L2 主题目录 | 整体换肤 + 相对资源 | MOD 作者 | `[Themes]` 主题目录（`theme.css` + `theme.ini`） | 会 CSS |
| L3 组件槽位替换 | 替换页面局部组件 | MOD 作者 | `[Components]` 槽位映射（主程序集内组件） | 会 Razor |
| L4 外部组件扩展 | 整组件由 MOD 提供 | 开发者 | RCL / 附加程序集（规划中） | 会编译 |

> 注（v0.8 评审）：**L1 用户个性化待定**——仅保留设计预留（§6.9.3），不进入当前里程碑；
> L2–L4（F1–F3）已落地。

定制由轻到重、由安全到强大：L1 用户零门槛换肤，L4 需要编译能力；
越下层改动越局部、影响面越小，越上层越灵活、风险越高。

#### 6.9.2 扩展性设计原则（保证"好扩展"）

1. **一切可换肤源于 design tokens**：颜色/字体/间距/时长/圆角必须走 CSS 变量（§5.4），
   自定义层只覆盖变量即可换肤；**组件内禁止硬编码色值**（代码评审红线，grep 可查）。
2. **结构稳定、内容可换**：核心页面拆"槽位 + 默认实现"（`ReplaceableComponent`），
   槽位契约（key、默认类型、解析优先级）公开文档化；布局骨架稳定，替换不破坏页面结构。
3. **定制源分层与优先级**：内置 < MOD 主题（L2）< 用户个性化（L1，**待定**）；后加载者覆盖前者。
   用户个性化文件（若启用）置于**游戏根外的用户配置目录**（避免权限问题与被更新覆盖），
   各来源 CSS 以目录隔离，提供变量全集文档（`docs/ui-tokens.md`）便于查询与冲突排查。
4. **资源本地伺服**：主题/用户目录的相对资源（`url()`）经 `GameContentServer` 以对应根
   伺服，无需打包进 wwwroot（已实现于 F1）。
5. **约束沿用**：`prefers-reduced-motion`、只动 `transform`/`opacity`、离线无 CDN 等
   硬性约束对自定义层同样生效（提供校验脚本检查自定义 CSS）。
6. **可逆与可诊断**：配置错误尽早报错（日志/设置页提示），最坏回退内置默认；
   变更**重启生效**（惰性缓存，不做热更新），保持行为可预测。

#### 6.9.3 用户个性化（L1，v0.8 新增 · **待定**）

> 状态：v0.8 评审后**待定**——仅保留设计预留，不进入当前里程碑。以下为若启用时的
> 设计要点，供后续评审时重新评估。

- 载体：`<用户配置目录>/UserCustomizations/theme.css`（可含 `url()` 相对资源，经
  `GameContentServer` 伺服）；一个文件即完整"个性包"，复制即分享。
- 应用顺序（后加载覆盖前）：内置 `:root` → 当前主题 `theme.css` → 用户 `theme.css`。
- 内容限定：仅 CSS 变量覆盖 + 允许的样式规则；变量全集与模板见 `docs/ui-tokens.md`。
- 设置页提供「打开个性化目录」入口（M5 可选）；非法内容报错并忽略，不影响启动。
- 个性化不落 settings INI（与主题持久化区分）：存在即生效，删除即还原，无状态残留。

#### 6.9.4 MOD 定制（L2–L4，现有 F1–F3 汇总）

- L2 主题目录：`ClientDefinitions.ini [Themes]` `key=相对目录`，`theme.css` 覆盖 tokens，
  可带 `theme.ini` 元信息；与内置主题重名 key 忽略（F1）。
- L3 组件槽位：`[Components]` `slot=组件类名`，解析优先级：`[Components]` 覆盖 → 内置默认；
  当前组件须位于主程序集，外部程序集扫描为 L4 规划（F2）。
- 配置入口单一：`Resources/ClientDefinitions.ini`（`[UI]/[Themes]/[Components]/[LoadingScreen]`，
  F3），惰性读取缓存，重启生效。
- 详细落地说明见 `docs/mod-customization.md`。

### 6.10 持久化体系

> 调研依据：`docs/persistence.md`（原版 `UserINISettings`/`FinalSunSettings`/`CnCNetUserData`/`spawn` 机制的代码级盘点）。

**原则**
- 复用原版 Settings/IniFile 体系（`CnCNetClient.Tools.IniFile`），不引入新存储格式。
- 双轨边界：**客户端/游戏设置**写 settings INI；**纯 UI 外观偏好**（主题切换）同样落 settings INI（原版语义），localStorage 仅作启动前的即时回显缓存。
- 保存时机：**变更即存**（各 UI 动作后调用 `UserINISettings.SaveSettings()`）+ **退出兜底**（订阅 `IAppExitNotifier.ApplicationExiting`）。

**初始化顺序**（启动链路）
1. `EnvironmentCheck` 定位 `GamePath`（Resources 父目录）
2. `ClientConfiguration.Instance` 加载 `Resources/ClientDefinitions.ini`（静态配置）
3. `UserINISettings.Initialize(SettingsIniName)` → 合并 `Resources/UserDefaults.ini`（只读默认值）
4. 注册 `SettingsService`（scoped）供设置页读写（并入 M4 设置中心接线）

**文件清单**

| 文件 | 位置 | 内容 | 读写方 | 编码 | 时机 |
|---|---|---|---|---|---|
| `RA2MD.ini`（settings INI，RA2/YR；由 `[Settings] SettingsFile` 决定，可被 MOD 覆盖） | 游戏根 | 玩家设置（分辨率/音量/昵称 `[MultiPlayer] Handle`/主题 `[MultiPlayer] Theme`/过滤器/收藏地图…） | `UserINISettings` | IniFile 默认 | 启动加载；变更即存 |
| `UserDefaults.ini` | Resources | 只读默认值，初始化合并 | `UserINISettings.Initialize` | — | 启动 |
| `ClientDefinitions.ini` | Resources | 静态配置（只读） | `ClientConfiguration` | — | 启动 |
| `FinalSun.ini` | 游戏根 `FinalSun/` | 地图编辑器设置（语言/路径） | `FinalSunSettings.WriteFinalSunIniAsync` | **强制 legacy ANSI** | 首次写入 |
| `spawn.ini` / `spawnmap.ini` / `Saved Games/spawnSG.ini` | 游戏根 / Saved Games | 开房/启动游戏 spawn 配置 | `GameLobbyBase`/`CampaignSelector`/`GameLoadingWindow` | IniFile 默认 | 启动游戏时 |
| `Client/Translation.ini` | Client | 翻译 stub（生成） | `Translation` | — | 按需 |
| `Client/ProcessedIniInfo.ini` | Client | INI 预处理器缓存 | `IniPreprocessInfoStore` | — | 预处理后 |
| `Client/friend_list` / `ignore_list` / `recent_list` | Client | 好友/忽略/最近玩家 | `CnCNetUserData` | JSON/文本 | **退出时**（ApplicationExiting） |
| 注册表（HKCU） | — | 游戏安装路径 | `WritePathToRegistry`（`InstallationPathRegKey`） | — | 按设置 |
| localStorage | WebView 数据目录 | 主题即时回显缓存（`cncnet.theme`） | `ThemeProvider` | — | 启动前回显 |

**RA2/YR 共享 `RA2MD.ini` 约束**
- settings INI 即游戏自己的配置文件，**只写客户端字段、不覆盖游戏字段**；写前解析合并，避免损坏。
- 修改/落盘采用解析 → 改键 → 整文件写回（`IniFile.WriteIniFile`）方式，不做行级盲改。
- 更新器 `ignoreMasks` 已含 `Theme.ini`/`gui_settings.xml` 等，settings INI 同样不得被更新覆盖。

**主题持久化（已定方案 A）**
- 主题切换 → `UserINISettings.ClientTheme.Value`（`[MultiPlayer] Theme`）→ `SaveSettings()`。
- localStorage 仅保存当前主题用于 WebView 首帧前回显，避免启动闪变；正式以 settings INI 为准。

---

## 7. 平台与部署

| 项 | 决策 |
|---|---|
| 目标平台 | 首发 Windows（WebView2 Runtime 依赖）；Linux（WebKit2GTK）保持架构兼容 |
| 架构 | AnyCPU/x64（废弃 x86/XNA） |
| 发布 | 保留"Resources + 客户端文件 + 更新服务"模型；自更新复用 |
| 运行时 | WebView2 Runtime 检测与提示 |
| 离线资源 | 所有库/字体本地打包，无网络运行时依赖（除网络功能本身） |

---

## 8. 风险与对策

| # | 风险 | 等级 | 对策 |
|---|---|---|---|
| 1 | DXGUI 夹带业务逻辑抽取工作量大 | 高 | 逐文件盘点"纯 UI/业务"，先抽业务+单测再写 UI |
| 2 | 线程迁移遗漏 | 中 | `IDispatcher` 统一；DEBUG 线程断言；聊天/大厅压测 |
| 3 | 高频列表渲染性能 | 中 | `Virtualize`+事件合并+JS 兜底；原型即压测 |
| 4 | 动效过度导致性能/体验问题 | 中 | tokens 统一时长/缓动；只动 transform/opacity；reduced-motion 降级；静态 HTML 阶段先验收动效手感 |
| 5 | 星际2科幻风实现复杂度（视觉资产） | 中 | 纯 CSS/渐变/SVG 实现装饰，避免图片资产；风格阶段原型先行 |
| 6 | InfiniFrame 0.6x 版本年轻 | 中 | 锁版本；跟进 issue；核心能力已在 0.60 提供 |
| 7 | 自更新/打包改造 | 中 | 尽早定义"新 exe + 单目录 + 更新清单"并端到端演练 |
| 8 | 游戏启动/窗口切换体验 | 中 | 隐藏/恢复策略区分最小化与完全隐藏 |
| 9 | 逻辑复制后与上游脱钩 | 低 | 记录 commit 基线；重大修复手动同步 |

---

## 9. 里程碑

| 阶段 | 交付物 | 出口条件 |
|---|---|---|
| M0 规格冻结 | 本 spec + checklist + tasks 评审通过 | 用户确认 |
| M1 逻辑抽取 | 4 类库编译通过无 XNA 依赖；`IDispatcher` 就位；ClientCore 无 UI 初始化成功 | 编译 + grep 基线 + 核心单测 |
| M2 骨架集成 | UI 壳加载服务；主菜单；主题框架 + 星际2科幻风静态 HTML（带动效） | 主菜单可进可退；主题切换生效 |
| M3 在线闭环 | 登录→大厅→建/加入→配置→启动游戏 | 端到端联机验收 |
| M4 完整功能 | 设置/更新/私聊/LAN/单机战役/统计 | checklist 全勾 |
| M5 打磨发布 | 动效打磨、主题、打包/自更新演练、Windows 验收 | 发布候选 |

---

## 附录 A：关键原代码位置

- 逻辑注册：`DXMainClient/DXGUI/GameClass.cs`
- 网络与 UI 桥：`DXMainClient/Online/CnCNetManager.cs`
- 大厅 UI+逻辑：`DXMainClient/DXGUI/Multiplayer/CnCNet/CnCNetLobby.cs`
- 游戏启动：`ClientGUI/GameProcessLogic.cs`；`ClientCore/ClientConfiguration.cs`
- 地图预览：`DXMainClient/Domain/Multiplayer/FastMapPreviewExtractor.cs`
- 翻译：`ClientCore/I18N/Translation.cs`、`TranslationGameFile.cs`
- 主题：`ClientCore/ClientConfiguration.cs`（Themes）、`Resources/ClientDefinitions.ini`
- 更新：`ClientUpdater/Updater.cs`

## 附录 B：本地化资源清单（已定稿 2026-09-09）

- [x] CSS 框架选型：**无框架**——原生 CSS + design tokens（`wwwroot/css/app.css` `:root` 变量 + `[data-theme]` 覆盖），本地引用，无 CDN（M2.6/2.6.1）
- [x] 动画库选型：**无第三方**——纯 CSS transitions/transforms + Web Animations；动效仅 transform/opacity（tokens 定义时长/缓动），respect `prefers-reduced-motion`（M2.6.2）
- [x] 字体：**已定稿** Orbitron + Rajdhani + Noto Sans SC（均 OFL 可分发，本地 `wwwroot/fonts/`，见 §5.4.4）
- [x] 图标：**内联符号字符 + 少量内联 SVG**（无图标库、无 CDN；主菜单按钮 icon 字段为 Unicode 符号，Lobby/Settings 等用内联 SVG 装饰，全部本地）

---

## 附录 C：从原 CNCNet 客户端迁移指南

> 目标读者：正在使用原 xna-cncnet-client 的玩家/用户。目的：无缝切换到新客户端，
> 尽量保留昵称、好友列表、设置等个人数据，并给出排障与回退方案。
> 本指南需随 M5 发布一并编写为独立 `docs/migration-guide.md`（面向用户的最终文档），此处为规格级指引。

### C.1 迁移准备

1. **确认版本匹配**：新客户端与游戏版本（RA2/YR 等）对应，游戏本体不修改（见 §1.2）。
2. **备份共享数据文件**：完整复制 `<游戏>/Client/`（好友/忽略/最近列表）、`<游戏>/RA2MD.ini`（设置）、
   `Resources/Translations/`（翻译）到安全位置，用于回退（见 C.5）。
3. **新旧可并存**：新旧客户端使用不同的程序目录与进程，可并行安装使用；
   **不建议同时在线**（共用数据文件，见 C.5 并行注意事项）。

### C.2 数据兼容性（共用文件，零迁移）

> 关键结论（v0.8 修订）：新客户端与原版**共用同一游戏目录下的数据文件**——
> 好友/忽略/最近列表（`Client/`）、设置 INI（`RA2MD.ini`）、翻译目录（`Resources/Translations/`）
> 路径与键名兼容，个人数据**自动迁移、无需手动复制**。

| 数据 | 原位置 | 新客户端 | 迁移方式 |
|---|---|---|---|
| 昵称 | CnCNet 账号=IRC 昵称（无密码） | 登录页输入（见 §5.3.1） | 无需迁移文件，直接输入原昵称 |
| 好友/忽略/最近列表 | `<游戏>/Client/friend_list` / `ignore_list` / `recent_list`（§6.10） | 同路径 | **共用文件，零迁移** |
| 设置项 | `<游戏>/RA2MD.ini`（RA2/YR；RA1 为 RA1.ini） | 同一文件 + `Resources/UserDefaults.ini` 补新键默认值（§6.10） | **共用文件，零迁移**；键名/键值兼容 |
| 翻译语言 | `<游戏>/Resources/Translations/<locale>/Translation.ini` | 同路径（§6.8） | **共用文件，零迁移**；缺失时自动回退 `en` |
| 主题/快捷键 | 原版 XNA 主题配置 | 新主题系统（§5.4） | 需在设置页**重选主题**；MOD 主题迁移见附录 D |

> 决策：**新旧共用数据文件 + 新键由 UserDefaults 补默认值**，绝不改动原客户端程序本体；
> 新客户端新增设置键原版不可见/不影响，可安全并存。

### C.3 首次启动流程

1. 启动新客户端 → 进入主菜单。
2. 进入**设置**：配置游戏目录（选择游戏安装目录）、分辨率、音量等；在「外观」**重选主题**（原版主题不迁移）。
3. 回到主菜单 → **在线（CnCNet）** → 登录 Modal 输入昵称连接（见 §5.3.1）。
4. 进入大厅：频道聊天 → 房间列表 → 建/加入房间 → 配置队伍/地图 → 启动游戏（见 §3、M3）。
5. 校验：游戏进程正常启动、窗口隐藏/恢复正常（见 §6.6）；好友/忽略/最近玩家已出现（共用 `Client/` 数据，无需导入）。

### C.4 常见问题（FAQ）

- **连接失败/卡在连接中**：检查网络与防火墙；CnCNet 服务器使用标准 IRC/游戏端口；查看 `logs/connection.log`（§6.10 日志）定位 DNS/超时问题。
- **语言显示异常**：确认 `<游戏>/Resources/Translations/<locale>/` 存在；新客户端默认按系统区域匹配语言，无匹配时回退英文；可在设置页手动切换。
- **游戏无法启动**：确认游戏目录配置正确、游戏文件完整；查看游戏启动日志（§6.6）。
- **好友列表为空**：确认 `<游戏>/Client/friend_list` 存在且未被移动/损坏（纯文本、每行一个昵称，可手工检查）；损坏时从备份还原（C.1 第 2 条）。
- **更新相关**：自动更新默认关闭或需确认（§6.7），避免误覆盖原客户端。

### C.5 回退方案与并行注意事项

- 保留原客户端目录即可随时回退；新客户端不修改游戏本体与原客户端程序数据。
- **并行注意事项**：新旧共用 `Client/`、`RA2MD.ini`、`Resources/Translations/`——同时在线会互相
  读写列表/设置（后退出者胜），迁移期建议以新客户端为主、回原版前先退出新客户端。
- 迁移期间共享文件被改动需还原时，用 C.1 第 2 条备份覆盖即可恢复原状。

### C.6 交付检查（随 M5 验收）

- [x] `docs/migration-guide.md` 已编写并通过用户走查（2026-09-09 编写，走查随 M5 发布）
- [ ] 迁移脚本/逻辑（好友列表、设置合并）经真实原客户端数据验证
- [ ] FAQ 覆盖：连接、语言、游戏启动、好友、更新 5 类场景（已写全 5+1 类，发布前复读）
- [ ] 回退路径验证通过（保留原客户端可正常使用）

---

## 附录 D：INI → HTML/CSS 迁移 Skill（MOD / 主题平滑过渡）

> 目标读者：原 xna-cncnet-client 的 **MOD 作者 / 主题制作者**。目的：把旧 INI 中的
> 主题色、UI 文案、客户端配置平滑迁移到新客户端的 HTML/CSS 体系（design tokens、
> Resx、新 `ClientDefinitions.ini`），**自动完成能自动的映射，产出可直接用的文件，
> 报告必须手改的项**。
>
> 定位决策（v0.8）：**设计为可复用的辅助 Skill（TRAE Skill），并配套映射表与使用手册**——
> 普通用户的数据迁移走附录 C（手动为主）；MOD 的主题/文案/配置迁移走本 Skill（自动化为主）。
> 交付物：映射表（v1）→ Skill 骨架 → 真实 MOD 验收 → 文档。

### D.1 迁移对象与映射

| 原 INI / 文件 | 新落点 | 自动映射度 | 说明 |
|---|---|---|---|
| 原主题色配置（`Theme.ini` 等） | `theme.css`（token 覆盖，§6.9 L2） | 高 | 颜色键 → design tokens 映射表（§D.2 表 T1） |
| 原 `ClientDefinitions.ini` 旧键 | 新 `ClientDefinitions.ini`（`[UI]/[Themes]/[Components]/[LoadingScreen]`） | 中 | 键名/结构变化，按映射表改写，无法映射的进报告 |
| `Translation.ini` / `Resources/Translations/<locale>` 的 **UI 键** | Resx（`UIStrings.*`） | 中 | UI 键 → Resx 键映射表（T2）；**游戏内容键保留原 INI**（§6.8 双轨） |
| 菜单入口 / Logo / 链接 / 特性开关 | `[UI]` 键（`HiddenMenus` 等） | 高 | 语义等价映射 |
| 玩家设置 `ClientSettings.ini` | settings INI 合并（§6.10） | 高 | 已有数据迁移逻辑（附录 C），Skill 不重复处理 |

### D.2 Skill 规格

- **名称**：`cncnet-ini-to-theme`（工作名）。
- **输入**：原版 INI 文件路径（可多个）+ 目标 MOD 名 / 输出目录。
- **处理流程**：
  1. 解析 INI → 按内置映射表转换；
  2. 产出 `theme.css`（**仅覆盖差异 tokens**，与内置差异最小化）、新 `ClientDefinitions.ini`、
     Resx 骨架（键名与 UI 文案一致化）；
  3. 产出**迁移报告**：成功映射清单 / 需手改项（含原键、原值、建议新键）。
- **输出目录**：`<目标>/Themes/<mod>/` 等，可直接放入客户端使用。
- **内置映射表**（随新客户端维护，权威来源为 `docs/ui-tokens.md`）：
  - T1：原主题色键 ↔ 新 design tokens（§5.4 全集）；
  - T2：原 UI 文案键 ↔ Resx 键（`UIStrings.KeyPrefix`）；
  - T3：旧 `ClientDefinitions.ini` 键 ↔ 新 `[UI]/[Themes]/[Components]` 键。
- **验收标准**：产出 `theme.css` 经客户端加载无报错；迁移报告无未解析的"高优先键"
  （主色/背景/文字/按钮四类必达）。
- **约束**：Skill 只读原文件、只写输出目录（不动原客户端）；遵循 §6.9.2 约束（reduced-motion
  等）；输出 CSS 不得含 CDN/外部资源引用。

### D.3 与现有迁移指南的分工

| 场景 | 受众 | 载体 | 方式 |
|---|---|---|---|
| 数据迁移（昵称/好友/设置） | 普通玩家 | 附录 C / `docs/migration-guide.md` | 手动 + 首次启动自动合并 |
| 主题 / 文案 / 配置迁移 | MOD 作者 | 本 Skill（§D.2）+ `docs/migration-skill.md` | 自动化 + 报告手改项 |

### D.4 交付与里程碑

- [ ] 映射表 v1（T1/T2/T3）——以 `docs/ui-tokens.md`（变量全集，§6.9.3 依赖）为基线
- [ ] Skill 骨架 + 转换执行（读 INI → 写文件 → 生成报告）
- [ ] 用 1 个真实 MOD 主题走通验收（产出加载无报错、高优先键全解析）
- [ ] 文档：`docs/ui-tokens.md`、`docs/migration-skill.md`（使用手册）
- [ ] 随 M5 发布：Skill 目录 + 文档纳入发布包
