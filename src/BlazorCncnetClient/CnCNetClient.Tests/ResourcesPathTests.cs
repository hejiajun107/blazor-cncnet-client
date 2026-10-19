using CnCNetClient.Core;
using Xunit;

namespace CnCNetClient.Tests;

/// <summary>
/// ProgramConstants.GetExpectedResourcesPath：资源缺失提示里的「正确位置」推断。
/// 覆盖三种布局（ClientApp 子目录 / Launcher.exe 同级 / 平铺）与盘符根边界。
/// </summary>
public class ResourcesPathTests : IDisposable
{
    private readonly string _dir;

    public ResourcesPathTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cncnet-respath-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void DriveRoot_ProducesDriveRootResourcesPath()
    {
        // 旧实现 TrimEnd + Path.GetDirectoryName 会得到 "C:"，拼出 "C:Resources"（驱动器相对路径）。
        Assert.Equal(@"C:\Resources", ProgramConstants.GetExpectedResourcesPath(@"C:\"));
    }

    [Fact]
    public void ClientAppDirectory_UsesParentAsInstallRoot()
    {
        string clientApp = Path.Combine(_dir, ProgramConstants.CLIENT_APP_DIRECTORY_NAME);
        Directory.CreateDirectory(clientApp);

        Assert.Equal(Path.Combine(_dir, ProgramConstants.BASE_RESOURCE_PATH),
            ProgramConstants.GetExpectedResourcesPath(clientApp));
    }

    [Fact]
    public void LauncherSibling_MarksInstallRootEvenIfDirectoryNameDiffers()
    {
        string clientDir = Path.Combine(_dir, "SomeClientFolder");
        Directory.CreateDirectory(clientDir);
        File.WriteAllText(Path.Combine(_dir, ProgramConstants.LAUNCHER_EXECUTABLE_NAME), string.Empty);

        Assert.Equal(Path.Combine(_dir, ProgramConstants.BASE_RESOURCE_PATH),
            ProgramConstants.GetExpectedResourcesPath(clientDir));
    }

    [Fact]
    public void FlatLayout_UsesStartupDirectoryItself()
    {
        Assert.Equal(Path.Combine(_dir, ProgramConstants.BASE_RESOURCE_PATH),
            ProgramConstants.GetExpectedResourcesPath(_dir));
    }
}
