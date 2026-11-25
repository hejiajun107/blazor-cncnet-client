using BlazorCncnetClient.Infrastructure;
using CnCNetClient.Core;
using CnCNetClient.Core.Extensions;
using CnCNetClient.Domain.Multiplayer.CnCNet;
using CnCNetClient.Tools;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace BlazorCncnetClient.Components.Shared;

/// <summary>
/// 阵营 / 颜色下拉的贴图与随机色工具：图标在 Resources/ThemeDefault 与 Resources 根下探测
/// （主题优先，同原版 AssetLoader），经 <see cref="GameContentServer"/> 伺服给浏览器。
/// </summary>
internal static class GameAssetIcons
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> gameIconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 游戏图标的 data URL（内置游戏来自嵌入资源，自定义游戏来自 Resources\&lt;IconFilename&gt;）。
    /// 图很小，按 InternalName 缓存，避免每次渲染重复编码。
    /// </summary>
    public static string GameIconUrl(CnCNetGame? game)
    {
        if (game is null)
            return string.Empty;

        string key = game.InternalName ?? game.UIName ?? string.Empty;
        if (key.Length > 0 && gameIconCache.TryGetValue(key, out string? cached))
            return cached;

        string url = string.Empty;
        try
        {
            if (game.Image is { } image)
            {
                using var ms = new System.IO.MemoryStream();
                image.SaveAsPng(ms);
                url = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch
        {
            // 图标缺失/解码失败时保持空串，UI 只显示文字。
        }

        if (key.Length > 0)
            gameIconCache[key] = url;

        return url;
    }

    /// <summary>在 Resources/ThemeDefault 与 Resources 根下依次查找第一个存在的图标。</summary>
    public static string Url(GameContentServer server, params string[] fileNames)
    {
        if (!server.IsRunning)
            return string.Empty;

        string[][] directories = [["Resources", "ThemeDefault"], ["Resources"]];
        foreach (string file in fileNames)
        {
            foreach (string[] dir in directories)
            {
                string relative = SafePath.CombineFilePath([.. dir, file]);
                if (SafePath.GetFile(ProgramConstants.GamePath, relative).Exists)
                    return server.MapUrl(relative);
            }
        }

        return string.Empty;
    }

    /// <summary>阵营图标：{side}icon.png；索引越界（观战）用 spectatoricon.png。</summary>
    public static string SideUrl(GameContentServer server, string[] sides, int sideIndex)
        => Url(server, (sideIndex >= 0 && sideIndex < sides.Length ? sides[sideIndex] : "Spectator") + "icon.png");

    /// <summary>随机颜色图标（原版 RandomColorTexture：默认 randomcolor.png，缺失回退 randomicon.png）。</summary>
    public static string RandomColorUrl(GameContentServer server) => Url(server, "randomcolor.png", "randomicon.png");

    /// <summary>随机颜色的色块颜色（GameOptions.ini [General] RandomColor，缺省白色）。</summary>
    public static Rgba32 RandomSwatch()
    {
        try
        {
            var ini = new IniFile(SafePath.CombineFilePath(ProgramConstants.GetBaseResourcePath(), "GameOptions.ini"));
            string[] parts = ini.GetStringListValue("General", "RandomColor", "255,255,255");
            if (parts.Length >= 3)
            {
                return new Rgba32(
                    (byte)Math.Min(255, int.Parse(parts[0].Trim())),
                    (byte)Math.Min(255, int.Parse(parts[1].Trim())),
                    (byte)Math.Min(255, int.Parse(parts[2].Trim())),
                    255);
            }
        }
        catch
        {
            // 配置缺失/非法时回退原版默认白色
        }

        return new Rgba32(255, 255, 255, 255);
    }
}
