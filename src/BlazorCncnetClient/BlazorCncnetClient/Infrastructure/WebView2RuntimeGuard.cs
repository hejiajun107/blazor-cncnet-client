using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using Serilog;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// M5.2 WebView2 运行时检测：Photino/InfiniFrame 渲染引擎依赖 WebView2 Runtime。
/// 在创建窗口前先探测运行时是否存在，缺失时用原生对话框提示并引导下载，
/// 避免 Photino 创建窗口时抛出晦涩的「未找到 WebView2」异常。
/// 探测以 EdgeUpdate 注册表项为准（官方判定方式），目录扫描仅作兜底，
/// 避免自定义安装路径 / 仅当前用户安装时误判为「未安装」。
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

        RuntimeVersion ??= ProbeRegistry() ?? ProbeInstallDirectories();

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

        string message = UiText.T("Startup.WebView2MissingMessage",
            "Microsoft Edge WebView2 Runtime was not detected.\r\n\r\n" +
            "The CNCNet client interface is rendered by WebView2, which is required to start.\r\n" +
            "Open the official download page to install it now? (Windows 11 usually ships with it)");
        int result = MessageBoxW(IntPtr.Zero, message,
            UiText.T("Startup.WebView2MissingTitle", "WebView2 Runtime Missing"),
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

    /// <summary>WebView2 Evergreen Runtime 的 EdgeUpdate 产品 GUID。</summary>
    private const string ClientGuid = "{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}";

    /// <summary>
    /// 从 EdgeUpdate 注册表读取已安装版本（官方推荐判定方式）：
    /// HKLM（32/64 视图）与 HKCU 各一处，pv 为空视为未安装。
    /// </summary>
    private static string? ProbeRegistry()
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var locations = new (RegistryHive Hive, RegistryView View)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Default),
        };

        string? best = null;
        foreach (var (hive, view) in locations)
        {
            try
            {
                using RegistryKey? baseKey = RegistryKey.OpenBaseKey(hive, view);
                using RegistryKey? clientKey = baseKey?.OpenSubKey(@"SOFTWARE\Microsoft\EdgeUpdate\Clients\" + ClientGuid);
                string? version = clientKey?.GetValue("pv") as string;
                if (!string.IsNullOrWhiteSpace(version))
                {
                    if (best is null || string.Compare(version, best, StringComparison.OrdinalIgnoreCase) > 0)
                        best = version;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "读取 WebView2 注册表失败：{Hive}/{View}", hive, view);
            }
        }

        return best;
    }

    /// <summary>
    /// 兜底：扫描 WebView2 Runtime 默认安装目录（机器级/用户级），
    /// 仅认可含 msedgewebview2.exe 的版本目录，避免残留空目录造成误判。
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
            try
            {
                if (!Directory.Exists(root))
                    continue;

                foreach (string dir in Directory.GetDirectories(root))
                {
                    string name = Path.GetFileName(dir);
                    if (name.Length == 0 || name[0] < '0' || name[0] > '9')
                        continue;
                    if (!File.Exists(Path.Combine(dir, "msedgewebview2.exe")))
                        continue;

                    if (best is null || IsNewerVersion(name, best))
                        best = name;
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "扫描 WebView2 目录失败：{Root}", root);
            }
        }

        return best;
    }

    /// <summary>按各段数值比较版本号（避免 "9.x" 与 "130.x" 按字符串比较出错）。</summary>
    private static bool IsNewerVersion(string candidate, string current)
    {
        if (Version.TryParse(candidate, out Version? a) && Version.TryParse(current, out Version? b))
            return a > b;
        return string.Compare(candidate, current, StringComparison.OrdinalIgnoreCase) > 0;
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
