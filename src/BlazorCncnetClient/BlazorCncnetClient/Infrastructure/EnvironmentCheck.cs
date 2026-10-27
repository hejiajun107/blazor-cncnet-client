using System;
using System.IO;
using CnCNetClient.Core;
using CnCNetClient.Integration.Resources;
using CnCNetClient.Tools;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 启动环境检测（M2.2）：定位游戏资源目录并尝试初始化 <see cref="ClientConfiguration"/>。
/// 路径定位复用 Integration 的 <see cref="IAssetPathProvider"/>（权威实现在 Core 的
/// <see cref="ProgramConstants.FindGamePath"/>），未找到游戏资源时 UI 仍可正常显示
/// （返回 <see cref="IsReady"/> = false 与原因），不会崩溃。
/// </summary>
public sealed class EnvironmentCheck
{
    private readonly IAssetPathProvider _assetPaths;

    /// <summary>可执行文件所在目录（原 <c>ProgramConstants.StartupPath</c> 逻辑）。</summary>
    public string StartupPath { get; }

    /// <summary>检测到的游戏根目录；未找到时为 null。</summary>
    public string? GamePath { get; private set; }

    /// <summary>是否找到 Resources 目录。</summary>
    public bool ResourcesFound { get; private set; }

    /// <summary>是否找到 ClientDefinitions.ini（客户端运行必需）。</summary>
    public bool ClientDefinitionsFound { get; private set; }

    /// <summary>客户端配置是否初始化成功。</summary>
    public bool ConfigurationReady { get; private set; }

    /// <summary>总体是否就绪（资源 + 配置均已就绪）。</summary>
    public bool IsReady => ResourcesFound && ClientDefinitionsFound && ConfigurationReady;

    /// <summary>供首页展示的检测信息。</summary>
    public string Message { get; private set; } = UiText.T("Env.Detecting", "Detecting…");

    public EnvironmentCheck(IAssetPathProvider assetPaths)
    {
        _assetPaths = assetPaths;
        StartupPath = SafePath.CombineDirectoryPath(
            new FileInfo(Environment.ProcessPath ?? AppContext.BaseDirectory).Directory!.FullName);
    }

    /// <summary>
    /// 执行检测。幂等，可重复调用（如首页"重新检测"）。
    /// </summary>
    public void Detect()
    {
        GamePath = _assetPaths.FindGamePath(StartupPath);
        ResourcesFound = GamePath != null;

        if (!ResourcesFound)
        {
            Message = UiText.T("Env.ResourcesNotFound",
                "Couldn't find the game resources directory. Please make sure the client is running from inside the game directory (the program should sit in a directory tree that contains the Resources directory).");
            return;
        }

        string clientDefs = SafePath.CombineFilePath(GamePath!, _assetPaths.BaseResourcePath, ClientConfiguration.CLIENT_DEFS);
        ClientDefinitionsFound = File.Exists(clientDefs);

        if (!ClientDefinitionsFound)
        {
            Message = string.Format(
                UiText.T("Env.ClientDefinitionsMissing",
                    "Game directory located: {0}, but {1} was not found. This mod may not have the client resources installed yet."),
                GamePath, $"{_assetPaths.BaseResourcePath}\\{ClientConfiguration.CLIENT_DEFS}");
            return;
        }

        // 资源就绪，尝试初始化客户端配置（无 UI 下亦可执行，符合 spec §M1.13）。
        try
        {
            _ = ClientConfiguration.Instance;
            ConfigurationReady = true;
            Message = string.Format(UiText.T("Env.Ready", "Game resources ready: {0}"), GamePath);
        }
        catch (Exception ex)
        {
            ConfigurationReady = false;
            Message = string.Format(UiText.T("Env.ConfigInitFailed", "Failed to initialize the client configuration: {0}"), ex.Message);
        }
    }
}
