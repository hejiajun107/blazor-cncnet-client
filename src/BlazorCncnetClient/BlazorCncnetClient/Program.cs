using InfiniFrame;
using InfiniFrame.BlazorWebView;
using InfiniFrame.WebServer;
using Serilog;
using System.Drawing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorCncnetClient.Components;

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
                    .SetLocation(new Point(100, 100))
                    .SetSize(new Size(800, 600))
                    .EnableDevTools(true)
                    //.SetChromeless(true)
                    // .SetMaxSize(new Size(800, 600))
                    // .SetMinSize(new Size(600, 400))
                    ;
            });

            InfiniFrameBlazorApp app = appBuilder.Build();

            app.Run();
        }
    }
}
