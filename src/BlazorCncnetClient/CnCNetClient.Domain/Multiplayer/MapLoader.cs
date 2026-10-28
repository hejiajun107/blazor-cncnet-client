using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

using CnCNetClient.Core;
using CnCNetClient.Core.Caching;
using CnCNetClient.Core.Extensions;

using CnCNetClient.Tools;

using SixLabors.ImageSharp;

namespace CnCNetClient.Domain.Multiplayer
{
    public enum MapChangeType
    {
        Added,
        Updated,
        Removed
    }

    public class MapLoader : IDisposable
    {
        private const string CUSTOM_MAPS_DIRECTORY = "Maps/Custom";

        private const int CurrentCustomMapCacheVersion = 5;

        private static string GetCustomMapCacheFileName(int version) => version == 1 ? "custom_map_cache" : $"custom_map_cache_v{version}";

        private static readonly string CUSTOM_MAPS_CACHE = SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, GetCustomMapCacheFileName(CurrentCustomMapCacheVersion));
        private static readonly IReadOnlyList<string> LEGACY_CUSTOM_MAP_CACHE_FILES = Enumerable.Range(0, CurrentCustomMapCacheVersion)
            .Select(version => SafePath.CombineFilePath(ProgramConstants.ClientUserFilesPath, GetCustomMapCacheFileName(version)))
            .ToList();

        private const string MultiMapsSection = "MultiMaps";
        private const string GameModesSection = "GameModes";
        private const string GameModeAliasesSection = "GameModeAliases";
        private readonly JsonSerializerOptions jsonSerializerOptions = new JsonSerializerOptions { IncludeFields = true };
        private MapFileWatcher mapFileWatcher;
        private readonly object mapModificationLock = new object();
        private const int _mapChangeRetryCount = 3;

        // 地图全量加载防重入：Program.cs 预热与 MapPreviewService 懒触发可能并发调用 LoadMapsAsync，
        // 若各自启动一次 LoadMapsInternalAsync，会重复解析 333+ 张地图并并发写 _initialGameModes，
        // 导致 CPU 双倍峰值与进入遭遇战页明显卡顿（M5.6 修复）。
        private readonly object _mapsLoadLock = new object();
        private Task? _mapsLoadingTask;

        // Mutable buffer used only during the initial map-loading pass. After
        // LoadMapsInternalAsync publishes the first snapshot, it is set to null
        // and every subsequent update goes through ReplaceGameModeSnapshot under
        // mapModificationLock.
        // TODO: Consider refactoring this MapLoader class into two classes, one for initial loading and one for runtime updates, to avoid having this mutable state that is only used during initialization.
        private List<GameMode> _initialGameModes = [];

        private sealed class Snapshot
        {
            public IReadOnlyList<GameMode> GameModes { get; }
            public IReadOnlyGameModeMapCollection GameModeMaps { get; }

            public Snapshot(IReadOnlyList<GameMode> gameModes, IReadOnlyGameModeMapCollection gameModeMaps)
            {
                GameModes = gameModes;
                GameModeMaps = gameModeMaps;
            }

            // 房间列表每张卡片每次渲染都要做匹配，预先建索引避免反复 O(卡片×地图) 扫描。
            // 用锁保证并发渲染时只建一次（字典发布前不可见）。
            private readonly object _indexLock = new();
            private Dictionary<string, Map>? _hashIndex;
            private Dictionary<string, Map>? _nameIndex;
            private Dictionary<string, Map>? _normalizedNameIndex;
            private Dictionary<string, Map>? _stemIndex;

            private void EnsureIndexes()
            {
                if (_hashIndex is not null)
                    return;

                lock (_indexLock)
                {
                    if (_hashIndex is not null)
                        return;

                    var hashIndex = new Dictionary<string, Map>(StringComparer.OrdinalIgnoreCase);
                    var nameIndex = new Dictionary<string, Map>(StringComparer.OrdinalIgnoreCase);
                    var normalizedNameIndex = new Dictionary<string, Map>(StringComparer.OrdinalIgnoreCase);
                    var stemIndex = new Dictionary<string, Map>(StringComparer.OrdinalIgnoreCase);

                    foreach (var map in GameModeMaps.Select(gmm => gmm.Map))
                    {
                        if (!string.IsNullOrEmpty(map.SHA1))
                            hashIndex.TryAdd(map.SHA1, map);

                        if (!string.IsNullOrEmpty(map.UntranslatedName))
                        {
                            nameIndex.TryAdd(map.UntranslatedName, map);
                            normalizedNameIndex.TryAdd(NormalizeMapName(map.UntranslatedName), map);
                        }

                        string stem = NormalizeMapName(GetPathStem(map.BaseFilePath));
                        if (stem.Length > 0)
                            stemIndex.TryAdd(stem, map);
                    }

                    _stemIndex = stemIndex;
                    _normalizedNameIndex = normalizedNameIndex;
                    _nameIndex = nameIndex;
                    _hashIndex = hashIndex;
                }
            }

            public Map? FindByHash(string hash)
            {
                EnsureIndexes();
                return _hashIndex!.TryGetValue(hash, out var map) ? map : null;
            }

            public Map? FindByName(string name)
            {
                EnsureIndexes();
                return _nameIndex!.TryGetValue(name, out var map) ? map : null;
            }

            public Map? FindByNormalizedName(string normalizedName)
            {
                EnsureIndexes();
                return _normalizedNameIndex!.TryGetValue(normalizedName, out var map) ? map : null;
            }

            public Map? FindByStem(string normalizedStem)
            {
                EnsureIndexes();
                return _stemIndex!.TryGetValue(normalizedStem, out var map) ? map : null;
            }
        }

        private volatile Snapshot _snapshot = new Snapshot(Array.Empty<GameMode>(), new GameModeMapCollection(Array.Empty<GameMode>()));

        /// <summary>
        /// List of game modes.
        /// </summary>
        public IReadOnlyList<GameMode> GameModes => _snapshot.GameModes;

        public IReadOnlyGameModeMapCollection GameModeMaps => _snapshot.GameModeMaps;

        /// <summary>
        /// 取本机已安装地图在当前语言下的显示名。
        /// 联机房间列表只有房主广播的未翻译原文（GAME 8 名字 / 13 哈希字段），本地装了同一张图时
        /// 即可换成当前语言的名字。匹配顺序：SHA1 → 原始名 → 归一化名（忽略空格/标点/大小写）
        /// → 文件名主干（房主广播文件名时）。未安装则原样返回（由缺失提示处理）。
        /// </summary>
        public string GetLocalizedMapName(string mapHash, string untranslatedName)
        {
            Snapshot snapshot = _snapshot;

            if (!string.IsNullOrEmpty(mapHash))
            {
                var byHash = snapshot.FindByHash(mapHash.Trim());
                if (byHash is not null)
                    return byHash.Name;
            }

            if (!string.IsNullOrEmpty(untranslatedName))
            {
                var byName = snapshot.FindByName(untranslatedName);
                if (byName is not null)
                    return byName.Name;

                // 不同地图包/服务端的 Description 常有细微差异（"(2) Dust Bowl" vs "(2) Dustbowl"），
                // 去掉空格与标点后比较可覆盖绝大多数情况。
                string normalized = NormalizeMapName(untranslatedName);
                if (normalized.Length > 0)
                {
                    var byNormalized = snapshot.FindByNormalizedName(normalized);
                    if (byNormalized is not null)
                        return byNormalized.Name;

                    var byStem = snapshot.FindByStem(NormalizeMapName(GetPathStem(untranslatedName)));
                    if (byStem is not null)
                        return byStem.Name;
                }
            }

            LogUnresolvedRoomMap(mapHash, untranslatedName, snapshot);

            // 本机没装这张图时只能显示房主广播的原文，但把 "[8] " 这类人数前缀去掉——
            // 原版 UI 也不显示它，留着会显示成 "[8] Black Sea" 这种很怪的名字。
            return string.IsNullOrWhiteSpace(untranslatedName)
                ? untranslatedName
                : StripPlayerCountPrefix(untranslatedName);
        }

        /// <summary>
        /// 房间列表出现本机匹配不到的地图时记录一次（同一个「哈希|名字」只记一条）。
        /// 房间列表是唯一无法离线复现的输入源，出问题时靠这条日志定位是本机缺图还是匹配规则不覆盖。
        /// </summary>
        private readonly HashSet<string> _loggedUnresolvedRoomMaps = new(StringComparer.OrdinalIgnoreCase);

        private void LogUnresolvedRoomMap(string mapHash, string untranslatedName, Snapshot snapshot)
        {
            if (string.IsNullOrEmpty(untranslatedName) && string.IsNullOrEmpty(mapHash))
                return;

            string key = (mapHash ?? string.Empty) + "|" + (untranslatedName ?? string.Empty);
            lock (_loggedUnresolvedRoomMaps)
            {
                if (!_loggedUnresolvedRoomMaps.Add(key))
                    return;
            }

            Logger.Log($"房间列表地图名未匹配到本机地图：name=\"{untranslatedName}\" hash=\"{mapHash}\"（本机地图 {snapshot.GameModeMaps.Count} 条）");
        }

        /// <summary>取路径最后一段（兼容 / 与 \ 分隔符），不区分是否带扩展名。</summary>
        private static string GetPathStem(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            int separator = path.LastIndexOfAny(['\\', '/']);
            return separator >= 0 ? path.Substring(separator + 1) : path;
        }

        /// <summary>
        /// 去掉 "(8) " / "[2-8] " 这类人数前缀。本机 MPMaps.ini 用圆括号，房主的地图包常写成方括号，
        /// 两种都要处理：否则同一张已安装的地图也会匹配失败
        /// （实测日志：name="[8] Russian Roulette"，本机是 "(8) Russian Roulette"）。
        /// 只剥短前缀（最多 6 字符），避免把名字里正常的括号内容吃掉。
        /// </summary>
        private static string StripPlayerCountPrefix(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name ?? string.Empty;

            string trimmed = name.Trim();

            if (trimmed.Length > 2 && (trimmed[0] == '(' || trimmed[0] == '['))
            {
                char close = trimmed[0] == '(' ? ')' : ']';
                int closeIndex = trimmed.IndexOf(close);
                if (closeIndex > 0 && closeIndex <= 6)
                    return trimmed[(closeIndex + 1)..].Trim();
            }

            return trimmed;
        }

        /// <summary>
        /// 比较用的地图名归一化：剥掉人数前缀后只保留字母与数字并转小写。
        /// 房主客户端的地图包与我们不完全一致时，名字常有括号样式、空格、大小写等差异。
        /// </summary>
        private static string NormalizeMapName(string name)
        {
            string trimmed = StripPlayerCountPrefix(name);
            if (trimmed.Length == 0)
                return string.Empty;

            var builder = new System.Text.StringBuilder(trimmed.Length);
            foreach (char c in trimmed)
            {
                if (char.IsLetterOrDigit(c))
                    builder.Append(char.ToLowerInvariant(c));
            }

            return builder.ToString();
        }

        /// <summary>
        /// 取游戏模式在当前语言下的显示名。房主广播的是未翻译的 UI 名
        /// （GAME 9 字段，见 GameRoomService 组播），本地匹配到该模式后换成译文。
        /// </summary>
        public string GetLocalizedGameModeName(string untranslatedName)
        {
            if (string.IsNullOrEmpty(untranslatedName))
                return untranslatedName;

            var gameMode = _snapshot.GameModes.FirstOrDefault(
                gm => string.Equals(gm.UntranslatedUIName, untranslatedName, StringComparison.OrdinalIgnoreCase));

            return gameMode?.UIName ?? untranslatedName;
        }

        /// <summary>
        /// An event that is fired when the maps have been loaded.
        /// </summary>
        public event EventHandler MapLoadingComplete;

        /// <summary>
        /// Fired when a map file is added, updated, or removed.
        /// </summary>
        public event EventHandler<MapChangedEventArgs> MapChanged;

        /// <summary>
        /// A list of game mode aliases.
        /// Every game mode entry that exists in this dictionary will get
        /// replaced by the game mode entries of the value string array
        /// when map is added to game mode map lists.
        /// </summary>
        private Dictionary<string, string[]> GameModeAliases = new Dictionary<string, string[]>();

        private Dictionary<string, string> _translatedMapNames = new();

        /// <summary>
        /// A dictionary of translated map names. Used to look up the 
        /// translated name of a map without knowing the ID of the map.
        /// </summary>
        public IReadOnlyDictionary<string, string> TranslatedMapNames => _translatedMapNames;

        /// <summary>
        /// List of gamemodes allowed to be used on custom maps in order for them to display in map list.
        /// </summary>
        private string[] AllowedGameModes = ClientConfiguration.Instance.AllowedCustomGameModes.Split(',');

        public const int MapPreviewCacheCapacity = 100;

        private readonly IMapPreviewCacheManager mapPreviewCacheManager = new MapPreviewCacheManager(capacity: MapPreviewCacheCapacity);

        public MapLoader() { }

        public void Initialize()
        {
            MapLoadingComplete += (sender, args) => StartMapFileWatcher();
        }

        /// <summary>
        /// Sets up file watching for maps.
        /// </summary>
        public void StartMapFileWatcher()
        {
            if (mapFileWatcher != null)
                return;

            string customMapsPath = SafePath.CombineDirectoryPath(ProgramConstants.GamePath, CUSTOM_MAPS_DIRECTORY);

            mapFileWatcher = new MapFileWatcher(customMapsPath, ClientConfiguration.Instance.MapFileExtension);
            mapFileWatcher.MapFileChanged += OnMapFileChanged;
            mapFileWatcher.StartWatching();
        }

        /// <summary>
        /// Asynchronously loads maps based on INI info as well as those in the custom maps directory.
        /// 防重入：预热与懒触发并发调用时复用同一个加载任务，避免重复解析/并发写 _initialGameModes。
        /// </summary>
        public Task LoadMapsAsync()
        {
            lock (_mapsLoadLock)
            {
                return _mapsLoadingTask ??= Task.Run(LoadMapsInternalAsync);
            }
        }

        private async Task LoadMapsInternalAsync()
        {
            Logger.Log("MapLoader: Map loading task started.");
            var stopwatch = Stopwatch.StartNew();

            string mpMapsPath = SafePath.CombineFilePath(ProgramConstants.GamePath, ClientConfiguration.Instance.MPMapsIniPath);

            Logger.Log($"MapLoader: Loading maps from {mpMapsPath}.");

            IniFile mpMapsIni = new IniFile(mpMapsPath);

            LoadGameModes(mpMapsIni);
            LoadGameModeAliases(mpMapsIni);
            // LoadMultiMapsAsync and LoadCustomMapsAsync both modify the game mode map collection. We intend to keep the collection non-thread-safe for performance, so the two methods must not be called simultaneously.
            await LoadMultiMapsAsync(mpMapsIni);
            await LoadCustomMapsAsync();

            Logger.Log("MapLoader: Post-processing game mode map collections.");
            PublishSnapshot(_initialGameModes);
            _initialGameModes = null;

            // Clean up any name-based favorite entries after migration (legacy: changed from name to sha1)
            CleanupMigratedFavorites();

            stopwatch.Stop();

            Logger.Log($"MapLoader: Map loading complete. Total time: {stopwatch.ElapsedMilliseconds} ms");
            MapLoadingComplete?.Invoke(this, EventArgs.Empty);
        }

        private async void OnMapFileChanged(object sender, MapFileEventArgs e)
        {
            switch (e.ChangeType)
            {
                case WatcherChangeTypes.Created:
                    await HandleMapFileAdded(e.FilePath);
                    break;
                case WatcherChangeTypes.Changed:
                    await HandleMapFileChanged(e.FilePath);
                    break;
                case WatcherChangeTypes.Deleted:
                    await HandleMapFileDeleted(e.FilePath);
                    break;
            }
        }

        private async Task HandleMapFileAdded(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                string baseFilePath = GetBaseFilePathFromFullPath(filePath);
                if (string.IsNullOrEmpty(baseFilePath))
                    return;

                // If, for instance, the file was just extracted, the program that created it may still
                // have a lock on the file. Retry a couple of times.
                Map map = null;
                bool success = false;

                for (int attempt = 0; attempt < _mapChangeRetryCount; attempt++)
                {
                    try
                    {
                        map = new Map(baseFilePath, true);
                        if (map.InitializeFromCustomMap())
                        {
                            success = true;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        if (attempt < _mapChangeRetryCount - 1)
                            await Task.Delay(100);
                        else
                            throw;
                    }
                }

                if (success && map != null)
                {
                    lock (mapModificationLock)
                    {
                        List<GameMode> gameModeSnapshot = CloneGameModeSnapshot();

                        if (IsMapAlreadyLoaded(map.SHA1, gameModeSnapshot))
                            return;

                        AddMapToGameModes(map, gameModeSnapshot, true);
                        ReplaceGameModeSnapshot(gameModeSnapshot);

                        Logger.Log($"MapLoader: Added new map {map.Name} from {filePath}");
                        MapChanged?.Invoke(this, new MapChangedEventArgs(map, MapChangeType.Added));
                    }
                }
                else
                {
                    Logger.Log($"MapLoader: Failed to load map info from {filePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"MapLoader: Error adding map from {filePath}: {ex.Message}");
            }
        }

        private async Task HandleMapFileChanged(string filePath)
        {
            try
            {
                string baseFilePath = GetBaseFilePathFromFullPath(filePath);
                if (string.IsNullOrEmpty(baseFilePath))
                    return;

                // If editing a map, the program that saved the new version may still
                // have a lock on the file. Retry a couple of times.
                Map newMap = null;
                bool success = false;

                for (int attempt = 0; attempt < _mapChangeRetryCount; attempt++)
                {
                    try
                    {
                        newMap = new Map(baseFilePath, true);
                        if (newMap.InitializeFromCustomMap())
                        {
                            success = true;
                            break;
                        }
                    }
                    catch (IOException)
                    {
                        if (attempt < _mapChangeRetryCount - 1)
                            await Task.Delay(100);
                        else
                            throw;
                    }
                }

                if (success && newMap != null)
                {
                    lock (mapModificationLock)
                    {
                        List<GameMode> gameModeSnapshot = CloneGameModeSnapshot();
                        string oldSHA1 = FindMapSHA1ByFilePath(baseFilePath, gameModeSnapshot);

                        if (!string.IsNullOrEmpty(oldSHA1))
                        {
                            if (oldSHA1 != newMap.SHA1)
                            {
                                // SHA1 changed, remove old and add new
                                RemoveMapBySHA1(oldSHA1, gameModeSnapshot);
                                AddMapToGameModes(newMap, gameModeSnapshot, true);
                                ReplaceGameModeSnapshot(gameModeSnapshot);

                                Logger.Log($"MapLoader: Updated map {newMap.Name} from {filePath} (SHA1 changed: {oldSHA1} -> {newMap.SHA1})");
                                MapChanged?.Invoke(this, new MapChangedEventArgs(newMap, MapChangeType.Updated, oldSHA1));
                            }
                            else
                            {
                                Logger.Log($"MapLoader: Map file {filePath} changed but SHA1 remained the same ({newMap.SHA1})");
                            }
                        }
                        else
                        {
                            // Map not found, treat as new
                            Logger.Log($"MapLoader: Changed event for unknown map {filePath}, treating as new");
                            AddMapToGameModes(newMap, gameModeSnapshot, true);
                            ReplaceGameModeSnapshot(gameModeSnapshot);
                            MapChanged?.Invoke(this, new MapChangedEventArgs(newMap, MapChangeType.Added));
                        }
                    }
                }
                else
                {
                    Logger.Log($"MapLoader: Failed to reload map info from {filePath}");
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"MapLoader: Error updating map from {filePath}: {ex.Message}");
            }
        }

        private async Task HandleMapFileDeleted(string filePath)
        {
            try
            {
                string baseFilePath = GetBaseFilePathFromFullPath(filePath);
                if (string.IsNullOrEmpty(baseFilePath))
                    return;

                lock (mapModificationLock)
                {
                    List<GameMode> gameModeSnapshot = CloneGameModeSnapshot();
                    string mapSHA1 = FindMapSHA1ByFilePath(baseFilePath, gameModeSnapshot);

                    if (!string.IsNullOrEmpty(mapSHA1))
                    {
                        var removedMap = FindMapBySHA1(mapSHA1, gameModeSnapshot);
                        RemoveMapBySHA1(mapSHA1, gameModeSnapshot);
                        ReplaceGameModeSnapshot(gameModeSnapshot);

                        Logger.Log($"MapLoader: Removed map from {filePath}");
                        if (removedMap != null)
                            MapChanged?.Invoke(this, new MapChangedEventArgs(removedMap, MapChangeType.Removed));
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"MapLoader: Error removing map from {filePath}: {ex.Message}");
            }
        }

        /// <summary>
        /// Converts a full file path to the base file path used by the map system.
        /// C:\YR\Maps\Custom\abc123.map > Maps\Custom\abc123
        /// </summary>
        private string GetBaseFilePathFromFullPath(string fullPath)
        {
            try
            {
                string gamePathNormalized = Path.GetFullPath(ProgramConstants.GamePath);
                string fullPathNormalized = Path.GetFullPath(fullPath);

                if (!fullPathNormalized.StartsWith(gamePathNormalized, StringComparison.OrdinalIgnoreCase))
                    return null;

                string relativePath = fullPathNormalized.Substring(gamePathNormalized.Length);
                if (relativePath.StartsWith(Path.DirectorySeparatorChar.ToString())
                    || relativePath.StartsWith(Path.AltDirectorySeparatorChar.ToString()))
                {
                    relativePath = relativePath.Substring(1);
                }

                string baseFilePath = relativePath.Substring(0, relativePath.Length - Path.GetExtension(relativePath).Length);

                return baseFilePath.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }
            catch (Exception ex)
            {
                Logger.Log($"MapLoader: Error converting file path {fullPath}: {ex.Message}");
                return null;
            }
        }

        private static bool IsMapAlreadyLoaded(string sha1, IEnumerable<GameMode> gameModes)
            => gameModes.SelectMany(gm => gm.Maps).Any(map => map.SHA1 == sha1);

        private static Map FindMapBySHA1(string sha1, IEnumerable<GameMode> gameModes)
            => gameModes.SelectMany(gm => gm.Maps).FirstOrDefault(map => map.SHA1 == sha1);

        private static string FindMapSHA1ByFilePath(string baseFilePath, IEnumerable<GameMode> gameModes)
            => gameModes.SelectMany(gm => gm.Maps).FirstOrDefault(map => !map.Official && map.BaseFilePath.Equals(baseFilePath, StringComparison.OrdinalIgnoreCase))?.SHA1;

        private static void RemoveMapBySHA1(string sha1, IEnumerable<GameMode> gameModes)
        {
            foreach (var gameMode in gameModes)
                gameMode.Maps.RemoveAll(map => map.SHA1 == sha1);
        }

        private void ReplaceGameModeSnapshot(List<GameMode> gameModes, bool removeEmptyGameModes = true) =>
            PublishSnapshot(gameModes, removeEmptyGameModes);

        private void PublishSnapshot(List<GameMode> gameModes, bool removeEmptyGameModes = true)
        {
            if (removeEmptyGameModes)
                gameModes.RemoveAll(g => g.Maps.Count < 1);

            _snapshot = new Snapshot(gameModes, new GameModeMapCollection(gameModes));
        }

        private List<GameMode> CloneGameModeSnapshot() => GameModes.Select(gameMode => gameMode.Clone()).ToList();

        private async Task LoadMultiMapsAsync(IniFile mpMapsIni)
        {
            List<string> keys = mpMapsIni.GetSectionKeys(MultiMapsSection);

            if (keys == null)
            {
                Logger.Log("Loading multiplayer map list failed!!!");
                return;
            }

            // 限流并行解析（M5.6）：333+ 张官方地图若全并行 Task.Run 会瞬间占满 CPU，
            // 预热加载期间 UI 交互卡顿；限制并发数后按原 keys 顺序回填结果。
            var mapsByIndex = new ConcurrentDictionary<int, Map>();
            await Parallel.ForEachAsync(
                keys.Select((key, index) => (key, index)),
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
                (item, _) =>
                {
                    try
                    {
                        string mapFilePathValue = mpMapsIni.GetStringValue(MultiMapsSection, item.key, string.Empty);
                        string mapFilePath = SafePath.CombineFilePath(mapFilePathValue);
                        FileInfo mapFile = SafePath.GetFile(ProgramConstants.GamePath, FormattableString.Invariant($"{mapFilePath}.{ClientConfiguration.Instance.MapFileExtension}"));

                        if (!mapFile.Exists)
                        {
                            Logger.Log("Map " + mapFile.FullName + " doesn't exist!");
                            return ValueTask.CompletedTask;
                        }

                        var map = new Map(mapFilePathValue, false);
                        if (map.InitializeFromMpMapsINI(mpMapsIni))
                            mapsByIndex[item.index] = map;
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"Error loading map for key {item.key}: {ex}");
                    }
                    return ValueTask.CompletedTask;
                });

            foreach ((int index, Map map) in mapsByIndex.OrderBy(kv => kv.Key))
            {
                AddMapToGameModes(map, _initialGameModes, false);
                _translatedMapNames[map.UntranslatedName] = map.Name;
            }
        }

        private void LoadGameModes(IniFile mpMapsIni)
        {
            var gameModes = mpMapsIni.GetSectionKeys(GameModesSection);
            if (gameModes != null)
            {
                foreach (string key in gameModes)
                {
                    string gameModeName = mpMapsIni.GetStringValue(GameModesSection, key, string.Empty);
                    if (!string.IsNullOrEmpty(gameModeName))
                    {
                        GameMode gm = new GameMode(gameModeName);
                        _initialGameModes.Add(gm);
                    }
                }
            }
        }

        private void LoadGameModeAliases(IniFile mpMapsIni)
        {
            var gmAliases = mpMapsIni.GetSectionKeys(GameModeAliasesSection);

            if (gmAliases != null)
            {
                foreach (string key in gmAliases)
                {
                    GameModeAliases.Add(key, mpMapsIni.GetStringValue(GameModeAliasesSection, key, string.Empty).Split(
                        new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }

        private async Task LoadCustomMapsAsync()
        {
            DirectoryInfo customMapsDirectory = SafePath.GetDirectory(ProgramConstants.GamePath, CUSTOM_MAPS_DIRECTORY);

            if (!customMapsDirectory.Exists)
            {
                Logger.Log($"Custom maps directory {customMapsDirectory} does not exist!");
                return;
            }

            Logger.Log("MapLoader: Loading custom maps...");

            // Load custom map cache from file system
            Stopwatch stopwatch = Stopwatch.StartNew();

            IEnumerable<FileInfo> mapFiles = customMapsDirectory.EnumerateFiles($"*.{ClientConfiguration.Instance.MapFileExtension}");

            // Note: using synchronous file I/O here saves a noticeable amount of latency compared to async.
            CustomMapCache customMapCache = LoadCustomMapCache();

            stopwatch.Stop();
            Logger.Log(FormattableString.Invariant($"MapLoader: Loaded custom map cache from file system in {stopwatch.ElapsedMilliseconds} ms"));

            // Process uncached custom maps.
            stopwatch.Restart();

            List<string> localMapPaths;
            {
                int mapFileExtensionWithDotLength = $".{ClientConfiguration.Instance.MapFileExtension}".Length;

                // 限流并行（M5.6）：与官方地图同理，避免自定义地图全并行解析占满 CPU。
                var pathsByIndex = new ConcurrentDictionary<int, string>();
                await Parallel.ForEachAsync(
                    mapFiles.Select((mapFile, index) => (mapFile, index)),
                    new ParallelOptions { MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount / 2) },
                    (item, _) =>
                    {
                        string baseFilePath = item.mapFile.FullName.Substring(ProgramConstants.GamePath.Length);
                        baseFilePath = baseFilePath.Substring(0, baseFilePath.Length - mapFileExtensionWithDotLength);

                        string normalizedPath = baseFilePath
                            .Replace(Path.DirectorySeparatorChar, '/')
                            .Replace(Path.AltDirectorySeparatorChar, '/');

                        if (customMapCache.Items.TryGetValue(normalizedPath, out var cachedItem) && !cachedItem.IsOutdated())
                        {
                            // Use cached map
                            pathsByIndex[item.index] = normalizedPath;
                            return ValueTask.CompletedTask;
                        }

                        // Not in cache or outdated
                        var map = new Map(normalizedPath, true);
                        if (map.InitializeFromCustomMap())
                            customMapCache.Items[normalizedPath] = new CustomMapCache.Item(map);

                        pathsByIndex[item.index] = normalizedPath;
                        return ValueTask.CompletedTask;
                    });

                localMapPaths = pathsByIndex.OrderBy(kv => kv.Key).Select(kv => kv.Value).ToList();
            }

            stopwatch.Stop();
            Logger.Log(FormattableString.Invariant($"MapLoader: Processed uncached custom maps in {stopwatch.ElapsedMilliseconds} ms"));

            // Remove cached maps that no longer exist locally
            stopwatch.Restart();

            HashSet<string> missingMapPaths;
            {
                HashSet<string> cachedMapPaths = customMapCache.Items.Keys.ToHashSet();
                cachedMapPaths.ExceptWith(localMapPaths);
                missingMapPaths = cachedMapPaths;
            }

            foreach (string missingPath in missingMapPaths)
                customMapCache.Items.TryRemove(missingPath, out _);

            stopwatch.Stop();
            Logger.Log(FormattableString.Invariant($"MapLoader: Removed outdated maps from cache in {stopwatch.ElapsedMilliseconds} ms"));

            // Save custom map cache
            stopwatch.Restart();
            CacheCustomMaps(customMapCache);
            stopwatch.Stop();
            Logger.Log(FormattableString.Invariant($"MapLoader: Saved custom map cache to disk in {stopwatch.ElapsedMilliseconds} ms"));

            foreach (Map map in customMapCache.Items.Values.Select(item => item.Map))
            {
                AddMapToGameModes(map, _initialGameModes, false);
            }

            Logger.Log("MapLoader: Custom maps loaded.");
        }

        /// <summary>
        /// Save cache of custom maps.
        /// </summary>
        /// <param name="customMapCache">Custom maps to cache</param>
        private void CacheCustomMaps(CustomMapCache customMapCache)
        {
            var jsonData = JsonSerializer.Serialize(customMapCache, jsonSerializerOptions);

            File.WriteAllText(CUSTOM_MAPS_CACHE, jsonData);
        }

        /// <summary>
        /// Load previously cached custom maps
        /// </summary>
        /// <returns></returns>
        private CustomMapCache LoadCustomMapCache()
        {
            // Delete any legacy cache files
            foreach (string legacyCacheFile in LEGACY_CUSTOM_MAP_CACHE_FILES.Where(File.Exists))
            {
                try
                {
                    File.Delete(legacyCacheFile);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Failed to delete legacy custom map cache file {legacyCacheFile}: {ex.Message}");
                }
            }

            // Load current cache
            try
            {
                var jsonData = File.ReadAllText(CUSTOM_MAPS_CACHE);

                var customMapCache = JsonSerializer.Deserialize<CustomMapCache>(jsonData, jsonSerializerOptions);

                if (customMapCache?.Version != CurrentCustomMapCacheVersion)
                    return new CustomMapCache() { Version = CurrentCustomMapCacheVersion, Items = [] };

                foreach (CustomMapCache.Item customMap in customMapCache.Items.Values)
                    customMap.Map.AfterDeserialize(recalculateSHA: false);

                // Remove outdated items
                foreach (var mapPath in customMapCache.Items.Keys.ToList())
                {
                    if (customMapCache.Items[mapPath].IsOutdated())
                    {
                        customMapCache.Items.TryRemove(mapPath, out _);
                    }
                }

                return customMapCache;
            }
            catch (Exception)
            {
                return new CustomMapCache() { Version = CurrentCustomMapCacheVersion, Items = [] };
            }
        }

        /// <summary>
        /// Attempts to load a custom map.
        /// </summary>
        /// <param name="mapPath">The path to the map file relative to the game directory.</param>
        /// <param name="resultMessage">When method returns, contains a message reporting whether or not loading the map failed and how.</param>
        /// <returns>The map if loading it was successful, otherwise false.</returns>
        public Map LoadCustomMap(string mapPath, out string resultMessage)
        {
            Debug.Assert(!mapPath.EndsWith($".{ClientConfiguration.Instance.MapFileExtension}", StringComparison.InvariantCultureIgnoreCase), $"Unexpected map path {mapPath}. It should not end with the map extension.");

            // mapPath 是相对路径（如 "Maps/Custom/abc"），路径分隔符是合法的，
            // 仅对最后一段文件名做 WIN32 保留字符校验。
            string mapFileName = mapPath.Split('/', '\\')[^1];

            if (mapFileName != mapFileName.ToWin32FileName())
            {
                Logger.Log("LoadCustomMap: Map " + FormattableString.Invariant($"{mapPath}.{ClientConfiguration.Instance.MapFileExtension}") + " contains WIN32API reserved characters!");

                // Return "map file does not exist" message to hide technical details towards users
                resultMessage = string.Format("Map file {0} doesn't exist!".L10N("Client:MapLoader:MapFileDoesNotExist"), FormattableString.Invariant($"{mapPath}.{ClientConfiguration.Instance.MapFileExtension}"));

                return null;
            }

            string customMapFilePath = SafePath.CombineFilePath(ProgramConstants.GamePath, FormattableString.Invariant($"{mapPath}.{ClientConfiguration.Instance.MapFileExtension}"));
            FileInfo customMapFile = SafePath.GetFile(customMapFilePath);

            if (!customMapFile.Exists)
            {
                Logger.Log("LoadCustomMap: Map " + customMapFile.FullName + " not found!");
                resultMessage = string.Format("Map file {0} doesn't exist!".L10N("Client:MapLoader:MapFileDoesNotExist"), customMapFile.Name);

                return null;
            }

            Logger.Log("LoadCustomMap: Loading custom map " + customMapFile.FullName);

            var map = new Map(mapPath, true);

            if (map.InitializeFromCustomMap())
            {
                lock (mapModificationLock)
                {
                    List<GameMode> gameModeSnapshot = CloneGameModeSnapshot();

                    if (IsMapAlreadyLoaded(map.SHA1, gameModeSnapshot))
                    {
                        Logger.Log("LoadCustomMap: Custom map " + customMapFile.FullName + " is already loaded!");
                        resultMessage = string.Format("Map {0} is already loaded.".L10N("Client:MapLoader:MapAlreadyLoaded"), map.Name);

                        return null;
                    }

                    AddMapToGameModes(map, gameModeSnapshot, true);
                    ReplaceGameModeSnapshot(gameModeSnapshot);
                }

                Logger.Log("LoadCustomMap: Map " + customMapFile.FullName + " added successfully.");

                resultMessage = string.Format("Map {0} loaded successfully.".L10N("Client:MapLoader:MapLoadedSuccessfully"), map.Name);

                return map;
            }

            Logger.Log("LoadCustomMap: Loading map " + customMapFile.FullName + " failed!");
            resultMessage = string.Format("Loading map {0} failed!".L10N("Client:MapLoader:MapLoadingFailed"), Path.GetFileNameWithoutExtension(customMapFile.Name));

            return null;
        }

        public void DeleteCustomMap(GameModeMap gameModeMap)
        {
            Logger.Log("Deleting map " + gameModeMap.Map.UntranslatedName);
            File.Delete(gameModeMap.Map.CompleteFilePath);

            lock (mapModificationLock)
            {
                List<GameMode> gameModeSnapshot = CloneGameModeSnapshot();
                RemoveMapBySHA1(gameModeMap.Map.SHA1, gameModeSnapshot);
                ReplaceGameModeSnapshot(gameModeSnapshot);
            }
        }

        /// <summary>
        /// Adds map to all eligible game modes.
        /// </summary>
        /// <param name="map">Map to add.</param>
        /// <param name="gameModes">Game modes collection.</param>
        /// <param name="enableLogging">If set to true, a message for each game mode the map is added to is output to the log file.</param>
        private void AddMapToGameModes(Map map, List<GameMode> gameModes, bool enableLogging)
        {
            foreach (string gameMode in map.GameModes)
            {
                if (!GameModeAliases.TryGetValue(gameMode, out string[] gameModeAliases))
                    gameModeAliases = new string[] { gameMode };

                foreach (string gameModeAlias in gameModeAliases)
                {
                    if (!map.Official && !(AllowedGameModes.Contains(gameMode) || AllowedGameModes.Contains(gameModeAlias)))
                        continue;

                    GameMode gm = gameModes.FirstOrDefault(g => g.Name == gameModeAlias);
                    if (gm == null)
                    {
                        gm = new GameMode(gameModeAlias);
                        gameModes.Add(gm);
                    }

                    gm.Maps.Add(map);
                    if (enableLogging)
                        Logger.Log("AddMapToGameModes: Added map " + map.UntranslatedName + " to game mode " + gm.Name);
                }
            }
        }

        /// <summary>
        /// Removes any name-based favorite entries that have been successfully migrated to SHA1.
        /// This runs after all maps have been processed to ensure complete migration.
        /// </summary>
        private void CleanupMigratedFavorites()
        {
            var favoriteMaps = UserINISettings.Instance.FavoriteMaps;
            if (favoriteMaps == null || !favoriteMaps.Any())
                return;

            var entriesToRemove = new List<string>();

            foreach (string favoriteKey in favoriteMaps)
            {
                string[] parts = favoriteKey.Split(':');
                if (parts.Length != 2)
                    continue;

                string mapName = parts[0];
                string gameModeName = parts[1];

                // Check if there's a corresponding SHA1-based entry for any map with this name
                var gameMode = GameModes.FirstOrDefault(gm => gm.Name == gameModeName);
                if (gameMode != null)
                {
                    bool hasMigratedVersion = gameMode.Maps
                        .Where(m => m.UntranslatedName == mapName)
                        .Any(m => favoriteMaps.Contains($"{m.SHA1}:{gameModeName}"));

                    if (hasMigratedVersion)
                        entriesToRemove.Add(favoriteKey);
                }
            }

            // Remove the name-based entries
            if (entriesToRemove.Any())
            {
                foreach (string entry in entriesToRemove)
                    favoriteMaps.Remove(entry);

                UserINISettings.Instance.WriteFavoriteMaps();
            }
        }

        public void PrefetchCachedPreviewImageFromMap(Map map)
        {
            if (map?.IsNonImmediatePreviewImageAvailable() ?? false)
            {
                _ = mapPreviewCacheManager.Request(map, out CacheLease<Image>? lease, addToQueue: true);
                lease?.Dispose();
            }
        }

        public Image? GetPreviewTextureFromMap(Map map, bool syncLoadOnCacheMiss = false)
        {
            // PreviewPath 是相对 GamePath 的路径，必须拼 GamePath 再加载，不能直接 Image.Load(map.PreviewPath)（依赖 CWD）。
            if (map?.IsImmediatePreviewImageAvailable() ?? false)
                return Image.Load(SafePath.GetFile(ProgramConstants.GamePath, map.PreviewPath).FullName);

            using var cacheLease = GetCachedPreviewImageFromMap(map, syncLoadOnCacheMiss);

            if (cacheLease != null)
                return cacheLease.Value;
            else
                return null;
        }

        public CacheLease<Image> GetCachedPreviewImageFromMap(Map map, bool syncLoadOnCacheMiss = false)
        {
            if (map?.IsImmediatePreviewImageAvailable() ?? false)
            {
                Image image = map.GetImmediatePreviewImage();
                return CacheLease<Image>.CreateOwned(image, image.Dispose);
            }
            else if (map?.IsNonImmediatePreviewImageAvailable() ?? false)
            {
                if (mapPreviewCacheManager.Request(map, out CacheLease<Image> lease, syncComputeOnCacheMiss: syncLoadOnCacheMiss, addToQueue: true))
                    return lease;
                else
                    return null;
            }
            else
            {
                return null;
            }
        }

        public Map FindMapByHash(string mapHash) => GameModeMaps?.FindMapByHash(mapHash);

        public void Dispose() => mapPreviewCacheManager?.Dispose();
    }
}
