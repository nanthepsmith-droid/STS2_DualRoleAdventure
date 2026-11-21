using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 手牌 UI「缺节点」单点补建的闸门（r389）。四条硬约束缺一不可：
/// 真的缺、手牌空闲、没有托管出牌作用域在飞、同一差异已持续够久；另加 5s 冷却防抖。
/// </summary>
[TestFixture]
public class HandUiNodeRepairPolicyTests
{
    private const long Now = 100_000L;

    private static bool Repair(
        int missingCount = 1,
        bool handBusy = false,
        bool playScopeBusy = false,
        long missingSinceMs = Now - 2_000L,
        long lastAttemptMs = 0L)
        => HandUiNodeRepairPolicy.ShouldRepair(
            missingCount, handBusy, playScopeBusy, Now, missingSinceMs, lastAttemptMs);

    [Test]
    public void 缺一张且一切空闲_允许补建()
    {
        Assert.That(Repair(), Is.True);
    }

    [Test]
    public void 没有缺失_不动()
    {
        Assert.That(Repair(missingCount: 0), Is.False);
    }

    [Test]
    public void 手牌忙_不动()
    {
        // 出牌中 / 选牌中 / 非 Play 模式 —— 此时内容不可比，补节点会插进出牌链
        Assert.That(Repair(handBusy: true), Is.False);
    }

    [Test]
    public void 有托管出牌作用域在飞_不动()
    {
        Assert.That(Repair(playScopeBusy: true), Is.False);
    }

    [Test]
    public void 差异还没持续够久_不动()
    {
        // 原版手牌变换的视觉更新故意延迟约 0.9s，窗口内"数据新、UI 旧"是正常态
        Assert.That(Repair(missingSinceMs: Now - 100L), Is.False);
        Assert.That(Repair(missingSinceMs: Now - HandUiNodeRepairPolicy.MinPersistMs + 1L), Is.False);
        Assert.That(Repair(missingSinceMs: Now - HandUiNodeRepairPolicy.MinPersistMs), Is.True);
    }

    [Test]
    public void 还没开始计时_不动()
    {
        Assert.That(Repair(missingSinceMs: 0L), Is.False);
    }

    [Test]
    public void 上次刚试过_冷却期内不动_冷却过后再试()
    {
        Assert.That(Repair(lastAttemptMs: Now - 1_000L), Is.False);
        Assert.That(Repair(lastAttemptMs: Now - HandUiNodeRepairPolicy.RetryCooldownMs + 1L), Is.False);
        Assert.That(Repair(lastAttemptMs: Now - HandUiNodeRepairPolicy.RetryCooldownMs), Is.True);
        Assert.That(Repair(lastAttemptMs: 0L), Is.True, "从没试过 ⇒ 不受冷却限制");
    }
}
