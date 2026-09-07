using System;
using System.Diagnostics;
using System.Threading.Tasks;
using CnCNetClient.Core;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 附加功能窗口服务（M4.7，原版 <c>ExtrasWindow</c>）。
/// 宿主组件 <c>Components/Shared/ExtrasWindow.razor</c> 订阅 <see cref="Changed"/> 渲染模态层；
/// 承载动作：启动地图编辑器（外部进程）、打开更新日志/鸣谢 URL、展示关于信息。
/// </summary>
public sealed class ExtrasService
{
    /// <summary>状态变化（打开/关闭）时触发，宿主据此刷新。</summary>
    public event Action? Changed;

    public bool IsOpen { get; private set; }

    /// <summary>关闭动画进行中（模态层保留以播 fade-out，随后 <see cref="CompleteClose"/>）。</summary>
    public bool IsClosing { get; private set; }

    private TaskCompletionSource<bool>? _tcs;

    /// <summary>打开附加窗口并等待用户操作完成。</summary>
    public Task ShowAsync()
    {
        _tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        IsClosing = false;
        IsOpen = true;
        Changed?.Invoke();
        return _tcs.Task;
    }

    /// <summary>用户点击任一动作/取消：进入关闭动画，由宿主完成实际关闭。</summary>
    public void BeginClose()
    {
        if (!IsOpen || IsClosing)
            return;
        IsClosing = true;
        Changed?.Invoke();
    }

    /// <summary>关闭动画结束，真正关闭并完成 <see cref="ShowAsync"/> 的等待任务。</summary>
    public void CompleteClose()
    {
        if (!IsOpen)
            return;
        IsOpen = false;
        IsClosing = false;
        _tcs?.TrySetResult(true);
        Changed?.Invoke();
    }

    /// <summary>启动地图编辑器（原版 ExtrasWindow.BtnExMapEditor_LeftClick：按操作系统选择 exe 路径，
    /// UseShellExecute=false 直接启动进程）。成功返回 null，失败返回错误信息。</summary>
    public string? LaunchMapEditor()
    {
        var config = ClientConfiguration.Instance;
        string mapEditorPath = config.GetOperatingSystemVersion() != OSVersion.UNIX
            ? config.MapEditorExePath
            : config.UnixMapEditorExePath;
        string fullPath = SafePath.CombineFilePath(ProgramConstants.GamePath, mapEditorPath);
        try
        {
            using var process = new Process();
            process.StartInfo.FileName = fullPath;
            process.StartInfo.UseShellExecute = false;
            process.Start();
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>打开外部 URL（原版 ProcessLauncher.StartShellProcess）。成功返回 null，失败返回错误信息。</summary>
    public string? OpenUrl(string url)
    {
        try
        {
            ProcessLauncher.StartShellProcess(url);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    /// <summary>关于信息：游戏/客户端名 + 客户端版本（原版 UpdateWindow 语义，去 XNA 展示）。</summary>
    public string GetAboutText()
    {
        var config = ClientConfiguration.Instance;
        string gameName = string.IsNullOrWhiteSpace(config.LongGameName) ? config.LocalGame : config.LongGameName;
        return $"CNC NET CLIENT · {gameName}\n游戏版本：{ProgramConstants.GAME_VERSION}\n引擎：Blazor WebView2 · 全息指挥视窗";
    }
}
