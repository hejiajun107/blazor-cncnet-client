using CnCNetClient.Domain.Multiplayer.CnCNet;

namespace BlazorCncnetClient.Infrastructure
{
    /// <summary>
    /// 大厅 → 游戏房间页之间的导航参数（scoped，随电路存续）。
    /// 由 Lobby.razor 写入，GameLobby.razor 初始化时读取并消费（用后即清）。
    /// </summary>
    public sealed class GameRoomNavigation
    {
        /// <summary>待创建房间参数（点「创建房间」后跳转）。</summary>
        public CreateRoomRequest? PendingCreate { get; set; }

        /// <summary>待加入房间参数（点「加入游戏」后跳转）。</summary>
        public GameJoinRequest? PendingJoin { get; set; }
    }

    /// <summary>创建房间请求参数（对应原版 Gcw_GameCreated 弹窗输入）。
    /// <paramref name="Tunnel"/> 为 null 表示由宿主自动择优（原版 TunnelListBox 默认行为）。</summary>
    public sealed record CreateRoomRequest(
        string RoomName,
        int MaxPlayers,
        string Password,
        CnCNetTunnel? Tunnel = null);

    /// <summary>加入房间请求参数（来自大厅 GAME 广播条目 + 用户输入的密码）。</summary>
    public sealed record GameJoinRequest(
        string RoomName,
        string HostName,
        string ChannelName,
        int MaxPlayers,
        bool Passworded,
        bool Locked,
        string? Password);
}
