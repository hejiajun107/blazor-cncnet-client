using CnCNetClient.Core.I18N;

using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 地图名/简介翻译：翻译文件的键按某一地图包目录结构编写（Maps\Yuri's Revenge\...），
/// 实际安装的地图常被重新归类（Maps/Standard/...）。完整路径命中优先，未命中按文件名词干回退，
/// 否则官方地图名全部显示英文。
/// </summary>
public class TranslationMapLookupTests : IDisposable
{
    private readonly string _iniPath;

    public TranslationMapLookupTests()
    {
        _iniPath = Path.Combine(Path.GetTempPath(), $"cncnet-map-i18n-{Guid.NewGuid():N}.ini");

        File.WriteAllText(_iniPath, """
            [General]
            Name=Test

            [Values]
            INI:Maps:Maps\Yuri's Revenge\xmp29u2:Description=(2) 火线冲突
            INI:Maps:Maps\Yuri's Revenge\xmp29u2:Briefing=测试简介
            INI:Maps:Maps\Yuri's Revenge\custom_stem:Description=自定义地图
            """);
    }

    public void Dispose()
    {
        try { File.Delete(_iniPath); } catch { }
    }

    private Translation Load() => new(_iniPath, "zh-Hans");

    [Fact]
    public void ExactPath_Resolves()
    {
        Assert.Equal("(2) 火线冲突", Load().LookUpMap(@"Maps\Yuri's Revenge\xmp29u2", "Description", "fallback"));
    }

    [Fact]
    public void ForwardSlashPath_Resolves()
    {
        Assert.Equal("(2) 火线冲突", Load().LookUpMap("Maps/Yuri's Revenge/xmp29u2", "Description", "fallback"));
    }

    [Fact]
    public void ReorganizedMapFolder_FallsBackToFileNameStem()
    {
        Assert.Equal("(2) 火线冲突", Load().LookUpMap("Maps/Standard/xmp29u2", "Description", "fallback"));
    }

    [Fact]
    public void Briefing_AlsoFallsBackToFileNameStem()
    {
        Assert.Equal("测试简介", Load().LookUpMap("Maps/Standard/xmp29u2", "Briefing", "fallback"));
    }

    [Fact]
    public void UnknownMap_ReturnsDefault()
    {
        Assert.Equal("fallback", Load().LookUpMap("Maps/Standard/unknown_map", "Description", "fallback"));
    }

    [Fact]
    public void EmptyPath_ReturnsDefault()
    {
        Assert.Equal("fallback", Load().LookUpMap(string.Empty, "Description", "fallback"));
    }
}
