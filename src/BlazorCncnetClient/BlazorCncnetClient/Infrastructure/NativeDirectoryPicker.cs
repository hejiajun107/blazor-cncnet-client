using System;
using System.Runtime.InteropServices;
using System.Text;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 原生目录选择器（仅 Windows 有效）：引导用户选择完整游戏（如尤里的复仇）安装目录。
/// 复用 Win32 <c>SHBrowseForFolderW</c>，不引入 WinForms/WPF 依赖；
/// 非 Windows 平台返回 null（UI 层降级提示），与 <see cref="NativeMessageBox"/> 同一策略。
/// </summary>
public static class NativeDirectoryPicker
{
    private const uint BIF_RETURNONLYFSDIRS = 0x0001;
    private const uint BIF_NEWDIALOGSTYLE = 0x0040;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct BrowseInfoW
    {
        public IntPtr hwndOwner;
        public IntPtr pidlRoot;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string pszDisplayName;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string lpszTitle;
        public uint ulFlags;
        public IntPtr lpfn;
        public IntPtr lParam;
        public int iImage;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHBrowseForFolderW(ref BrowseInfoW lpbi);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool SHGetPathFromIDListW(IntPtr pidl, StringBuilder pszPath);

    /// <summary>
    /// 弹出文件夹选择对话框，返回所选目录绝对路径；用户取消或非 Windows 平台返回 null。
    /// </summary>
    public static string? PickDirectory(string title)
    {
        if (!OperatingSystem.IsWindows())
            return null;

        var info = new BrowseInfoW
        {
            // pszDisplayName 必须是已分配固定长度缓冲的 string（封送器按字符串长度复制，
            // 不能用 StringBuilder——结构字段不支持其封送，运行时会抛 TypeLoadException）。
            pszDisplayName = new string('\0', 260),
            lpszTitle = title,
            ulFlags = BIF_RETURNONLYFSDIRS | BIF_NEWDIALOGSTYLE
        };

        IntPtr pidl = SHBrowseForFolderW(ref info);
        if (pidl == IntPtr.Zero)
            return null;

        try
        {
            var path = new StringBuilder(260);
            return SHGetPathFromIDListW(pidl, path) ? path.ToString() : null;
        }
        finally
        {
            Marshal.FreeCoTaskMem(pidl);
        }
    }
}
