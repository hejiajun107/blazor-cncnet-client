namespace CnCNetClient.Plugin.Abstractions;

/// <summary>
/// 启动在线更新闸门。插件可在 <see cref="ICncNetPlugin.RegisterServices"/> 中
/// 以 <c>AddSingleton&lt;IStartupUpdateGate&gt;()</c> 注册实现；
/// 宿主在「启动更新检查」前询问，未注册时保持原有行为。
/// </summary>
/// <remarks>
/// 仅在启动检查路径询问一次；不影响设置页的手动检查/强制更新。
/// 返回 false 表示本次启动跳过宿主在线更新。
/// </remarks>
public interface IStartupUpdateGate
{
    /// <summary>
    /// 是否执行宿主在线更新。true = 继续；false = 本次启动跳过。
    /// 抛异常或超时由宿主按「跳过」处理（对齐旧客户端判定失败即不更新的语义）。
    /// </summary>
    Task<bool> ShouldRunOnlineUpdateAsync(CancellationToken cancellationToken = default);
}
