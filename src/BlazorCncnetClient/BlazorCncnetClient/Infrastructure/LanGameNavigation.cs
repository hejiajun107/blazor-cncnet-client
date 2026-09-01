using CnCNetClient.Domain.LAN;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// LAN 大厅 → LAN 游戏房间页之间的导航参数（scoped，随电路存续）。
    /// 由 LanLobby.razor 写入，LanGame.razor 初始化时读取并消费（用后即清）。
    /// </summary>
    public sealed class LanGameNavigation
    {
        /// <summary>待创建房间名（点「创建房间」后跳转）。</summary>
        public string? PendingCreate { get; set; }

        /// <summary>待加入房间条目（点「加入游戏」后跳转）。</summary>
        public HostedLANGame? PendingJoin { get; set; }
    }
}
