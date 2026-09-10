using System;
using System.IO;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 启动期日志清理（M5）：connection.log 由原版 Logger 无限追加、无滚动，
/// 启动时若超限归档为 connection.old.log 后重新开始；并清除 logs 目录下超过
/// 保留期的旧日志，防止长期运行磁盘膨胀。清理失败不阻塞启动。
/// </summary>
public static class LogCleanup
{
    /// <summary>connection.log 触发归档的体积阈值。</summary>
    public const long ConnectionLogMaxBytes = 2 * 1024 * 1024;

    /// <summary>logs 目录下日志文件的保留天数。</summary>
    public const int RetainDays = 14;

    public static void Run(string logDir)
    {
        try
        {
            if (!Directory.Exists(logDir))
                return;

            // connection.log：超限则归档（覆盖上次归档），随后由 Logger.Initialize 重建空文件
            string conn = Path.Combine(logDir, "connection.log");
            var fi = new FileInfo(conn);
            if (fi.Exists && fi.Length > ConnectionLogMaxBytes)
            {
                File.Copy(conn, Path.Combine(logDir, "connection.old.log"), true);
                File.Delete(conn);
            }

            // 兜底清除超保留期的 *.log（Serilog 自身按 14 天滚动保留，此处覆盖其他日志文件）
            DateTime cutoff = DateTime.Now.AddDays(-RetainDays);
            foreach (string file in Directory.GetFiles(logDir, "*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    try
                    {
                        File.Delete(file);
                    }
                    catch
                    {
                        // 文件被占用（如正在写入）则跳过，下轮启动再清理
                    }
                }
            }
        }
        catch
        {
            // 清理失败不影响启动
        }
    }
}
