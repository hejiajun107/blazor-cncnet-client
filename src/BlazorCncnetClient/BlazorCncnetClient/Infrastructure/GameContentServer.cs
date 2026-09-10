using System.Net;
using System.Net.Sockets;
using System.Text;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 游戏目录内容本地伺服（M5.x 启动闪屏）：进程内轻量 HTTP 伺服游戏根目录，
/// 供 Blazor UI 以「独立文件 URL」加载游戏目录媒体（视频/图片），
/// 避免将大文件打包进 wwwroot 或转 base64。
/// 基于 TcpListener 手写 HTTP/1.1（Connection: close + Range），
/// 绑定 127.0.0.1 动态端口——普通用户无需 URLACL 权限（HttpListener 在 Windows 上任意端口需预保留）。
/// 只读、防路径穿越、支持 Range（视频 seek/拖拽）。
/// </summary>
public sealed class GameContentServer : IDisposable
{
    private readonly object _lock = new();
    private CancellationTokenSource _cts = new();
    private TcpListener? _listener;
    private Thread? _thread;
    private string? _root;

    /// <summary>实际监听端口（动态分配）。</summary>
    public int Port { get; private set; }

    /// <summary>服务器基址，如 http://127.0.0.1:54321/。</summary>
    public string BaseUrl { get; private set; } = string.Empty;

    public bool IsRunning => _listener is not null;

    /// <summary>按游戏根目录相对路径生成可加载 URL（如 Resources/splash.mp4）。</summary>
    public string MapUrl(string relativePath)
        => BaseUrl + relativePath.TrimStart('/', '\\').Replace('\\', '/');

    /// <summary>启动（幂等）：伺服指定根目录；根目录未变化且已在运行则直接复用。失败时不抛出，保持不可用。</summary>
    public void Start(string root)
    {
        lock (_lock)
        {
            string fullRoot = Path.GetFullPath(root);
            if (IsRunning && string.Equals(_root, fullRoot, StringComparison.OrdinalIgnoreCase))
                return;

            StopLocked();

            try
            {
                var listener = new TcpListener(IPAddress.Loopback, 0);
                listener.Start();
                _listener = listener;
                Port = ((IPEndPoint)listener.LocalEndpoint).Port;
                BaseUrl = $"http://127.0.0.1:{Port}/";
                _root = fullRoot;

                _thread = new Thread(() => Loop(_cts.Token))
                {
                    IsBackground = true,
                    Name = "GameContentServer"
                };
                _thread.Start();
            }
            catch
            {
                // 服务器不可用（罕见）：调用方应降级（闪屏仅品牌画面），不影响应用启动
                StopLocked();
            }
        }
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        lock (_lock)
        {
            StopLocked();
        }
    }

    private void StopLocked()
    {
        // 先更换 token 再取消旧的，避免已取消的 token 被新的 Loop 复用（Start 后立即退出）
        var cts = _cts;
        _cts = new CancellationTokenSource();
        cts.Cancel();

        try { _listener?.Stop(); } catch { }
        _listener = null;
        _thread = null;
    }

    private void Loop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = _listener!.AcceptTcpClient();
            }
            catch (Exception)
            {
                break; // 监听被关闭
            }

            ThreadPool.QueueUserWorkItem(s => Handle((TcpClient)s!), client);
        }
    }

    private void Handle(TcpClient client)
    {
        using (client)
        using (NetworkStream stream = client.GetStream())
        {
            try
            {
                if (!TryReadRequestHead(stream, out string head))
                {
                    WriteHead(stream, 400, "Bad Request", 0, "text/plain");
                    return;
                }

                string[] lines = head.Split("\r\n");
                string[] req = lines[0].Split(' ');
                if (req.Length < 2 || (req[0] != "GET" && req[0] != "HEAD"))
                {
                    WriteHead(stream, 400, "Bad Request", 0, "text/plain");
                    return;
                }

                string range = lines
                    .FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase))
                    ?.Substring("Range:".Length).Trim() ?? string.Empty;

                ServeFile(stream, req[1], range, req[0] == "HEAD");
            }
            catch
            {
                // 单请求失败不中断服务器
            }
        }
    }

    private static bool TryReadRequestHead(NetworkStream stream, out string head)
    {
        head = string.Empty;
        byte[] buf = new byte[8192];
        int received = 0;

        while (received < buf.Length)
        {
            int n = stream.Read(buf, received, buf.Length - received);
            if (n <= 0)
                return false;

            received += n;
            head = Encoding.ASCII.GetString(buf, 0, received);
            if (head.Contains("\r\n\r\n", StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private void ServeFile(NetworkStream stream, string requestPath, string rangeHeader, bool headOnly)
    {
        string raw = Uri.UnescapeDataString(requestPath);
        string rel = raw.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(_root ?? string.Empty, rel));

        // 路径穿越防护
        if (_root is null
            || !full.StartsWith(_root, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(full))
        {
            WriteHead(stream, 404, "Not Found", 0, "text/plain");
            return;
        }

        long length = new FileInfo(full).Length;
        string contentType = ContentTypeFor(full);

        long start = 0;
        long end = length - 1;
        int status = 200;
        string statusText = "OK";
        string extraHeader = string.Empty;

        if (!string.IsNullOrEmpty(rangeHeader) && TryParseRange(rangeHeader, length, out start, out end))
        {
            status = 206;
            statusText = "Partial Content";
            extraHeader = $"Content-Range: bytes {start}-{end}/{length}\r\n";
        }

        long bodyLength = end - start + 1;
        WriteHead(stream, status, statusText, bodyLength, contentType, extraHeader);

        if (headOnly || bodyLength == 0)
            return;

        using FileStream fs = File.OpenRead(full);
        if (start > 0)
            fs.Seek(start, SeekOrigin.Begin);

        long remaining = bodyLength;
        byte[] buf = new byte[64 * 1024];
        while (remaining > 0)
        {
            int read = fs.Read(buf, 0, (int)Math.Min(buf.Length, remaining));
            if (read <= 0)
                break;
            stream.Write(buf, 0, read);
            remaining -= read;
        }
    }

    private static void WriteHead(NetworkStream stream, int status, string statusText, long contentLength, string contentType, string extraHeader = "")
    {
        string resp = "HTTP/1.1 " + status + " " + statusText + "\r\n"
            + "Content-Type: " + contentType + "\r\n"
            + "Content-Length: " + contentLength + "\r\n"
            + "Accept-Ranges: bytes\r\n"
            + "Connection: close\r\n"
            + extraHeader
            + "\r\n";
        stream.Write(Encoding.ASCII.GetBytes(resp));
    }

    /// <summary>解析 Range: bytes=start-end / bytes=start- / bytes=-suffix。</summary>
    private static bool TryParseRange(string header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;

        string[] parts = header.Trim().Split('=');
        if (parts.Length != 2 || !string.Equals(parts[0], "bytes", StringComparison.OrdinalIgnoreCase))
            return false;

        string spec = parts[1].Split(',')[0].Trim();
        int dash = spec.IndexOf('-');
        if (dash < 0)
            return false;

        string from = spec[..dash].Trim();
        string to = spec[(dash + 1)..].Trim();

        if (from.Length == 0)
        {
            // 后缀区间：bytes=-N → 最后 N 字节
            if (!long.TryParse(to, out long suffix) || suffix <= 0)
                return false;
            start = Math.Max(0, length - suffix);
            end = length - 1;
            return start < length;
        }

        if (!long.TryParse(from, out start))
            return false;

        if (start >= length)
            return false;

        end = to.Length > 0 && long.TryParse(to, out long toVal) ? Math.Min(toVal, length - 1) : length - 1;
        return start <= end;
    }

    private static string ContentTypeFor(string path)
    {
        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogg" or ".ogv" => "video/ogg",
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".ico" => "image/x-icon",
            ".svg" => "image/svg+xml",
            ".css" => "text/css; charset=utf-8",
            _ => "application/octet-stream"
        };
    }
}
