using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CnCNetClient.Core;
using CnCNetClient.Domain;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Integration;
using CnCNetClient.Integration.Threading;
using CnCNetClient.Tools;
using Microsoft.AspNetCore.Components;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 加载单机存档服务（对齐原版 <c>GameLoadingWindow</c>）：
/// 枚举 <c>Saved Games/*.SAV</c>（解析 GUIName/时间），加载（生成加载存档 spawn.ini + 启动游戏进程）
/// 与删除（确认框后删文件）。存档属于自定义任务时自动附加 CustomMissionID 与任务配置节。
/// </summary>
public sealed class LoadGameService : IDisposable
{
    private const string SavedGamesDirectory = "Saved Games";

    private readonly NavigationManager _nav;
    private readonly CampaignService _campaign;
    private readonly MessageBoxService _messageBox;
    private readonly IDispatcher _dispatcher;

    /// <summary>一次加载是否正在启动/运行（过滤游戏进程退出时的无关回调）。</summary>
    private bool _launchInProgress;

    public LoadGameService(
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

    /// <summary>枚举单机存档（按修改时间倒序；无法解析的 .SAV 跳过）。</summary>
    public IReadOnlyList<SavedGame> ListSaves()
    {
        string dir = SafePath.CombineDirectoryPath(ProgramConstants.GamePath, SavedGamesDirectory);
        if (!Directory.Exists(dir))
            return Array.Empty<SavedGame>();

        var saves = new List<SavedGame>();
        foreach (string file in Directory.EnumerateFiles(dir, "*.SAV", SearchOption.TopDirectoryOnly))
        {
            var saveGame = new SavedGame(Path.GetFileName(file));
            if (saveGame.ParseInfo())
                saves.Add(saveGame);
        }

        return saves.OrderByDescending(s => s.LastModified).ToList();
    }

    /// <summary>加载选中的存档：生成 spawn.ini / spawnmap.ini 并启动游戏进程，随后返回主菜单。</summary>
    public async Task LaunchAsync(SavedGame saveGame)
    {
        try
        {
            Mission? mission = _campaign.AllMissions
                .FirstOrDefault(m => m.CustomMissionID == saveGame.CustomMissionID);

            SpawnIniGenerator.WriteSaveGameSpawnIni(saveGame, mission);

            _launchInProgress = true;
            _nav.NavigateTo("/menu");
            GameProcessLogic.StartGameProcess();
        }
        catch (Exception ex)
        {
            _launchInProgress = false;
            await _messageBox.ShowAsync("生成游戏配置出错", ex.Message,
                MessageBoxButtons.Ok, MessageBoxKind.Error);
        }
    }

    /// <summary>确认后永久删除存档文件（原版 BtnDelete_LeftClick 语义）。</summary>
    public async Task DeleteAsync(SavedGame saveGame)
    {
        bool confirmed = await _messageBox.ShowAsync(
            "删除确认",
            $"以下存档将被永久删除：\n\n文件名：{saveGame.FileName}\n存档名：{saveGame.GUIName}\n日期时间：{saveGame.LastModified:yyyy-MM-dd HH:mm}\n\n确定要继续吗？",
            MessageBoxButtons.YesNo, MessageBoxKind.Confirm);

        if (!confirmed)
            return;

        SafePath.DeleteFileIfExists(ProgramConstants.GamePath, SavedGamesDirectory, saveGame.FileName);
    }

    private void OnGameProcessExited()
    {
        // Process.Exited 在后台线程触发，调度回 UI 线程
        _dispatcher.Post(() =>
        {
            if (!_launchInProgress)
                return;
            _launchInProgress = false;

            // 原版 GameProcessExited：清理复制到游戏目录的自定义任务补充文件（.shp/.pal 等）
            SpawnIniGenerator.DeleteMissionSupplementalFiles();
        });
    }

    public void Dispose() => GameProcessLogic.GameProcessExited -= OnGameProcessExited;
}
