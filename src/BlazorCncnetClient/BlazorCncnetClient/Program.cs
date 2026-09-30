using InfiniFrame;
using InfiniFrame.BlazorWebView;
using InfiniFrame.WebServer;
using Serilog;
using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorCncnetClient.Components;
using BlazorCncnetClient.Infrastructure;
using BlazorCncnetClient.Infrastructure.Theme;
using CnCNetClient.Core;
using CnCNetClient.Core.I18N;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Integration.Lifecycle;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Online;
using CnCNetClient.Tools;

namespace BlazorCncnetClient
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            var appBuilder = InfiniFrameBlazorAppBuilder.CreateDefault(args);

            appBuilder.Services.AddLogging(config =>
            {
                config.ClearProviders();
                config.AddSerilog();
            });

            appBuilder.Services.AddSerilog(config =>
            {
                // 2.8 日志落盘：logs/client-yyyyMMdd.log 滚动文件（保留 14 天），崩溃/错误可离线定位。
                // 路径放在应用目录下，避免写入保护目录；异步写入避免阻塞 UI。
                var logDir = Path.Combine(AppContext.BaseDirectory, "logs");
                config.WriteTo.Async(c => c.Console())
                    .WriteTo.Async(c => c.File(
                        Path.Combine(logDir, "client-.log"),
                        rollingInterval: RollingInterval.Day,
                        retainedFileCountLimit: 14,
                        shared: true,
                        flushToDiskInterval: TimeSpan.FromSeconds(1)))
                    .MinimumLevel.Debug();
            });

            // M5 日志清理：connection.log 无限追加，超限归档重开；logs 下超 14 天日志兜底清除
            // （须在 Logger.Initialize 之前执行，避免文件句柄冲突）
            LogCleanup.Run(Path.Combine(AppContext.BaseDirectory, "logs"));

            // CnCNetClient 内部 Logger 落盘（连接/网络诊断，与 Serilog 互补；原版 DXMainClient 亦输出 client.log）
            Logger.Initialize(Path.Combine(AppContext.BaseDirectory, "logs"), "connection.log");
            Logger.WriteLogFile = true;

            // M5.2 WebView2 运行时检测：Photino 渲染引擎依赖 WebView2 Runtime，
            // 先于窗口创建探测，缺失时原生弹窗引导下载后退出，避免创建窗口时才抛晦涩异常。
            if (!WebView2RuntimeGuard.EnsureAvailable())
                return;

            InfiniFrameBlazorApp app;
            AppExitNotifier exitNotifier;
            try
            {
            // M4.2 语言切换（spec §6.8，镜像原版 PreStartup）：先记录启动 UI 文化，
            // 供 UserINISettings.Initialize 计算 [Options] Translation 默认值时做系统匹配。
            Translation.InitialUICulture = CultureInfo.CurrentUICulture;

            // CnCNet 用户设置（ClientSettings.ini）：原版在主窗口初始化时调用 UserINISettings.Initialize。
            // 移植版在启动时初始化，否则创建 persistent Channel 时会访问 UserINISettings.Instance 并抛出
            // InvalidOperationException("UserINISettings not initialized!")，导致登录一直卡在「连接中」。
            UserINISettings.Initialize("ClientSettings.ini");

            // 按持久化语言（默认 zh-CN）设置 CurrentUICulture，保证首帧 Resx 即正确语言。
            try
            {
                CultureInfo.CurrentUICulture = new CultureInfo(UserINISettings.Instance.Translation.Value);
            }
            catch (CultureNotFoundException)
            {
                // 无效持久化区域 → 保持系统文化（UI Resx 回退 neutral=en）
            }

            // ── 宿主适配服务（M2.1）────────────────────────────────────────
            // IDispatcher：网络/后台线程 → UI 线程调度（替代 WindowManager.AddCallback）
            appBuilder.Services.AddSingleton<InfiniFrameDispatcher>();
            appBuilder.Services.AddSingleton<IDispatcher>(sp => sp.GetRequiredService<InfiniFrameDispatcher>());

            // IAppExitNotifier：应用退出前持久化（替代 windowManager.GameClosing）
            exitNotifier = new AppExitNotifier();
            appBuilder.Services.AddSingleton(exitNotifier);
            appBuilder.Services.AddSingleton<IAppExitNotifier>(exitNotifier);

            // 资源/路径提供（1.11 spec §4.2C/§6.5）：统一游戏目录定位，UI 层不再复制查找逻辑
            appBuilder.Services.AddSingleton<CnCNetClient.Integration.Resources.IAssetPathProvider,
                CnCNetClient.Integration.Resources.ProgramConstantsAssetPathProvider>();

            // 启动环境检测（M2.2）：定位游戏资源 + 初始化 ClientConfiguration
            appBuilder.Services.AddSingleton<EnvironmentCheck>();
            // 游戏运行文件引导：检测 GamePath 缺失的必要 YR 文件，缺失时首页引导选择完整目录并提取
            appBuilder.Services.AddSingleton<GameFileSetupService>();
            // 游戏渲染器管理（渲染器设置）：读 Renderers.ini 按 OS 兼容选择 ddraw 渲染器，Save() 应用配置。
            // 依赖游戏资源（Renderers.ini 在 Resources），环境未就绪时构造抛 ClientConfigurationException →
            // 工厂兜底返回 null，设置页渲染器下拉在不可用时禁用（GetService 走 null，不阻断页面）。
            appBuilder.Services.AddSingleton<CnCNetClient.Domain.DirectDrawWrapperManager>(_ =>
            {
                try
                {
                    return new CnCNetClient.Domain.DirectDrawWrapperManager();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "渲染器列表加载失败（游戏资源未就绪），渲染器选择暂不可用");
                    return null!;
                }
            });

            // 主题系统（M2.5）：design tokens（CSS 变量）+ IThemeProvider + 运行时切换 + localStorage 持久化
            // 注：scoped 注册，与组件共享同一 WebView 上下文的 IJSRuntime。
            appBuilder.Services.AddScoped<IThemeProvider, ThemeProvider>();
            // F3：ClientDefinitions.ini [UI] 节 UI 扩展配置（MOD 可定制）
            appBuilder.Services.AddScoped<UiDefinitionService>();
            // F2：可替换组件注册表（ClientDefinitions.ini [Components] 覆盖 → 槽位组件类型）
            appBuilder.Services.AddScoped<ReplaceableComponentService>();

            // 全局弹窗（M2.7）：MessageBoxService 驱动 <MessageBox /> 宿主组件，供任意页面 ShowAsync 等待确认
            appBuilder.Services.AddScoped<MessageBoxService>();

            // 启动闪屏（M5 2.3 LoadingScreen）：ClientDefinitions.ini [LoadingScreen] 配置 +
            // 进程内本地内容服务器（独立文件 URL 加载游戏目录媒体，视频支持 Range，不打包 base64）
            appBuilder.Services.AddScoped<LoadingScreenService>();
            appBuilder.Services.AddSingleton<GameContentServer>();

            // 全局异常兜底（M2.8）：Serilog 落盘定位崩溃 + 迁移 DisplayErrorAction 语义（MessageBox 呈现错误、exit 时退出）
            appBuilder.Services.AddScoped<GlobalExceptionHandler>();

            // 登录浮窗（M3.1 spec §5.3.1）：LoginService 驱动 <LoginModal /> 宿主组件
            appBuilder.Services.AddScoped<LoginService>();

            // 附加功能窗（M4.7）：ExtrasService 驱动 <ExtrasWindow /> 宿主组件（地图编辑器/更新日志/鸣谢/关于）
            appBuilder.Services.AddScoped<ExtrasService>();

            // 聊天链接安全打开（第五轮补缺，原版 URLHandler）：可信域名直开、非可信确认
            appBuilder.Services.AddScoped<ChatLinkService>();

            // 游戏进程日志收集（第五轮补缺，原版 GameInProgressWindow）：崩溃/同步错误日志归档 + 截图转 PNG；
            // App.razor 注入实例化以订阅 GameProcessLogic 静态事件
            appBuilder.Services.AddScoped<GameLogCollector>();

            // Discord Rich Presence（r9-4，原版 DiscordHandler）：包装 DiscordHandler 按页面更新呈现，
            // 设置页「Discord 集成」开关经 SettingsChanged 运行时启停。
            appBuilder.Services.AddScoped<DiscordPresenceService>();

            // 设置中心（M4.1 spec §5.3/§6.10）：包装 UserINISettings（settings INI），
            // 提供变更即存 Save() 与 SettingsChanged；退出兜底订阅 IAppExitNotifier。
            appBuilder.Services.AddScoped<SettingsService>();

            // 语言切换（M4.2 spec §6.8）：运行时切 Culture（UI Resx）+ 重载 INI 游戏翻译 +
            // 持久化 [Options] Translation。scoped，与组件共享同一 WebView 上下文。
            appBuilder.Services.AddScoped<ITranslationService, TranslationService>();

            // 自动更新（M4.3 spec §5.3/D6）：包装静态 CnCNetClient.Updater.Updater，
            // 懒初始化（环境就绪后）→ 启动检查/手动查询/下载解压；Restart 时持久化并退出。
            appBuilder.Services.AddScoped<UpdateService>();

            // 在线连接（M3.1）：CnCNetManager 以工厂注册并懒实例化——
            // 其依赖链（GameCollection 依赖 ClientConfiguration 的 LocalGame）在环境检测后才就绪，
            // 首次发起登录/自动连接时（此时已在主菜单）才解析构造。Random 显式提供（DI 无法解析）。
            appBuilder.Services.AddSingleton<GameCollection>();
            appBuilder.Services.AddSingleton<CnCNetUserData>();
            appBuilder.Services.AddSingleton<CnCNetManager>(sp =>
                new CnCNetManager(
                    sp.GetRequiredService<IDispatcher>(),
                    sp.GetRequiredService<GameCollection>(),
                    sp.GetRequiredService<CnCNetUserData>(),
                    new Random()));

            // 隧道服务器（M3.11 D2.3）：刷新/选择隧道列表并为多人对局下发端口；
            // 随 CnCNetManager 连接生命周期单例运行（Connected 自动 Start，懒实例化时若已连接也立即启动）。
            appBuilder.Services.AddSingleton<TunnelHandler>();

            // 在线大厅（M3.2 spec §5.3.2）：聚合频道/聊天/房间/玩家状态，供 /online/lobby 读取
            appBuilder.Services.AddScoped<LobbyService>();

            // LAN 大厅（M4.4 D3.1 spec §5.3 LAN）：UDP 广播发现 + 游戏列表 + 聊天 + 玩家，
            // 供 /lan/lobby 读取；进大厅 Start、离开 Stop。
            // Random：LanLobbyService 构造注入（消息去重种子），显式注册（DI 无法解析）。
            appBuilder.Services.AddSingleton<Random>();
            appBuilder.Services.AddScoped<LanLobbyService>();

            // LAN 游戏房间（M4.4 D3.2）：TCP 1233 大厅通信 + GAME/OPTS/POPTS/POREQ/READY/GETREADY/LAUNCH，
            // 供 /lan/game 读取；依赖 LanLobbyService 复用 UDP 广播 socket 转发 GAME/GAMECLOSED。
            appBuilder.Services.AddScoped<LanGameRoomService>();

            // LAN 大厅 → LAN 房间页导航参数（创建/加入请求），scoped 随电路存续。
            appBuilder.Services.AddScoped<LanGameNavigation>();

            // LAN 游戏启动协调（M4.4 D3.2）：生成无隧道 spawn.ini + 启动游戏进程 + 最小化/恢复窗口 + 跳转加载页。
            // scoped 随电路存续；注入 LanGame.razor 触发实例化，从而订阅 LanGameRoomService.GameStartRequested。
            appBuilder.Services.AddScoped<LanGameLaunchService>();

            // 连接状态监视（M3.10 D2.7）：断线/重连状态机 + 通知，供全局顶栏状态灯与 Toast
            appBuilder.Services.AddScoped<ConnectionMonitor>();

            // 地图加载/预览（M3.4 spec §6.4）：MapLoader 单例持全量地图集合（官方 + 自定义），
            // 首次被解析时后台 LoadMapsAsync；MapPreviewService 单例复用其提取/缓存，
            // 预览图 base64 缓存跨页面导航保留。均不依赖 UI 线程，可安全单例。
            appBuilder.Services.AddSingleton<MapLoader>();
            appBuilder.Services.AddSingleton<MapPreviewService>();

            // 游戏房间（M3.5 spec §5.3）：建房间/加入/离开 + 房间内协议（GAME/GSETTINGS/GO/PO/OR/R/START）。
            // 依赖 CnCNetManager/MapLoader 等单例，状态按会话（scoped）隔离。
            appBuilder.Services.AddScoped<GameRoomService>();

            // 大厅 → 游戏房间页导航参数（创建/加入请求），scoped 随电路存续。
            appBuilder.Services.AddScoped<GameRoomNavigation>();

            // 游戏启动协调（M3.7 spec §5.3）：生成 spawn.ini、启动游戏进程、最小化/恢复窗口、跳转加载页。
            // scoped 随电路存续；注入 GameLobby.razor 触发实例化，从而订阅 GameStartRequested。
            appBuilder.Services.AddScoped<GameLaunchService>();

            // 遭遇战（M4.5 spec §5.3）：单个人类玩家 + AI 玩家本地对局。
            // SkirmishService：状态管理 + Client/SkirmishSettings.ini 持久化 + 校验 + GameStartRequested。
            // SkirmishLaunchService：生成含 AI 的 spawn.ini + 启动游戏进程 + 窗口管理（注入 Skirmish.razor 触发订阅）。
            appBuilder.Services.AddScoped<SkirmishService>();
            appBuilder.Services.AddScoped<SkirmishLaunchService>();

            // 战役（M4.6 spec §5.3）：单机任务选择（Battle.ini / BattleFS.ini 解析 + 自定义任务）。
            // CampaignService：任务列表/选择/难度（UserINISettings 持久化）+ 作弊检查 + GameStartRequested。
            // CampaignLaunchService：生成任务 spawn.ini + 启动游戏进程 + 窗口管理（注入 Campaign.razor 触发订阅）。
            appBuilder.Services.AddScoped<CampaignService>();
            appBuilder.Services.AddScoped<CampaignLaunchService>();

            // 加载单机存档（原版 GameLoadingWindow）：枚举 Saved Games/*.SAV → 生成加载 spawn.ini → 启动/删除。
            appBuilder.Services.AddScoped<LoadGameService>();

            // 统计窗口（M4.8 spec §5.3，原版 StatisticsWindow）：包装 StatisticsManager 读取 dscore.dat，
            // 提供对局列表过滤（类别/模式/观战）、玩家明细、总统计汇总、清空统计，供 /stats 页面读取。
            appBuilder.Services.AddScoped<StatisticsService>();

            // register the root component and selector
            appBuilder.RootComponents.Add<App>("app");

            // M4.3 D6.2：Global 互斥锁 —— SecondStageUpdater 靠等待它获知主程序已退出，
            // 随后才替换更新文件并重启客户端。GUID 与原版 DXMainClient/Program.cs 保持一致。
            // 持有引用到 Run() 返回，进程存活期间不释放（释放即视为退出）。
            var clientMutex = new System.Threading.Mutex(false, "Global{1CC9F8E7-9F69-4BBC-B045-E734204027A9}");


            appBuilder.WithInfiniFrameWindowBuilder(builder => {
                // M 设置对齐旧客户端：客户端窗口分辨率读 ClientSettings.ini [Video] ClientResolutionX/Y
                // （原版 GameClass.SetGraphicsMode 语义），下限保持 1280x720（窗口最小尺寸）。
                var clientResX = Math.Max(1280, (int)UserINISettings.Instance.ClientResolutionX);
                var clientResY = Math.Max(720, (int)UserINISettings.Instance.ClientResolutionY);
                builder
                    // .SetTransparent(true)
                    // .SetChromeless(true)
                    // .SetResizable(true)
                    .SetIconFile("wwwroot/favicon.ico")
                    .SetWindowsAppUserModelId("InfiniLore.InfiniFrame.BlazorCNCNetClient")
                    // .Center()
                    // .SetUseOsDefaultSize(true)
                    // .SetUseOsDefaultLocation(true);
                    .SetTitle("CNCNet Client")
                    // 用 int 重载，避免宿主层引入 System.Drawing
                    .SetLocation(100, 100)
                    .SetSize(clientResX, clientResY)
                    .SetMinSize(1280, 720) // 限制窗口最小尺寸（适配 1280*720 / 1920*1080）
                    .EnableDevTools(true)
                    //.SetChromeless(true)
                    // .SetMaxSize(new Size(1280, 720))
                    ;
            });

            app = appBuilder.Build();
            }
            catch (Exception ex)
            {
                // 启动加固（M5 排查）：窗口创建前的任何初始化失败都不再静默——
                // 落盘 Serilog + 原生 MessageBoxW 提示（WebView 未就绪只能用原生），再非零退出。
                Log.Fatal(ex, "客户端启动初始化失败（窗口创建前），已终止启动");
                NativeMessageBox.Show(
                    "启动失败",
                    "客户端初始化失败，无法启动。\r\n\r\n" +
                    ex.Message +
                    "\r\n\r\n详细错误已写入日志：logs\\client-*.log",
                    error: true);
                Environment.Exit(1);
                return;
            }

            try
            {
                // M5.6 地图集合预热：启动即后台加载官方 + 自定义地图。
                // 否则懒加载首次触发在进入遭遇战/房间页时（MapThumbnail → MapPreviewService），
                // 333+ 张地图解析会与页面渲染抢 CPU，导致进入遭遇战页明显卡顿。
                try
                {
                    // InfiniFrameBlazorApp 暴露的是 ServiceProvider（IServiceProvider），而非 Services（后者在 Builder 上）。
                    var mapLoader = app.ServiceProvider.GetRequiredService<MapLoader>();
                    _ = mapLoader.LoadMapsAsync();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "地图集合预热启动失败（进入遭遇战页时仍会懒加载兜底）");
                }

                app.Run();
            }
            finally
            {
                // 窗口已关闭（Run 返回），在进程退出前通知订阅方持久化状态。
                exitNotifier.NotifyExiting();
            }
        }
    }
}
