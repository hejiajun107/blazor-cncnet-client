# CNCNet 重构 · 任务拆分（Tasks）

> 配套 `spec.md` / `checklist.md`。按里程碑推进，任务可勾选。

## M0 规格冻结

- [x] 0.1 评审 spec.md / checklist.md / tasks.md，确认范围与验收基线
- [x] 0.2 确认首个目标 MOD 与验收环境（游戏本体、地图、账号）（RA2/YR=ra2md.ini 语义，验收环境随各 acceptance 推进）
- [x] 0.3 确定 UI 风格阶段输入（用户将提出要求/提供参考）（M2.6 三套风格原型 → 选定 theme-d 并固化进 spec §5.4/§5.5）

## M1 逻辑抽取（去 XNA 化）

### M1.1 工程搭建
- [x] 1.1 新建 `CnCNetClient.Core` 类库（net10.0），复制 ClientCore 源码
- [x] 1.2 新建 `CnCNetClient.Domain` / `CnCNetClient.Online` / `CnCNetClient.Updater`（均已复制并编译通过；TunnelHandler 由 Domain 迁至 Online 以破循环依赖）
- [x] 1.3 新建 `CnCNetClient.Integration`（IDispatcher、类型替身 Rgba32 常量表/ColorParser、IAppExitNotifier、GameProcessLogic 骨架）
- [x] 1.4 建立"禁止 XNA / System.Drawing 引用"的编译检查（grep 基线核对通过：全部逻辑类库零引用；宿主层仅 Program.cs 曾用 System.Drawing，已改用 int 重载）
- [x] 1.5 记录逻辑复制上游 commit 基线（`e6e367bbe04c1a0dc1e34a8fed2856ea3ab7e8c4` 2026-08-01）

### M1.2 去 XNA 化
- [x] 1.6 替换 `Color/Rectangle/Point/Vector2`（ImageSharp `Rgba32`/`Rectangle`/`Point`/`Size`；IRC 颜色表建常量表 `ColorConstants`；**禁用 System.Drawing**）
- [x] 1.6.1 位图绘制适配：统一 ImageSharp（`MapPreviewExtractor` 已适配 3.x API）替代需真实绘制的原调用点；配置并行度限制避免吃满 CPU（待运行验证）
- [x] 1.7 定义并实现 `IDispatcher`；替换 `CnCNetManager` 的 `WindowManager` 依赖（30 处 `wm.AddCallback` → `dispatcher.Post`）
- [x] 1.8 替换全部 `WindowManager.AddCallback` 调用点（grep 基线核对：仅注释/方法名残留）
- [x] 1.9 聊天颜色表/IRC 颜色迁移（`IRCColor.XnaColor` 用 `Rgba32` + `ColorConstants`）
- [x] 1.10 移除 `Rampastring.Tools` 源码依赖（已复制为独立工程 `CnCNetClient.Tools`：剔除 `WindowFlasher.cs`/CsWin32，命名空间改 `CnCNetClient.Tools`）
- [x] 1.11 处理 `ProgramConstants`/路径/`SafePath` 依赖，统一到 Core（`ProgramConstants.FindGamePath` 权威定位；Integration 建 `IAssetPathProvider` 抽象 + `ProgramConstantsAssetPathProvider`；UI 层 `EnvironmentCheck` 注入复用，删除复制的 `FindGamePath`）
- [x] 1.12 单测：`CCIniFile` 继承合并、`ClientConfiguration` 加载、`MapLoader` 加载、`Connection` 连接解析、`Updater` 版本比对（30 用例全绿；MapLoader 修复 Win32 保留字符检查仅校验文件名段，不误伤相对路径分隔符）
- [x] 1.13 出口条件：4 个类库编译通过且无 XNA/UI 引用；无 UI 下 `ClientConfiguration` 初始化成功（UI 层 EnvironmentCheck 已验证可在无 UI 下初始化）

## M2 骨架集成

- [x] 2.1 UI 工程引用逻辑类库 + Integration，注册全部服务（`IDispatcher`=InfiniFrameDispatcher、`IAppExitNotifier`=AppExitNotifier、`EnvironmentCheck`）
- [x] 2.2 启动流程：`Program` → 服务注册 → 窗口创建 → 首页（环境检测页已就绪；`ClientConfiguration` 初始化为惰性，随资源就绪触发）（M5.2.3 起前置 WebView2 检测；5.4 启动加固 + WinExe；`Index.razor` 调 `Env.Detect()` 完成资源定位与配置初始化，构建 0 错误）
- [x] 2.3 启动屏/LoadingScreen 组件（`Components/Shared/SplashScreen.razor` 全屏覆盖层 z-90 + `Infrastructure/LoadingScreenService` 读 `ClientDefinitions.ini [LoadingScreen]` + `GameContentServer` 本地伺服游戏目录，独立文件 URL 加载媒体含 Range；图片/视频/品牌画面可选，超时淡出，reduced-motion 适配；配置说明见 `docs/splash-screen.md`）
- [x] 2.4 主菜单页面 + 导航（Router/MainLayout）：`MainMenu.razor`（/menu，青单色 HUD：全息地球视口 + 读数 + 斜切按钮面板），`MainLayout` 承载全局背景层/HUD 顶栏/底栏/toast，环境检测页可跳转并含开发预览入口
- [x] 2.5 主题系统：design tokens（`app.css :root`，含 `[data-theme="inner"]` 蓝色系覆盖）+ `IThemeProvider`（`Infrastructure/Theme/`，scoped 注册共享 WebView JS 上下文）+ 运行时切换（HUD 顶栏「主菜单/内页」切换，仅 opacity 过渡）+ localStorage 持久化；动效时长/缓动走 tokens
  - 2.5.1 红警原版风格主题（`[data-theme="ra"]` 金×橄榄军绿，直角 3D 凸出按钮）：ThemeProvider 注册 `ra`；`app.css` 配色/背景层/按钮覆盖；`app.js` canvas 星粒/地球读 CSS 变量随主题换色；MainMenu 品牌文案切换（RED ALERT / CNC NET）
- [x] 2.6 风格阶段：产出 theme-a（现代简洁浅色）、theme-b（星际2科幻风）、theme-d（融合版，最终按 **IRON TIDE 参考**重构为**青单色 HUD 主菜单**：canvas 全息地球 + HUD 读数 + 斜切按钮）三套带动效静态 HTML → 用户选定 theme-d → 已固化进 spec.md §5.4/§5.5（v0.4）
- [x] 2.6.1 本地资源落地：CSS/JS/字体已下载到 `wwwroot`（Orbitron/Rajdhani/Noto Sans SC 本地 @font-face，`app.css`+`app.js` 本地引用，核对无 CDN）
- [x] 2.6.2 动效基础：动效 tokens 全集（--t-micro/--t-item/--t-sheet/--t-pop/--t-form + --ease-out-back）+ 通用过渡/入场类（page-anim 页面切换 fade-slide 经 `@key=Nav.Uri` 重放、item-in 列表/按钮 stagger、pop-in、slide-in-r、shake、pulse）；分辨率适配：720p 紧凑媒体查询（1280×720 压缩间距/字号，1920×1080 不触发）+ 面板滚动兜底
- [x] 2.7 通用组件：MessageBox、Tooltip、TopBar（含对应动效）
  - 2.7.1 `MessageBoxService`（scoped 全局弹窗服务，`ShowAsync` 异步等待）+ `Components/Shared/MessageBox.razor` 宿主组件（pop-in 240ms ease-out-back 打开 / fade-out 120ms 关闭；Info/Warn/Error/Confirm 四类配色；OK/OKCancel/YesNo）
  - 2.7.2 `Components/Shared/TopBar.razor` 可复用顶栏（状态呼吸灯 pulse + 实时时钟每秒滚动 + 玩家名 + 右侧插槽），MainLayout 顶栏重构为该组件，主题切换控件放入插槽并套 Tooltip
  - 2.7.3 `Components/Shared/Tooltip.razor`（淡入+微上移 120ms，hover 延迟 400ms，top/bottom 方位，focus-visible 可达）
  - 演示接入：主菜单「设置」→ Info MessageBox，「鸣谢」→ YesNo MessageBox 回填 toast
- [x] 2.8 日志/异常：Serilog 接入 UI 崩溃兜底（迁移 `DisplayErrorAction`）
  - 2.8.1 Serilog 配置：控制台 + 文件落盘 `logs/client-yyyyMMdd.log`（滚动间隔日、保留 14 天、异步写入避免阻塞 UI），`Program.cs` 注册
  - 2.8.2 `GlobalExceptionHandler`（Infrastructure/，scoped）：订阅 `AppDomain.UnhandledException` / `TaskScheduler.UnobservedTaskException`（未观察 Task 仅记录 SetObserved 避免误杀）；注入 `MainClientConstants.DisplayErrorAction` 迁移原 XNA 语义 —— 调度回 UI 线程经 `MessageBoxService` 呈现错误，exit=true 时先 `AppExitNotifier.NotifyExiting()` 持久化再 `Environment.Exit(1)`；`App.razor` 根组件订阅启用
  - 2.8.3 `MainClientConstants` 剥离 `#if WINFORMS` 分支，移除 System.Windows.Forms 依赖
- [x] 2.9 出口条件：主菜单可进可退；主题切换生效；交互动效手感在静态原型阶段已验收
  - 2.9.1 可退：主菜单 ESC 键返回首页（`app.js` `initMenuKeyNav` 监听 keydown + `MainMenu.OnEscKey` JSInvokable 导航 `/`），可进：环境检测页「预览主菜单」入口进入
  - 2.9.2 主题切换：HUD 顶栏三主题（主菜单青单色 / 内页蓝 / 红警金×橄榄绿）运行时切换 + localStorage 持久化，canvas 随 CSS 变量换色
  - 2.9.3 交互动效手感：2.6.2 已验收（动效 tokens + stagger 入场 + 按钮 hover 位移/扫光，reduced-motion 降级）

## M3 在线闭环（核心验收）

- [x] 3.1 登录 Modal：昵称 + 记住我/持久模式/自动连接 → CnCNet 连接成功（**无密码**，spec §5.3.1）
  - 3.1.1 `LoginModal` 组件（浮窗，复用 MessageBox 宿主机制）+ 主菜单「多人游戏」触发
  - 3.1.2 连接：`NameValidator` 校验 → `ProgramConstants.PLAYERNAME` → `CnCNetManager.Connect()`；成功 toast 提示（`/online/lobby` 待 M3.2），失败/取消回主菜单
  - 3.1.3 自动连接：记住我+持久模式勾选即自动连接跳过 Modal；登录设置持久化等 M4 4.1.1（M3 期间内存+localStorage 过渡）
- [x] 3.2 大厅：频道列表 + 聊天框 + 房间列表 + 玩家列表（Virtualize/节流）
  - 3.2.1 `LobbyService`（聚合频道/聊天/房间/玩家，解析 `CTCP "GAME "` 广播协议 → `LobbyGameItem`，15s 过期清理）
  - 3.2.2 `Lobby.razor`（`/online/lobby` 四区布局：频道选择/房间列表/聊天/玩家，Virtualize 渲染 + 房间筛选 + Enter 发送）
  - 3.2.3 接线：主菜单「多人游戏」连接成功/已连接 → 跳转大厅；`Program.cs` 注册 `LobbyService`；app.css 大厅样式 + 720p 紧凑适配 + 聊天滚动 `cncnet.scrollToBottom`
- [x] 3.3 右键菜单（私聊/邀请/加友/屏蔽）
  - 3.3.1 `LobbyService` 右键动作：加友/取消（`ToggleFriend`）、屏蔽/取消（`ToggleIgnore`，按 ident）、邀请（CTCP INVITE 到游戏广播频道，`CanInvite` 判定）、私聊（`SendPrivateMessage`）
  - 3.3.2 `Lobby.razor` 右键菜单（玩家列表项 + 聊天用户名触发，fixed 定位 + 视口钳制，遮罩点击/ESC 关闭，spec §5.5 scale+fade）+ 私聊快捷窗（单聊发送，M3.8 扩展多标签）
  - 3.3.3 app.js `getViewport` / `initLobbyKeyNav`（ESC 关菜单/私聊）；app.css `.ctx-menu`/`.pm-panel` 样式；好友★/屏蔽徽章展示
- [x] 3.4 地图预览 `<img>` 化 + 缓存
- [x] 3.5 建房间/加入房间：队伍/地图/规则配置
- [x] 3.6 出生点映射 + 游戏信息面板
- [x] 3.7 游戏加载等待页 + `GameProcessLogic` 启动游戏 + 窗口隐藏/恢复
  - 3.7.1 `GameProcessLogic`（Integration）迁移原版启动逻辑：可执行文件解析/-SPAWN 参数/QRes 窗口模式/单核亲和性；XNAMessageBox 替换为 `GameProcessLaunchFailed` 事件回调
  - 3.7.2 `SpawnIniGenerator`（Domain）生成 spawn.ini + spawnmap.ini（玩家/阵营/颜色/出生点/联盟/GameID/Host/Port）
  - 3.7.3 `GameRoomService` START 消息扩展：`UniqueGameId` + 基准端口分配广播/解析 + `GetPlayerInfos`/`SideCount`
  - 3.7.4 `GameLaunchService`（Infrastructure）协调：生成配置 → `/online/loading` → 启动进程 → 按设置最小化宿主窗口；退出/失败恢复窗口并回房间
  - 3.7.5 `GameLoading.razor`（/online/loading）雷达式加载等待页 + 对局计时 + 返回大厅；app.css 样式
  - 3.7.6 接线：`Program.cs` 注册 `GameLaunchService`；`GameLobby.razor` 注入触发订阅、`OnStartGame` 交由服务处理
- [x] 3.8 私聊多标签
  - 3.8.1 `LobbyService` 私聊存储：`PmSession`/`PmMessage` 会话模型 + 未读数，订阅 `CnCNetManager.PrivateMessageReceived`（屏蔽按 ident 过滤）→ `PmReceived`/`PmChanged` 事件；`SendPrivateMessage` 本方消息即时入列
  - 3.8.2 `Lobby.razor` 多标签私聊中心：标签栏（激活/未读徽章/关闭）、会话消息历史（本方/对方样式）、输入发送 Enter、新消息自动打开并激活会话、关闭面板保留历史
  - 3.8.3 接线：`OnInitialized`/`Dispose` 订阅退订、ESC 关闭面板、私聊滚动 `cncnet.scrollToBottom`；app.css `.pm-tabs`/`.pm-msg`/未读徽章跳动 + `slideInUp` 动效（respect reduced-motion）
- [x] 3.9 频道指令（邀请/踢/禁言）复用 CommandHandler
  - 3.9.1 `LobbyService` 频道指令注册表：`ChannelCommand`（指令名/描述/是否需管理员/处理器）复用原客户端 `ChatBoxCommand` 模式；`ExecuteChannelCommand` 解析 `/` 输入、大小写不敏感匹配、未知指令与权限不足在频道内回黄色系统提示；`IsCurrentChannelAdmin` 按当前频道自身 `@` 状态判定
  - 3.9.2 指令实现：`/kick`（SendKickMessage）、`/ban`（SendBanMessage 按 host + 连带踢出）、`/mute` `/voice`（MODE ±v）、`/invite`（复用 `InvitePlayer`）、`/help`；`SendMessage` 遇 `/` 开头走指令分支
  - 3.9.3 `Lobby.razor`：右键菜单管理员项（踢出/封禁/禁言-toggle，红警示样式，非管理员不显示）；聊天输入 `/` 指令提示浮层（按前缀过滤、管理员项锁定标记、点击补全）；app.css `.cmd-hint`/`.ctx-item.warn`
- [x] 3.10 断线/重连提示与状态显示
  - 3.10.1 `ConnectionMonitor`（新增，scoped）：订阅 `CnCNetManager` 的 Connected/ConnectionLost/ReconnectAttempt/Disconnected/ConnectAttemptFailed，维护状态机 `Offline/Connecting/Online/Reconnecting`；`StateChanged` + `Notification`（Connected/Reconnected/Disconnected/ConnectionLost/Failed）事件；自动重连由 Connection 层负责
  - 3.10.2 `TopBar` 三态状态灯（`StatusClass`: 在线青呼吸/重连琥珀/离线红常亮）+ `StatusText`；`MainLayout` 顶栏与底栏 SYSTEM STATUS 接线 + 断线（err 红）/重连（默认青）/失败（warn 金）Toast
  - 3.10.3 `Lobby.razor` 顶栏连接指示（状态点 + 在线人数）+ 聊天断线横幅（重连中显示原因）；app.js `toast(msg, kind)` 支持 err/warn；app.css `.live.retry`/`.live-txt`/`.foot-status`/`.lt-conn`/`.conn-banner`/`.toast.err`/`.toast.warn`（respect reduced-motion）
- [x] 3.11 出口条件：端到端联机对局验收通过（checklist D2 全勾）
  - 3.11.1 隧道接入（`TunnelHandler` 单例注册，懒实例化时已连接则自动 `Start`）：`GameRoomService` 注入；建房间/开始游戏时按原版 TunnelListBox 评分（`ping² × 负载率`，仅 Official/Recommended）自动选优隧道；隧道列表就绪后房主在房间内自动选优并刷新 GAME 广播
  - 3.11.2 多人局端口下发：`StartGame` 向隧道 `GetPlayerPortInfo(人数)` 请求端口并逐玩家分配，START 广播真实 `隧道地址:玩家端口`（单人局免隧道，保持 0.0.0.0）
  - 3.11.3 加入者解析：`HandleStartMessage` 从 START 提取隧道地址并经隧道列表反查监听端口（TunnelAddress/TunnelPort）；GAME 广播已选隧道时广播真实 address:port（房间列表识别隧道房）
  - 3.11.4 `SpawnIniGenerator.WriteSpawnIni` 新增 `tunnelIp/tunnelPort` 参数写 `[Tunnel] Ip/Port` 节；`GameLaunchService` 透传 `_room.TunnelAddress/TunnelPort`

## M4 完整功能

- [x] 4.1 设置中心：显示/音频/游戏/组件/更新/CnCNet/隐私，读写 ini
  - [x] 4.1.1 持久化接线（spec §6.10）：`UserINISettings.Initialize(SettingsIniName)`（RA2/YR=RA2MD.ini，合并 `UserDefaults.ini`）+ `SettingsService`（scoped）+ 变更即存 `SaveSettings()` + 退出兜底（订阅 `ApplicationExiting`）+ 主题持久化改走 settings INI `[MultiPlayer] Theme`（localStorage 仅作回显缓存）
- [x] 4.2 语言切换（Resx + INI 双轨，见 spec §6.8）与主题切换
  - [x] 4.2.1 双轨制桥接：`UIStrings`（新建，Resx 桥接，`Client:UI:` 键前缀隔离）+ `StringExtensions.L10N` 按前缀路由 Resx / INI `Translation`
  - [x] 4.2.2 Resx 资源：`Resources/UI.resx`（neutral=en）+ `UI.zh-CN.resx`（中文卫星，客户端默认 UI 语言），中/英两套
  - [x] 4.2.3 `TranslationService`（新建，scoped）：运行时切 `CultureInfo.CurrentUICulture`（UI Resx）+ 重载 INI `Translation`（游戏内容，含主题翻译叠加，镜像原版 PreStartup）+ 持久化 `[Options] Translation`（即改即存）+ `LanguageChanged` 通知 UI 重渲染
  - [x] 4.2.4 中文优先默认：`Translation.GetDefaultUiLocaleCode`（中文系统默认 zh-CN，否则跟随系统匹配翻译目录）；`UserINISettings.Translation` 默认值接入
  - [x] 4.2.5 启动接线：`Program.cs` 记录 `Translation.InitialUICulture` → `UserINISettings.Initialize` → 按持久化语言设首帧 `CurrentUICulture` → 注册 `ITranslationService`
  - [x] 4.2.6 设置页语言下拉：`Settings.razor` 显示分区语言选择（内置 UI 语言 + 游戏翻译目录），切换即持久化 + 全页重渲染；页面文案本地化（标题/返回/导航/分区标题）
- [x] 4.3 自动更新：启动检查/手动查询/下载解压；新 exe 名+单目录端到端演练
  - [x] 4.3.1 `UpdateService`（新建，scoped）：懒初始化（环境就绪后 `Updater.Initialize`）+ 状态/进度读取（VersionState/GameVersion/ServerGameVersion/UpdateSizeInKb/ManualUpdateRequired）+ 事件转发 + `Restart` 接线（`AppExitNotifier.NotifyExiting` 持久化后退出进程，交由 SecondStageUpdater 替换重启）
  - [x] 4.3.2 `Program.cs`：注册 `UpdateService`；创建 Global 互斥锁 `Global{1CC9F8E7-9F69-4BBC-B045-E734204027A9}`（与原版 DXMainClient 一致，进程存活期间不释放）供 SecondStageUpdater 等待主程序退出
  - [x] 4.3.3 设置中心「更新」Tab：`Settings.razor` 扩成完整更新面板（启动时检查开关/本地版本/服务器版本/立即检查/立即更新/进度条/取消/手动下载/失败重试，UI 由 `UpdateService.StateChanged` 驱动）
  - [x] 4.3.4 启动检查接线：`MainMenu.razor` 加载时按 `[Options] CheckforUpdates`（默认 true）非阻塞触发 `UpdateService.CheckForUpdates()`；开关关闭不检查
  - [x] 4.3.5 更新面板样式：`app.css` 补 `.upd-progress`（进度条）/.upd-actions（操作区）
  - [x] 4.3.6 构建 0 错误（主工程 Debug 通过）；验收标准见 `docs/acceptance-m4.3.md`（D6.1 手动/启动检查 + D6.2 新 exe 名单目录端到端演练）
- [x] 4.4 LAN：广播/发现/建入/启动（4.4.1-4.4.5 全部实现；4.4.6 双端端到端验收归真机走查，见 `docs/acceptance-run.md` §2）
  - [x] 4.4.1 `CnCNetClient.Online/LAN/` 基础设施移植：`LanBroadcastManager`（UDP 广播/多网卡发现/5s 刷新/`SO_REUSEADDR` 同机多开）、`LanMessageDeduplicator`（`MID_` 去重 60s 过期）、`LanPlayerManager`（按端点线程安全）；`HostedLANGame` 改 public（Domain 供 UI 解析 GAME 广播）
  - [x] 4.4.2 `LanLobbyService`（scoped）：进大厅 Start（UDP 1232 绑定 + ALIVE 5s 心跳 + 玩家/房间 10/20s 超时清理）、聊天（颜色索引 + `[Options] LANChatColor` 持久化）、GAME 广播解析进房间列表、QUIT/GAMECLOSED；`Program.cs` 注册 + `Random` 单例
  - [x] 4.4.3 `/lan/lobby` 页面：游戏列表（地图预览/筛选/详情/版本不符标记）+ 聊天（颜色选择）+ 玩家列表；主菜单「局域网」入口接线；app.css `.ci-color`/`.ver-mismatch`；构建 0 错误（验收见 `docs/acceptance-m4.4.md` D3.1）
  - [x] 4.4.4 D3.2 建/加入房间：`LanGameRoomService`（TCP 1233 大厅 + JOIN/QUIT + GAME/OPTS/POPTS/POREQ/READY/GETREADY/LAUNCH 协议；`ClientNoParamCommandHandler`/`HostedLANGame` 改 public）+ `LanGameNavigation`（大厅→房间参数传递）
  - [x] 4.4.5 D3.2 启动：spawn.ini（`[Tunnel]` 无：空隧道参数不写节）+ `GameProcessLogic` + LAN 玩家端口 1234（`LANPlayerInfo.Port=1234`，POPTS 解析 127.0.0.1→主机真实 IP）→ `LanGameLaunchService` + `/lan/loading` 加载等待页 + 窗口最小化/恢复
  - [~] 4.4.6 D3.2 `/lan/game` 页面（玩家/地图/规则/聊天/锁定/就绪/开始，已实现）+ 双端端到端验收；加载存档局（spawnSG.ini GameID 匹配）——**代码完成**，真机走查归 `docs/acceptance-run.md` §2（含 2.7 加载存档局）
- [~] 4.5 遭遇战入口与配置
  - [x] 4.5.1 `SpawnIniGenerator` AI 扩展（Domain）：`WriteSpawnIni` 新增 `aiPlayers` 参数，AI 并入 `houseInfos` 写入 `[AIPlayers]`/`[HouseHandicaps]`/`[HouseCountries]`/`[HouseColors]`/AI 出生点等节
  - [x] 4.5.2 `SkirmishService`（scoped，Infrastructure/）：状态管理（单个人类 + 动态 AI 列表）、`Client/SkirmishSettings.ini` 持久化（`Player`/`AIPlayers`/`Settings` 节，原版兼容格式）、游戏规则校验（单机限制/人数/出生点冲突）、`GameStartRequested` 事件；`Program.cs` 注册
  - [x] 4.5.3 `SkirmishLaunchService`（scoped，Infrastructure/）：订阅 `GameStartRequested` → 生成含 AI 的 spawn.ini/spawnmap.ini（无隧道、本机主机）→ `/skirmish/loading` → `GameProcessLogic.StartGameProcess()` → 按设置最小化宿主窗口；退出/失败恢复窗口回遭遇战页；`Program.cs` 注册
  - [x] 4.5.4 `/skirmish` 页面（`Skirmish.razor`）：地图/模式选择（分组下拉 + 地图预览 + 出生点图层）、人类玩家配置（阵营/颜色/出生点）、AI 玩家槽位（添加/移除/阵营/颜色/出生点/难度）、开始游戏（校验失败 toast）；app.css 样式
  - [x] 4.5.5 `/skirmish/loading` 加载等待页（`SkirmishLoading.razor`）：雷达式加载环 + 对局信息（模式/地图/玩家/GameID）+ 计时 + 返回
  - [x] 4.5.6 主菜单「遭遇战」入口接线（`MainMenu.razor` `case "skirmish"` → `/skirmish`）；构建 0 错误、0 新增 CS 警告；验收见 `docs/acceptance-m4.5.md`
  - [x] 4.5.6a 原版对照补齐：G1 游戏退出后重生成随机种子（`SkirmishService.RegenerateSeed` + `SkirmishLaunchService.OnGameProcessExited`）；G2 coop 地图强制全员 `TeamId=1` + `AutoSurrender=false`（`SpawnIniGenerator`）
  - [x] 4.5.7 后续补全（对照原版完整功能，**不裁剪**，按序推进）——G6/G3/G7/G5/G8/G9 已闭环；Discord Rich Presence 经核对 **spec 未要求**，判定超范围不接线
    - [x] G6 每槽队伍配置：人类 + 各 AI 队伍（TeamId）下拉 + TeamStartMapping 校验（`GetTeamMappingsError`）+ spawn.ini 联盟写入 + 自动结盟面板（启用开关/预设下拉/出生点映射编辑，`SkirmishService` + `Skirmish.razor`）
    - [x] G3 出生点随机：支持 `StartingLocation=0`（随机出生）与 `ForceRandomStartLocations` 图 → 由随机化分配（原版 `Randomize` 语义；`SpawnIniGenerator.RandomizeStart` + `SkirmishService.ForceRandomStarts` + `Skirmish.razor`「随机」选项/禁用）
    - [x] G7 游戏选项持久化：`SaveSkirmishGameOptions` 时读写 `[GameOptions]`（下拉/复选，GameMode/Map Forced 选项跳过）；`GameSessionOption` 模型（`GameSessionCheckBoxOption`/`GameSessionDropDownOption`，SpawnIniOption/CustomIniPath/Reversed/WriteMode）+ `SkirmishService.GameOptions`（12 复选 + 2 下拉，`SetCheckBoxOption`/`SetDropDownOption`/`IsGameOptionForced`）+ `SpawnIniGenerator` 选项写入与 `MapCodeHelper` 地图代码应用 + `Skirmish.razor` 游戏选项区 + `app.css` 样式
    - [x] G5 人类 Spectator 阵营：人类阵营下拉末项含 Spectator（AI 无）；coop 不可观战校验 + 观战不计入玩家数（min/max 校验）+ spawn.ini `IsSpectator`（`[Settings]`/`[IsSpectator]` MultiN 节 + 固定 waypoint 90 + SpectatorInternalSideIndex）+ 加载越界修复（人类可含观战索引，`>=`→`>`）；`SkirmishService.SpectatorSideIndex/IsHumanSpectator` + `SpawnIniGenerator` + `Skirmish.razor`
    - [x] G8 收藏地图：地图下拉星标 + 地图信息区「收藏此图/已收藏」切换按钮（`Skirmish.razor` OnToggleFavorite），复用原版语义——`UserINISettings.ToggleFavoriteMap`（settings INI `FavoriteMaps` 持久化，含 legacy 迁移）+ `GameModeMap.IsFavorite`（加载时经 `IsFavoriteMap` 注入）；app.css `.mi-star`；构建 0 错误
    - [x] G9 地图排名显示：遭遇战地图信息区「排名 ★☆☆」行（原版 GetDefaultMapRankIndex 语义），`StatisticsManager.GetSkirmishRankForDefaultMap`（Map.UntranslatedName × MaxPlayers，-1..2 → 0..3 星，金色 `★` + 灰 `☆`）；懒初始化一次 `ReadStatistics`（与统计页共用单例）；app.css `.mi-rank`；构建 0 错误
- [x] 4.6 战役/任务选择（BattleFS 解析）
  - [x] 4.6.1 `SpawnIniGenerator` 战役扩展（Domain）：`WriteMissionSpawnIni`（`[Settings]` Scenario/CampaignID/GameSpeed/Ra2Mode·Firestorm/CustomLoadScreen/IsSinglePlayer/SidebarHack/Side/BuildOffAlly/DifficultyModeHuman·Computer/CustomMissionID + `[GameMissionConfig]` 任务配置节 + `CampaignForcedSpawnIniOptions` 强制选项 + CopyMissionsToSpawnmapINI 时难度地图代码合并写 spawnmap.ini）+ `WriteMissionSectionToSpawnIni`（加载画面键缺失回退补充文件）+ 难度常量 `MissionDifficultyNames`/`MissionDifficultyIniPaths` + `GetComputerDifficulty` 取反 + `DeleteMissionSupplementalFiles`/`InitializeMissionSupplementFiles` 公开包装
  - [x] 4.6.2 `CampaignService`（scoped，Infrastructure/）：任务列表加载（`ParseBattleIni` INI/Battle.ini → 空则 BattleFS.ini → `LoadCustomMissions` 自定义任务）、`AddMission` 去重（重复 CustomMissionID 禁用）、选择器仅展示官方任务（同原版 filter）、任务选择/难度（`UserINISettings.Difficulty` 持久化）、启动校验（任务选择/地图配置/Enabled）、作弊检查（`Updater.IsFileNonexistantOrOriginal` + filesToCheck，ModMode 跳过）、`CheaterDetected`/`GameStartRequested` 事件；`Program.cs` 注册
  - [x] 4.6.3 `CampaignLaunchService`（scoped，Infrastructure/）：订阅 `GameStartRequested` → `WriteMissionSpawnIni` → `/campaign/loading` → `GameProcessLogic.StartGameProcess()` → 按设置最小化宿主窗口；退出恢复窗口回 `/campaign` + 清理自定义任务补充文件；启动失败弹框不重复导航；`Program.cs` 注册
  - [x] 4.6.4 `/campaign` 页面（`Campaign.razor`）：任务列表（header 标题/任务项/禁用项/EMPTY 空行，阵营色点 + FS 标签）、任务描述（名称/阵营/扩展包/说明）、难度分段选择（Easy/Medium/Hard + 电脑难度取反提示）、启动（校验/作弊弹窗确认放行）；app.css 样式
  - [x] 4.6.5 `/campaign/loading` 加载等待页（`CampaignLoading.razor`）：雷达式加载环 + 任务信息（任务/难度/阵营）+ 计时 + 返回
  - [x] 4.6.6 主菜单「战役」入口接线（`MainMenu.razor` `case "campaign"` → `/campaign`）；构建 0 错误、0 新增 CS 警告；验收见 `docs/acceptance-m4.6.md`
- [x] 4.7 附加窗口（地图编辑器、更新日志、鸣谢、关于）
  - [x] 4.7.1 `ExtrasService`（scoped，Infrastructure/）：附加窗状态（ShowAsync/BeginClose/CompleteClose 同 MessageBox 模式）+ `LaunchMapEditor`（按 `GetOperatingSystemVersion` 选 `MapEditorExePath`/`UnixMapEditorExePath`，`UseShellExecute=false` 启动外部进程，失败返回错误信息）+ `OpenUrl`（`ProcessLauncher.StartShellProcess` 开 `ChangelogURL`/`CreditsURL`）+ `GetAboutText`（游戏名/`GAME_VERSION`/引擎）；`Program.cs` 注册
  - [x] 4.7.2 `ExtrasWindow` 弹窗组件（`Components/Shared/ExtrasWindow.razor`，复用 `.msgbox` 骨架）：竖排五项（地图编辑器/更新日志/鸣谢/关于/取消），动作成功 toast+关闭、失败 toast 保持打开可重试；关于 → MessageBox 展示信息；`MainLayout.razor` 挂载宿主
  - [x] 4.7.3 主菜单「鸣谢」入口接线（`MainMenu.razor` `case "credits"` → `Extras.ShowAsync()`）；`app.css` `.exw-*` 样式（斜切按钮/图标块/英文副标/悬停左移微光）；构建 0 错误；验收见 `docs/acceptance-m4.7.md`
- [x] 4.8 统计窗口
  - [x] 4.8.1 `StatisticsService`（scoped，Infrastructure/）：包装 `StatisticsManager` 读取 dscore.dat（`Load`）+ 对局过滤（类别 All/Online/PvP/CoOp/Skirmish + 模式 + 包含观战局）+ 玩家明细（`SelectMatch` 按 Score 降序，观战/未完成局打 `-`）+ 总统计汇总（开始/完成/胜/负/胜负比/平均时长/总时长/敌友数/击杀/损失/经济/得分/常用阵营/平均 AI 难度）+ `ClearAll` 清空；`Program.cs` 注册
  - [x] 4.8.2 `/stats` 页面（`Statistics.razor`）：两 Tab（对局统计/总统计）+ 类别/模式/观战筛选工具栏 + 对局列表（时间/地图/模式/FPS/时长/完成，选中行高亮）+ 玩家明细（8 列，玩家颜色点 + 本地玩家青色高亮）+ 总统计两列 18 项 + 清空统计（MessageBox 确认）+ 返回主菜单；app.css `.stats-*` 样式（含 720p 紧凑适配）
  - [x] 4.8.3 主菜单「统计」入口接线（`MainMenu.razor` `case "stats"` → `/stats`）；构建 0 错误；验收见 `docs/acceptance-m4.8.md`
- [x] 4.9 性能压测：聊天/大厅高频 60fps；内存/冷启动达标
  - [x] 4.9.1 高频渲染节流（`Lobby.razor`）：网络事件（刷屏聊天/广播/玩家变动）经 66ms 窗口合并为一次 DOM 更新（`Interlocked` 标志 + 单发 `Timer`，滚动标记即时更新保证停在最新消息）；OnlineCount 懒计算缓存（Changed 时失效，避免每次渲染遍历全频道用户）；私聊/房间详情等低频事件保持即时
  - [x] 4.9.2 内存有界：私聊消息上限 300 条（`LobbyService.AddPmMessage`，同 `Channel.MESSAGE_LIMIT=1024` 削头语义）；聊天 1024（Channel 既有）+ 可见 300（既有）+ 房间/玩家 Virtualize（既有）
  - [x] 4.9.3 动画泄漏修复（`app.js`）：`initGlobe` rAF 在 canvas 移出 DOM 后停止（`cv.isConnected`），离开主菜单不再空转；星粒常驻 220 点 + reduced-motion 降级（既有）
  - [x] 4.9.4 既有已达标项核对：房间/玩家 `Virtualize`、地图预览 base64 缓存（`MapPreviewService`）、动效仅 transform/opacity、本地资源无 CDN、启动无同步阻塞（连接异步/配置惰性/地图后台）；构建 0 错误；验收见 `docs/acceptance-m4.9.md`
- [x] 4.10 出口条件：checklist 全勾核对（排除项除外）
  - [x] 4.10.1 逐项核对 checklist（A-G 全部条目）：逻辑层/UI/在线/LAN/单机/设置/更新/统计/多语言/非功能已完成项全部勾选 ✅（M1-M4.9 交付物映射）；排除项（XNAUI 迁移/WASM/多引擎矩阵/游戏内嵌渲染）保持 🚫
  - [x] 4.10.2 G4 密码日志缺口修复（已确认方案后执行）：`Connection.cs:310` 原始 IRC 消息日志经 `SanitizeForLog` 脱敏（PASS/JOIN+密码/MODE+k 参数 → `[REDACTED]`，其余保留），构建 0 错误
  - [x] 4.10.3 G2.2 星际2科幻风独立主题：裁决排除（theme-d 融合版已覆盖，M2.6 选型记录），checklist 标记 🚫
  - [x] 4.10.4 剩余工作清单（见 checklist 尾部）：A6 首发运行验收、C3 线程断言（或运行验收确认）、D1.7 动效逐项验收（M5.1）、D3.2 LAN 双端端到端验收、F1-F3 MOD 定制（M5 打磨，附加）

## M5 打磨发布

- [x] 5.1 动效打磨：对照 spec §5.5 动效清单逐项验收（时长/缓动统一、无卡顿、reduced-motion 降级）
  - [x] 5.1.1 逐项核对 §5.5 清单：hover/active/focus（mbtn 补 :active 微缩放）、页面切换 pageIn 240ms、弹窗/右键菜单 pop-in（ctx-menu/pm-panel/cr-panel/gd-panel/cmd-hint 均已挂）、关闭 pop-out、侧栏 slide-in-r（定义就绪）、连接状态脉冲、加载 spinner/雷达、Toast 240ms、表单错误 shake+红描边（补 `.lg-input.err`）、科幻专属（地球/星粒/极光 drift/sweep 扫光线/mbtn hover 6px 左移+扫光/呼吸灯）全部覆盖
  - [x] 5.1.2 token 统一：硬编码时长替换为 tokens（`--t-item`/`--t-fast`/`--t-micro`/`--t-toast`：mbtn/ic/go、toast、mb-btn、exw-btn、env-check .btn、theme-switch）；`.mbtn::after` 扫光 .4s 保留（非标准交互时长）；聊天消息入场刻意不做（高频虚拟化性能优先，M4.9 节流）
  - [x] 5.1.3 reduced-motion 降级生效（1048 行全局 `animation-duration:.001s!important` + 装饰性循环动画）；构建 0 错误；验收见 `docs/acceptance-m5.1.md`
- [x] 5.1.1 主题切换体验打磨（含星际2科幻风与默认主题切换过渡、MOD 主题目录）
  - [x] 主题切换过渡确认：`applyTheme` 触发 `body.theme-fade`（仅 opacity 240ms）已生效；星际2科幻风独立主题已裁决排除（G2.2，theme-d 覆盖）
  - [x] F1-F3 MOD 定制（附加项，checklist F）：全部落地——
    - F1 主题目录机制（`ClientDefinitions.ini [Themes]` + 主题目录换肤）：`ThemeProvider.LoadExternalThemes` 读 `[Themes]`（key=游戏根目录相对目录），目录含 `theme.css`（覆盖 design tokens 整体换肤，`url()` 相对资源经 `GameContentServer` 伺服）与可选 `theme.ini`（[Theme] Name/Hint）；与内置主题重名忽略；顶栏切换按钮自动出现
    - F2 关键页面组件可替换：`ReplaceableComponentService` 注册表 + `Components/Shared/ReplaceableComponent.razor` 槽位组件（`DynamicComponent` 渲染）；`MainLayout` 底栏改走 `layout.footer` 槽位（默认 `FooterBar`），`[Components]` 节 key=程序集内组件类名即可覆盖
    - F3 `ClientDefinitions.ini [UI]` 节生效：`UiDefinitionService` 懒读取（`DefaultTheme`/`HiddenMenus`/`GetString`/`GetBool`），主题初始回退链与 `HiddenMenus` 已接线
- [x] 5.2 打包脚本 + WebView2 运行时检测提示
  - [x] 5.2.1 离线资源最终核对：`wwwroot` 完整自包含（css/app.css、js/app.js、fonts/ 全本地；index.html 无外链），发布校验扫描 html/css/js 无外部 URL（排除框架常量：localhost 回退/SVG 命名空间/aka.ms 文档链接）
  - [x] 5.2.2 `tools/publish.ps1` 打包脚本（UTF-8 BOM）：`dotnet publish` Release/win-x64/自包含（`-SelfContained:$false` 切框架依赖、`-OutputDir` 自定义）→ 校验关键文件（exe/wwwroot/index.html/app.css/app.js/blazor.webview.js/字体）→ 外部 URL 扫描 → 体积汇总 → `dist/_publish-report.txt` 报告；实跑通过（411 文件 / 135MB / 无外部引用）
  - [x] 5.2.3 WebView2 运行时检测（`WebView2RuntimeGuard`，Infrastructure/）：启动先于窗口创建探测 Evergreen 默认安装目录（ProgramFiles(x86)/ProgramFiles/LocalAppData 下 EdgeWebView\Application，取最大版本目录），缺失时原生 `MessageBoxW` 弹窗（避免 WinForms 依赖）引导打开官方下载页后退出，避免 Photino 创建窗口才抛晦涩异常；Linux 自动放行（WebKit2GTK）；`Program.cs` 接线；构建 0 错误；验收见 `docs/acceptance-m5.2.md`
- [x] 5.3 自更新发布链路（服务器端清单/归档）演练
  - [x] 5.3.1 `tools/update-e2e` 工具（net10.0 控制台，引用 CnCNetClient.Updater）三子命令：
    - `stage`：发布目录 → 服务器端清单/归档（MD5 十进制标识符与 `Updater.GetUniqueIdForFile` 同算法；`[DTA] Version/UpdaterVersion/ManualDownloadURL` + `[FileVersions]`/`[ArchivedFiles]`；排除 .pdb/logs；`-archive` 生成 .lzma 归档；自动生成 `preupdateexec`/`updateexec` 空脚本——原版要求镜像根存在，缺失 404 会中止更新）
    - `verify`：闭环验证（构造临时游戏目录 + 进程内 HttpListener 本地镜像 + 驱动 Updater API：版本检查 → UPTODATE/OUTDATED 断言 → 下载/解压/标识符校验就位断言；`-scenario up-to-date|outdated`）
    - `serve`：本地静态服务器（GUI 人工验收）
  - [x] 5.3.2 `tools/publish.ps1 -StageUpdate` 集成：publish 后自动生成 `dist/updatestage`（`-ServerVersion` 指定版本）；`tools/serve-update.ps1` 薄包装 serve
  - [x] 5.3.3 演练实跑：stage 404 文件/135MB；verify up-to-date PASS（版本一致判 UPTODATE）；verify outdated PASS（404 文件全部下载+标识符校验就位）；发现并修复 preupdateexec 缺失导致更新中止的问题；验收见 `docs/acceptance-m5.3.md`
- [~] 5.4 Windows 发布候选验收；Linux 编译通过确认
  - [x] 启动加固（排查启动不出来）：窗口创建前初始化包 try/catch，异常落盘 Serilog + 原生 MessageBoxW 提示后退出（`NativeMessageBox.cs`），避免静默失败
  - [x] 日志自动清理（`LogCleanup.cs`）：connection.log 超 2MB 归档重开；logs 下超 14 天 `*.log` 兜底清除（Serilog 自身按天滚动保留 14 天）
  - [x] 输出类型改 `WinExe`（消除黑色控制台窗口，桌面分发标准）
  - [x] Linux 编译确认：逻辑类库（Core/Tools/Integration/Domain/Online/Updater）`-r linux-x64` Release 构建 0 错误（2026-09-09 实跑；NU1900 为既有 NuGet 漏洞数据缓存警告，与代码无关）
  - [x] C3 DEBUG 线程断言（checklist C3）：`InfiniFrameDispatcher.AssertUiThread`（Initialize 记录调度器线程，DEBUG 断言当前线程即 UI 线程）+ `GlobalExceptionHandler.ClassifyCrossThreadViolation`（Blazor「not associated with the Dispatcher」异常单独标记 C3 违规日志）；聊天/大厅已确认经 ComponentBase.InvokeAsync 调度；走查核对见 `docs/acceptance-run.md` §3.1
  - [ ] Windows 发布候选真机验收（发布后按 `docs/acceptance-run.md` 走查：A6 首发 / D3.2 LAN 双端 / C3 / D1.7）
- [x] 5.5 文档：运行/构建说明、MOD 定制 UI 说明（附加）
  - [x] `docs/build-guide.md`：环境要求/目录布局/构建（Debug、单测、publish.ps1 各参数）/部署（游戏目录树、原生 DLL、杀软提示）/运行期文件/常见问题
  - [x] `docs/mod-customization.md`：checklist F1-F3 定制说明——`ClientDefinitions.ini` 入口；F3 `[UI]`（DefaultTheme/HiddenMenus）；F1 `[Themes]` 主题目录（theme.css 覆盖 tokens + theme.ini 元信息 + GameContentServer 伺服 url() 资源 + 重名忽略规则）；F2 `[Components]` 槽位覆盖（layout.footer 默认 FooterBar；程序集内组件类名）；关联 `docs/splash-screen.md`
  - [x] 5.5.1 F2 接线补全：`MainLayout` 底栏改走可替换槽位（新增 `Components/Shared/ReplaceableComponent.razor`，`DynamicComponent` 渲染），`[Components] layout.footer` 覆盖真实生效；构建 0 错误
  - [x] 5.5.2 真机验收清单 `docs/acceptance-run.md`：A6 首发（启动链路/主菜单主题/在线核心/日志）+ D3.2 LAN 双端端到端（含加载存档局）+ C3 线程/健壮性 + D1.7 动效抽查 + 记录模板；checklist 出口条件已引用
  - [x] 5.5.3 用户迁移指南 `docs/migration-guide.md`（spec 附录 C 交付物）：迁移准备（备份/并存）、需迁移数据表（昵称/好友/设置/翻译/主题）、首次启动流程、FAQ（连接/语言/启动/好友/闪退/更新）、回退方案；spec 附录 B 本地化资源清单四项定稿（无 CSS 框架/无动画库/字体/内联图标，均本地无 CDN）
  - [x] 5.5.4 迁移指南修订（2026-09-10）：核实新旧客户端**共用数据文件**（`Client/friend_list` 等、`RA2MD.ini`、`Resources/Translations/`），迁移结论改为**零迁移**——数据表改「数据兼容性」、新增「新旧并行注意事项」（勿同时在线、后退出者胜）、FAQ 好友列表条目细化、回退方案更新；spec 附录 C（C.1–C.5）同步修订
- [~] 5.6 spec v0.8 扩展：个性化 UI（§6.9）+ INI→HTML/CSS 迁移 Skill（附录 D）
  - [x] spec 落文：§6.9 重写为可定制性总纲（L1 令牌覆盖 / L2 主题目录 / L3 组件槽位 / L4 外部组件 + 扩展性设计原则 + 用户个性化目录 L1）；新增附录 D（迁移对象映射表 / Skill 规格 / 与附录 C 分工 / 交付里程碑）（2026-09-10）
  - [x] 评审确认（2026-09-10）：**迁移 Skill 落地；L1 用户个性化待定**——spec 已标注（§6.9.1 表注 / §6.9.3 状态），以下 L1 落地项挂起
  - [ ] ~~L1 用户个性化目录落地~~（**待定挂起**）：`<用户配置目录>/UserCustomizations/theme.css` 加载（后于当前主题覆盖）+ `url()` 资源经 `GameContentServer` 伺服 + 校验容错 + 设置页「打开个性化目录」入口（M5 可选）
  - [ ] `docs/ui-tokens.md`：design tokens 变量全集（§5.4 A/B/C + 各组件用到的变量）——迁移 Skill 映射表（T1）的权威基线（同时是未来 L1 模板的预留）
  - [ ] 迁移 Skill：映射表 v1（T1 主题色→tokens / T2 UI 文案→Resx / T3 旧 ClientDefinitions 键→新键）+ 转换执行（读 INI → 产出 theme.css/新 ClientDefinitions.ini/Resx 骨架 + 迁移报告）+ 1 个真实 MOD 主题验收
  - [ ] `docs/migration-skill.md`：Skill 使用手册（输入/输出/报告解读/手改项示例）

---

## 6. 功能对齐补缺（原版 xna-cncnet-client 对照审查）

> 2026-09-09 与原版 `MainMenu.cs`/`CnCNetGameLobby.cs`/`PlayerInfo.cs` 逐项对照审查结论：
> 原版主菜单含 战役/载入/遭遇战/多人/局域网/设置/地图编辑器/统计/鸣谢/附加/退出，**无「资料库(lore)」**；
> 原版 `PlayerInfo` 无 Supporter 字段，**无赞助者标记功能**（无数据来源）。

### P0 · 加载游戏（已完成）
- [x] `/load` 页面（`LoadGame.razor`）+ `LoadGameService`：枚举 `Saved Games/*.SAV`（名称/修改时间/自定义任务 ID）、加载、删除；加载生成 `spawn.ini`/`spawnmap.ini`（`SpawnIniGenerator.WriteSaveGameSpawnIni`：含存档名称与加载标记）；`Program.cs` 注册

### P1 · 在线房间观战/AI 槽位 + 地图下载（已完成）
- [x] `GameRoomService`：`_aiPlayers` 集合、`SpectatorSideIndex`、PO 协议编解码（人类 3 字段 / AI 2 字段）、coop 不可观战与全员观战校验、房主 AI 增删/难度/阵营/出生点/队伍配置广播
- [x] `GameLobby.razor`：人类/AI/空槽/不可用槽差异化渲染、观战者标记与出生点禁用；`GameLaunchService` 传入 `aiPlayers` 生成 spawn.ini
- [x] 地图共享协议（MAPREQ/MAPOK/MAPFAIL/MAPSDISABLED）+ 缺图检测 + 下载提示条/状态管理（`MapDownloadState`）+ 自动选中

### P2 · 剩余项（已完成）
- [x] 主菜单「退出」：`MainMenu.razor` 菜单末项 `exit`，MessageBox 确认 → `IAppExitNotifier.NotifyExiting()` 持久化 → `Environment.Exit(0)`；与 F3 `HiddenMenus` 兼容（可隐藏）
- [x] 赞助者标记：对照原版确认**无此功能**（原版 `PlayerInfo` 无 Supporter 字段、无 PLAYERINFO 支持管道），不实现，保持隐藏（无代码改动）
- [x] lore 占位处理：**默认隐藏**——`UiDefinitionService.HiddenMenus` 缺省回退 `lore` + 分发 `ClientDefinitions.ini [UI] HiddenMenus=lore`；MOD 写 `HiddenMenus=`（空）或省略 lore 可重新显示（F3 定制保留）

---

## 7. 功能对齐补缺 · 第二轮（在线大厅/房间 + 主菜单区域）

> 2026-09-09 对照原版 `GameFiltersPanel.cs`/`GameLobbySettingsWindow.cs`/`GameLobbyBase.cs`/`CnCNetLobby.cs`/`MainMenu.cs` 复查结论。
> 已覆盖项：大厅玩家全局右键菜单（私聊/邀请/加友/屏蔽 + 管理员踢/封禁/禁言）、地图信息区 Briefing（近似原版 CoopBriefingBox）、隧道自动最优选择（原版 TunnelListBox 评分迁移）、房间锁定/人数/技能/地图/出生点/队伍/颜色/观战/AI、私聊多标签。

### P2 · 在线区域缺口（已完成）
- [x] 2-1 房间列表筛选面板（原版 `GameFiltersPanel`）：好友专用 / 隐藏锁定 / 隐藏密码 / 隐藏不兼容 / 最大人数 / 游戏选项筛选 —— 现仅文本搜索
- [x] 2-2 在线房间玩家槽位右键菜单（原版 `MultiplayerNameRightClicked` + `GlobalContextMenu`）：私聊 / 加友 / 屏蔽 + 房主 踢出/封禁/禁言 —— 房间内无右键菜单
- [x] 2-3 房主修改房间密码（原版 `GameLobbySettingsWindow` 含密码字段）—— 现仅建房时设密码，进房后不可改
- [x] 2-4 在线房间游戏选项面板（原版 `GameLobbyBase` 共享 `GameOptions.ini` + `LoadOrSaveGameOptionPresetWindow` 预设存取；GO 消息含游戏选项）—— 现仅遭遇战有游戏选项，在线房间 GO 为精简版（需协议扩展）
- [x] 2-5 地图列表右键菜单（原版 `mapContextMenu`：收藏 / 复制地图名 / 复制原名 / 删除地图 / 显示在文件夹）—— 现为原生下拉 select，无右键
- [x] 2-6 主菜单「地图编辑器」「附加功能」独立入口（原版 `btnMapEditor` + `btnExtras`）—— 现统一挂在「鸣谢」打开 ExtrasWindow

### 已核实无需补
- 赞助者标记 / lore：原版无（见 §6）
- 地图共享上传侧确认面板（原版 `MapSharingConfirmationPanel`）：小项，随 2-4 一并评估

### 第三轮补缺（已完成）
- [x] 2-4 配套：游戏选项预设存取（原版 `LoadOrSaveGameOptionPresetWindow` + `GameOptionPresets.ini`）—— 共享组件 `Components/Shared/GameOptionPresetDialog.razor`（载入/保存/删除，名称校验，删除确认）；遭遇战 `Skirmish.razor` 与在线房间 `GameLobby.razor`（仅房主）游戏选项面板头挂「预设」按钮；载入后遭遇战 `NotifyGameOptionsChanged` / 房间 `BroadcastGameOptionsRefresh` 一次性重广播 GO+GAME
- [x] 地图工具条（原版 `tbMapSearch` / `btnShowFavoriteMaps` / `btnMapSortAlphabetically` / `btnPickRandomMap`）—— 遭遇战与在线房间（房主）地图区：搜索框（名称/原名匹配）、仅收藏 ★、A–Z/Z–A/默认三态排序、随机图；过滤/排序后重建 select 索引保证回写一致

### 第四轮补缺（已完成）
- [x] 房间隧道手动选择（原版 `btnChangeTunnel` + `TunnelSelectionWindow` + `CHTNL` 协议）：房主房间顶栏「更换隧道」按钮 + 隧道选择模态（名称/国家码/负载/延迟，点击即选）；`GameRoomService.SelectTunnel` 本地生效 + 频道广播 `CHTNL address:port` + 重广播 GAME；加入者 `HandleTunnelServerChangeMessage` 匹配隧道并切换、无效隧道置 `TunnelErrorMode` 并提示「本局无法进行」；进房/建房/退房时清零错误态
- [x] 首次运行隐私声明（原版 `PrivacyNotification`）：主菜单未接受隐私政策前弹模态（说明 + 条款/隐私链接外开 + 「知道了」写入 `PrivacyPolicyAccepted` 并保存，对应设置→隐私已勾选项）

### 第五轮补缺（已完成）
- [x] 聊天链接可点击 + 安全校验（原版 `URLHandler.OpenLink` + `ChatListBox` 链接检测）：`ChatLinkService`（可信域名 `TrustedDomains` + `AlwaysTrustedDomains` 含子域名直开，非可信域名 YesNo 确认警告框）+ 共享组件 `Components/Shared/ChatText.razor`（与 Core `GetLinks` 同源正则拆分消息，链接渲染为可点击 `.chat-link`）；接入在线大厅/在线房间/LAN 大厅/LAN 房间/私聊消息渲染；`Program.cs` 注册
- [x] 游戏退出日志收集（原版 `GameInProgressWindow.HandleGameProcessExited`）：`GameLogCollector`（App.razor 注入实例化）——启动前清理 EXCEPT.TXT/SYNC*.TXT；退出后非 Ares：EXCEPT.TXT → Client/GameCrashLogs、SYNC*.TXT → Client/SyncErrorLogs（时间戳 + 删除原件）；Ares：debug snapshot 归档 + syringe.log 收集 + SCRN*.bmp → Screenshots/*.png（ImageSharp BMP→PNG）
- [x] 顺带修复：MainMenu.razor 隐私链接按钮 Razor 属性嵌套双引号导致源代码生成 CS1026（改单引号包裹表达式，此前增量构建未暴露）

### 第六轮补缺（已完成）
- [x] 游戏邀请（原版 `GameLobby.HandleGameInviteCommand` + `ChoiceNotificationBox` 邀请场景）：`LobbyService.GameInvitationReceived` 事件（按发送者去重、好友过滤、游戏名/房间名白名单校验）+ 全局邀请条 `Components/Shared/InvitationToast.razor`（MainLayout 挂载，接受前离房 + 加入对应房间，拒绝即关）；`GameRoomService` 迁入 `InvitePlayer`；房间内玩家槽右键「邀请加入」（`GameLobby.razor` 邀请对话框列出在线玩家候选）

### 第七轮补缺（已完成）
- [x] 房间内 `/roll` 骰子指令（原版 `GameLobbyRollDiceCommand` + `DR` 协议）：`GameRoomService` 解析 `/roll <n>d<s>`（n≤MAX_DICE、s≤MAX_DIE_SIDES），构造 `DR <面数>,<结果>...` 广播，全员房间消息展示掷骰结果
- [x] 房间内 `/tunnelinfo` 指令（原版 `GameLobby.PrintTunnelInformation`）：`GameRoomService` 输出当前隧道服务器地址/端口/负载
- [x] 房间内 `/downloadmap` 指令（原版 `GameLobbyDownloadMapCommand`）：`GameRoomService` 按 SHA1 查 `GameModeMap` 并触发 `MapSharer.DownloadMap` 下载（含地图名可选参数）
- [x] 房主不活跃检测（原版 `GameHostInactiveChecker` + `InactiveHostWarningMessageSeconds`/`InactiveHostKickSeconds`）：`GameRoomService` 定时器监控 + 页面 `mousemove`/`keydown` 节流上报（app.js `initHostActivityWatcher`）→ 超时弹「你还在吗？」警告（确认重置）→ 再超时自动离房
- [x] 私聊中心「最近玩家」表（原版 `PrivateMessagingWindow` 双标签 + `RecentPlayerTable`）：`LobbyService.RecentPlayers`（数据源 `CnCNetUserData.RecentList`，开始游戏时 `AddRecentPlayers` 记录）+ `Lobby.razor` 私聊/最近玩家双 tab（在线状态标记 + 右键菜单私聊/加友/屏蔽）
- [x] 顺带修复：`InvitationToast.razor` 引用 `LobbyService.GameInvitation` 编译失败 CS0426——`GameInvitation`/`PmSession` 等 record 实际位于类体外（命名空间级），改用直接引用；`GameLobby.razor` `InviteCandidates.Count()` 方法组、`OnHostInactiveWarning` 事件签名 `async void`、`LobbyService` 补 `CanInvite`/`InvitePlayer(ChannelUser)`（此前增量构建未暴露）

### 第八轮补缺（已完成）
- [x] 在线房间玩家附加选项（原版 `PlayerExtraOptionsPanel` + `PEO`/`GAME_PLAYERS_EXTRA_MESSAGE` 协议）：
  - `GameRoomService`：`ExtraOptions` 状态（强制随机阵营/颜色/出生点、无队伍、自动结盟）+ 房主 setter（`SetForceRandomSides`/`SetForceRandomColors`/`SetForceRandomStarts`/`SetForceNoTeams`/`SetUseTeamStartMappings`/`ApplyTeamStartMappingPreset`/`SetTeamStartMapping`，复用 `SkirmishService` 语义）+ `PlayerExtraOptionsChanged` 事件 + `BroadcastPlayerExtraOptions`（PEO CTCP，`GAME_PLAYERS_EXTRA_MESSAGE` 队列）+ 加入者接收 `HandlePlayerExtraOptions`（`PlayerExtraOptions.FromMessage` 容错解析）+ 换图重置（coop/ForceNoTeams 图强制关闭自动结盟/无队伍，同原版 `ChangeMap`）+ 新玩家加入 / `OR 0` 配置请求时一并下发 PEO
  - `GameLaunchService`：`WriteSpawnIni` 传入 `_room.GetActiveTeamStartMappings()`（未启用自动结盟返回空列表，spawn.ini 不写联盟）
  - `GameLobby.razor`：附加选项面板（`cfg-extra-options`）——5 个复选框（加入者只读、`IsUseTeamStartMappings` 启用时禁用「强制无队伍」）+ 自动结盟预设下拉（第 0 项自定义）+ 出生点→队伍映射网格（阻挡/无队伍/A..D，禁用不可用出生点）；订阅 `PlayerExtraOptionsChanged` 刷新
  - `app.css`：`eo-*` 样式（grid 布局、面板/单元格、禁用态）

### 第九轮补缺（已完成）
- [x] 主菜单 CnCNet 在线人数 + 更新状态链接（原版 `CnCNetPlayerCountTask` + `lblUpdateStatus`）：
  - `CnCNetPlayerCountTask`：解析 status API `total` 字段（`TotalPlayerCount`），首拉与刷新均放后台线程（避免闪屏期同步 HTTP 卡顿），新增 `CnCNetGameCountUpdated` 事件
  - `MainMenu.razor`：`#r1` 在线人数读数改由真实数据写入（app.js `setPlayerCount`），新增 `.updstrip` 更新状态链接（指示灯：ok/warn/err/busy + 可点击文案 + 版本号；OUTDATED 跳 `/settings?tab=update`；`Settings.razor` 支持 `?tab=` 查询参数直达分区）
- [x] 战役标签筛选器（原版 `CampaignTagSelector`，`ClientConfiguration.CampaignTagSelectorEnabled`）：`CampaignService` 新增 `ActiveTag`/`AvailableTags`/`SetTagFilter`（`RefreshVisibleMissions` 按标签过滤，含自定义任务 `CUSTOM` 标签）；`Campaign.razor` 标签行 UI（「全部」清除筛选）
- [x] ForceRandomStarts 联动（原版出生点选择器随 `ForceRandomStarts`/地图强制随机禁用）：`GameRoomService.StartLocationSelectionDisabled`（地图 `ForceRandomStartLocations` 或附加选项「强制随机出生点」）；`GameLobby.razor` 玩家出生点下拉/AI 出生点下拉/地图预览出生点点击（`.spawn-dot:disabled` 样式）三处禁用，`OnSpawnClick` 忽略点击
- [x] Discord Rich Presence 集成（原版 `DiscordHandler`，各窗口按场景 `UpdatePresence`）：
  - 新建 `DiscordPresenceService`：包装 `DiscordHandler`，提供 `MainMenu`/`Game`/`GameLoading`/`Skirmish`/`Campaign`/`SavedGame` 场景方法；监听 `SettingsChanged` 运行时启停（原版需重启生效）；`Program.cs` 注册 Scoped
  - 页面接入：`MainMenu`/`Lobby`（In Client）、`GameLobby.OnChanged`（In Lobby，含房主👑/密码🔑/锁定🔒）、`GameLoading`（Playing）、`Skirmish`（Skirmish，Spectator 无阵营小图标）、`Campaign`（Playing Mission）；设置页既有「Discord 集成」开关即时生效
- [x] 组件检查面板（原版 `ComponentsPanel`，`UpdaterConfig.ini [CustomComponents]`）：
  - `UpdateService`：暴露 `CustomComponents`/`IsComponentDownloadInProgress`/`GetComponentDownloadPercentage`，订阅各组件 `DownloadProgressChanged`/`DownloadFinished` 转发 `ComponentChanged`（组件实例在 `Initialize` 时固定，只订阅一次）
  - `Settings.razor` 新增「组件 COMPONENTS」tab：状态列表（未检查/未安装/可更新/已最新徽章）+ 单独「下载」按钮 + 下载进度条；「检查组件更新」复用主更新检查以填充远程标识
  - `app.css`：`cc-*` 样式（组件行、状态徽章、进度条）

---

## 备注

- 任务编号与 checklist 条目对应（如 3.x → D2）。
- M1 是最大不确定源头（DXGUI 业务逻辑抽取），若 M1 中发现某业务逻辑与 XNAUI 强耦合，需回 spec 评估补列抽取子任务。
- 逻辑类库复制自 `f:\work\PhotinoCLient\CncnetClient\xna-cncnet-client`（当前 HEAD），后续升级需手动同步并更新基线。
- **协作约定**：重大设计/结构决策先列方案讨论再落地；不擅自大规模重构；逻辑层改动先给改动清单（spec §0）。
