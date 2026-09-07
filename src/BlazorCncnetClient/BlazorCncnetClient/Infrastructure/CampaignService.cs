using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CnCNetClient.Core;
using CnCNetClient.Domain;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Tools;
using CnCNetClient.Updater;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 战役（单机任务）服务（M4.6 spec §5.3）：任务列表加载（Battle.ini / BattleFS.ini + 自定义任务）、
    /// 任务选择、难度配置与持久化（UserINISettings.Difficulty）、启动前作弊检查，
    /// 并通过 <see cref="GameStartRequested"/> 交由 <see cref="CampaignLaunchService"/>
    /// 生成任务 spawn.ini 并启动游戏进程。
    /// 移植原版 <c>CampaignSelector</c> 的数据与启动校验逻辑（去 XNA）。
    /// </summary>
    public sealed class CampaignService
    {
        /// <summary>难度档位（0=Easy / 1=Medium / 2=Hard，原版 trbDifficultySelector.Value）。</summary>
        public const int MinDifficulty = 0;
        public const int MaxDifficulty = 2;

        /// <summary>作弊检查文件列表（原版 CampaignSelector.filesToCheck：核心 INI + 三级难度地图代码）。</summary>
        private static readonly string[] FilesToCheck =
        {
            "INI/AI.ini",
            "INI/AIE.ini",
            "INI/Art.ini",
            "INI/ArtE.ini",
            "INI/Enhance.ini",
            "INI/Rules.ini",
            "INI/Map Code/Difficulty Hard.ini",
            "INI/Map Code/Difficulty Medium.ini",
            "INI/Map Code/Difficulty Easy.ini"
        };

        /// <summary>状态变化（任务列表 / 选择 / 难度），驱动 UI 刷新。</summary>
        public event Action? Changed;

        /// <summary>任务启动请求（CampaignLaunchService 接此生成任务 spawn.ini 并启动游戏进程）。</summary>
        public event Action? GameStartRequested;

        /// <summary>作弊检测命中（参数为待启动任务；UI 弹窗确认后调用 <see cref="StartGame"/> ignoreCheater=true 放行）。</summary>
        public event Action<Mission>? CheaterDetected;

        private readonly List<Mission> _allMissions = new();
        private readonly List<Mission> _visibleMissions = new();
        private readonly Dictionary<int, Mission> _uniqueIdToMissions = new();
        private bool _initialized;

        /// <summary>全部任务（官方 + 自定义；自定义任务供后续 CampaignTagSelector / 标签筛选使用）。</summary>
        public IReadOnlyList<Mission> AllMissions => _allMissions;

        /// <summary>选择器展示的任务（默认仅官方任务，同原版 LoadMissionsWithFilter(null, disableCustomMissions: true)）。</summary>
        public IReadOnlyList<Mission> Missions => _visibleMissions;

        /// <summary>当前选中的任务（null = 未选择）。</summary>
        public Mission? SelectedMission { get; private set; }

        /// <summary>当前难度（0=Easy / 1=Medium / 2=Hard，随 UserINISettings.Difficulty 持久化）。</summary>
        public int Difficulty { get; private set; } = 1;

        /// <summary>是否已初始化（进入过 /campaign 页并完成任务列表加载）。</summary>
        public bool IsActive => _initialized;

        // ── 初始化 ──

        /// <summary>进入 /campaign 页面时初始化：加载任务列表、恢复难度设置。</summary>
        public void Initialize()
        {
            if (_initialized)
                return;
            _initialized = true;

            // 自定义任务补充文件定义（.shp/.pal 等）：生成/清理任务补充文件的前置条件
            SpawnIniGenerator.InitializeMissionSupplementFiles();

            Difficulty = Math.Clamp(UserINISettings.Instance.Difficulty.Value, MinDifficulty, MaxDifficulty);

            ReadMissionList();

            NotifyChanged();
        }

        /// <summary>加载任务列表（原版 ReadMissionList：Battle.ini 为空时回退 BattleFS.ini，再加载自定义任务）。</summary>
        private void ReadMissionList()
        {
            _allMissions.Clear();
            _uniqueIdToMissions.Clear();

            ParseBattleIni("INI/Battle.ini");
            if (_allMissions.Count == 0)
                ParseBattleIni("INI/" + ClientConfiguration.Instance.BattleFSFileName);

            LoadCustomMissions();
            RefreshVisibleMissions();
        }

        /// <summary>解析 Battle*.ini：按 [Battles] 键顺序生成任务（原版 ParseBattleIni）。</summary>
        private void ParseBattleIni(string path)
        {
            Logger.Log("Attempting to parse " + path + " to populate mission list.");

            FileInfo battleIniFileInfo = SafePath.GetFile(ProgramConstants.GamePath, path);
            if (!battleIniFileInfo.Exists)
            {
                Logger.Log("File " + path + " not found. Ignoring.");
                return;
            }

            var battleIni = new IniFile(battleIniFileInfo.FullName);

            List<string>? battleKeys = battleIni.GetSectionKeys("Battles");
            if (battleKeys is null)
                return; // 文件存在但无 [Battles] 节

            for (int i = 0; i < battleKeys.Count; i++)
            {
                string battleEntry = battleKeys[i];
                string battleSection = battleIni.GetStringValue("Battles", battleEntry, "NOT FOUND");

                if (!battleIni.SectionExists(battleSection))
                    continue;

                var mission = new Mission(battleIni.GetSection(battleSection), battleEntry);
                AddMission(mission);
            }

            Logger.Log("Finished parsing " + path + ".");
        }

        /// <summary>加载自定义任务：扫描自定义任务目录下带 [ClientMissionConfig] 的地图文件（原版 LoadCustomMissions）。</summary>
        private void LoadCustomMissions()
        {
            string customMissionsDirectory = SafePath.CombineDirectoryPath(ProgramConstants.GamePath, ClientConfiguration.Instance.CustomMissionPath);
            if (!Directory.Exists(customMissionsDirectory))
                return;

            string[] mapFiles = Directory.GetFiles(customMissionsDirectory, "*." + ClientConfiguration.Instance.MapFileExtension);
            if (mapFiles.Length == 0)
                return;

            foreach (string mapFilePath in mapFiles)
            {
                var mapFile = new IniFile(mapFilePath);

                IniSection? clientMissionDataSection = mapFile.GetSection("ClientMissionConfig");
                if (clientMissionDataSection is null)
                    continue;

                IniSection? gameMissionDataSection = mapFile.GetSection("GameMissionConfig");

                string filename = new FileInfo(mapFilePath).Name;
                string scenario = SafePath.CombineFilePath(ClientConfiguration.Instance.CustomMissionPath, filename);
                Mission mission = Mission.NewCustomMission(clientMissionDataSection, filename, scenario, gameMissionDataSection);
                AddMission(mission);
            }
        }

        /// <summary>登记任务：始终加入 AllMissions；CustomMissionID 重复的任务禁用（原版 AddMission 语义，避免存档 ID 冲突）。</summary>
        private void AddMission(Mission mission)
        {
            _allMissions.Add(mission);

            if (_uniqueIdToMissions.ContainsKey(mission.CustomMissionID))
            {
                Logger.Log($"CampaignSelector: duplicated mission. CodeName: {mission.CodeName}. ID: {mission.CustomMissionID}. Description: {mission.UntranslatedGUIName}.");
                if (!string.IsNullOrEmpty(mission.Scenario))
                    mission.Enabled = false;
            }
            else
            {
                _uniqueIdToMissions.Add(mission.CustomMissionID, mission);
            }
        }

        /// <summary>重建选择器展示列表（默认仅官方任务），选中项失效时清空选择。</summary>
        private void RefreshVisibleMissions()
        {
            _visibleMissions.Clear();
            _visibleMissions.AddRange(_allMissions.Where(m => !m.IsCustomMission));

            if (SelectedMission is not null && !_visibleMissions.Contains(SelectedMission))
                SelectedMission = null;

            NotifyChanged();
        }

        // ── 选择与难度 ──

        /// <summary>选择任务（必须是展示列表中的任务）。</summary>
        public void SelectMission(Mission mission)
        {
            if (!_visibleMissions.Contains(mission))
                return;
            SelectedMission = mission;
            NotifyChanged();
        }

        /// <summary>设置难度（0=Easy / 1=Medium / 2=Hard，越界回退到边界值）。</summary>
        public void SetDifficulty(int difficulty)
        {
            Difficulty = Math.Clamp(difficulty, MinDifficulty, MaxDifficulty);
            NotifyChanged();
        }

        /// <summary>难度显示名（Easy/Medium/Hard，与 spawn.ini 难度映射一致）。</summary>
        public static string GetDifficultyName(int difficulty) =>
            difficulty >= MinDifficulty && difficulty <= MaxDifficulty
                ? SpawnIniGenerator.MissionDifficultyNames[difficulty]
                : SpawnIniGenerator.MissionDifficultyNames[1];

        // ── 启动与校验 ──

        /// <summary>
        /// 开始任务：校验选中任务合法性 → 保存设置 → 作弊检查。
        /// 作弊命中时触发 <see cref="CheaterDetected"/>（UI 弹窗确认后以 ignoreCheater=true 重入放行）；
        /// 否则触发 <see cref="GameStartRequested"/>。
        /// </summary>
        public void StartGame(bool ignoreCheater = false)
        {
            Mission? mission = SelectedMission;
            if (mission is null)
                throw new InvalidOperationException("请先选择任务");
            if (string.IsNullOrEmpty(mission.Scenario))
                throw new InvalidOperationException("该任务未配置地图文件，无法启动。");
            if (!mission.Enabled)
                throw new InvalidOperationException("该任务当前不可用。");

            SaveSettings();

            if (IsCheater(mission) && !ignoreCheater)
            {
                CheaterDetected?.Invoke(mission);
                return;
            }

            GameStartRequested?.Invoke();
        }

        /// <summary>
        /// 作弊检查（原版 BtnLaunch_LeftClick）：非 ModMode 下，任务地图文件或核心 INI 被改动
        /// （Updater 判定非原始文件）即视为作弊。Updater 未初始化时视为未修改（同原版）。
        /// </summary>
        public bool IsCheater(Mission mission) =>
            !ClientConfiguration.Instance.ModMode
            && (!Updater.IsFileNonexistantOrOriginal(mission.Scenario) || AreFilesModified());

        private bool AreFilesModified()
        {
            foreach (string filePath in FilesToCheck)
            {
                if (!Updater.IsFileNonexistantOrOriginal(filePath))
                    return true;
            }
            return false;
        }

        /// <summary>保存战役设置：难度随 UserINISettings 持久化（同原版 SaveSettings → SaveUserSettings）。</summary>
        public void SaveSettings()
        {
            try
            {
                UserINISettings.Instance.Difficulty.Value = Difficulty;
                UserINISettings.Instance.SaveSettings();
            }
            catch (Exception ex)
            {
                Logger.Log("Saving campaign settings failed! Reason: " + ex);
            }
        }

        /// <summary>难度取反（原版 GetComputerDifficulty：电脑难度 = |难度 - 2|，Hard→Easy），供 UI 展示。 </summary>
        public static int GetComputerDifficulty(int difficulty) => Math.Abs(difficulty - 2);

        private void NotifyChanged() => Changed?.Invoke();
    }
}
