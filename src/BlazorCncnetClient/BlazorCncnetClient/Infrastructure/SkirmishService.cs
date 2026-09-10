using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 遭遇战（Skirmish）服务（M4.5 spec §5.3）：本地对局的状态管理与配置持久化。
    /// 移植原版 <c>SkirmishLobby</c> 的数据与持久化逻辑（去 XNA）：
    /// 单个人类玩家 + 若干 AI 玩家，地图 / 游戏模式选择，AI 数量与难度配置，
    /// 开始游戏前校验合法性，并通过 <see cref="GameStartRequested"/> 交由
    /// <see cref="SkirmishLaunchService"/> 生成含 AI 的 spawn.ini 并启动游戏。
    /// </summary>
    public sealed class SkirmishService
    {
        /// <summary>遭遇战设置持久化路径（相对游戏目录，与原版一致）。</summary>
        public const string SETTINGS_PATH = "Client/SkirmishSettings.ini";

        /// <summary>最大玩家总数（含 AI，与原版 MAX_PLAYER_COUNT 一致）。</summary>
        public const int MaxPlayersConst = 8;

        /// <summary>可添加的 AI 玩家上限（人类玩家固定占 1 位）。</summary>
        public const int MaxAIPlayersConst = MaxPlayersConst - 1;

        /// <summary>状态变化（玩家 / AI / 地图 / 难度），驱动 UI 刷新。</summary>
        public event Action? Changed;

        /// <summary>开始游戏请求（SkirmishLaunchService 接此生成含 AI 的 spawn.ini 并启动游戏进程）。</summary>
        public event Action? GameStartRequested;

        private readonly MapLoader _mapLoader;
        private readonly GameCollection _gameCollection;
        private readonly Random _random;

        private readonly List<PlayerInfo> _aiPlayers = new();

        public CnCNetGame? LocalGame { get; }

        /// <summary>人类玩家（遭遇战固定唯一，名称随 ProgramConstants.PLAYERNAME）。</summary>
        public PlayerInfo? HumanPlayer { get; private set; }

        public GameModeMap? SelectedMap { get; private set; }
        public int RandomSeed { get; private set; }
        public int UniqueGameId { get; private set; }

        public IReadOnlyList<PlayerInfo> AIPlayers => _aiPlayers;

        /// <summary>是否已初始化（进入过遭遇战页，人类玩家已建立）。</summary>
        public bool IsActive => HumanPlayer is not null;

        public bool IsCoop => SelectedMap?.IsCoop ?? false;

        /// <summary>当前地图是否强制随机出生点（G3：出生点下拉禁用，全员随机出生）。</summary>
        public bool ForceRandomStarts => SelectedMap?.ForceRandomStartLocations ?? false;

        /// <summary>当前地图允许的最大玩家数（未选图时用通用上限）。</summary>
        public int MaxPlayers => SelectedMap?.MaxPlayers ?? MaxPlayersConst;

        /// <summary>本局阵营数量（spawn.ini 强制选项用，取 Sides 逗号分隔数量）。</summary>
        public int SideCount =>
            Math.Max(1, ClientConfiguration.Instance.Sides.Split(',', StringSplitOptions.RemoveEmptyEntries).Length);

        /// <summary>观战（Spectator）阵营索引 = 真实阵营数量（本端无 Random/随机选择器，Spectator 位于最后一个索引，同原版 GetSpectatorSideIndex 语义）。</summary>
        public int SpectatorSideIndex => SideCount;

        /// <summary>人类玩家是否选择了观战（Spectator）阵营（G5）。</summary>
        public bool IsHumanSpectator => HumanPlayer is not null && HumanPlayer.SideId == SpectatorSideIndex;

        public SkirmishService(MapLoader mapLoader, GameCollection gameCollection, Random random)
        {
            _mapLoader = mapLoader;
            _gameCollection = gameCollection;
            _random = random;

            LocalGame = _gameCollection.GameList.Find(g =>
                string.Equals(g.InternalName, ClientConfiguration.Instance.LocalGame, StringComparison.InvariantCultureIgnoreCase));
        }

        // ── 游戏选项（G7：复选/下拉，[GameOptions] 持久化 + spawn.ini/地图代码应用） ──

        /// <summary>
        /// 遭遇战游戏选项（复选 + 下拉）。定义见 <see cref="GameSessionOption.CreateDefaultGameOptions"/>
        /// （与原版 GameLobbyBase.ini 一致，与在线房间共用同一集合）。
        /// 状态随 SkirmishSettings.ini [GameOptions] 节持久化，并在生成 spawn.ini 时写入 / 应用地图代码。
        /// </summary>
        public IReadOnlyList<GameSessionOption> GameOptions { get; } = GameSessionOption.CreateDefaultGameOptions();

        /// <summary>设置复选框游戏选项状态（仅接受 GameOptions 中的选项，避免外部对象）。</summary>
        public void SetCheckBoxOption(GameSessionCheckBoxOption option, bool isChecked)
        {
            if (!GameOptions.Contains(option))
                return;
            option.Checked = isChecked;
            NotifyChanged();
        }

        /// <summary>设置下拉游戏选项选中索引（仅接受 GameOptions 中的选项，越界回退到边界值）。</summary>
        public void SetDropDownOption(GameSessionDropDownOption option, int selectedIndex)
        {
            if (!GameOptions.Contains(option))
                return;
            option.SelectedIndex = option.Items.Count > 0 ? Math.Clamp(selectedIndex, 0, option.Items.Count - 1) : 0;
            NotifyChanged();
        }

        /// <summary>选项是否被游戏模式/地图强制（原版 LoadSettings：强制选项忽略已保存值，避免 UI 显示不一致）。</summary>
        private bool IsGameOptionForced(GameSessionOption option)
        {
            if (SelectedMap is null)
                return false;

            if (option is GameSessionCheckBoxOption)
            {
                return SelectedMap.GameMode.ForcedCheckBoxValues.Any(p => p.Key == option.Name)
                    || SelectedMap.Map.ForcedCheckBoxValues.Any(p => p.Key == option.Name);
            }

            if (option is GameSessionDropDownOption)
            {
                return SelectedMap.GameMode.ForcedDropDownValues.Any(p => p.Key == option.Name)
                    || SelectedMap.Map.ForcedDropDownValues.Any(p => p.Key == option.Name);
            }

            return false;
        }

        /// <summary>进入 /skirmish 页面时初始化：加载持久化设置（或默认配置）并生成随机种子。</summary>
        public void Initialize()
        {
            if (IsActive)
                return;

            RandomSeed = _random.Next();
            LoadSettings();

            if (HumanPlayer is not null)
                HumanPlayer.Name = ProgramConstants.PLAYERNAME;

            NotifyChanged();
        }

        /// <summary>重新生成随机种子。原版在每次游戏进程退出后调用（<c>GameProcessExited</c>），
        /// 保证连续多局不共用同一 <c>Seed</c>（地图事件/资源分布随机性）。</summary>
        public void RegenerateSeed() => RandomSeed = _random.Next();

        /// <summary>选择地图 / 游戏模式：切换后重置人类出生点到地图允许的首个位置。</summary>
        public void ChangeMap(GameModeMap gameModeMap)
        {
            if (SelectedMap == gameModeMap)
                return;

            SelectedMap = gameModeMap;

            // 换图后出生点重置：ForceRandomStartLocations 图 → 全员随机（0）；否则人类回到允许的首个位置
            // （与 AI 可能冲突的旧值由开始前校验兜底）
            if (ForceRandomStarts)
            {
                if (HumanPlayer is not null)
                    HumanPlayer.StartingLocation = 0;
                foreach (var ai in _aiPlayers)
                    ai.StartingLocation = 0;
            }
            else if (HumanPlayer is not null)
            {
                var allowed = GetSortedAllowedStartingLocations();
                HumanPlayer.StartingLocation = allowed.Count > 0 ? allowed[0] : 1;
            }

            // 原版 ChangeMap（GameLobbyBase）：非 coop 且 ForceNoTeams 地图 → 全员 TeamId=0（无队伍）
            if (!IsCoop && SelectedMap.ForceNoTeams)
            {
                if (HumanPlayer is not null)
                    HumanPlayer.TeamId = 0;
                foreach (var ai in _aiPlayers)
                    ai.TeamId = 0;
            }

            // 自动结盟依赖当前地图的 TeamStartMapping 预设：换图后重置（无预设则关闭）
            ResetTeamStartMappingsForCurrentMap();

            // 换图后自动结盟仍启用（原版 ForceNoTeams 语义）：全员队伍清零，保证映射生效
            if (ExtraOptions.IsUseTeamStartMappings)
                ResetAllPlayerTeams();

            NotifyChanged();
        }

        // ── 队伍配置（G6） ──

        /// <summary>队伍是否可配置：coop 或 ForceNoTeams 地图禁用（同原版 AllowDropDown 条件）。</summary>
        public bool TeamsEnabled => !IsCoop && !(SelectedMap?.ForceNoTeams ?? false);

        /// <summary>人类玩家队伍编号（0=无，1..4=A..D）。</summary>
        public int HumanTeamId => HumanPlayer?.TeamId ?? 0;

        /// <summary>队伍编号转显示名（0→"-"，1..4→A..D）。</summary>
        public static string GetTeamName(int teamId) =>
            teamId > 0 && teamId <= ProgramConstants.TEAMS.Count
                ? ProgramConstants.TEAMS[teamId - 1]
                : "-";

        /// <summary>设置人类玩家队伍（0..4，越界回退 0；队伍禁用时忽略）。</summary>
        public void SetHumanTeam(int teamId)
        {
            if (HumanPlayer is null || !TeamsEnabled)
                return;
            HumanPlayer.TeamId = Math.Clamp(teamId, 0, ProgramConstants.TEAMS.Count);
            NotifyChanged();
        }

        /// <summary>设置指定 AI 玩家的队伍（0..4，越界回退 0；队伍禁用时忽略）。</summary>
        public void SetAITeam(PlayerInfo ai, int teamId)
        {
            if (!_aiPlayers.Contains(ai) || !TeamsEnabled)
                return;
            ai.TeamId = Math.Clamp(teamId, 0, ProgramConstants.TEAMS.Count);
            NotifyChanged();
        }

        /// <summary>全员（人类 + AI）队伍清零（原版 ForceNoTeams 语义，自动结盟启用时调用）。</summary>
        private void ResetAllPlayerTeams()
        {
            if (HumanPlayer is not null)
                HumanPlayer.TeamId = 0;
            foreach (var ai in _aiPlayers)
                ai.TeamId = 0;
        }

        // ── 自动结盟（Auto Allying，G6） ──

        /// <summary>自动结盟配置（会话级，不持久化，同原版 PlayerExtraOptions 语义）。</summary>
        public PlayerExtraOptions ExtraOptions { get; } = new();

        /// <summary>是否启用自动结盟（按出生点分配队伍）。</summary>
        public bool UseTeamStartMappings => ExtraOptions.IsUseTeamStartMappings;

        /// <summary>当前地图的 TeamStartMapping 预设（原版 ddTeamStartMappingPreset 数据源）。</summary>
        public IReadOnlyList<TeamStartMappingPreset> TeamStartMappingPresets =>
            SelectedMap?.Map.TeamStartMappingPresets ?? new List<TeamStartMappingPreset>();

        /// <summary>自动结盟是否可用：有预设且非 coop / ForceNoTeams（原版 UseTeamStartMappingsAllowChecking）。</summary>
        public bool TeamStartMappingsAllowed =>
            SelectedMap is not null
            && !SelectedMap.IsCoop
            && !SelectedMap.ForceNoTeams
            && TeamStartMappingPresets.Count > 0;

        /// <summary>当前地图允许编辑的自动结盟出生点集合（同原版 AllowedStartingLocations）。</summary>
        public HashSet<int> AllowedTeamStartMappingLocations
        {
            get
            {
                if (SelectedMap is null)
                    return new HashSet<int>(Enumerable.Range(1, MaxPlayersConst));
                try
                {
                    return SelectedMap.AllowedStartingLocations
                        .Where(l => l > 0)
                        .ToHashSet();
                }
                catch
                {
                    return new HashSet<int>(Enumerable.Range(1, MaxPlayersConst));
                }
            }
        }

        /// <summary>启用/禁用自动结盟；启用时用地图首个预设（或默认映射）初始化映射列表。
        /// 同原版：启用自动结盟即强制 ForceNoTeams，全员 TeamId 清零，保证 AllianceHolder 走映射分支。</summary>
        public void SetUseTeamStartMappings(bool use)
        {
            if (use && !TeamStartMappingsAllowed)
                return;

            ExtraOptions.IsUseTeamStartMappings = use;
            if (use)
            {
                ResetTeamStartMappingsForCurrentMap(forceEnable: true);
                ResetAllPlayerTeams();
            }
            else
            {
                ExtraOptions.TeamStartMappings.Clear();
            }

            NotifyChanged();
        }

        /// <summary>应用指定索引的预设映射到自动结盟列表。</summary>
        public void ApplyTeamStartMappingPreset(int presetIndex)
        {
            var presets = TeamStartMappingPresets;
            if (presetIndex < 0 || presetIndex >= presets.Count)
                return;
            ExtraOptions.TeamStartMappings = new List<TeamStartMapping>(
                presets[presetIndex].TeamStartMappings.Select(m => new TeamStartMapping
                {
                    Start = m.Start,
                    Team = m.Team
                }));
            NotifyChanged();
        }

        /// <summary>更新单个出生点的自动结盟队伍分配。</summary>
        public void SetTeamStartMapping(int start, string team)
        {
            if (!UseTeamStartMappings)
                return;
            var existing = ExtraOptions.TeamStartMappings.FirstOrDefault(m => m.Start == start);
            if (existing is not null)
                existing.Team = team;
            else
                ExtraOptions.TeamStartMappings.Add(new TeamStartMapping { Start = start, Team = team });
            NotifyChanged();
        }

        /// <summary>生效中的出生点→队伍映射（未启用自动结盟时返回空列表，spawn.ini 不写联盟兜底）。</summary>
        public List<TeamStartMapping> GetActiveTeamStartMappings() =>
            ExtraOptions.IsUseTeamStartMappings
                ? new List<TeamStartMapping>(ExtraOptions.TeamStartMappings)
                : new List<TeamStartMapping>();

        /// <summary>按当前地图重置自动结盟映射：有预设取首个，无预设取地图默认映射，仍无则清空。</summary>
        private void ResetTeamStartMappingsForCurrentMap(bool forceEnable = false)
        {
            var presets = TeamStartMappingPresets;
            if (presets.Count > 0)
            {
                ExtraOptions.TeamStartMappings = new List<TeamStartMapping>(
                    presets[0].TeamStartMappings.Select(m => new TeamStartMapping { Start = m.Start, Team = m.Team }));
            }
            else if (SelectedMap?.Map.TeamStartMappings is { } defaults && defaults.Count > 0)
            {
                ExtraOptions.TeamStartMappings = new List<TeamStartMapping>(
                    defaults.Select(m => new TeamStartMapping { Start = m.Start, Team = m.Team }));
            }
            else
            {
                ExtraOptions.TeamStartMappings = new List<TeamStartMapping>();
                if (!forceEnable)
                    ExtraOptions.IsUseTeamStartMappings = false;
            }
        }

        // ── 人类玩家配置 ──

        public void SetHumanOption(int side, int color, int start)
        {
            if (HumanPlayer is null)
                return;
            HumanPlayer.SideId = side;
            HumanPlayer.ColorId = color;
            HumanPlayer.StartingLocation = start;
            NotifyChanged();
        }

        // ── AI 玩家配置 ──

        /// <summary>添加一个 AI 玩家：阵营/颜色/出生点取第一个空闲值，默认难度 Easy（AILevel=0）。</summary>
        public void AddAI()
        {
            if (_aiPlayers.Count >= MaxAIPlayersConst)
                throw new InvalidOperationException($"最多只能添加 {MaxAIPlayersConst} 个 AI 玩家");

            int index = _aiPlayers.Count;
            var ai = new PlayerInfo(GetAIName(index), 0, 0, 0, 0)
            {
                IsAI = true,
                AILevel = 0,
                Index = index
            };
            ai.StartingLocation = ForceRandomStarts ? 0 : GetFirstFreeStartingLocation();
            _aiPlayers.Add(ai);
            NotifyChanged();
        }

        /// <summary>移除指定 AI 玩家（需是列表中的 AI）。</summary>
        public void RemoveAI(PlayerInfo ai)
        {
            if (!_aiPlayers.Remove(ai))
                return;
            for (int i = 0; i < _aiPlayers.Count; i++)
                _aiPlayers[i].Index = i;
            NotifyChanged();
        }

        /// <summary>修改指定 AI 玩家的阵营 / 颜色 / 出生点。</summary>
        public void SetAIOption(PlayerInfo ai, int side, int color, int start)
        {
            if (!_aiPlayers.Contains(ai))
                return;
            ai.SideId = side;
            ai.ColorId = color;
            ai.StartingLocation = start;
            NotifyChanged();
        }

        /// <summary>设置指定 AI 玩家的难度（0=Easy / 1=Medium / 2=Hard，逻辑递增刻度）。</summary>
        public void SetAILevel(PlayerInfo ai, int level)
        {
            if (!_aiPlayers.Contains(ai))
                return;
            ai.AILevel = Math.Clamp(level, 0, 2);
            NotifyChanged();
        }

        // ── 校验与开始 ──

        /// <summary>
        /// 校验对局合法性，返回错误描述（null 表示合法）。
        /// 移植原版 SkirmishLobby.CheckGameValidity：模式单机限制、最小/最大玩家数、
        /// 出生点冲突（仅 EnforceMaxPlayers 时校验，同原版）。
        /// </summary>
        public string? CheckGameValidity()
        {
            if (SelectedMap is null)
                return "请先选择地图与游戏模式";

            var gmm = SelectedMap;
            // G5：观战者不计入玩家数（同原版 CheckGameValidity：Players 中 Spectator 阵营的玩家不计入 totalPlayerCount）
            int totalPlayerCount = (IsHumanSpectator ? 0 : 1) + _aiPlayers.Count;

            if (gmm.MultiplayerOnly)
                return $"{gmm} 只能在 CnCNet 和局域网中游玩。";

            if (gmm.EnforceMinPlayers && totalPlayerCount < gmm.MinPlayers)
                return $"{gmm} 需要至少 {gmm.MinPlayers} 名玩家才能游玩。";

            if (gmm.EnforceMaxPlayers)
            {
                if (totalPlayerCount > gmm.MaxPlayers)
                    return $"{gmm} 最多支持 {gmm.MaxPlayers} 名玩家。";

                IEnumerable<PlayerInfo> all = new[] { HumanPlayer! }.Concat(_aiPlayers);
                foreach (var p in all)
                {
                    if (p.StartingLocation == 0)
                        continue;
                    if (all.Count(x => x.StartingLocation == p.StartingLocation) > 1)
                        return "多名玩家不能共享同一出生点。";
                }
            }

            // G5：合作任务不可观战（同原版 SkirmishLobby.CheckGameValidity）
            if (gmm.IsCoop && IsHumanSpectator)
                return "合作任务不能观战，请选择实际阵营。";

            // 自动结盟映射校验（原版 GetTeamMappingsError：出生点映射不重复 + 至少两个队伍）
            if (UseTeamStartMappings)
            {
                string? teamMappingsError = ExtraOptions.GetTeamMappingsError();
                if (teamMappingsError is not null)
                    return teamMappingsError;
            }

            return null;
        }

        /// <summary>开始游戏：校验通过后保存设置、生成唯一 GameID 并触发启动事件。</summary>
        public void StartGame()
        {
            string? error = CheckGameValidity();
            if (error is not null)
                throw new InvalidOperationException(error);

            SaveSettings();

            // 原版按战绩统计 + 日月时分生成 GameID；本端无战绩库，取随机正数保证唯一性
            UniqueGameId = _random.Next(1, int.MaxValue);
            GameStartRequested?.Invoke();
        }

        /// <summary>将人类 + 全部 AI 玩家合并为玩家列表（供加载页展示 / spawn.ini 生成）。</summary>
        public List<PlayerInfo> GetPlayerInfos()
        {
            var result = new List<PlayerInfo>(1 + _aiPlayers.Count);
            if (HumanPlayer is not null)
                result.Add(HumanPlayer);
            result.AddRange(_aiPlayers);
            return result;
        }

        /// <summary>地图预览上的出生点（百分比坐标 + 当前占用者），与在线/局域网房间一致。</summary>
        public List<SpawnPointState> GetSpawnPointStates()
        {
            var result = new List<SpawnPointState>();
            var map = SelectedMap?.Map;
            if (map is null)
                return result;

            List<Point> coords;
            try
            {
                coords = map.GetStartingLocationPreviewCoords(new Point(1000, 1000));
            }
            catch
            {
                return result;
            }

            int shown = Math.Min(coords.Count, MaxPlayers);
            var all = GetPlayerInfos();
            for (int i = 0; i < shown; i++)
            {
                var occupant = all.FirstOrDefault(p => p.StartingLocation == i + 1);
                result.Add(new SpawnPointState(
                    i + 1,
                    Math.Clamp(coords[i].X / 1000.0, 0.0, 1.0),
                    Math.Clamp(coords[i].Y / 1000.0, 0.0, 1.0),
                    occupant?.Name,
                    occupant?.ColorId ?? -1,
                    occupant is not null && !occupant.IsAI && occupant.Name == ProgramConstants.PLAYERNAME));
            }
            return result;
        }

        // ── 持久化（Client/SkirmishSettings.ini，格式与原版兼容） ──

        /// <summary>保存遭遇战配置：人类玩家、AI 玩家、地图与游戏模式。</summary>
        public void SaveSettings()
        {
            try
            {
                FileInfo settingsFileInfo = SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH);

                // 先删除旧文件，避免残留多余 AI 玩家（同原版）
                settingsFileInfo.Delete();

                var ini = new IniFile(settingsFileInfo.FullName);

                if (HumanPlayer is not null)
                    ini.SetStringValue("Player", "Info", HumanPlayer.ToString());

                for (int i = 0; i < _aiPlayers.Count; i++)
                    ini.SetStringValue("AIPlayers", i.ToString(), _aiPlayers[i].ToString());

                ini.SetStringValue("Settings", "Map", SelectedMap?.Map.SHA1 ?? string.Empty);
                ini.SetStringValue("Settings", "GameModeMapFilter", SelectedMap?.GameMode.Name ?? string.Empty);

                // G7：游戏选项持久化（同原版 SaveSettings：SaveSkirmishGameOptions 开启时写入 [GameOptions]）
                if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
                {
                    foreach (GameSessionOption option in GameOptions)
                        ini.SetStringValue("GameOptions", option.Name, option.GetPersistedValue());
                }

                ini.WriteIniFile();
            }
            catch (Exception ex)
            {
                Logger.Log("Saving skirmish settings failed! Reason: " + ex);
            }
        }

        /// <summary>加载遭遇战配置；文件不存在或损坏时回退默认配置（人类 + 1 个 Easy AI）。</summary>
        private void LoadSettings()
        {
            if (!SafePath.GetFile(ProgramConstants.GamePath, SETTINGS_PATH).Exists)
            {
                InitDefaultSettings();
                return;
            }

            var ini = new IniFile(SafePath.CombineFilePath(ProgramConstants.GamePath, SETTINGS_PATH));

            // 游戏模式：新格式 GameModeMapFilter / 旧格式 GameMode 回退
            string modeName = ini.GetStringValue("Settings", "GameModeMapFilter", string.Empty);
            if (string.IsNullOrEmpty(modeName))
                modeName = ini.GetStringValue("Settings", "GameMode", string.Empty);

            string mapSHA1 = ini.GetStringValue("Settings", "Map", string.Empty);
            var gmm = FindGameModeMap(modeName, mapSHA1);
            SelectedMap = gmm;

            var player = PlayerInfo.FromString(ini.GetStringValue("Player", "Info", string.Empty));
            if (player is null)
            {
                Logger.Log("Failed to load human player information from skirmish settings!");
                InitDefaultSettings();
                return;
            }

            CheckLoadedPlayerVariableBounds(player);
            player.Name = ProgramConstants.PLAYERNAME;
            HumanPlayer = player;

            List<string> keys = ini.GetSectionKeys("AIPlayers") ?? new List<string>();

            bool aiAllowed = gmm is not null && !gmm.HumanPlayersOnly;
            foreach (string key in keys)
            {
                if (!aiAllowed)
                    break;
                if (_aiPlayers.Count >= MaxAIPlayersConst)
                    break;

                var aiPlayer = PlayerInfo.FromString(ini.GetStringValue("AIPlayers", key, string.Empty));
                if (aiPlayer is null)
                {
                    Logger.Log("Failed to load AI player information from skirmish settings!");
                    InitDefaultSettings();
                    return;
                }

                CheckLoadedPlayerVariableBounds(aiPlayer, true);
                aiPlayer.IsAI = true;
                _aiPlayers.Add(aiPlayer);
            }

            // G7：加载游戏选项（同原版 LoadSettings：SaveSkirmishGameOptions 开启时读取 [GameOptions]，
            // 被游戏模式/地图强制的选项忽略已保存值）
            if (ClientConfiguration.Instance.SaveSkirmishGameOptions)
            {
                foreach (GameSessionOption option in GameOptions)
                {
                    if (IsGameOptionForced(option))
                        continue;
                    option.SetPersistedValue(ini.GetStringValue("GameOptions", option.Name, string.Empty));
                }
            }
        }

        /// <summary>默认配置：人类玩家（阵营 0/颜色 0/无出生点）+ 1 个 Easy AI（同原版 InitDefaultSettings）。</summary>
        private void InitDefaultSettings()
        {
            HumanPlayer = new PlayerInfo(ProgramConstants.PLAYERNAME, 0, 0, 0, 0);

            _aiPlayers.Clear();
            var aiPlayer = new PlayerInfo(GetAIName(0), 0, 0, 0, 0)
            {
                IsAI = true,
                AILevel = 0,
                Index = 0
            };
            _aiPlayers.Add(aiPlayer);

            SelectedMap = _mapLoader.GameModeMaps.FirstOrDefault();
        }

        // ── 工具方法 ──

        /// <summary>校验加载的玩家字段不越界（阵营/颜色/出生点），越界回退默认值（同原版）。
        /// G5：人类允许阵营索引 == SideCount（观战 Spectator）；AI 减去观战索引（sideCount--，不可观战）。</summary>
        private void CheckLoadedPlayerVariableBounds(PlayerInfo pInfo, bool isAIPlayer = false)
        {
            int sideCount = SideCount;
            if (isAIPlayer)
                sideCount--;

            if (pInfo.SideId < 0 || pInfo.SideId > sideCount)
                pInfo.SideId = 0;

            if (pInfo.ColorId < 0 || pInfo.ColorId > MultiplayerColor.LoadColors().Count)
                pInfo.ColorId = 0;

            if (pInfo.StartingLocation < 0 || pInfo.StartingLocation > MaxPlayersConst
                || (SelectedMap?.ForceRandomStartLocations ?? false))
            {
                pInfo.StartingLocation = 0;
            }
        }

        /// <summary>按地图/模式允许的出生点（升序）生成当前可用的完整出生点列表。</summary>
        private List<int> GetSortedAllowedStartingLocations()
        {
            if (SelectedMap is null)
                return Enumerable.Range(1, MaxPlayersConst).ToList();
            try
            {
                return SelectedMap.AllowedStartingLocations
                    .Where(l => l > 0)
                    .OrderBy(l => l)
                    .ToList();
            }
            catch
            {
                return Enumerable.Range(1, MaxPlayersConst).ToList();
            }
        }

        /// <summary>返回当前未被任何玩家占用的最小出生点（用于新增 AI 的默认出生点）。</summary>
        private int GetFirstFreeStartingLocation()
        {
            var occupied = GetPlayerInfos().Select(p => p.StartingLocation).ToHashSet();
            foreach (int loc in GetSortedAllowedStartingLocations())
            {
                if (!occupied.Contains(loc))
                    return loc;
            }
            return 0;
        }

        /// <summary>按游戏模式名 + 地图 SHA1 在 MapLoader 全量集合中查找 GameModeMap。</summary>
        private GameModeMap? FindGameModeMap(string modeName, string mapSHA1)
        {
            if (string.IsNullOrEmpty(modeName) && string.IsNullOrEmpty(mapSHA1))
                return _mapLoader.GameModeMaps.FirstOrDefault();

            return _mapLoader.GameModeMaps.FirstOrDefault(g =>
                (string.IsNullOrEmpty(modeName)
                    || string.Equals(g.GameMode.Name, modeName, StringComparison.OrdinalIgnoreCase))
                && (string.IsNullOrEmpty(mapSHA1)
                    || string.Equals(g.Map.SHA1, mapSHA1, StringComparison.OrdinalIgnoreCase)))
                ?? _mapLoader.GameModeMaps.FirstOrDefault(g =>
                    string.IsNullOrEmpty(modeName)
                    || string.Equals(g.GameMode.Name, modeName, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>清除当前地图选择（删除自定义地图且其为当前选中图时调用，同原版删除后重选语义）。</summary>
        public void ClearSelectedMap()
        {
            SelectedMap = null;
            NotifyChanged();
        }

        /// <summary>游戏选项预设载入后通知 UI 刷新（同原版 LoadGameOptionPreset 后的变更通知）。</summary>
        public void NotifyGameOptionsChanged() => NotifyChanged();

        /// <summary>按 AI 序号取默认名称（AI 难度命名，与原版一致）。</summary>
        private static string GetAIName(int index) =>
            index >= 0 && index < ProgramConstants.AI_PLAYER_NAMES.Count
                ? ProgramConstants.AI_PLAYER_NAMES[index]
                : $"AI {index + 1}";

        private void NotifyChanged() => Changed?.Invoke();
    }
}
