namespace CnCNetClient.Tools
{
    /// <summary>
    /// 宿主扩展点：玩家昵称在连接 / 保存前的标准化处理（如替换非法字符、强制专属名称）。
    /// 默认实现原样返回；插件在 <c>RegisterServices</c> 注册同接口实现即覆盖（后注册者胜）。
    /// 处理结果仍会走宿主 <c>NameValidator</c> 校验，不得返回空串或非法名称。
    /// </summary>
    public interface IPlayerNameProcessor
    {
        /// <summary>处理用户输入或设置页保存的昵称，返回最终要使用的昵称。</summary>
        string Process(string name);
    }

    /// <summary>默认的 <see cref="IPlayerNameProcessor"/>：原样返回，不改变任何昵称。</summary>
    public sealed class DefaultPlayerNameProcessor : IPlayerNameProcessor
    {
        public string Process(string name) => name;
    }
}
