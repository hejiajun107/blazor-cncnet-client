using System;
using System.Linq;

using CnCNetClient.Core;
using CnCNetClient.Integration.GameFiles;

using Serilog;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 禁止文件检查：ClientDefinitions.ini [Settings] ForbiddenFiles（非必填）列出的文件
/// 会干扰游戏/模组运行。游戏根目录存在其中任一文件时，用原生对话框告知并退出进程
/// （此时 WebView 尚未创建，只能用 <see cref="NativeMessageBox"/>）。
/// 配置不可用（资源/ClientDefinitions 缺失）时跳过，交由 <see cref="EnvironmentCheck"/> 在界面提示。
/// </summary>
public static class ForbiddenFilesGuard
{
    /// <summary>返回 false 表示检测到禁止文件并已弹窗告知，调用方应退出进程。</summary>
    public static bool EnsureNoForbiddenFiles()
    {
        string[] present = GameFileExtractor.GetForbiddenFilesPresent(ProgramConstants.GamePath);
        if (present.Length == 0)
            return true;

        string list = string.Join(", ", present);
        Log.Fatal("检测到禁止的文件，终止启动：{Files}", list);

        NativeMessageBox.Show(
            UiText.T("Startup.ForbiddenFilesTitle", "Forbidden files detected"),
            string.Format(
                UiText.T("Startup.ForbiddenFilesMessage",
                    "The following files must not be present in the game directory:\n\n{0}\n\nThey interfere with the game/mod and must be removed before the client can start."),
                list),
            error: true);

        return false;
    }
}
