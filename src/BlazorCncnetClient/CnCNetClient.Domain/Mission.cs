using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

using CnCNetClient.Core;
using CnCNetClient.Core.Enums;
using CnCNetClient.Core.Extensions;

using CnCNetClient.Tools;

namespace CnCNetClient.Domain
{
    /// <summary>
    /// A Tiberian Sun mission listed in Battle(E).ini.
    /// </summary>
    public class Mission
    {
        public Mission(IniSection missionSection, string missionCodeName)
        {
            if (missionSection == null)
                throw new ArgumentNullException(nameof(missionSection));

            CD = missionSection.GetIntValue(nameof(CD), 0);
            Side = missionSection.GetIntValue(nameof(Side), 0);
            Scenario = missionSection.GetStringValue(nameof(Scenario), string.Empty);
            // 原版以 ini 节名作为翻译键（[Battles] 映射键通常与节名相同，但不保证一致）
            string l10nKey = missionSection.SectionName ?? missionCodeName;

            UntranslatedGUIName = missionSection.GetStringValue("Description", "Undefined mission");
            GUIName = UntranslatedGUIName
                .L10N($"INI:Missions:{l10nKey}:Description");

            IconPath = missionSection.GetStringValue("SideName", string.Empty);
            GUIDescription = missionSection.GetStringValue("LongDescription", string.Empty)
                .FromIniString()
                .L10N($"INI:Missions:{l10nKey}:LongDescription");
            FinalMovie = missionSection.GetStringValue(nameof(FinalMovie), "none");
            RequiredAddon = missionSection.GetBooleanValue(nameof(RequiredAddon),
               ClientConfiguration.Instance.ClientGameType == ClientType.YR ||
               ClientConfiguration.Instance.ClientGameType == ClientType.Ares ?
                true :  // In case of YR this toggles Ra2Mode instead which should not be default
                false
            );
            Enabled = missionSection.GetBooleanValue(nameof(Enabled), true);
            BuildOffAlly = missionSection.GetBooleanValue(nameof(BuildOffAlly), false);
            PlayerAlwaysOnNormalDifficulty = missionSection.GetBooleanValue(nameof(PlayerAlwaysOnNormalDifficulty), false);
            Tags = missionSection.GetStringValue(nameof(Tags), string.Empty).SplitWithCleanup();

            // 可选展示标签（如扩展包标识）：ini 未配置时不显示；文本可经 Translation.ini 翻译。
            UntranslatedTag = missionSection.GetStringValue("Tag", string.Empty);
            Tag = UntranslatedTag.L10N($"INI:Missions:{l10nKey}:Tag");
            UntranslatedTagAbbreviation = missionSection.GetStringValue("TagAbbreviation", string.Empty);
            TagAbbreviation = UntranslatedTagAbbreviation.L10N($"INI:Missions:{l10nKey}:TagAbbreviation");

            CodeName = missionCodeName;
            CustomMissionID = ComputeCustomMissionID(missionCodeName);
            PreviewImage = missionSection.GetStringValue("PreviewImage", string.Empty);
        }

        public static Mission NewCustomMission(IniSection clientMissionConfigSection, string missionCodeName, string scenario, IniSection? gameMissionConfigSection)
        {
            var mission = new Mission(clientMissionConfigSection, missionCodeName)
            {
                IsCustomMission = true,
                Scenario = scenario,
                GameMissionConfigSection = gameMissionConfigSection,
                Tags = ["CUSTOM"],
            };
            return mission;
        }

        private static int ComputeCustomMissionID(string missionCodeName)
        {
#pragma warning disable CA5350 // Do Not Use Weak Cryptographic Algorithms
#pragma warning disable CA1850 // Prefer static 'HashData' method over 'ComputeHash'
            using var sha1 = SHA1.Create();
            byte[] digest = sha1.ComputeHash(Encoding.UTF8.GetBytes(missionCodeName));
            return BinaryPrimitives.ReadInt32LittleEndian(digest);
#pragma warning restore CA1850 // Prefer static 'HashData' method over 'ComputeHash'
#pragma warning restore CA5350 // Do Not Use Weak Cryptographic Algorithms
        }

        public string CodeName { get; private set; }
        public int CampaignID { get; } = -1;
        public int CustomMissionID { get; private set; }

        public int CD { get; private set; }
        public int Side { get; private set; }

        /// <summary>
        /// Refers to the map file. Must be a relative path to the game folder. If it contains invalid path characters like '>', the client treats it as a special scenario that does not have a map file, passing the string directly to the spawner.
        /// </summary>
        public string Scenario { get; private set; }
        public string GUIName { get; private set; }
        public string UntranslatedGUIName { get; private set; }
        public string IconPath { get; private set; }
        public string GUIDescription { get; private set; }
        public string FinalMovie { get; private set; }
        public bool RequiredAddon { get; private set; }
        public bool Enabled { get; set; }
        public bool BuildOffAlly { get; private set; }
        public bool PlayerAlwaysOnNormalDifficulty { get; private set; }
        public IReadOnlyCollection<string> Tags { get; private set; }

        /// <summary>可选展示标签（ini <c>Tag</c>，如扩展包名），未配置为空串。</summary>
        public string Tag { get; private set; }

        /// <summary>可选展示标签原文（用于翻译键回填）。</summary>
        public string UntranslatedTag { get; private set; }

        /// <summary>可选标签缩写（ini <c>TagAbbreviation</c>），用于左侧任务列表脚标，未配置为空串。</summary>
        public string TagAbbreviation { get; private set; }

        /// <summary>可选标签缩写原文（用于翻译键回填）。</summary>
        public string UntranslatedTagAbbreviation { get; private set; }

        /// <summary>
        /// This property is not set through the ini file.
        /// For a user custom mission, "scenario" will be assumed as the filename of a map file, with the suffix ".map" (case-insensitive).
        /// The map file is assumed to be placed at ClientConfiguration.CustomMissionPath.
        /// When launching a user custom mission, all supplemental files, i.e., files with the same filename (excepts for the suffix), will be temporarily copied into game folder.
        /// </summary>
        public bool IsCustomMission { get; private set; }

        public IniSection? GameMissionConfigSection { get; set; }

        public string PreviewImage { get; private set; }

        public bool TryGetScenarioFilePath(out string scenarioFilePath)
        {
            scenarioFilePath = null;
            if (string.IsNullOrEmpty(Scenario))
                return false;

            foreach (string segment in Scenario.Split('/', '\\'))
            {
                if (string.IsNullOrEmpty(segment) || segment != segment.ToWin32FileName())
                    return false;
            }

            scenarioFilePath = SafePath.CombineFilePath(ProgramConstants.GamePath, Scenario);
            return true;
        }
    }
}