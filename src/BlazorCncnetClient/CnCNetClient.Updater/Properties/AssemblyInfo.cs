using System.Runtime.CompilerServices;

// 暴露内部成员给单元测试工程（CnCNetClient.Tests）。
// 仅放行测试所需的最小内部面：Updater.GetUniqueIdForFile 等。
[assembly: InternalsVisibleTo("CnCNetClient.Tests")]
