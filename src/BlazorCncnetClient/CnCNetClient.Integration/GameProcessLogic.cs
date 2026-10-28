using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Tools;

namespace CnCNetClient.Integration;

/// <summary>
/// Controls launching and exiting of the game executable.
/// Migrated from ClientGUI.GameProcessLogic. UI-dependent dialogs (XNAMessageBox)
/// are replaced by host-provided error callbacks (<see cref="GameProcessLaunchFailed"/>).
/// <see cref="UseQres"/> / <see cref="SingleCoreAffinity"/> are set by
/// <c>DirectDrawWrapperManager</c> (Domain) at renderer selection time.
/// </summary>
public static class GameProcessLogic
{
    /// <summary>Raised after the game process has started successfully.</summary>
    public static event Action? GameProcessStarted;

    /// <summary>Raised just before the game process is launched (after spawn files are written).</summary>
    public static event Action? GameProcessStarting;

    /// <summary>Raised when the game process exits.</summary>
    public static event Action? GameProcessExited;

    /// <summary>
    /// Raised when the game process could not be launched (replaces the original
    /// XNAMessageBox dialog). Parameters: (title, message).
    /// </summary>
    public static event Action<string, string>? GameProcessLaunchFailed;

    public static bool UseQres { get; set; }

    public static bool SingleCoreAffinity { get; set; }

    /// <summary>
    /// Starts the main game process.
    /// </summary>
    public static void StartGameProcess()
    {
        Logger.Log("About to launch main game executable.");

        // 启动前补齐游戏 INI 里缺失/非法的必需键（分辨率 + [Intro] Play=no），不覆盖已有值：
        // 游戏以 0x0 建屏或播放开场影片都会在 GScreenClass::DoBlit 崩溃（2026-10-19 实测）。
        UserINISettings.Instance.SyncGameSettingsIni();

        OSVersion osVersion = ClientConfiguration.Instance.GetOperatingSystemVersion();

        string gameExecutableName;
        string additionalExecutableName = string.Empty;

        // 文案按当前语言取（原版 Translation.ini 兼容，未命中回退内置 Resx）。
        string errorLaunchingTitle = "Failed to launch game".L10N("Client:UI:Launch.ErrorTitle");
        string errorLaunchingText = ("Failed to launch {0}. Check whether antivirus software blocked CnCNet Client. " +
            "You can also try running the client as administrator.\n\nYou cannot join this match.\n\nReturned error: {1}")
            .L10N("Client:UI:Launch.ErrorText");

        if (osVersion == OSVersion.UNIX)
        {
            gameExecutableName = ClientConfiguration.Instance.UnixGameExecutableName;
        }
        else
        {
            string launcherExecutableName = ClientConfiguration.Instance.GameLauncherExecutableName;
            if (string.IsNullOrEmpty(launcherExecutableName))
                gameExecutableName = ClientConfiguration.Instance.GetGameExecutableName();
            else
            {
                gameExecutableName = launcherExecutableName;
                additionalExecutableName = "\"" + ClientConfiguration.Instance.GetGameExecutableName() + "\" ";
            }
        }

        string extraCommandLine = ClientConfiguration.Instance.ExtraExeCommandLineParameters;

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "DTA.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TI.LOG");
        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, "TS.LOG");

        GameProcessStarting?.Invoke();

        if (UserINISettings.Instance.WindowedMode && UseQres && RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            Logger.Log("Windowed mode is enabled - using QRes.");
            Process qresProcess = new Process();
            qresProcess.StartInfo.FileName = ProgramConstants.QRES_EXECUTABLE;
            qresProcess.StartInfo.UseShellExecute = false;
            // 工作目录设回游戏根目录，避免 QRes 派生的游戏进程在 ClientApp\ 下找不到 spawn.ini
            qresProcess.StartInfo.WorkingDirectory = ProgramConstants.GamePath;

            if (!string.IsNullOrEmpty(extraCommandLine))
                qresProcess.StartInfo.Arguments = "c=16 /R " + "\"" + SafePath.CombineFilePath(ProgramConstants.GamePath, gameExecutableName) + "\" " + additionalExecutableName + "-SPAWN " + extraCommandLine;
            else
                qresProcess.StartInfo.Arguments = "c=16 /R " + "\"" + SafePath.CombineFilePath(ProgramConstants.GamePath, gameExecutableName) + "\" " + additionalExecutableName + "-SPAWN";
            qresProcess.EnableRaisingEvents = true;
            qresProcess.Exited += Process_Exited;
            Logger.Log("Launch executable: " + qresProcess.StartInfo.FileName);
            Logger.Log("Launch arguments: " + qresProcess.StartInfo.Arguments);
            try
            {
                qresProcess.Start();
            }
            catch (Exception ex)
            {
                Logger.Log("Error launching QRes: " + ex.ToString());
                GameProcessLaunchFailed?.Invoke(errorLaunchingTitle,
                    string.Format(errorLaunchingText, ProgramConstants.QRES_EXECUTABLE, ex.Message));
                Process_Exited(qresProcess, EventArgs.Empty);
                return;
            }

            if (Environment.ProcessorCount > 1 && SingleCoreAffinity)
                qresProcess.ProcessorAffinity = (IntPtr)2;
        }
        else
        {
            string arguments;

            if (!string.IsNullOrWhiteSpace(extraCommandLine))
                arguments = " " + additionalExecutableName + "-SPAWN " + extraCommandLine;
            else
                arguments = additionalExecutableName + "-SPAWN";

            FileInfo gameFileInfo = SafePath.GetFile(ProgramConstants.GamePath, gameExecutableName);

            var gameProcess = new Process();
            gameProcess.StartInfo.FileName = gameFileInfo.FullName;
            gameProcess.StartInfo.Arguments = arguments;
            gameProcess.StartInfo.UseShellExecute = false;
            // 对齐旧版启动语义：游戏按进程工作目录读 spawn.ini/资源。旧版客户端位于游戏根目录
            // 即 CWD=GamePath；迁移版自包含在 ClientApp\ 子目录，须显式设回游戏根目录，否则找不到 spawn.ini。
            gameProcess.StartInfo.WorkingDirectory = ProgramConstants.GamePath;

            gameProcess.EnableRaisingEvents = true;
            gameProcess.Exited += Process_Exited;

            Logger.Log("Launch executable: " + gameProcess.StartInfo.FileName);
            Logger.Log("Launch arguments: " + gameProcess.StartInfo.Arguments);
            try
            {
                gameProcess.Start();
                Logger.Log("GameProcessLogic: Process started.");
            }
            catch (Exception ex)
            {
                Logger.Log("Error launching " + gameFileInfo.Name + ": " + ex.ToString());
                GameProcessLaunchFailed?.Invoke(errorLaunchingTitle,
                    string.Format(errorLaunchingText, gameFileInfo.Name, ex.Message));
                Process_Exited(gameProcess, EventArgs.Empty);
                return;
            }

            if ((RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                && Environment.ProcessorCount > 1 && SingleCoreAffinity)
            {
                gameProcess.ProcessorAffinity = (IntPtr)2;
            }
        }

        GameProcessStarted?.Invoke();

        Logger.Log("Waiting for qres.dat or " + gameExecutableName + " to exit.");
    }

    private static void Process_Exited(object? sender, EventArgs e)
    {
        Logger.Log("GameProcessLogic: Process exited.");
        if (sender is Process proc)
        {
            proc.Exited -= Process_Exited;
            proc.Dispose();
        }
        GameProcessExited?.Invoke();
    }
}
