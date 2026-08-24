using InfiniFrame;
using InfiniFrame.BlazorWebView;
using InfiniFrame.WebServer;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorCncnetClient.Components;
using BlazorCncnetClient.Infrastructure;
using CnCNetClient.Integration.Lifecycle;
using CnCNetClient.Integration.Threading;

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
                config.WriteTo.Async(static c => c.Console())
                    .MinimumLevel.Debug();
            });

            // ── 宿主适配服务（M2.1）────────────────────────────────────────
            // IDispatcher：网络/后台线程 → UI 线程调度（替代 WindowManager.AddCallback）
            appBuilder.Services.AddSingleton<InfiniFrameDispatcher>();
            appBuilder.Services.AddSingleton<IDispatcher>(sp => sp.GetRequiredService<InfiniFrameDispatcher>());

            // IAppExitNotifier：应用退出前持久化（替代 windowManager.GameClosing）
            var exitNotifier = new AppExitNotifier();
            appBuilder.Services.AddSingleton<IAppExitNotifier>(exitNotifier);

            // 启动环境检测（M2.2）：定位游戏资源 + 初始化 ClientConfiguration
            appBuilder.Services.AddSingleton<EnvironmentCheck>();

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
                    // .SetTitle("InfiniLore InfiniFrame.Blazor Sample")
                    // 用 int 重载，避免宿主层引入 System.Drawing
                    .SetLocation(100, 100)
                    .SetSize(800, 600)
                    .EnableDevTools(true)
                    //.SetChromeless(true)
                    // .SetMaxSize(new Size(800, 600))
                    // .SetMinSize(new Size(600, 400))
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
