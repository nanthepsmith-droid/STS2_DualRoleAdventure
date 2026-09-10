using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 运行清理期「预期中止」判定（r110）纯逻辑测试。
/// 场景：退出这一局/回主菜单时，自动出牌作用域仍在飞（`run-cleanup` 探针 inFlight=1），
/// 游戏状态已被拆掉 → 异步链抛 Nullable 等异常。这类异常应被视为预期中止（降级 INFO、不上抛）。
/// </summary>
[TestFixture]
public class WakuuTeardownPolicyTests
{
    [Test]
    public void 运行已结束_视为预期中止()
    {
        Assert.That(
            WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(runInProgress: false, hasRunState: true),
            Is.True);
    }

    [Test]
    public void RunState已拆除_视为预期中止()
    {
        Assert.That(
            WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(runInProgress: true, hasRunState: false),
            Is.True);
    }

    [Test]
    public void 两者都不可用_视为预期中止()
    {
        Assert.That(
            WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(runInProgress: false, hasRunState: false),
            Is.True);
    }

    [Test]
    public void 运行中且状态可读_不是预期中止_应按故障上报()
    {
        Assert.That(
            WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(runInProgress: true, hasRunState: true),
            Is.False);
    }
}
