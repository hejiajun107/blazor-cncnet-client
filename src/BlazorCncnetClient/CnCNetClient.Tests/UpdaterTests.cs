// 本文件位于 CnCNetClient.* 命名空间层级内，未限定名 Updater 会被解析为
// 外层命名空间成员 CnCNetClient.Updater（命名空间），故用不同名的别名指向类型。
using UpdaterApi = CnCNetClient.Updater.Updater;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// 1.12 Updater：验证版本清单解析、文件标识符计算与镜像/组件配置读取（不访问网络）。
/// </summary>
public class UpdaterTests : IDisposable
{
    private readonly string _gameDir;
    private readonly string _resourceDir;

    public UpdaterTests()
    {
        _gameDir = Path.Combine(Path.GetTempPath(), "updater-tests-" + Guid.NewGuid().ToString("N"));
        _resourceDir = Path.Combine(_gameDir, "Resources");
        Directory.CreateDirectory(_resourceDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_gameDir, true); } catch { }
    }

    private void Initialize(string settingsIniName = "ClientSettings.ini")
    {
        UpdaterApi.Initialize(_gameDir, _resourceDir, settingsIniName, "YR", "BlazorCncnetClient.exe");
    }

    [Fact]
    public void GetUniqueIdForFile_UsesMd5DecimalAlgorithm()
    {
        Initialize();

        // 已知内容的文件：MD5 各字节按十进制拼接（与 update-e2e stage 同算法）
        File.WriteAllText(Path.Combine(_gameDir, "sample.bin"), "CnCNet test payload");
        string expectedId = string.Concat(
            System.Security.Cryptography.MD5.HashData(File.ReadAllBytes(Path.Combine(_gameDir, "sample.bin")))
                .Select(b => b.ToString()));

        string actualId = UpdaterApi.GetUniqueIdForFile("sample.bin");

        Assert.Equal(expectedId, actualId);
    }

    [Fact]
    public void GetUniqueIdForFile_DeterministicForSameContent()
    {
        Initialize();

        // 同一真实文件两次计算应得到相同标识符
        File.WriteAllText(Path.Combine(_gameDir, "sample.bin"), "CnCNet test payload");
        string a = UpdaterApi.GetUniqueIdForFile("sample.bin");
        string b = UpdaterApi.GetUniqueIdForFile("sample.bin");

        Assert.Equal(a, b);
    }

    [Fact]
    public void CheckLocalFileVersions_ParsesVersionAndFileIdentifiers()
    {
        File.WriteAllText(Path.Combine(_gameDir, UpdaterApi.VERSION_FILE), """
            [DTA]
            Version=1.2.3
            UpdaterVersion=4.5.6

            [FileVersions]
            Game.exe=12345,1024
            Resources/sound.mix=67890,2048
            BadEntry=onlyonefield

            [ArchivedFiles]
            Game.exe=111,256
            """);
        Initialize();

        UpdaterApi.CheckLocalFileVersions();

        Assert.Equal("1.2.3", UpdaterApi.GameVersion);
        Assert.Equal("4.5.6", UpdaterApi.UpdaterVersion);
    }

    [Fact]
    public void IsFileNonexistantOrOriginal_MatchesIdentifierForExistingFile()
    {
        // 真实文件 + 清单内一致标识符 → 视为原始/未篡改
        byte[] bytes = "untampered game payload"u8.ToArray();
        File.WriteAllBytes(Path.Combine(_gameDir, "Game.exe"), bytes);
        string id = string.Concat(System.Security.Cryptography.MD5.HashData(bytes).Select(b => b.ToString()));

        File.WriteAllText(Path.Combine(_gameDir, UpdaterApi.VERSION_FILE), $"""
            [DTA]
            Version=0.0.1

            [FileVersions]
            Game.exe={id},100
            """);
        Initialize();

        UpdaterApi.CheckLocalFileVersions();

        Assert.True(UpdaterApi.IsFileNonexistantOrOriginal("Game.exe"));
    }

    [Fact]
    public void IsFileNonexistantOrOriginal_UnlistedFileIsTreatedAsOriginal()
    {
        File.WriteAllText(Path.Combine(_gameDir, UpdaterApi.VERSION_FILE), """
            [DTA]
            Version=0.0.1

            [FileVersions]
            """);
        Initialize();

        UpdaterApi.CheckLocalFileVersions();

        Assert.Equal("0.0.1", UpdaterApi.GameVersion);
        // 未列入清单的文件一律放行
        Assert.True(UpdaterApi.IsFileNonexistantOrOriginal("random_file.dat"));
    }

    [Fact]
    public void Initialize_ReadsMirrorsAndCustomComponentsFromUpdaterConfig()
    {
        File.WriteAllText(Path.Combine(_resourceDir, "UpdaterConfig.ini"), """
            [Settings]
            IgnoreMasks=*.pdb,logs

            [DownloadMirrors]
            0=https://mirror.example.com/,Main Mirror,cncnet
            1=https://backup.example.com/,Backup,cncnet

            [CustomComponents]
            0=Extra Pack,extra_pack,https://mirror.example.com/extra.lzma,Client/Extra
            """);
        Initialize("ClientSettings.ini");

        Assert.Equal(2, UpdaterApi.UpdateMirrors.Count);
        Assert.Equal("https://mirror.example.com/", UpdaterApi.UpdateMirrors[0].URL);
        Assert.Single(UpdaterApi.CustomComponents);
        Assert.Equal("extra_pack", UpdaterApi.CustomComponents[0].ININame);
    }
}
