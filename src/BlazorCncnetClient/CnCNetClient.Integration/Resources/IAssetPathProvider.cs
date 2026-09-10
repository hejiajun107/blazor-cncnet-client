namespace CnCNetClient.Integration.Resources;

/// <summary>
/// 资源/路径提供抽象（spec §4.2C/§6.5）：统一向 UI 层暴露游戏目录、资源目录等路径，
/// 避免 UI 层各自拼接/定位导致实现漂移。所有定位最终复用
/// <see cref="CnCNetClient.Core.ProgramConstants"/> 的权威实现。
/// </summary>
public interface IAssetPathProvider
{
    /// <summary>可执行文件所在目录（原 <c>ProgramConstants.StartupPath</c> 逻辑）。</summary>
    string StartupPath { get; }

    /// <summary>当前解析到的游戏根目录；未定位时可能为 null。</summary>
    string? GamePath { get; }

    /// <summary>资源目录名（如 "Resources"）。</summary>
    string BaseResourcePath { get; }

    /// <summary>资源目录完整路径（GamePath + BaseResourcePath）。</summary>
    string GetResourcePath();

    /// <summary>
    /// 从启动目录向上查找游戏根目录（Resources 的父目录）。
    /// 与 <c>ProgramConstants.FindGamePath</c> 同一策略；未找到时返回 null。
    /// </summary>
    string? FindGamePath(string startupPath);
}
