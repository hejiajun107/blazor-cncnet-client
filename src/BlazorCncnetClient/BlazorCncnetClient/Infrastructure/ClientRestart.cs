using System;
using System.Diagnostics;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 客户端进程重启（原版 <c>WindowManager.RestartGame</c> / <c>Application.Restart</c> 的宿主替代）。
/// 由界面层在用户确认「立即重启」后调用：拉起同路径的新实例，再终止当前进程。
/// 当前进程退出后单实例互斥锁释放，新实例最多等待 8 秒即可继续启动。
/// </summary>
internal static class ClientRestart
{
    /// <summary>
    /// 请求重启当前客户端。调用方须先持久化（<see cref="AppExitNotifier.NotifyExiting"/>）。
    /// 成功拉起新实例后本方法直接终止进程，不再返回；拉起失败时保留当前实例继续运行。
    /// </summary>
    public static void Restart()
    {
        string? exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
            return;

        try
        {
            var startInfo = new ProcessStartInfo(exePath)
            {
                // 工作目录与当前进程一致，ClientSettings.ini/日志仍落在原位置。
                WorkingDirectory = AppContext.BaseDirectory,
            };

            // 透传启动参数（与原版 Application.Restart 保持一致的运行上下文）。
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 1; i < args.Length; i++)
                startInfo.ArgumentList.Add(args[i]);

            if (Process.Start(startInfo) is null)
                return;
        }
        catch
        {
            // 新实例未拉起时不退出，避免用户失去可用的客户端窗口。
            return;
        }

        Environment.Exit(0);
    }
}
