using CnCNetClient.Core;
using CnCNetClient.Domain.Multiplayer;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;

ProgramConstants.GamePath = @"F:\work\PhotinoCLient\blazor-cncnet-client\blazor-cncnet-client\src\BlazorCncnetClient\BlazorCncnetClient\bin\Debug\net10.0";
// 故意不改 CWD（保持为 _maploader-check 目录），模拟 CWD ≠ 游戏目录的异常启动环境
Console.WriteLine($"CWD = {Environment.CurrentDirectory}");
Console.WriteLine($"GamePath = {ProgramConstants.GamePath}");
Console.WriteLine($"Resources exists = {Directory.Exists(ProgramConstants.GetBaseResourcePath())}");
Console.WriteLine($"ClientDefinitions.ini exists = {File.Exists(System.IO.Path.Combine(ProgramConstants.GetBaseResourcePath(), "ClientDefinitions.ini"))}");

try
{
    var cfg = ClientConfiguration.Instance;
    Console.WriteLine($"MPMapsIniPath = {cfg.MPMapsIniPath}");
    Console.WriteLine($"MapFileExtension = {cfg.MapFileExtension}");
    Console.WriteLine($"AllowedCustomGameModes = {cfg.AllowedCustomGameModes}");
}
catch (Exception ex)
{
    Console.WriteLine($"ClientConfiguration failed: {ex.GetType().Name}: {ex.Message}");
}

try
{
    UserINISettings.Initialize("ClientSettings.ini");
    Console.WriteLine($"UserINISettings initialized OK");
}
catch (Exception ex)
{
    Console.WriteLine($"UserINISettings failed: {ex.GetType().Name}: {ex.Message}");
}

CnCNetClient.Tools.Logger.WriteToConsole = true;
CnCNetClient.Tools.Logger.Initialize(System.IO.Path.GetTempPath(), "maploader-check.log");

// 直接单测一张官方地图
try
{
    var ini = new IniFile(System.IO.Path.Combine(ProgramConstants.GamePath, "INI", "MPMaps.ini"));
    Console.WriteLine($"[MultiMaps] keys = {ini.GetSectionKeys("MultiMaps")?.Count ?? -1}");
    var single = new Map("Maps/Standard/xmp29u2", false);
    Console.WriteLine($"single init = {single.InitializeFromMpMapsINI(ini)}");
    Console.WriteLine($"single name='{single.Name}' modes=[{string.Join(",", single.GameModes ?? Array.Empty<string>())}] official={single.Official}");
}
catch (Exception ex)
{
    Console.WriteLine($"single map test failed: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

var loader = new MapLoader();
try
{
    await loader.LoadMapsAsync();
    Console.WriteLine($"== RESULT == GameModes={loader.GameModes.Count}  GameModeMaps={loader.GameModeMaps.Count}");
    foreach (var gm in loader.GameModes)
        Console.WriteLine($"  mode: '{gm.Name}' maps={gm.Maps.Count} ui='{gm.UIName}'");
    var first = loader.GameModeMaps.FirstOrDefault();
    if (first != null)
    {
        Console.WriteLine($"  first gmm: map='{first.Map.Name}' mode='{first.GameMode.UIName}' display='{first.ToUIString()}'");
        Console.WriteLine($"  PreviewPath='{first.Map.PreviewPath}' immediate={first.Map.IsImmediatePreviewImageAvailable()}");
        try
        {
            using var img = loader.GetPreviewTextureFromMap(first.Map, syncLoadOnCacheMiss: true);
            Console.WriteLine($"  preview texture = {(img != null ? $"OK {img.Width}x{img.Height}" : "NULL")}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  preview texture FAILED: {ex.GetType().Name}: {ex.Message}");
        }
    }
    else
        Console.WriteLine("  GameModeMaps is EMPTY");

    // ── 全量预览可用性统计 ──
    int immediate = 0, nonImmediate = 0, none = 0, ok = 0, fail = 0;
    var samples = loader.GameModeMaps.Take(6).ToList();
    foreach (var gmm in loader.GameModeMaps)
    {
        var m = gmm.Map;
        if (m.IsImmediatePreviewImageAvailable()) immediate++;
        else if (m.IsNonImmediatePreviewImageAvailable()) nonImmediate++;
        else none++;
    }
    Console.WriteLine($"== PREVIEW STATS == immediate={immediate} nonImmediate={nonImmediate} none={none} (total={loader.GameModeMaps.Count})");
    foreach (var gmm in samples)
    {
        var m = gmm.Map;
        string status;
        try
        {
            using var img = loader.GetPreviewTextureFromMap(m, syncLoadOnCacheMiss: true);
            status = img != null ? $"OK {img.Width}x{img.Height}" : "NULL";
            if (img != null) ok++; else none++;
        }
        catch (Exception ex)
        {
            status = $"EX {ex.GetType().Name}: {ex.Message}";
            fail++;
        }
        Console.WriteLine($"  [{status}] '{m.Name}' official={m.Official} PreviewPath='{m.PreviewPath}'");
    }

    // ── 模拟 MapPreviewService 完整链路（FindMapByHash + base64） ──
    var target = samples.FirstOrDefault();
    if (target != null)
    {
        var byHash = loader.FindMapByHash(target.Map.SHA1);
        Console.WriteLine($"== SERVICE FLOW == hash='{target.Map.SHA1}' foundByHash={(byHash != null)} sameRef={ReferenceEquals(byHash, target.Map)}");
        if (byHash != null)
        {
            try
            {
                using var img = loader.GetPreviewTextureFromMap(byHash, syncLoadOnCacheMiss: true);
                using var ms = new System.IO.MemoryStream();
                img?.SaveAsPng(ms);
                Console.WriteLine($"  encode = {(img != null ? $"OK len={ms.Length}" : "NULL")}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  encode FAILED: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"LoadMapsAsync failed: {ex.GetType().Name}: {ex.Message}");
    Console.WriteLine(ex.StackTrace);
}
