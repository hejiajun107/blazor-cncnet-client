using Xunit;

// 被测代码依赖较多静态/单例状态（ProgramConstants.GamePath、ClientConfiguration.Instance），
// 且测试会改写共享配置，禁止并行执行以保证确定性。
[assembly: CollectionBehavior(DisableTestParallelization = true)]
