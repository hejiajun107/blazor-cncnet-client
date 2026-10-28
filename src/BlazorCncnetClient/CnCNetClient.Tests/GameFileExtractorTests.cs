using CnCNetClient.Integration.GameFiles;

using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 运行文件检查遵循 ClientDefinitions.ini [Settings]：
/// RequiredFiles 为权威清单（内置 YR 清单仅在其缺失/为空时兜底），
/// ForbiddenFiles（非必填）命中的文件由启动流程弹窗告知并退出。
/// </summary>
public class GameFileExtractorTests : IDisposable
{
    private readonly TestGameEnvironment _env;

    public GameFileExtractorTests()
    {
        _env = new TestGameEnvironment();
        _env.ResetConfigurationInstance();
    }

    public void Dispose() => _env.Dispose();

    [Fact]
    public void RequiredFiles_FromClientDefinitions_TakePrecedenceOverBuiltinList()
    {
        WriteClientDefinitions("RequiredFiles=custom_a.dat, custom_b.dat");

        Assert.Equal(new[] { "custom_a.dat", "custom_b.dat" }, GameFileExtractor.GetRequiredFiles());
    }

    [Fact]
    public void RequiredFiles_Empty_FallsBackToBuiltinList()
    {
        WriteClientDefinitions("RequiredFiles=");

        string[] files = GameFileExtractor.GetRequiredFiles();

        Assert.Contains("gamemd.exe", files);
        Assert.Contains("ra2md.mix", files);
    }

    [Fact]
    public void MissingFiles_UseConfiguredRequiredFiles()
    {
        WriteClientDefinitions("RequiredFiles=custom_a.dat,custom_b.dat");
        File.WriteAllText(Path.Combine(_env.GameDir, "custom_b.dat"), string.Empty);

        Assert.Equal(new[] { "custom_a.dat" }, GameFileExtractor.GetMissingFiles(_env.GameDir));
    }

    [Fact]
    public void ForbiddenFilesPresent_ReturnsOnlyExistingFiles()
    {
        WriteClientDefinitions("ForbiddenFiles=bad1.dll, bad2.dll");
        File.WriteAllText(Path.Combine(_env.GameDir, "bad2.dll"), string.Empty);

        Assert.Equal(new[] { "bad2.dll" }, GameFileExtractor.GetForbiddenFilesPresent(_env.GameDir));
    }

    [Fact]
    public void ForbiddenFiles_Unconfigured_ReturnsEmpty()
    {
        WriteClientDefinitions("ForbiddenFiles=");

        Assert.Empty(GameFileExtractor.GetForbiddenFilesPresent(_env.GameDir));
    }

    /// <summary>在 [Settings] 节内追加配置行并重建 ClientConfiguration 单例。</summary>
    private void WriteClientDefinitions(string settingsLine)
    {
        string content = TestGameEnvironment.DefaultClientDefinitions
            .Replace("MapFileExtension=map", "MapFileExtension=map\r\n        " + settingsLine);

        File.WriteAllText(_env.ClientDefinitionsPath, content);
        _env.ResetConfigurationInstance();
    }
}
