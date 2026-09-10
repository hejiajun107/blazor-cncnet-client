using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// M5.2 WebView2 运行时检测：Photino/InfiniFrame 渲染引擎依赖 WebView2 Runtime。
/// 在创建窗口前先探测运行时是否存在，缺失时用原生对话框提示并引导下载，
/// 避免 Photino 创建窗口时抛出晦涩的「未找到 WebView2」异常。
/// 探测仅依赖 BCL（目录扫描），不引入注册表 API / WinForms 依赖。
/// </summary>
public static class WebView2RuntimeGuard
{
    /// <summary>WebView2 Evergreen Runtime 官方下载页（独立安装器）。</summary>
    private const string DownloadUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";

    /// <summary>检测到的 WebView2 运行时版本号（目录名）；未检测到为 null。</summary>
    public static string? RuntimeVersion { get; private set; }

    /// <summary>WebView2 运行时是否可用。</summary>
    public static bool IsAvailable => RuntimeVersion is not null;

    /// <summary>
    /// 探测 WebView2 运行时（幂等，可重复调用）。
    /// 仅 Windows 有效；其他平台（Linux WebKit2GTK）直接返回 true。
    /// </summary>
    public static bool Detect()
    {
        if (!OperatingSystem.IsWindows())
        {
            RuntimeVersion = null;
            return true;
        }

        RuntimeVersion ??= ProbeInstallDirectories();

        if (IsAvailable)
            Log.Information("WebView2 Runtime 检测到：{Version}", RuntimeVersion);
        else
            Log.Warning("WebView2 Runtime 未检测到（Evergreen 默认安装目录均无版本目录）");
        return IsAvailable;
    }

    /// <summary>
    /// 确保 WebView2 可用：可用返回 true；缺失时弹原生对话框提示并引导下载，返回 false（调用方应退出进程）。
    /// </summary>
    public static bool EnsureAvailable()
    {
        if (Detect())
            return true;

        const string message =
            "未检测到 Microsoft Edge WebView2 Runtime。\r\n\r\n" +
            "CNCNet 客户端的界面由 WebView2 渲染，需要该运行时才能启动。\r\n" +
            "是否立即打开官方下载页面安装？（Windows 11 一般已预装）";
        int result = MessageBoxW(IntPtr.Zero, message, "缺少 WebView2 Runtime",
            MbOkCancel | MbIconWarning | MbTopmost | MbSetForeground);

        if (result == IdOk)
        {
            try
            {
                Process.Start(new ProcessStartInfo(DownloadUrl) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "打开 WebView2 下载页失败：{Url}", DownloadUrl);
            }
        }

        return false;
    }

    /// <summary>
    /// 扫描 WebView2 Runtime 默认安装目录（Evergreen：机器级/用户级），取版本号最大的目录名。
    /// 目录名形如 "130.0.2849.56"。
    /// </summary>
    private static string? ProbeInstallDirectories()
    {
        string[] roots =
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft", "EdgeWebView", "Application"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft", "EdgeWebView", "Application"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "EdgeWebView", "Application"),
        };

        string? best = null;
        foreach (string root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (string dir in Directory.GetDirectories(root))
            {
                string name = Path.GetFileName(dir);
                if (name.Length > 0 && name[0] >= '0' && name[0] <= '9')
                {
                    if (best is null || string.Compare(name, best, StringComparison.OrdinalIgnoreCase) > 0)
                        best = name;
                }
            }
        }

        return best;
    }

    // ── 原生对话框（启动引导场景：WebView 尚不可用，仅此一处使用原生 API）──

    private const uint MbOkCancel = 0x1;
    private const uint MbIconWarning = 0x30;
    private const uint MbTopmost = 0x40000;
    private const uint MbSetForeground = 0x10000;
    private const int IdOk = 1;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint uType);
}
