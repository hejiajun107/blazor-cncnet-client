using InfiniFrame;
using InfiniFrame.BlazorWebView;
using InfiniFrame.WebServer;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorCncnetClient.Components;
using BlazorCncnetClient.Infrastructure;
using BlazorCncnetClient.Infrastructure.Theme;
using CnCNetClient.Core;
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

            // CnCNetClient 内部 Logger 落盘（连接/网络诊断，与 Serilog 互补；原版 DXMainClient 亦输出 client.log）
            Logger.Initialize(Path.Combine(AppContext.BaseDirectory, "logs"), "connection.log");
            Logger.WriteLogFile = true;

            // CnCNet 用户设置（ClientSettings.ini）：原版在主窗口初始化时调用 UserINISettings.Initialize。
            // 移植版在启动时初始化，否则创建 persistent Channel 时会访问 UserINISettings.Instance 并抛出
            // InvalidOperationException("UserINISettings not initialized!")，导致登录一直卡在「连接中」。
            UserINISettings.Initialize("ClientSettings.ini");

            // ── 宿主适配服务（M2.1）────────────────────────────────────────
            // IDispatcher：网络/后台线程 → UI 线程调度（替代 WindowManager.AddCallback）
            appBuilder.Services.AddSingleton<InfiniFrameDispatcher>();
            appBuilder.Services.AddSingleton<IDispatcher>(sp => sp.GetRequiredService<InfiniFrameDispatcher>());

            // IAppExitNotifier：应用退出前持久化（替代 windowManager.GameClosing）
            var exitNotifier = new AppExitNotifier();
            appBuilder.Services.AddSingleton(exitNotifier);
            appBuilder.Services.AddSingleton<IAppExitNotifier>(exitNotifier);

            // 启动环境检测（M2.2）：定位游戏资源 + 初始化 ClientConfiguration
            appBuilder.Services.AddSingleton<EnvironmentCheck>();

            // 主题系统（M2.5）：design tokens（CSS 变量）+ IThemeProvider + 运行时切换 + localStorage 持久化
            // 注：scoped 注册，与组件共享同一 WebView 上下文的 IJSRuntime。
            appBuilder.Services.AddScoped<IThemeProvider, ThemeProvider>();

            // 全局弹窗（M2.7）：MessageBoxService 驱动 <MessageBox /> 宿主组件，供任意页面 ShowAsync 等待确认
            appBuilder.Services.AddScoped<MessageBoxService>();

            // 全局异常兜底（M2.8）：Serilog 落盘定位崩溃 + 迁移 DisplayErrorAction 语义（MessageBox 呈现错误、exit 时退出）
            appBuilder.Services.AddScoped<GlobalExceptionHandler>();

            // 登录浮窗（M3.1 spec §5.3.1）：LoginService 驱动 <LoginModal /> 宿主组件
            appBuilder.Services.AddScoped<LoginService>();

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

            // 在线大厅（M3.2 spec §5.3.2）：聚合频道/聊天/房间/玩家状态，供 /online/lobby 读取
            appBuilder.Services.AddScoped<LobbyService>();

            // register the root component and selector
            appBuilder.RootComponents.Add<App>("app");


            appBuilder.WithInfiniFrameWindowBuilder(builder => {
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
                    .SetSize(1280, 720)
                    .SetMinSize(1280, 720) // 限制窗口最小尺寸（适配 1280*720 / 1920*1080）
                    .EnableDevTools(true)
                    //.SetChromeless(true)
                    // .SetMaxSize(new Size(1280, 720))
                    ;
            });

            InfiniFrameBlazorApp app = appBuilder.Build();

            try
            {
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
