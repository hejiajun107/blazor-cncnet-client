using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CnCNetClient.Core;
using CnCNetClient.Core.Enums;
using CnCNetClient.Integration;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 游戏进程日志收集服务（第五轮补缺，迁移原版 <c>GameInProgressWindow</c> 的游戏退出逻辑）：
/// 游戏启动前清理旧错误日志（EXCEPT.TXT / SYNC*.TXT）；游戏退出后——
/// 非 Ares：EXCEPT.TXT → Client/GameCrashLogs、SYNC*.TXT → Client/SyncErrorLogs（带时间戳，删除原文件）；
/// Ares：debug snapshot 目录归档 + syringe.log 收集 + SCRN*.bmp 截图转 PNG（Screenshots/）。
/// 宿主组件 App.razor 注入以确保实例化并订阅静态事件。
/// </summary>
public sealed class GameLogCollector : IDisposable
{
    private List<string> _debugSnapshotDirectories = new();
    private DateTime _debugLogLastWriteTime;
    private bool _deletingLogFilesFailed;

    public GameLogCollector()
    {
        GameProcessLogic.GameProcessStarting += OnGameProcessStarting;
        GameProcessLogic.GameProcessExited += OnGameProcessExited;
    }

    private void OnGameProcessStarting()
    {
        if (ClientConfiguration.Instance.ClientGameType == ClientType.Ares)
        {
            _debugSnapshotDirectories = GetAllDebugSnapshotDirectories();
        }
        else
        {
            try
            {
                SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "EXCEPT.TXT");

                for (int i = 0; i < 8; i++)
                    SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "SYNC" + i + ".TXT");

                _deletingLogFilesFailed = false;
            }
            catch (Exception ex)
            {
                Logger.Log("GameLogCollector: 清理错误日志文件失败！" + ex);
                _deletingLogFilesFailed = true;
            }
        }
    }

    private void OnGameProcessExited()
    {
        // 异步执行，避免阻塞 UI 线程（原版 WindowManager.AddCallback 后的逻辑同样脱离渲染循环）
        _ = Task.Run(() =>
        {
            try
            {
                DateTime now = DateTime.Now;

                if (ClientConfiguration.Instance.ClientGameType == ClientType.Ares)
                {
                    Task.Run(ProcessScreenshots);

                    string? snapshotDirectory = GetNewestDebugSnapshotDirectory();
                    bool snapshotCreated = snapshotDirectory != null;

                    snapshotDirectory = snapshotDirectory ?? SafePath.CombineDirectoryPath(
                        ProgramConstants.GamePath, "debug",
                        FormattableString.Invariant($"snapshot-{now.ToString("yyyyMMdd-HHmmss")}"));

                    bool debugLogModified = false;
                    FileInfo debugLogFileInfo = SafePath.GetFile(ProgramConstants.GamePath, "debug", "debug.log");
                    DateTime lastWriteTime = new();

                    if (debugLogFileInfo.Exists)
                        lastWriteTime = debugLogFileInfo.LastAccessTime;

                    if (!lastWriteTime.Equals(_debugLogLastWriteTime))
                    {
                        debugLogModified = true;
                        _debugLogLastWriteTime = lastWriteTime;
                    }

                    if (CopySyncErrorLogs(snapshotDirectory, null) || snapshotCreated)
                    {
                        FileInfo snapShotDebugLogFileInfo = SafePath.GetFile(snapshotDirectory, "debug.log");

                        if (debugLogFileInfo.Exists && !snapShotDebugLogFileInfo.Exists && debugLogModified)
                            File.Copy(debugLogFileInfo.FullName, snapShotDebugLogFileInfo.FullName);

                        CopyErrorLog(snapshotDirectory, "syringe.log", null);
                    }
                }
                else
                {
                    if (_deletingLogFilesFailed)
                        return;

                    CopyErrorLog(
                        SafePath.CombineDirectoryPath(ProgramConstants.ClientUserFilesPath, "GameCrashLogs"),
                        "EXCEPT.TXT", now);
                    CopySyncErrorLogs(
                        SafePath.CombineDirectoryPath(ProgramConstants.ClientUserFilesPath, "SyncErrorLogs"), now);
                }
            }
            catch (Exception ex)
            {
                Logger.Log("GameLogCollector: 收集游戏退出日志时出错！" + ex);
            }
        });
    }

    /// <summary>把游戏目录中的错误日志复制到目标目录（带时间戳文件名，原文件保留）。</summary>
    private bool CopyErrorLog(string directory, string filename, DateTime? dateTime)
    {
        bool copied = false;

        try
        {
            FileInfo errorLogFileInfo = SafePath.GetFile(ProgramConstants.GamePath, filename);

            if (errorLogFileInfo.Exists)
            {
                DirectoryInfo errorLogDirectoryInfo = SafePath.GetDirectory(directory);

                if (!errorLogDirectoryInfo.Exists)
                    errorLogDirectoryInfo.Create();

                Logger.Log("游戏崩溃！正在复制 " + filename + " 文件。");

                string timeStamp = dateTime.HasValue ? dateTime.Value.ToString("_yyyy_MM_dd_HH_mm") : "";
                string filenameCopy = Path.GetFileNameWithoutExtension(filename) + timeStamp + Path.GetExtension(filename);

                File.Copy(errorLogFileInfo.FullName, SafePath.CombineFilePath(directory, filenameCopy));
                copied = true;
            }
        }
        catch (Exception ex)
        {
            Logger.Log("复制 " + filename + " 文件时出错：" + ex);
        }

        return copied;
    }

    /// <summary>把游戏目录中的同步错误日志（SYNC0-7.TXT）复制到目标目录并删除原文件。</summary>
    private bool CopySyncErrorLogs(string directory, DateTime? dateTime)
    {
        bool copied = false;

        try
        {
            for (int i = 0; i < 8; i++)
            {
                string filename = "SYNC" + i + ".TXT";
                FileInfo syncErrorLogFileInfo = SafePath.GetFile(ProgramConstants.GamePath, filename);

                if (syncErrorLogFileInfo.Exists)
                {
                    DirectoryInfo syncErrorLogDirectoryInfo = SafePath.GetDirectory(directory);

                    if (!syncErrorLogDirectoryInfo.Exists)
                        syncErrorLogDirectoryInfo.Create();

                    Logger.Log("检测到同步错误！正在复制文件 " + filename);

                    string timeStamp = dateTime.HasValue ? dateTime.Value.ToString("_yyyy_MM_dd_HH_mm") : "";
                    string filenameCopy = Path.GetFileNameWithoutExtension(filename) + timeStamp + Path.GetExtension(filename);

                    File.Copy(syncErrorLogFileInfo.FullName, SafePath.CombineFilePath(directory, filenameCopy));
                    copied = true;
                    syncErrorLogFileInfo.Delete();
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Log("复制 SYNCX.TXT 文件时出错：" + ex);
        }

        return copied;
    }

    /// <summary>返回 debug 目录中在上次启动后新建且非空的 snapshot 目录；空的 snapshot 目录被删除。</summary>
    private string? GetNewestDebugSnapshotDirectory()
    {
        string? snapshotDirectory = null;

        var newDirectories = GetAllDebugSnapshotDirectories().Except(_debugSnapshotDirectories);

        foreach (string directory in newDirectories)
        {
            if (Directory.EnumerateFileSystemEntries(directory).Any())
                snapshotDirectory = directory;
            else
            {
                try
                {
                    Directory.Delete(directory);
                }
                catch { }
            }
        }

        return snapshotDirectory;
    }

    private List<string> GetAllDebugSnapshotDirectories()
    {
        var directories = new List<string>();

        try
        {
            directories.AddRange(Directory.GetDirectories(
                SafePath.CombineDirectoryPath(ProgramConstants.GamePath, "debug"), "snapshot-*"));
        }
        catch { }

        return directories;
    }

    /// <summary>把游戏目录中的 SCRN*.bmp 截图转换为 PNG 并存入 Screenshots/ 目录（ImageSharp，替换原版 XNA 纹理加载）。</summary>
    private void ProcessScreenshots()
    {
        IEnumerable<FileInfo> files;
        try
        {
            files = SafePath.GetDirectory(ProgramConstants.GamePath).EnumerateFiles("SCRN*.bmp");
        }
        catch (Exception ex)
        {
            Logger.Log("GameLogCollector: 枚举截图文件失败。" + ex);
            return;
        }

        DirectoryInfo screenshotsDirectory = SafePath.GetDirectory(ProgramConstants.GamePath, "Screenshots");

        if (!screenshotsDirectory.Exists)
        {
            try
            {
                screenshotsDirectory.Create();
            }
            catch (Exception ex)
            {
                Logger.Log("GameLogCollector: 创建 Screenshots 目录失败。" + ex);
                return;
            }
        }

        foreach (FileInfo file in files)
        {
            try
            {
                using FileStream stream = file.OpenRead();
                using var image = Image.Load(stream);
                FileInfo newFile = SafePath.GetFile(
                    screenshotsDirectory.FullName,
                    FormattableString.Invariant($"{Path.GetFileNameWithoutExtension(file.FullName)}.png"));
                using FileStream newFileStream = newFile.OpenWrite();

                image.SaveAsPng(newFileStream);

                Logger.Log("GameLogCollector: " + Path.GetFileNameWithoutExtension(file.FullName) + ".png 已保存到 Screenshots 目录。");
                file.Delete();
            }
            catch (Exception ex)
            {
                Logger.Log("GameLogCollector: 保存 " + Path.GetFileNameWithoutExtension(file.FullName) + ".png 时出错。" + ex);
            }
        }
    }

    public void Dispose()
    {
        GameProcessLogic.GameProcessStarting -= OnGameProcessStarting;
        GameProcessLogic.GameProcessExited -= OnGameProcessExited;
    }
}
