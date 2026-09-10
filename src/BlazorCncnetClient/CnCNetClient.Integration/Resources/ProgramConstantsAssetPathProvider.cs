using CnCNetClient.Core;

namespace CnCNetClient.Integration.Resources;

/// <summary>
/// 基于 <see cref="ProgramConstants"/> 的默认 <see cref="IAssetPathProvider"/> 实现。
/// 路径定位全部转发到 Core 的权威实现，UI 层不再复制查找逻辑。
/// </summary>
public sealed class ProgramConstantsAssetPathProvider : IAssetPathProvider
{
    public string StartupPath => ProgramConstants.StartupPath;

    public string? GamePath => ProgramConstants.GamePath;

    public string BaseResourcePath => ProgramConstants.BASE_RESOURCE_PATH;

    public string GetResourcePath() => ProgramConstants.GetResourcePath();

    public string? FindGamePath(string startupPath) => ProgramConstants.FindGamePath(startupPath);
}
