using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using CnCNetClient.Tools;
using CnCNetClient.Core.Extensions;

namespace CnCNetClient.Core
{
    /// <summary>
    /// Contains various static variables and constants that the client uses for operation.
    /// </summary>
    public static class ProgramConstants
    {
        public static readonly string StartupExecutable = Assembly.GetEntryAssembly().Location;

        public static readonly string StartupPath = SafePath.CombineDirectoryPath(new FileInfo(StartupExecutable).Directory.FullName);

        public static string GamePath = SafePath.CombineDirectoryPath(GetGamePath(StartupPath));

        public static string ClientUserFilesPath => SafePath.CombineDirectoryPath(GamePath, "Client");

        public static event EventHandler PlayerNameChanged;

        public const string QRES_EXECUTABLE = "qres.dat";

        /// <summary>
        /// CnCNet 游戏列表/房间协议修订号（原版即为 "A"，勿改为网络握手修订号）。
        /// 服务器 GAME 广播、建房广播、房间 TOPIC 均以此字段校验；迁移版曾误设 "R14"
        /// 导致 <c>LobbyService.OnBroadcastCTCP</c> 丢弃全部房间广播、大厅房间列表恒为空。
        /// </summary>
        public const string CNCNET_PROTOCOL_REVISION = "A";
        public const string LAN_PROTOCOL_REVISION = "RL8";
        public const int LAN_PORT = 1234;
        public const int LAN_INGAME_PORT = 1234;
        public const int LAN_LOBBY_PORT = 1232;
        public const int LAN_GAME_LOBBY_PORT = 1233;
        public const char LAN_DATA_SEPARATOR = (char)01;
        public const char LAN_MESSAGE_SEPARATOR = (char)02;

        public const string SPAWNMAP_INI = "spawnmap.ini";
        public const string SPAWNER_SETTINGS = "spawn.ini";
        public const string SAVED_GAME_SPAWN_INI = "Saved Games/spawnSG.ini";

        /// <summary>
        /// The locale code that corresponds to the language the hardcoded client strings are in.
        /// </summary>
        public const string HARDCODED_LOCALE_CODE = "en";

        /// <summary>
        /// Used to denote <see cref="Environment.NewLine"/> in the INI files.
        /// </summary>
        /// <remarks>
        /// Historically Westwood used '@' for this purpose, so we keep it for compatibility.
        /// </remarks>
        public const string INI_NEWLINE_PATTERN = "@";

        public const int GAME_ID_MAX_LENGTH = 4;

        public static readonly Encoding LAN_ENCODING = Encoding.UTF8;

#if NETFRAMEWORK
        private static bool? isMono;

        /// <summary>
        /// Gets a value whether or not the application is running under Mono. Uses lazy loading and caching.
        /// </summary>
        public static bool ISMONO => isMono ??= Type.GetType("Mono.Runtime") != null;
#endif

        public static string GAME_VERSION = "Undefined";
        private static string PlayerName = "No name";

        public static string PLAYERNAME
        {
            get { return PlayerName; }
            set
            {
                string oldPlayerName = PlayerName;
                PlayerName = value;
                if (oldPlayerName != PlayerName)
                    PlayerNameChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static string BASE_RESOURCE_PATH = "Resources";
        public static string RESOURCES_DIR = BASE_RESOURCE_PATH;

        /// <summary>外壳启动器约定：客户端程序目录名（与 CnCNetClient.Launcher 的 ClientDir 一致）。</summary>
        public const string CLIENT_APP_DIRECTORY_NAME = "ClientApp";

        /// <summary>外壳启动器文件名（安装根下的自更新重启入口）。</summary>
        public const string LAUNCHER_EXECUTABLE_NAME = "Launcher.exe";

        public static int LOG_LEVEL = 1;

        public static bool IsInGame { get; set; }

        public static string GetResourcePath()
        {
            return SafePath.CombineDirectoryPath(GamePath, RESOURCES_DIR);
        }

        public static string GetBaseResourcePath()
        {
            return SafePath.CombineDirectoryPath(GamePath, BASE_RESOURCE_PATH);
        }

        public const string GAME_INVITE_CTCP_COMMAND = "INVITE";
        public const string GAME_INVITATION_FAILED_CTCP_COMMAND = "INVITATION_FAILED";

        public static string GetAILevelName(int aiLevel)
        {
            if (aiLevel > -1 && aiLevel < AI_PLAYER_NAMES.Count)
                return AI_PLAYER_NAMES[aiLevel];

            return "";
        }

        public static readonly List<string> TEAMS = new List<string> { "A", "B", "C", "D" };

        // Static fields might be initialized before the translation file is loaded. Change to readonly properties here.
        public static List<string> AI_PLAYER_NAMES => new List<string> { "Easy AI".L10N("Client:Main:EasyAIName"), "Medium AI".L10N("Client:Main:MediumAIName"), "Hard AI".L10N("Client:Main:HardAIName") };

        public static string LogFileName { get; set; }

        /// <summary>
        /// This method finds the "Resources" directory by traversing the directory tree upwards from the startup path,
        /// and returns the parent directory (the game root) if found.
        /// </summary>
        /// <remarks>
        /// 资源定位的唯一权威实现：UI 层 <c>EnvironmentCheck</c> 与 Integration 的
        /// <c>IAssetPathProvider</c> 均复用本方法，避免多份相似实现漂移。
        /// </remarks>
        /// <param name="startupPath">启动目录（可执行文件所在目录）。</param>
        /// <returns>游戏根目录；未找到 Resources 目录时返回 null。</returns>
        public static string FindGamePath(string startupPath)
        {
            DirectoryInfo currentDir = new(startupPath);
            for (int i = 0; i < 3; i++)
            {
                if (currentDir is null)
                    break;

                // Determine if currentDir is the "Resources" folder
                if (string.Equals(currentDir.Name, "Resources", StringComparison.OrdinalIgnoreCase))
                    return currentDir.Parent?.FullName;

                // Additional check. This makes developers to debug the client inside Visual Studio a little bit easier.
                DirectoryInfo resourcesDir = currentDir.GetDirectories("Resources", SearchOption.TopDirectoryOnly).FirstOrDefault();
                if (resourcesDir is not null)
                    return resourcesDir.Parent?.FullName;

                currentDir = currentDir.Parent;
            }

            return null;
        }

        /// <summary>
        /// 推断 Resources 的「正确位置」（仅用于资源缺失提示）。
        /// 客户端由外壳启动器放在 <c>ClientApp\</c> 内时，安装根是启动目录的上一级
        /// （Resources 与 Launcher.exe 同级）；否则视启动目录本身为安装根。
        /// 用 <see cref="DirectoryInfo.Parent"/> 而非 <c>Path.GetDirectoryName</c>，
        /// 避免盘符根（如 <c>C:\</c>）拼出 <c>C:Resources</c> 这类驱动器相对路径。
        /// </summary>
        /// <param name="startupPath">启动目录（可执行文件所在目录）。</param>
        public static string GetExpectedResourcesPath(string startupPath)
        {
            var startupDirectory = new DirectoryInfo(startupPath);
            string? parent = startupDirectory.Parent?.FullName;

            bool insideClientApp =
                string.Equals(startupDirectory.Name, CLIENT_APP_DIRECTORY_NAME, StringComparison.OrdinalIgnoreCase) ||
                (parent is not null && File.Exists(Path.Combine(parent, LAUNCHER_EXECUTABLE_NAME)));

            string installRoot = insideClientApp && parent is not null ? parent : startupDirectory.FullName;
            return Path.Combine(installRoot, BASE_RESOURCE_PATH);
        }

        private static string GetGamePath(string startupPath)
        {
            // FindGamePath 返回游戏根目录（Resources 的父目录）；未找到时回退启动目录，
            // 避免类型初始化器抛异常（TypeInitializationException 会被 CLR 永久缓存）。
            // 资源缺失由 EnvironmentCheck 软提示，不再使整个 ProgramConstants 类型失效。
            return FindGamePath(startupPath) ?? startupPath;
        }
    }
}