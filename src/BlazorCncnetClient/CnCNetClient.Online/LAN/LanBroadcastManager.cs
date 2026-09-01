#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;

using CnCNetClient.Tools;

using NetworkInterface = System.Net.NetworkInformation.NetworkInterface;

namespace CnCNetClient.Online.LAN;

/// <summary>
/// 事件参数：UDP 广播收到一条消息。
/// </summary>
public sealed class LanBroadcastMessageReceivedEventArgs(string data, IPEndPoint endPoint) : EventArgs
{
    public string Data { get; } = data;
    public IPEndPoint EndPoint { get; } = endPoint;
}

/// <summary>
/// LAN 大厅广播管理器（M4.4 D3.1）。
/// 移植自原版 <c>DTAClient.DXGUI.Multiplayer.LANLobbyBroadcastManager</c>：
/// 封装 UDP socket、广播网卡发现、消息发送与监听。
/// 向所有可用网卡的 IPv4 广播地址广播，避免 255.255.255.255 只走低 metric 网卡
/// （VPN / 多路由器无公网场景）。
/// 线程安全：发送/监听/网卡刷新分别加锁；收到消息在监听线程上触发 <see cref="MessageReceived"/>，
/// 订阅方自行调度回 UI 线程。
/// </summary>
public sealed class LanBroadcastManager : IDisposable
{
    private readonly object socketLock = new();
    private readonly ConcurrentDictionary<string, PlayerNetworkInterface> broadcastInterfaces = new();
    private readonly Encoding encoding;
    private readonly int lobbyPort;

    private Socket? socket;
    private Thread? listener;
    private Thread? interfaceRefresher;
    private volatile bool stopRefresher;
    private int disposed;

    /// <summary>收到网络消息（监听线程触发）。</summary>
    public event EventHandler<LanBroadcastMessageReceivedEventArgs>? MessageReceived;

    /// <summary>网络接口信息：本机 IP + 广播端点。</summary>
    private sealed record PlayerNetworkInterface(IPAddress LocalIP, IPEndPoint Broadcast);

    public LanBroadcastManager(int lobbyPort, Encoding encoding)
    {
        this.lobbyPort = lobbyPort;
        this.encoding = encoding ?? throw new ArgumentNullException(nameof(encoding));
    }

    /// <summary>socket 是否已初始化并绑定。</summary>
    public bool IsInitialized
    {
        get
        {
            lock (socketLock)
                return socket != null && socket.IsBound;
        }
    }

    /// <summary>初始化：建 socket、绑定 lobby 端口、发现初始广播网卡、启动监听与网卡刷新线程。</summary>
    public void Initialize()
    {
        lock (socketLock)
        {
            if (socket != null)
            {
                try
                {
                    socket.Close();
                }
                catch (ObjectDisposedException)
                {
                    // 已释放
                }
                socket = null;
            }

            broadcastInterfaces.Clear();

            Logger.Log("Creating LAN socket.");

            try
            {
                socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                {
                    EnableBroadcast = true
                };
                // 允许同机多开同时绑定 UDP 1232（验收 D3.1 主路径）：多实例各自收到广播副本。
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                socket.Bind(new IPEndPoint(IPAddress.Any, lobbyPort));

                var initialInterfaces = DiscoverBroadcastInterfaces(lobbyPort);
                foreach (var (key, netIf) in initialInterfaces)
                    broadcastInterfaces[key] = netIf;
            }
            catch (SocketException ex)
            {
                Logger.Log("Creating LAN socket failed! Message: " + ex);
                throw;
            }

            stopRefresher = false;

            Logger.Log("Starting LAN broadcast message listener.");
            listener = new Thread(Listen) { IsBackground = true };
            listener.Start();

            Logger.Log("Starting network interface refresh thread.");
            interfaceRefresher = new Thread(RefreshInterfacesPeriodically) { IsBackground = true };
            interfaceRefresher.Start();
        }
    }

    /// <summary>扫描 up 网卡，计算 IPv4 广播地址并生成广播端点。</summary>
    private static Dictionary<string, PlayerNetworkInterface> DiscoverBroadcastInterfaces(int port)
    {
        Logger.Log("Discovering broadcast interfaces.");

        var discovered = new Dictionary<string, PlayerNetworkInterface>();
        foreach (NetworkInterface iface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (iface.OperationalStatus != OperationalStatus.Up)
                continue;

            IPInterfaceProperties prop = iface.GetIPProperties();
            UnicastIPAddressInformation? info = prop.UnicastAddresses.FirstOrDefault(i =>
                i.Address.AddressFamily == AddressFamily.InterNetwork);

            if (info == null || info.IPv4Mask == null)
                continue;

            IPAddress local = info.Address;
            byte[] ipBytes = local.GetAddressBytes();
            byte[] maskBytes = info.IPv4Mask.GetAddressBytes();
            byte[] broadcastBytes = new byte[ipBytes.Length];
            for (int i = 0; i < ipBytes.Length; i++)
                broadcastBytes[i] = (byte)(ipBytes[i] | ~maskBytes[i]);

            discovered[local.ToString()] = new PlayerNetworkInterface(
                local, new IPEndPoint(new IPAddress(broadcastBytes), port));
        }

        if (discovered.Count == 0)
        {
            Logger.Log("Warning: No broadcast interfaces found! LAN lobby broadcasting will not function. " +
                "Please ensure that your network adapters are enabled and have valid IPv4 addresses.");
        }

        return discovered;
    }

    /// <summary>向所有广播网卡发送一条 UTF-8 消息；任一成功即返回 true。</summary>
    public bool SendMessage(string message)
    {
        lock (socketLock)
        {
            if (socket == null || !socket.IsBound)
                return false;

            byte[] buffer = encoding.GetBytes(message);

            if (broadcastInterfaces.IsEmpty)
                Logger.Log("Warning: No broadcast interfaces available in SendMessage!");

            bool success = false;
            foreach (var networkInterface in broadcastInterfaces.Values)
            {
                try
                {
                    _ = socket.SendTo(buffer, networkInterface.Broadcast);
                    success = true;
                }
                catch (SocketException)
                {
                    // 忽略单个网卡失败
                }
            }

            return success;
        }
    }

    /// <summary>后台监听线程。</summary>
    private void Listen()
    {
        try
        {
            while (true)
            {
                Socket? currentSocket;
                lock (socketLock)
                    currentSocket = socket;

                if (currentSocket == null)
                    break;

                EndPoint endPoint = new IPEndPoint(IPAddress.Any, lobbyPort);
                byte[] buffer = new byte[4096];
                int receivedBytes = currentSocket.ReceiveFrom(buffer, ref endPoint);

                string data = encoding.GetString(buffer, 0, receivedBytes);
                if (string.IsNullOrEmpty(data))
                    continue;

                MessageReceived?.Invoke(this, new LanBroadcastMessageReceivedEventArgs(data, (IPEndPoint)endPoint));
            }
        }
        catch (Exception ex)
        {
            if (ex is SocketException { SocketErrorCode: SocketError.Interrupted })
            {
                // 关闭 socket 的预期结束方式
            }
            else
            {
                Logger.Log("LAN socket listener: exception: " + ex);
            }
        }
    }

    private const int INTERFACE_REFRESH_INTERVAL_MS = 5000;
    private const int STOP_CHECK_INTERVAL_MS = 100;

    /// <summary>周期刷新广播网卡（5s），保持广播列表随网络变化更新。</summary>
    private void RefreshInterfacesPeriodically()
    {
        try
        {
            while (!stopRefresher)
            {
                int iterations = INTERFACE_REFRESH_INTERVAL_MS / STOP_CHECK_INTERVAL_MS;
                for (int i = 0; i < iterations && !stopRefresher; i++)
                    Thread.Sleep(STOP_CHECK_INTERVAL_MS);

                if (stopRefresher || Volatile.Read(ref disposed) != 0)
                    break;

                lock (socketLock)
                {
                    if (stopRefresher || socket == null || !socket.IsBound)
                        break;
                }

                var newInterfaces = DiscoverBroadcastInterfaces(lobbyPort);

                lock (socketLock)
                {
                    if (stopRefresher || socket == null || !socket.IsBound)
                        break;

                    broadcastInterfaces.Clear();
                    foreach (var (key, netIf) in newInterfaces)
                        broadcastInterfaces[key] = netIf;
                }
            }
        }
        catch (ThreadInterruptedException)
        {
            // 关闭时预期
        }
        catch (Exception ex)
        {
            Logger.Log("Network interface refresh thread: exception: " + ex);
        }
    }

    private const int THREAD_SHUTDOWN_TIMEOUT_MS = 1000;

    /// <summary>关闭 socket 并停止监听/刷新线程。</summary>
    public void Shutdown()
    {
        lock (socketLock)
        {
            stopRefresher = true;

            if (socket != null && socket.IsBound)
            {
                try
                {
                    socket.Close();
                }
                catch (ObjectDisposedException)
                {
                    // 已释放
                }
            }

            socket = null;
        }

        if (listener != null)
        {
            if (!listener.Join(THREAD_SHUTDOWN_TIMEOUT_MS))
                Logger.Log("Failed to shut down listener after timeout!");
            listener = null;
        }

        if (interfaceRefresher != null)
        {
            interfaceRefresher.Interrupt();
            if (!interfaceRefresher.Join(THREAD_SHUTDOWN_TIMEOUT_MS))
                Logger.Log("Failed to shut down interface refresher after timeout!");
            interfaceRefresher = null;
        }

        broadcastInterfaces.Clear();
    }

    public int BroadcastInterfaceCount => broadcastInterfaces.Count;

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref disposed, 1, 0) == 0)
            Shutdown();
        GC.SuppressFinalize(this);
    }
}
