using System;
using System.Linq;
using System.Threading.Tasks;

using CnCNetClient.Integration.GameFiles;
using CnCNetClient.Tools;

using InfiniFrame;
using Microsoft.Extensions.DependencyInjection;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 游戏运行文件就绪服务（M-游戏文件引导）：检测客户端目录（GamePath）是否具备运行
/// 尤里的复仇的必要文件；缺失时首页引导用户选择完整 YR 安装目录并从其中提取缺失文件。
/// 由首页 <c>Index</c> 在启动检测时调用 <see cref="Detect"/>，提取结果通过
/// <see cref="StateChanged"/> 通知 UI 刷新。
/// </summary>
public sealed class GameFileSetupService
{
    private readonly EnvironmentCheck _env;
    private readonly IServiceProvider _services;

    /// <summary>客户端目录下缺失的必要游戏文件（相对文件名）；空表示已就绪。</summary>
    public string[] MissingFiles { get; private set; } = Array.Empty<string>();

    /// <summary>是否已具备运行游戏的必要文件。</summary>
    public bool IsGameReady => MissingFiles.Length == 0;

    /// <summary>最近一次操作的结果说明（提取成功/失败/取消），供首页展示。</summary>
    public string? LastMessage { get; private set; }

    /// <summary>状态（缺失清单/结果说明）变化时触发，UI 订阅刷新。</summary>
    public event Action? StateChanged;

    public GameFileSetupService(EnvironmentCheck env, IServiceProvider services)
    {
        _env = env;
        _services = services;
    }

    /// <summary>
    /// 检测当前游戏目录缺失的必要文件。幂等，可重复调用（如提取完成后复检）。
    /// 未定位到游戏目录（<see cref="EnvironmentCheck.GamePath"/> 为 null）时按无缺失处理，
    /// 环境问题由 <see cref="EnvironmentCheck"/> 自身提示。
    /// </summary>
    public void Detect()
    {
        string? gamePath = _env.GamePath;
        MissingFiles = string.IsNullOrEmpty(gamePath)
            ? Array.Empty<string>()
            : GameFileExtractor.GetMissingFiles(gamePath);
        LastMessage = null;
        StateChanged?.Invoke();
    }

    /// <summary>
    /// 引导用户选择完整 YR 游戏目录，并把缺失的必要文件提取（拷贝）到客户端目录。
    /// 目录选择走 InfiniFrame 官方封装的文件夹选择框（<c>Features.FilePickerDialogs.ShowOpenFolder</c>，
    /// 跨平台，替代手写 Win32 API）；DI 未注册窗口时回退 <see cref="NativeDirectoryPicker"/>。
    /// 目录选择须在 UI 线程同步执行（原生对话框），文件拷贝放后台线程，完成后复检缺失清单。
    /// </summary>
    public async Task<(string[] Copied, string[] Skipped)> PickAndExtractAsync()
    {
        string gamePath = _env.GamePath ?? _env.StartupPath;

        string? source = PickGameDirectory();
        if (string.IsNullOrEmpty(source))
        {
            LastMessage = "已取消选择";
            StateChanged?.Invoke();
            return (Array.Empty<string>(), Array.Empty<string>());
        }

        string[] toExtract = MissingFiles.Length > 0
            ? MissingFiles
            : GameFileExtractor.GetRequiredFiles();

        (IReadOnlyList<string> Copied, IReadOnlyList<string> Skipped) result =
            await Task.Run(() => GameFileExtractor.ExtractFiles(source, gamePath, toExtract));

        // 提取后复检：剩余缺失 = 源目录也没有的文件。
        MissingFiles = GameFileExtractor.GetMissingFiles(gamePath);
        LastMessage = result.Copied.Count > 0
            ? $"已从所选目录提取 {result.Copied.Count} 个文件"
            : "没有需要提取的文件";
        if (result.Skipped.Count > 0)
            LastMessage += $"；以下 {result.Skipped.Count} 个文件在所选目录中缺失：{string.Join("、", result.Skipped)}";
        if (MissingFiles.Length == 0)
            LastMessage += "。游戏文件已齐备。";

        StateChanged?.Invoke();
        return (result.Copied.ToArray(), result.Skipped.ToArray());
    }

    /// <summary>
    /// 弹出原生文件夹选择框（UI 线程调用）。优先 InfiniFrame 官方封装的
    /// <c>IInfiniFrameWindow.Features.FilePickerDialogs.ShowOpenFolder</c>，
    /// 解析不到窗口（如窗口尚未创建/未注册进 DI）时回退 <see cref="NativeDirectoryPicker"/>。
    /// </summary>
    private string? PickGameDirectory()
    {
        const string title = "请选择尤里的复仇（Yuri's Revenge）游戏安装目录";

        try
        {
            IInfiniFrameWindow? window = _services.GetService<IInfiniFrameWindow>();
            if (window is not null)
            {
                string[] folders = window.Features.FilePickerDialogs.ShowOpenFolder(
                    title, _env.GamePath ?? _env.StartupPath, multiSelect: false);
                return folders.Length > 0 ? folders[0] : null;
            }
        }
        catch
        {
            // 窗口 feature 不可用时回退到独立原生目录选择器。
        }

        return NativeDirectoryPicker.PickDirectory(title);
    }
}
