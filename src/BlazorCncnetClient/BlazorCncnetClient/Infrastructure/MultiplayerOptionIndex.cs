using CnCNetClient.Core;

namespace BlazorCncnetClient.Infrastructure;

/// <summary>
/// 多人房间阵营 / 颜色选项编码（对齐上游 CnCNet 的下拉索引语义，即 GameLobbyBase.AddSideToDropDown）：
/// <list type="bullet">
/// <item>SideId：<c>0</c> = 随机；<c>1..RandomCount-1</c> = 第 i 个自定义随机选择器（GameOptions.ini [RandomSelectors]）；
/// <c>RandomCount+i</c> = 真实阵营 i；<c>SideCount + RandomCount</c> = 观战。</item>
/// <item>ColorId：<c>0</c> = 随机；<c>k</c> = <c>MultiplayerColor.LoadColors()</c> 的第 k-1 项。</item>
/// </list>
/// 选项在房间内保持“随机”状态（不立即解析），开局时由 <see cref="MultiplayerOptionResolver"/>
/// 按共享随机种子在所有客户端本地解析为具体值。
/// </summary>
internal static class MultiplayerOptionIndex
{
    /// <summary>完全随机（同上游 SideId / ColorId 的 0）。</summary>
    public const int Random = 0;

    /// <summary>自定义随机阵营选择器（GameOptions.ini [RandomSelectors]）。</summary>
    public static IReadOnlyList<(string Name, int[] Sides)> Selectors => ClientConfiguration.Instance.RandomSelectors;

    /// <summary>随机选项总数 = 完全随机 + 各随机选择器（上游 RandomCount = RandomSelectors.Count + 1）。</summary>
    public static int RandomCount => Selectors.Count + 1;

    /// <summary>真实阵营索引 → SideId（上游 SideId = randomCount + sideIndex）。</summary>
    public static int SideIdOfRealSide(int realSideIndex) => RandomCount + realSideIndex;

    /// <summary>SideId → 真实阵营索引（调用方需保证 SideId 落在真实阵营区间）。</summary>
    public static int RealSideOf(int sideId) => sideId - RandomCount;

    /// <summary>观战 SideId（上游 GetSpectatorSideIndex = SideCount + RandomSelectorCount）。</summary>
    public static int SpectatorSideId(int sideCount) => sideCount + RandomCount;

    /// <summary>指定 SideId 是否为观战。</summary>
    public static bool IsSpectator(int sideId, int sideCount) => sideId == SpectatorSideId(sideCount);
}
