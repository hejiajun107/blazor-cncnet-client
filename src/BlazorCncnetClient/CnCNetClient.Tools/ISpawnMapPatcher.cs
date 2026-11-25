namespace CnCNetClient.Tools
{
    /// <summary>
    /// spawnmap.ini 补丁上下文：供 <see cref="ISpawnMapPatcher"/> 判断是否需要、以及如何打补丁。
    /// 只包含基础类型，避免与领域程序集产生反向依赖。
    /// </summary>
    public sealed class SpawnMapPatchContext
    {
        /// <summary>地图未翻译名称（.map 文件名去掉扩展名）。</summary>
        public string MapName { get; init; } = string.Empty;

        /// <summary>人类玩家数量。</summary>
        public int HumanPlayerCount { get; init; }

        /// <summary>AI 玩家数量（遭遇战/多人房主添加的 AI）。</summary>
        public int AiPlayerCount { get; init; }

        /// <summary>是否为合作任务模式。</summary>
        public bool IsCoop { get; init; }
    }

    /// <summary>
    /// 宿主扩展点：在 spawnmap.ini 写盘前对地图 INI 做最后修改（例如节日限定代码、活动单位替换）。
    /// 默认实现不做任何事；插件在 <c>RegisterServices</c> 注册同接口实现即覆盖（后注册者胜）。
    /// </summary>
    public interface ISpawnMapPatcher
    {
        /// <summary>在 spawnmap.ini 写入前调用，可直接修改 <paramref name="spawnMapIni"/>。</summary>
        void Patch(IniFile spawnMapIni, SpawnMapPatchContext context);
    }

    /// <summary>默认的 <see cref="ISpawnMapPatcher"/>：不修改任何内容，保持宿主原有行为。</summary>
    public sealed class DefaultSpawnMapPatcher : ISpawnMapPatcher
    {
        public void Patch(IniFile spawnMapIni, SpawnMapPatchContext context)
        {
        }
    }
}
