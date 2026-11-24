namespace CnCNetClient.Online
{
    /// <summary>
    /// 隧道服务器列表来源扩展口（插件可覆盖，与 <see cref="IIrcServerListProvider"/> 同模式）。
    /// 宿主刷新隧道列表时优先询问本接口；返回空或抛异常则继续走
    /// <c>CnCNetTunnelListURL</c> 下载 + <c>tunnel_cache</c> 缓存的默认逻辑。
    /// </summary>
    public interface ITunnelListProvider
    {
        /// <summary>
        /// 返回隧道列表文本：首行可为表头，其后每行
        /// <c>address;country;countrycode;name;password;clients;maxclients;official;latitude;longitude;version;distance</c>。
        /// 返回 null 或空字符串表示交由宿主默认逻辑处理。
        /// </summary>
        string GetTunnelList();
    }

    /// <summary>默认实现：不提供数据，宿主保持原有的下载 / 缓存行为。</summary>
    public sealed class DefaultTunnelListProvider : ITunnelListProvider
    {
        public string GetTunnelList() => null;
    }
}
