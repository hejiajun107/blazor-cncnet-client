using System.Diagnostics;

namespace CnCNetClient.Launcher;

/// <summary>
/// CNCNet 客户端外壳启动器。
/// 目的：真实客户端（自包含发布）整体放入本目录下的 ClientApp\，以保持安装根目录整洁——
/// 根目录只留 Launcher.exe 这一个可执行（NativeAOT 单文件，无运行时依赖）。
///
/// 说明（目录定位）：
///  客户端不依赖本启动器传"安装根"——其 <c>FindGamePath</c> 会从自身 exe 目录向上最多 3 层
///  查找 Resources 目录取父目录为游戏根（与官方 xna-cncnet-client 同款契约）。故 ClientApp\
///  只要位于安装根（含 Resources）之下三层内，Resources/游戏路径/配置即自动落位。
///  工作目录设为 ClientApp\，使客户端用户数据（ClientSettings.ini、日志）与程序一同落这里
///  （避开官方用于写用户数据的 GamePath\Client\ 目录，防止被自更新整目录覆盖混搅）。
///
/// 职责（轻量、无业务逻辑）：
///  1. 拉起 ClientApp\BlazorCncnetClient.exe（工作目录=ClientApp\），转发本进程全部启动参数。
///  2. 等待其退出并透传退出码。
///  3. 客户端返回 <see cref="RestartExitCode"/>（自更新完成请求重启）时，重新拉起客户端。
/// </summary>
public static class Program
{
    /// <summary>客户端位于本启动器目录下的子目录名（避开官方/游戏用户数据目录 Client\）。</summary>
    private const string ClientDir = "ClientApp";

    /// <summary>客户端主程序文件名。</summary>
    private const string ClientExeName = "BlazorCncnetClient.exe";

    /// <summary>客户端请求重启时返回的退出码（勿与业务退出码冲突）。</summary>
    private const int RestartExitCode = 86;

    private static readonly string ClientExePath =
        Path.Combine(AppContext.BaseDirectory, ClientDir, ClientExeName);

    public static void Main(string[] args)
    {
        if (!File.Exists(ClientExePath))
        {
            Environment.ExitCode = 1;
            return;
        }

        // 客户端可能多次请求重启（更新→重启→再更新）。循环直到其以普通码退出。
        while (true)
        {
            int exit = LaunchClient(args);

            if (exit == RestartExitCode)
                continue;

            Environment.ExitCode = exit;
            return;
        }
    }

    /// <summary>
    /// 以本启动器目录为工作目录与 CNCNET_APP_ROOT，拉起客户端并等待退出，返回其退出码。
    /// 启动失败（进程无法创建/异常）返回 -1。
    /// </summary>
    private static int LaunchClient(string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(ClientExePath)
            {
                UseShellExecute = false,
                // 工作目录指向 ClientApp\，使客户端用户数据（ClientSettings.ini、日志）落程序目录。
                // GamePath 不依赖此值：客户端 FindGamePath 向上找 Resources 自有根法。
                WorkingDirectory = Path.Combine(AppContext.BaseDirectory, ClientDir),
                WindowStyle = ProcessWindowStyle.Normal,
            };

            // 透传本启动器收到的全部参数；避免 shell 转义，用 ArgumentList（含空格也安全）。
            foreach (string a in args)
                psi.ArgumentList.Add(a);

            using var proc = Process.Start(psi);
            if (proc is null)
                return -1;

            proc.WaitForExit();
            return proc.ExitCode;
        }
        catch
        {
            return -1;
        }
    }
}