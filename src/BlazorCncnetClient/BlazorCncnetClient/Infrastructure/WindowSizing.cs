using System.Runtime.InteropServices;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 把「期望的客户区（页面视口）大小」换算成窗口外框大小。
/// InfiniFrame 的 SetSize/SetMinSize 作用于窗口外框（含标题栏与边框），
/// 直接传 1280x720 时页面视口只有 1264x681；高 DPI 下还会按缩放比例进一步变小。
/// </summary>
internal static class WindowSizing
{
    private const uint WS_OVERLAPPEDWINDOW = 0x00CF0000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectExForDpi(ref RECT rect, uint style, bool menu, uint exStyle, uint dpi);

    [DllImport("user32.dll")]
    private static extern bool AdjustWindowRectEx(ref RECT rect, uint style, bool menu, uint exStyle);

    /// <summary>
    /// 返回能让客户区达到 <paramref name="clientWidth"/> x <paramref name="clientHeight"/> 的外框尺寸。
    /// 尺寸按系统 DPI 缩放（DPI 96 时不变），高 DPI 下页面视口仍为指定 CSS 像素。
    /// </summary>
    public static (int Width, int Height) FromClientSize(int clientWidth, int clientHeight)
    {
        uint dpi = 96;
        try
        {
            dpi = GetDpiForSystem();
        }
        catch (EntryPointNotFoundException)
        {
            // 老系统没有该 API，按 96 DPI 处理。
        }

        if (dpi == 0)
            dpi = 96;

        double scale = dpi / 96.0;
        int scaledWidth = (int)Math.Round(clientWidth * scale);
        int scaledHeight = (int)Math.Round(clientHeight * scale);

        var rect = new RECT { Left = 0, Top = 0, Right = scaledWidth, Bottom = scaledHeight };

        bool adjusted;
        try
        {
            adjusted = AdjustWindowRectExForDpi(ref rect, WS_OVERLAPPEDWINDOW, false, 0, dpi);
        }
        catch (EntryPointNotFoundException)
        {
            adjusted = AdjustWindowRectEx(ref rect, WS_OVERLAPPEDWINDOW, false, 0);
        }

        return adjusted
            ? (rect.Right - rect.Left, rect.Bottom - rect.Top)
            : (scaledWidth, scaledHeight);
    }
}
