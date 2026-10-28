using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace CnCNetClient.Integration.GameFiles;

/// <summary>
/// 尤里的复仇（YR）运行必要文件的检测与提取。
/// 客户端目录（GamePath）缺失清单内任一文件时，引导用户选择完整 YR 安装目录，
/// 并从该目录把缺失文件提取（拷贝）到客户端目录，使目录成为完整可玩的游戏根目录。
/// 文件清单以 ClientDefinitions.ini [Settings] RequiredFiles 为准（MOD 可配置）；
/// 该键缺失/为空时才退回内置清单（核心 11 项 + ClientDefinitions 配置的启动加载器）。
/// </summary>
public static class GameFileExtractor
{
    /// <summary>
    /// YR 核心运行文件（用户确认清单）：
    /// gamemd.exe 主程序；ra2/ra2md.mix 核心数据；language/langmd.mix 语言；
    /// expandmd01.mix 扩展包；movmd03.mix 过场动画；multimd.mix 多人界面资源；
    /// mapsmd03.mix 地图包（游戏检测存在性，即使内容为空也必须存在）；
    /// BINKW32.DLL 视频解码；Blowfish.dll 加密。
    /// </summary>
    private static readonly string[] CoreGameFiles =
    {
        "BINKW32.DLL",
        "Blowfish.dll",
        "expandmd01.mix",
        "gamemd.exe",
        "langmd.mix",
        "language.mix",
        "mapsmd03.mix",
        "movmd03.mix",
        "multimd.mix",
        "ra2.mix",
        "ra2md.mix"
    };

    /// <summary>
    /// 启动链附加文件：ClientDefinitions 配置了游戏加载器（如 syringe.exe）时，
    /// <see cref="GameProcessLogic"/> 会以它作为实际启动进程，缺失则游戏无法启动，须一并检测/提取。
    /// 配置未就绪或未配置时返回 null（跳过该项）。
    /// </summary>
    private static string? LauncherFile
    {
        get
        {
            try
            {
                string? name = ClientConfiguration.Instance.GameLauncherExecutableName;
                return string.IsNullOrWhiteSpace(name) ? null : name;
            }
            catch
            {
                // 配置未初始化（缺 ClientDefinitions.ini 等）：启动链本就走不通，暂不纳入清单。
                return null;
            }
        }
    }

    /// <summary>内置兜底清单：核心 11 项 + 可选加载器，忽略大小写去重。</summary>
    private static string[] FallbackRequiredFiles()
    {
        string? launcher = LauncherFile;
        return launcher is null
            ? CoreGameFiles
            : CoreGameFiles.Append(launcher).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    /// <summary>
    /// ClientDefinitions.ini [Settings] RequiredFiles 配置的清单（遵循 ini 设置）；
    /// 配置不可用或未定义时返回空数组，由调用方退回 <see cref="FallbackRequiredFiles"/>。
    /// </summary>
    private static string[] ConfiguredRequiredFiles()
    {
        try
        {
            return ClientConfiguration.Instance.RequiredFiles
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(file => file.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            // 配置未初始化（缺 ClientDefinitions.ini 等）：用内置清单兜底。
            return Array.Empty<string>();
        }
    }

    /// <summary>
    /// 完整必要文件清单：优先取 ClientDefinitions.ini [Settings] RequiredFiles，
    /// 未配置时退回内置 YR 清单（<see cref="CoreGameFiles"/> + 启动加载器）。
    /// </summary>
    public static string[] GetRequiredFiles()
    {
        string[] configured = ConfiguredRequiredFiles();
        return configured.Length > 0 ? configured : FallbackRequiredFiles();
    }

    /// <summary>
    /// ClientDefinitions.ini [Settings] ForbiddenFiles 配置的清单（非必填）：
    /// 这些文件会干扰游戏/模组运行，存在即应提示用户移除。配置不可用时返回空数组。
    /// </summary>
    private static string[] ConfiguredForbiddenFiles()
    {
        try
        {
            return ClientConfiguration.Instance.ForbiddenFiles
                .Where(file => !string.IsNullOrWhiteSpace(file))
                .Select(file => file.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>返回游戏根目录下已存在、且被 [Settings] ForbiddenFiles 禁止的文件（相对文件名）。</summary>
    public static string[] GetForbiddenFilesPresent(string gamePath)
    {
        try
        {
            return ConfiguredForbiddenFiles()
                .Where(file => File.Exists(SafePath.CombineFilePath(gamePath, file)))
                .ToArray();
        }
        catch
        {
            // 目录不可访问等异常：无法判定，交由环境检测提示。
            return Array.Empty<string>();
        }
    }

    /// <summary>返回游戏根目录下缺失的必要文件（相对文件名）。</summary>
    public static string[] GetMissingFiles(string gamePath)
    {
        try
        {
            return GetRequiredFiles()
                .Where(file => !File.Exists(SafePath.CombineFilePath(gamePath, file)))
                .ToArray();
        }
        catch
        {
            // 目录不可访问等异常：按全部缺失处理（提示引导），由调用方兜底。
            return GetRequiredFiles();
        }
    }

    /// <summary>
    /// 从源目录（完整 YR 安装目录）把指定文件提取（拷贝）到目标目录（客户端目录）。
    /// 返回 (成功拷贝的文件, 源目录中缺失被跳过的文件)。
    /// </summary>
    public static (IReadOnlyList<string> Copied, IReadOnlyList<string> Skipped) ExtractFiles(
        string sourceDir, string targetDir, IEnumerable<string> files)
    {
        var copied = new List<string>();
        var skipped = new List<string>();

        foreach (string file in files.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                string sourcePath = Path.Combine(sourceDir, file);
                if (!File.Exists(sourcePath))
                {
                    skipped.Add(file);
                    continue;
                }

                File.Copy(sourcePath, SafePath.CombineFilePath(targetDir, file), overwrite: true);
                copied.Add(file);
            }
            catch
            {
                // 单个文件失败（占用/权限/IO）不中断整体提取，记录后继续。
                skipped.Add(file);
            }
        }

        return (copied, skipped);
    }
}
