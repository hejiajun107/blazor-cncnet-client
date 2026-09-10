using System;
using System.Runtime.InteropServices;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 原生消息框（启动期专用）：WebView 尚未就绪、Blazor 弹窗不可用时，
/// 用系统对话框呈现启动错误，避免「完全没反应」的静默失败。
/// 仅 Windows 使用 user32；其他平台回退 stderr（日志兜底）。
/// </summary>
public static class NativeMessageBox
{
    public static void Show(string title, string message, bool error = false)
    {
        if (OperatingSystem.IsWindows())
        {
            MessageBoxW(IntPtr.Zero, message, title,
                (error ? MbIconError : MbIconWarning) | MbTopmost | MbSetForeground);
        }
        else
        {
            Console.Error.WriteLine($"[{title}] {message}");
        }
    }

    private const uint MbOk = 0x0;
    private const uint MbIconError = 0x10;
    private const uint MbIconWarning = 0x30;
    private const uint MbTopmost = 0x40000;
    private const uint MbSetForeground = 0x10000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint uType);
}
