#nullable enable
using CnCNetClient.Core;
using System;
using System.Threading;

namespace CnCNetClient.Domain.Multiplayer.CnCNet
{
    /// <summary>
    /// A class for updating of the CnCNet game/player count.
    /// </summary>
    public static class CnCNetPlayerCountTask
    {
        /// <summary>当前游戏标识（如 cncnet5_ts）的在线人数（原版字段，-1 表示不可用）。</summary>
        public static int PlayerCount { get; private set; } = -1;

        /// <summary>CnCNet 全部游戏在线总人数（status API 的 total 字段，-1 表示不可用）。</summary>
        public static int TotalPlayerCount { get; private set; } = -1;

        private static int REFRESH_INTERVAL = 60000; // 1 minute

        /// <summary>在线人数刷新事件（首次拉取与每轮刷新均在后台线程触发）。</summary>
        public static event EventHandler<PlayerCountEventArgs>? CnCNetGameCountUpdated;

        private static string? cncnetLiveStatusIdentifier;

        public static void InitializeService(CancellationTokenSource cts)
        {
            cncnetLiveStatusIdentifier = ClientConfiguration.Instance.CnCNetLiveStatusIdentifier;

            // 首次拉取同样放入后台线程，避免同步 HTTP 阻塞 UI（原版在闪屏期同步拉取会卡顿）。
            ThreadPool.QueueUserWorkItem(new WaitCallback(RunService), cts);
        }

        private static void RunService(object? tokenObj)
        {
            var cts = (CancellationTokenSource)tokenObj!;
            var waitHandle = cts.Token.WaitHandle;

            while (!cts.Token.IsCancellationRequested)
            {
                (int game, int total) = GetCnCNetPlayerCount(timeoutMilliseconds: 5000);
                PlayerCount = game;
                TotalPlayerCount = total;
                CnCNetGameCountUpdated?.Invoke(null, new PlayerCountEventArgs(game, total));

                try
                {
                    if (waitHandle.WaitOne(REFRESH_INTERVAL))
                        return; // Cancellation signaled
                }
                catch (ObjectDisposedException)
                {
                    // UI 层在离开主菜单时 Dispose 了 CTS，等待句柄随之释放，WaitOne 抛
                    // ObjectDisposedException —— 视同取消，正常结束本轮线程，避免线程池未捕获异常。
                    return;
                }
            }
        }

        /// <summary>拉取 CnCNet 状态 API，返回（当前游戏标识人数, 全部游戏总人数）。</summary>
        private static (int GameCount, int TotalCount) GetCnCNetPlayerCount(int timeoutMilliseconds = 5000)
        {
            try
            {
                // Don't fetch the player count if it is explicitly disabled.
                // For example, the official CnCNet server might be unavailable/unstable in a country with Internet censorship,
                // which causes lags in the splash screen. In the worst case, say if packets are dropped, it waits until timeouts.
                if (string.IsNullOrWhiteSpace(ClientConfiguration.Instance.CnCNetPlayerCountURL))
                    return (-1, -1);

                string info = new TimedHttpClient(timeoutMilliseconds)
                    .GetString(ClientConfiguration.Instance.CnCNetPlayerCountURL);

                info = info.Replace("{", string.Empty);
                info = info.Replace("}", string.Empty);
                info = info.Replace("\"", string.Empty);
                string[] values = info.Split(new char[] { ',' });

                int gameCount = -1;
                int totalCount = -1;

                foreach (string value in values)
                {
                    if (cncnetLiveStatusIdentifier is not null && value.Contains(cncnetLiveStatusIdentifier))
                        gameCount = Convert.ToInt32(value.Substring(cncnetLiveStatusIdentifier.Length + 1));
                    else if (value.StartsWith("total", StringComparison.OrdinalIgnoreCase))
                        totalCount = Convert.ToInt32(value.Substring("total".Length + 1));
                }

                return (gameCount, totalCount);
            }
            catch
            {
                return (-1, -1);
            }
        }
    }

    /// <summary>在线人数事件参数（当前游戏人数 + 全部游戏总人数）。</summary>
    public sealed class PlayerCountEventArgs : EventArgs
    {
        public PlayerCountEventArgs(int playerCount, int totalPlayerCount)
        {
            PlayerCount = playerCount;
            TotalPlayerCount = totalPlayerCount;
        }

        /// <summary>当前游戏标识的在线人数（原版字段，保留兼容）。</summary>
        public int PlayerCount { get; set; }

        /// <summary>CnCNet 全部游戏在线总人数。</summary>
        public int TotalPlayerCount { get; set; }
    }
}
