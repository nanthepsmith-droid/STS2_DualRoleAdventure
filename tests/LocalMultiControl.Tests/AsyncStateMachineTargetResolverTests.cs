using System.Reflection;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「异步方法本体所在状态机 MoveNext」解析器测试（r202）。
///
/// 存在的理由：r201 就是在这里翻车的 —— 解析状态机 MoveNext 时只写了 <c>BindingFlags.Public</c>，
/// 而 Roslyn 生成的状态机 <c>MoveNext</c> 是 **private**（元数据实见 `Private, Final, Virtual`）
/// ⇒ 解析返回 null ⇒ 补丁静默降级成"只挂 kickoff 单次让开"，实机表现与 r199 一样（照抛）。
/// 这类失败是**离线的**，用测试工程里的 dummy async 方法就能钉死，不必等实机。
/// </summary>
[TestFixture]
public class AsyncStateMachineTargetResolverTests
{
    /// <summary>带 try/finally 的 async 方法：Roslyn 会生成 class 状态机（MoveNext 为 private）。</summary>
    private static async Task<int> DummyStateMachineAsync()
    {
        try
        {
            await Task.Yield();
            return 1;
        }
        finally
        {
            // 故意留空：仅为让编译器生成 class 形态的状态机。
        }
    }

    private static void NonAsyncMethod()
    {
    }

    [Test]
    public void 解析异步方法的状态机MoveNext_必须穿透private()
    {
        MethodInfo kickoff = typeof(AsyncStateMachineTargetResolverTests)
            .GetMethod(nameof(DummyStateMachineAsync), BindingFlags.NonPublic | BindingFlags.Static)!;

        MethodInfo? moveNext = AsyncStateMachineTargetResolver.FindMoveNext(kickoff);

        Assert.That(moveNext, Is.Not.Null,
            "解析不到状态机 MoveNext：Roslyn 生成的状态机 MoveNext 是 private，查找必须带 NonPublic");
        Assert.That(moveNext!.DeclaringType!.Name, Does.Contain(nameof(DummyStateMachineAsync)));
        Assert.That(moveNext.IsPrivate, Is.True,
            "记录事实：状态机 MoveNext 为 private（别用只含 Public 的 BindingFlags 去找它）");
        Assert.That(moveNext.ReturnType, Is.EqualTo(typeof(void)));
    }

    [Test]
    public void 解析不到时返回空而不是抛异常()
    {
        // 非 async 方法（没有状态机）与 null 输入都必须安全返回 null：解析失败只允许"降级 + WARN"。
        MethodInfo? nonAsync = typeof(AsyncStateMachineTargetResolverTests)
            .GetMethod(nameof(NonAsyncMethod), BindingFlags.NonPublic | BindingFlags.Static);

        Assert.Multiple(() =>
        {
            Assert.That(AsyncStateMachineTargetResolver.FindMoveNext(nonAsync), Is.Null);
            Assert.That(AsyncStateMachineTargetResolver.FindMoveNext(null), Is.Null);
            Assert.That(AsyncStateMachineTargetResolver.FindMoveNextOn(null), Is.Null);
        });
    }
}
