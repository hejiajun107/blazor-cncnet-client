#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Core.PlatformShim;
using CnCNetClient.Domain;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Tools;

using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>回放播放选项（对应原版 ReplaysPanel 的复选框/下拉框）。</summary>
public sealed class ReplayPlaybackOptions
{
    /// <summary>以哪位玩家视角播放（spawn.ini 玩家槽位）；观战时忽略。</summary>
    public int ViewPlayerIndex { get; set; }

    public bool Spectator { get; set; }

    /// <summary>播放速度（FPS），取值见 <see cref="YRReplayGame.PlaybackSpeedLadder"/>。</summary>
    public int PlaybackSpeed { get; set; } = 60;

    public bool ShroudEnabled { get; set; }

    public bool FollowCamera { get; set; } = true;

    public bool ShowSelections { get; set; } = true;

    public bool ShowChatAndBeacons { get; set; } = true;

    public int KeyframeInterval { get; set; } = 750;
}

/// <summary>
/// 回放列表与播放（对齐原版 <c>ReplaysPanel</c>）：
/// 从回放中提取 spawn.ini/spawnmap.ini，校验游戏文件哈希与前缀补充文件，
/// 写入播放用 spawn.ini（ReplayFile/ReplayViewPlayer/Replay*）后启动游戏进程。
/// </summary>
public sealed class ReplayLaunchService : IDisposable
{
    private readonly NavigationManager _nav;
    private readonly CampaignService _campaign;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次回放播放是否正在启动/运行（过滤游戏进程退出时的无关回调）。</summary>
    private bool _launchInProgress;

    public ReplayLaunchService(
        NavigationManager nav,
        CampaignService campaign,
        MessageBoxService messageBox,
        IDispatcher dispatcher)
    {
        _nav = nav;
        _campaign = campaign;
        _messageBox = messageBox;
        _dispatcher = dispatcher;
        GameProcessLogic.GameProcessExited += OnGameProcessExited;
    }

    /// <summary>是否启用回放（ClientDefinitions.ini [Settings] ReplaySupport）。</summary>
    public bool IsSupported => ReplayManager.IsSupported;

    /// <summary>回放目录名（相对游戏目录）。</summary>
    public string DirectoryName => ReplayManager.DirectoryName;

    /// <summary>回放扩展名（不含点）。</summary>
    public string FileExtension => ReplayManager.FileExtension;

    /// <summary>列出可解析的回放（按录制时间倒序）。</summary>
    public IReadOnlyList<YRReplayGame> ListReplays() => ReplayManager.List();

    /// <summary>从本地文件导入一个回放，返回导入后的文件名；失败返回 null。</summary>
    public string? ImportReplay(string path) => ReplayManager.Import(path);

    /// <summary>删除回放文件。</summary>
    public async Task DeleteAsync(YRReplayGame replay)
    {
        bool confirmed = await _messageBox.ShowAsync(
            UiText.T("Replays.DeleteTitle", "Confirm deletion"),
            string.Format(UiText.T("Replays.DeleteBody",
                    "The following replay will be permanently deleted:\n\nFile: {0}\nMap: {1}\nDate: {2}\n\nContinue?"),
                replay.FileName, replay.GUIName, replay.RecordedAt.ToString("yyyy-MM-dd HH:mm")),
            MessageBoxButtons.YesNo, MessageBoxKind.Confirm);

        if (confirmed)
            ReplayManager.Delete(replay);
    }

    /// <summary>打开回放目录（不存在则创建）。</summary>
    public void OpenDirectory() => ReplayManager.OpenDirectory();

    /// <summary>播放回放；返回是否成功启动。</summary>
    public async Task<bool> LaunchAsync(YRReplayGame replay, ReplayPlaybackOptions options)
    {
        if (!replay.IsPlayable)
            return false;

        if (!replay.TryReadSpawnFiles(out string spawnIniContent, out byte[] spawnMapContent))
        {
            await _messageBox.ShowAsync(
                UiText.T("Replays.UnreadableTitle", "Replay unreadable"),
                UiText.T("Replays.UnreadableText",
                    "This replay does not contain the game setup it was recorded with, so it cannot be played back."),
                MessageBoxButtons.Ok, MessageBoxKind.Error);
            return false;
        }

        IniFile spawnIni = ReadSpawnIni(spawnIniContent);

        List<string> mismatches;
        try
        {
            // 校验时先还原录制时用到的任务补充文件，校验后立即清理，避免取消播放时残留。
            PrepareMissionFiles(spawnIni);
            mismatches = ReplayFileHashes.FindMismatches(spawnIni);
        }
        finally
        {
            SpawnIniGenerator.DeleteMissionSupplementalFiles();
        }

        if (mismatches.Count > 0)
        {
            foreach (string mismatch in mismatches)
                Logger.Log("Replay file mismatch: " + mismatch);

            string recordedVersion = replay.GamePackageVersion;
            string currentVersion = ReplayManager.GamePackageVersion;

            bool sameVersion = string.Equals(recordedVersion, currentVersion, StringComparison.OrdinalIgnoreCase);

            string text = sameVersion
                ? UiText.T("Replays.FilesDifferText",
                    "Some of your game files are different from the ones this replay was recorded with. " +
                    "If you continue, it may not play back correctly.\n\nPlay it anyway?")
                : string.Format(UiText.T("Replays.VersionMismatchText",
                        "This replay was recorded on a different version of the game. " +
                        "If you continue, it may not play back correctly.\n\nRecorded with: {0}\nCurrent version: {1}\n\nPlay it anyway?"),
                    recordedVersion, currentVersion);

            bool playAnyway = await _messageBox.ShowAsync(
                sameVersion
                    ? UiText.T("Replays.FilesDifferTitle", "Different game files")
                    : UiText.T("Replays.VersionMismatchTitle", "Different game version"),
                text,
                MessageBoxButtons.YesNo, MessageBoxKind.Confirm);

            if (!playAnyway)
                return false;
        }

        return StartPlayback(replay, spawnIni, spawnMapContent, options);
    }

    private static IniFile ReadSpawnIni(string spawnIniContent)
    {
        using MemoryStream stream = new MemoryStream(EncodingExt.UTF8NoBOM.GetBytes(spawnIniContent));
        return new IniFile(stream, EncodingExt.UTF8NoBOM, applyBaseIni: false);
    }

    private void PrepareMissionFiles(IniFile spawnIni)
    {
        SpawnIniGenerator.DeleteMissionSupplementalFiles();

        if (!spawnIni.GetBooleanValue("Settings", "IsSinglePlayer", false))
            return;

        int customMissionID = spawnIni.GetIntValue("Settings", "CustomMissionID", 0);
        Mission? mission = _campaign.AllMissions.FirstOrDefault(m => m.CustomMissionID == customMissionID);

        if (mission != null)
            SpawnIniGenerator.CopyMissionSupplementalFiles(mission);
    }

    private bool StartPlayback(YRReplayGame replay, IniFile spawnIni, byte[] spawnMapContent,
        ReplayPlaybackOptions options)
    {
        try
        {
            spawnIni.SetStringValue("Settings", "ReplayFile", ReplayManager.GetReplayFileRelativePath(replay.FileName));

            // ReplayViewPlayer 是 spawn.ini 的玩家槽位。
            spawnIni.SetIntValue("Settings", "ReplayViewPlayer", options.Spectator ? 0 : options.ViewPlayerIndex);

            // 有选中玩家时使用该玩家阵营的加载画面（录制者除外）。
            if (!options.Spectator)
            {
                ReplayPlayer? perspective = replay.Players
                    .FirstOrDefault(p => p.SpawnIniIndex == options.ViewPlayerIndex);

                if (perspective != null && !perspective.IsRecorder && perspective.SideIndex >= 0)
                {
                    spawnIni.SetStringValue("Settings", "CustomLoadScreen",
                        LoadingScreenController.GetLoadScreenName(perspective.SideIndex.ToString()));
                }
            }

            // 播放速度只影响播放节奏，不改变录制时的模拟速度。
            spawnIni.SetIntValue("Settings", "ReplayPlaybackSpeed", options.PlaybackSpeed);

            spawnIni.SetBooleanValue("Settings", "ReplayShroudEnabled", options.ShroudEnabled);
            // spawner 的开关语义是“自由镜头”，故取复选框的否定。
            spawnIni.SetBooleanValue("Settings", "ReplayFreeCamera", !options.FollowCamera);
            spawnIni.SetBooleanValue("Settings", "ReplayShowSelections", options.ShowSelections);
            spawnIni.SetBooleanValue("Settings", "ReplaySpectator", options.Spectator);
            spawnIni.SetBooleanValue("Settings", "ReplayShowChatAndBeacons", options.ShowChatAndBeacons);

            // 关键帧用于加速回退，播放结束后由游戏删除。
            spawnIni.SetIntValue("Settings", "ReplayKeyframeInterval", options.KeyframeInterval);
            spawnIni.SetIntValue("Settings", "ReplayKeyframeStorageLimitMB",
                UserINISettings.Instance.ReplayKeyframeStorageLimitMB.Value);

            spawnIni.SetBooleanValue("Settings", "EnableReplayRecording", false);

            spawnIni.RemoveSection(ReplayFileHashes.SECTION);

            FileInfo spawnerSettingsFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNER_SETTINGS);
            if (spawnerSettingsFile.Exists)
                spawnerSettingsFile.Delete();

            spawnIni.FileName = spawnerSettingsFile.FullName;
            spawnIni.Encoding = EncodingExt.UTF8NoBOM;
            spawnIni.WriteIniFile();

            FileInfo spawnMapIniFile = SafePath.GetFile(ProgramConstants.GamePath, ProgramConstants.SPAWNMAP_INI);
            if (spawnMapIniFile.Exists)
                spawnMapIniFile.Delete();

            // 战役回放的地图可能直接来自游戏压缩包，不需要外部 spawnmap.ini。
            if (spawnMapContent.Length > 0)
                File.WriteAllBytes(spawnMapIniFile.FullName, spawnMapContent);

            Logger.Log("Extracted spawn files from replay successfully");

            _launchInProgress = true;
            _nav.NavigateTo("/menu");

            PrepareMissionFiles(spawnIni);
            GameProcessLogic.StartGameProcess();

            return true;
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            Logger.Log("ReplayLaunchService: could not start replay playback: " + ex.ToString());
            return false;
        }
    }

    private void OnGameProcessExited()
    {
        // Process.Exited 在后台线程触发，调度回 UI 线程。
        _dispatcher.Post(() =>
        {
            if (!_launchInProgress)
                return;
            _launchInProgress = false;

            SpawnIniGenerator.DeleteMissionSupplementalFiles();

            // 播放结束/中断后按数量与容量上限清理旧回放。
            ReplayManager.Prune();
        });
    }

    public void Dispose() => GameProcessLogic.GameProcessExited -= OnGameProcessExited;
}
