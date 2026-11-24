using System.Collections.Generic;

namespace CnCNetClient.Online;

/// <summary>
/// IRC 服务器列表选择器（插件扩展口）。
/// 宿主在建立连接前把配置的服务器条目（<c>Host|Name|ports</c>）交给实现方挑选；
/// 默认实现原样返回，不改变客户端原有行为；插件注册同接口即可覆盖（按“国内/国际节点”等策略筛选）。
/// </summary>
public interface IIrcServerListProvider
{
    /// <summary>从配置的服务器条目中挑选本次连接使用的条目（返回空集合时按原列表处理）。</summary>
    IReadOnlyList<string> SelectServers(IReadOnlyList<string> configuredServers);
}

/// <summary>默认实现：原样返回配置的服务器列表。</summary>
public sealed class DefaultIrcServerListProvider : IIrcServerListProvider
{
    public IReadOnlyList<string> SelectServers(IReadOnlyList<string> configuredServers) => configuredServers;
}
