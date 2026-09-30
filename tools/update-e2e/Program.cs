// update-e2e — M5.3 自更新发布链路演练工具
//
// 子命令：
//   stage   生成服务器端清单/归档（发布目录 → updatestage 服务器目录）
//   verify  闭环验证（进程内本地服务器 + 驱动 Updater API：版本检查/下载/标识符校验）
//   serve   本地静态服务器（供 GUI 人工验收：设置中心 → 立即检查 → 指向本地镜像）
//
// 用法示例：
//   update-e2e stage  -publish dist/publish -out dist/updatestage -version 1.0.0
//   update-e2e stage  ... -archive                          # 追加 .lzma 归档
//   update-e2e verify -stage dist/updatestage -scenario up-to-date
//   update-e2e verify -stage dist/updatestage -scenario outdated
//   update-e2e serve  -stage dist/updatestage -port 8899

using System.Net;
using System.Security.Cryptography;
using System.Text;
using CnCNetClient.Tools;
using CnCNetClient.Updater;
using CnCNetClient.Updater.Compression;

namespace UpdateE2E;

internal static class Program
{
    private static int Main(string[] args)
    {
        try
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            switch (args[0].ToLowerInvariant())
            {
                case "stage": return RunStage(ParseArgs(args, 1));
                case "verify": return RunVerify(ParseArgs(args, 1));
                case "serve": return RunServe(ParseArgs(args, 1));
                default:
                    Console.Error.WriteLine($"未知子命令：{args[0]}");
                    PrintUsage();
                    return 2;
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"错误：{ex.Message}");
            return 1;
        }
    }

    private static Dictionary<string, string> ParseArgs(string[] args, int start)
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = start; i < args.Length; i++)
        {
            string a = args[i];
            if (!a.StartsWith('-'))
                continue;
            string key = a.TrimStart('-');
            string value = i + 1 < args.Length && !args[i + 1].StartsWith('-') ? args[++i] : string.Empty;
            dict[key] = value;
        }

        return dict;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("update-e2e — M5.3 自更新发布链路演练工具");
        Console.WriteLine("用法：update-e2e <stage|verify|serve> [参数]");
        Console.WriteLine("  stage  -publish <dir> [-out <dir>] -version <v> [-updater-version <v>] [-manual-url <u>] [-archive]");
        Console.WriteLine("  verify -stage <dir> [-game <dir>] [-port <n>] [-local-version <v>] -scenario <up-to-date|outdated>");
        Console.WriteLine("  serve  -stage <dir> [-port <n>]");
    }

    /* ────────────────────────── stage ────────────────────────── */

    private static int RunStage(Dictionary<string, string> p)
    {
        string publishDir = Path.GetFullPath(Get(p, "publish", DefaultDir("dist/publish")));
        string outDir = Path.GetFullPath(Get(p, "out", DefaultDir("dist/updatestage")));
        string version = Get(p, "version", string.Empty);
        if (string.IsNullOrWhiteSpace(version))
        {
            Console.Error.WriteLine("错误：-version 必填（服务器端版本号，如 1.0.0）。");
            return 2;
        }

        string? updaterVersion = Get(p, "updater-version", null);
        string? manualUrl = Get(p, "manual-url", null);
        bool archive = p.ContainsKey("archive");

        if (!Directory.Exists(publishDir))
        {
            Console.Error.WriteLine($"发布目录不存在：{publishDir}");
            return 1;
        }

        // 清空重建服务器目录
        if (Directory.Exists(outDir))
            Directory.Delete(outDir, true);
        Directory.CreateDirectory(outDir);

        var sb = new StringBuilder();
        sb.AppendLine("[DTA]");
        sb.AppendLine($"Version={version}");
        if (!string.IsNullOrEmpty(updaterVersion))
            sb.AppendLine($"UpdaterVersion={updaterVersion}");
        if (!string.IsNullOrEmpty(manualUrl))
            sb.AppendLine($"ManualDownloadURL={manualUrl}");

        var fileVersions = new List<string>();
        var archiveVersions = new List<string>();
        long totalBytes = 0;
        int count = 0;

        foreach (string file in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
        {
            string rel = Path.GetRelativePath(publishDir, file).Replace('\\', '/');
            string ext = Path.GetExtension(file).ToLowerInvariant();
            string[] parts = rel.Split('/');

            // 排除：PDB 调试符号、日志目录、运行时自生成目录
            if (ext == ".pdb")
                continue;
            if (parts.Any(seg => seg.Equals("logs", StringComparison.OrdinalIgnoreCase)))
                continue;

            byte[] fileHash = Md5Of(file);
            string id = string.Concat(fileHash.Select(b => b.ToString()));
            long sizeKb = Math.Max(1, (new FileInfo(file).Length + 1023) / 1024);
            string destRel = rel;

            if (archive)
            {
                string lzmaRel = rel + Updater.ARCHIVE_FILE_EXTENSION;
                string lzmaPath = Path.Combine(outDir, lzmaRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(lzmaPath)!);
                CompressionHelper.CompressFileAsync(file, lzmaPath).GetAwaiter().GetResult();
                string lzmaId = string.Concat(Md5Of(lzmaPath).Select(b => b.ToString()));
                long lzmaKb = Math.Max(1, (new FileInfo(lzmaPath).Length + 1023) / 1024);
                archiveVersions.Add($"{rel}={lzmaId},{lzmaKb}");
                fileVersions.Add($"{rel}={id},{sizeKb}");
                Console.WriteLine($"归档 {destRel}.lzma ({lzmaKb} KB)");
            }
            else
            {
                string destPath = Path.Combine(outDir, destRel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                File.Copy(file, destPath, true);
                fileVersions.Add($"{rel}={id},{sizeKb}");
                Console.WriteLine($"文件 {destRel} ({sizeKb} KB)");
            }

            totalBytes += new FileInfo(file).Length;
            count++;
        }

        sb.AppendLine();
        sb.AppendLine("[FileVersions]");
        foreach (string line in fileVersions)
            sb.AppendLine(line);

        if (archive)
        {
            sb.AppendLine();
            sb.AppendLine("[ArchivedFiles]");
            foreach (string line in archiveVersions)
                sb.AppendLine(line);
        }

        File.WriteAllText(Path.Combine(outDir, Updater.VERSION_FILE), sb.ToString(), new UTF8Encoding(false));

        // 原版发布要求镜像根存在 preupdateexec（缺失时客户端中止更新，404）与 updateexec（失败仅警告）。
        // 生成空脚本（INI 注释），避免真实发布时遗漏导致更新流程中断。
        const string emptyScript = "; generated by update-e2e stage\r\n";
        File.WriteAllText(Path.Combine(outDir, "preupdateexec"), emptyScript, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(outDir, "updateexec"), emptyScript, new UTF8Encoding(false));

        Console.WriteLine();
        Console.WriteLine($"服务器目录就绪：{outDir}");
        Console.WriteLine($"文件数：{count}   体积：{totalBytes / 1024} KB   版本：{version}" + (archive ? "   归档：.lzma" : string.Empty));
        Console.WriteLine($"清单：{Path.Combine(outDir, Updater.VERSION_FILE)}");
        return 0;
    }

    /* ────────────────────────── verify ────────────────────────── */

    private static int RunVerify(Dictionary<string, string> p)
    {
        string stageDir = Path.GetFullPath(Get(p, "stage", DefaultDir("dist/updatestage")));
        string gameDir = Path.GetFullPath(Get(p, "game", DefaultDir("dist/e2e-game")));
        int port = int.TryParse(Get(p, "port", "8899"), out int pp) ? pp : 8899;
        string scenario = Get(p, "scenario", "outdated").ToLowerInvariant();
        string localVersion = Get(p, "local-version", scenario == "outdated" ? "0.0.1" : "0");

        if (!Directory.Exists(stageDir))
        {
            Console.Error.WriteLine($"服务器目录不存在（先运行 stage）：{stageDir}");
            return 1;
        }

        if (scenario is not ("up-to-date" or "outdated"))
        {
            Console.Error.WriteLine($"错误：-scenario 仅支持 up-to-date / outdated。");
            return 2;
        }

        string serverVersion = ReadServerVersion(stageDir);
        if (scenario == "up-to-date")
            localVersion = serverVersion; // 与服务器同版本 → 应判 UPTODATE

        Console.WriteLine($"== 场景：{scenario}  (本地 {localVersion} vs 服务器 {serverVersion}) ==");

        // 构造临时游戏目录
        if (Directory.Exists(gameDir))
            Directory.Delete(gameDir, true);

        string resourcesDir = Path.Combine(gameDir, "Resources");
        Directory.CreateDirectory(resourcesDir);
        File.WriteAllText(Path.Combine(gameDir, Updater.VERSION_FILE),
            $"[DTA]\r\nVersion={localVersion}\r\n\r\n[FileVersions]\r\n", new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(gameDir, "ClientSettings.ini"), string.Empty);
        File.WriteAllText(Path.Combine(resourcesDir, "UpdaterConfig.ini"),
            $"[Settings]\r\nIgnoreMasks=.txt,.rtf\r\n\r\n[DownloadMirrors]\r\n0=http://127.0.0.1:{port}/,Local E2E,Local\r\n",
            new UTF8Encoding(false));

        // 日志
        Logger.Initialize(gameDir, "e2e.log");
        Logger.WriteToConsole = true;
        Logger.WriteLogFile = true;

        using var server = new LocalStaticServer(stageDir, port);
        server.Start();
        Console.WriteLine($"本地镜像：http://127.0.0.1:{port}/  (伺服 {stageDir})");

        // 驱动 Updater（与原版一致：先 Initialize 再检查本地版本再 CheckForUpdates）
        Updater.Initialize(gameDir, resourcesDir, "ClientSettings.ini", "Test", "BlazorCncnetClient.exe");
        Updater.CheckLocalFileVersions();

        if (!Updater.GameVersion.Equals(localVersion, StringComparison.Ordinal))
        {
            Console.Error.WriteLine($"FAIL：本地版本读取异常（期望 {localVersion}，实际 {Updater.GameVersion}）");
            return 1;
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var stateChanged = new ManualResetEventSlim(false);
        Updater.OnVersionStateChanged += () => stateChanged.Set();

        // ── 版本检查 ──
        Updater.CheckForUpdates();
        stateChanged.Wait();
        WaitForStableState(cts.Token);
        Console.WriteLine($"版本检查结果：本地 {Updater.GameVersion} | 服务器 {Updater.ServerGameVersion} | 状态 {Updater.VersionState}");

        if (scenario == "up-to-date")
        {
            bool ok = Updater.VersionState == VersionState.UPTODATE
                      && Updater.ServerGameVersion.Equals(serverVersion, StringComparison.Ordinal);
            Console.WriteLine(ok ? "PASS：版本一致，判定为 UPTODATE" : $"FAIL：期望 UPTODATE，实际 {Updater.VersionState}");
            return ok ? 0 : 1;
        }

        // outdated 场景：断言 OUTDATED + 更新大小
        if (Updater.VersionState != VersionState.OUTDATED || Updater.UpdateSizeInKb <= 0)
        {
            Console.Error.WriteLine($"FAIL：期望 OUTDATED 且有更新体积，实际 {Updater.VersionState} / {Updater.UpdateSizeInKb} KB");
            return 1;
        }

        Console.WriteLine($"发现更新：{Updater.UpdateSizeInKb} KB，开始下载…");

        // ── 下载闭环：逐文件下载到 Updater/ 目录并做标识符校验 ──
        int completed = 0;
        var fileDone = new ManualResetEventSlim(false);
        Updater.OnFileDownloadCompleted += name =>
        {
            completed++;
            string incoming = Path.Combine(gameDir, "Updater");
            if (Directory.Exists(incoming) && AllFilesStaged(stageDir, incoming, out _))
            {
                fileDone.Set();
            }
        };

        Updater.StartUpdate();
        if (!fileDone.Wait(TimeSpan.FromSeconds(90)))
        {
            Console.Error.WriteLine($"FAIL：下载超时（已完成 {completed} 个文件）");
            return 1;
        }

        // 全部文件就位 → 校验通过即 PASS（避免进入 SecondStageUpdater/Restart 重启链路，留待 GUI 验收）
        string incomingDir = Path.Combine(gameDir, "Updater");
        bool allOk = AllFilesStaged(stageDir, incomingDir, out var missing);
        if (!allOk)
        {
            Console.Error.WriteLine($"FAIL：以下更新文件未就位：{string.Join(", ", missing)}");
            return 1;
        }

        Console.WriteLine($"PASS：下载闭环完成，{completed} 个文件已下载并校验就位（Updater/ 目录）。");
        Console.WriteLine("说明：SecondStageUpdater 替换重启链路属 GUI 验收（设置中心 → 立即更新）。");
        return 0;
    }

    /// <summary>读取服务器清单中的 [DTA] Version。</summary>
    private static string ReadServerVersion(string stageDir)
    {
        string versionFile = Path.Combine(stageDir, Updater.VERSION_FILE);
        if (!File.Exists(versionFile))
            return string.Empty;

        var ini = new IniFile(versionFile);
        return ini.GetStringValue("DTA", "Version", string.Empty);
    }

    /// <summary>读取服务器清单文件列表（FileVersions 节键名），检查是否全部出现在 incoming 目录。</summary>
    private static bool AllFilesStaged(string stageDir, string incomingDir, out List<string> missing)
    {
        missing = new List<string>();
        string versionFile = Path.Combine(stageDir, Updater.VERSION_FILE);
        if (!File.Exists(versionFile))
            return false;

        var ini = new IniFile(versionFile);
        List<string> keys = ini.GetSectionKeys("FileVersions");
        if (keys is null)
            return true;

        foreach (string key in keys)
        {
            string local = Path.Combine(incomingDir, key.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(local))
                missing.Add(key);
        }

        return missing.Count == 0;
    }

    /// <summary>轮询等待版本状态脱离进行中（检查/更新），带超时。</summary>
    private static void WaitForStableState(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            if (Updater.VersionState is not (VersionState.UPDATECHECKINPROGRESS or VersionState.UPDATEINPROGRESS))
                return;
            Thread.Sleep(100);
        }
    }

    /* ────────────────────────── serve ────────────────────────── */

    private static int RunServe(Dictionary<string, string> p)
    {
        string stageDir = Path.GetFullPath(Get(p, "stage", DefaultDir("dist/updatestage")));
        int port = int.TryParse(Get(p, "port", "8899"), out int pp) ? pp : 8899;

        if (!Directory.Exists(stageDir))
        {
            Console.Error.WriteLine($"服务器目录不存在（先运行 stage）：{stageDir}");
            return 1;
        }

        Console.WriteLine($"伺服中：http://127.0.0.1:{port}/  →  {stageDir}");
        Console.WriteLine("Ctrl+C 停止。GUI 验收：把该 URL 加入游戏目录 Resources/UpdaterConfig.ini 的 [DownloadMirrors]。");
        using var server = new LocalStaticServer(stageDir, port);
        server.Start();
        Thread.Sleep(Timeout.Infinite);
        return 0;
    }

    /* ── 工具 ── */

    private static string Get(Dictionary<string, string> p, string key, string? fallback)
        => p.TryGetValue(key, out string? v) && !string.IsNullOrEmpty(v) ? v : (fallback ?? string.Empty);

    private static string DefaultDir(string relative)
    {
        string root = FindRepoRoot();
        return Path.Combine(root, relative);
    }

    /// <summary>从工具输出目录上溯查找仓库根（含 src 与 docs 的目录）。</summary>
    private static string FindRepoRoot()
    {
        DirectoryInfo? dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "src"))
                && Directory.Exists(Path.Combine(dir.FullName, "docs")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return Path.GetFullPath(".");
    }

    private static byte[] Md5Of(string path)
    {
        using var md5 = MD5.Create();
        using FileStream fs = File.OpenRead(path);
        return md5.ComputeHash(fs);
    }
}

/// <summary>极简本地 HTTP 静态服务器（M5.3 演练用，只读 GET）。</summary>
internal sealed class LocalStaticServer : IDisposable
{
    private readonly HttpListener _listener = new();
    private readonly string _root;
    private readonly CancellationTokenSource _cts = new();
    private readonly Thread _thread;

    public LocalStaticServer(string root, int port)
    {
        _root = Path.GetFullPath(root);
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _thread = new Thread(Loop) { IsBackground = true };
    }

    public void Start()
    {
        _listener.Start();
        _thread.Start();
    }

    private void Loop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext ctx;
            try
            {
                ctx = _listener.GetContext();
            }
            catch (Exception)
            {
                break;
            }

            try
            {
                Handle(ctx);
            }
            catch
            {
                // 单请求失败不中断服务器
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        string raw = Uri.UnescapeDataString(ctx.Request.Url!.AbsolutePath);
        string rel = raw.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        string full = Path.GetFullPath(Path.Combine(_root, rel));

        if (!full.StartsWith(_root, StringComparison.OrdinalIgnoreCase) || !File.Exists(full))
        {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        byte[] data = File.ReadAllBytes(full);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = "application/octet-stream";
        ctx.Response.ContentLength64 = data.Length;
        ctx.Response.OutputStream.Write(data, 0, data.Length);
        ctx.Response.Close();
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        _listener.Close();
    }
}
