#nullable enable

using System;
using System.Globalization;
using System.IO;

using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;

using CnCNetClient.Tools;

namespace CnCNetClient.Domain;

/// <summary>
/// 复盘路径、命名与录制元数据（迁移自旧版 <c>ReplayManager</c>，去 XNA）。
/// 仅实现「录制侧」：在 spawn.ini 写入 EnableReplayRecording / GamePackageVersion / ReplayFileOut。
/// TODO（未完成待办）：[ReplayFileHashes] 完整性哈希节（需移植 FileHashCalculator 的
/// EnumerateTrackedFiles / GetTrackedFileHash 到 Domain 可访问层），以及 Prune/List/OpenDirectory。
/// </summary>
public static class ReplayManager
{
    /// <summary>时间戳与地图名的最大 UTF-8 字节长度，spawner 的 ReplayFileOut 读了 MAX_PATH 字节缓冲区。</summary>
    private const int MaxRecordingBaseFileNameBytes = 180;

    /// <summary>是否支持复盘录制（旧版来自 ClientConfiguration.ReplaySupport）。</summary>
    public static bool IsSupported => ClientConfiguration.Instance.ReplaySupport;

    /// <summary>复盘文件目录（相对游戏目录，旧版 ReplaysDirectory）。</summary>
    public static string DirectoryName => ClientConfiguration.Instance.ReplaysDirectory;

    /// <summary>复盘文件扩展名（无前导点，旧版 ReplayFileExtension）。</summary>
    public static string FileExtension => ClientConfiguration.Instance.ReplayFileExtension;

    /// <summary>
    /// 已安装游戏包与版本，写入复盘元数据。Domain 无法引用 Updater，
    /// 由应用层启动时用 <see cref="UpdaterReplayGameVersion"/> 注入版本号（旧版用 Updater.GameVersion）。
    /// </summary>
    public static string GamePackageVersion
    {
        get
        {
            string gameVersion = string.IsNullOrWhiteSpace(UpdaterReplayGameVersion) ? "Unknown" : UpdaterReplayGameVersion!;
            return $"{ClientConfiguration.Instance.LocalGame} {gameVersion}".Trim();
        }
    }

    /// <summary>应用层在初始化时注入的实际游戏版本号（对应旧版 Updater.GameVersion）。</summary>
    public static string? UpdaterReplayGameVersion { get; set; }

    public static DirectoryInfo GetReplayDirectory()
        => SafePath.GetDirectory(ProgramConstants.GamePath, DirectoryName);

    public static FileInfo GetReplayFile(string fileName)
        => SafePath.GetFile(ProgramConstants.GamePath, DirectoryName, fileName);

    public static string GetReplayFileRelativePath(string fileName)
        => SafePath.CombineFilePath(DirectoryName, fileName);

    /// <summary>
    /// 用户设置开启录制，或 spawn.ini 已通过 [ForcedSpawnIniOptions] 开启录制时写入录制元数据。
    /// </summary>
    public static void PrepareRecording(IniFile spawnIni, string mapName)
    {
        if (!IsSupported)
            return;

        bool record = UserINISettings.Instance.RecordReplays
            || spawnIni.GetBooleanValue("Settings", "EnableReplayRecording", false);
        spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", record);

        if (!record)
            return;

        spawnIni.SetStringValue("Settings", "GamePackageVersion", GamePackageVersion);
        spawnIni.SetStringValue("Settings", "ReplayFileOut", BuildRecordingPath(mapName));

        // TODO（未完成待办）：ReplayFileHashes.Write 写入 [ReplayFileHashes] 完整性哈希节
    }

    public static string BuildRecordingPath(string mapName)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        string safeMapName = mapName.ToWin32FileName();

        string baseName = string.IsNullOrWhiteSpace(safeMapName)
            ? timestamp
            : timestamp + " " + safeMapName;

        baseName = baseName.TruncateToUtf8ByteLength(MaxRecordingBaseFileNameBytes).TrimEnd();

        string fileName = baseName + "." + FileExtension;

        int counter = 1;
        while (GetReplayFile(fileName).Exists)
        {
            fileName = $"{baseName} ({counter})." + FileExtension;
            counter++;
        }

        return GetReplayFileRelativePath(fileName);
    }
}