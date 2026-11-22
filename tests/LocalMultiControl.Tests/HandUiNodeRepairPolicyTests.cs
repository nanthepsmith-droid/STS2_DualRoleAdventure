using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 手牌 UI「缺节点」单点补建的闸门（r389 引入，r390 去掉"出牌作用域在飞"那道把唯一该修时刻全挡掉的闸门）。
/// 现在只有三条硬约束 + 每道闸都要能说出"为什么没补"。
/// </summary>
[TestFixture]
public class HandUiNodeRepairPolicyTests
{
    private const long Now = 100_000L;

    private static HandUiNodeRepairDecision Decide(
        int missingCount = 1,
        bool handBusy = false,
        long missingSinceMs = Now - 2_000L,
        long lastAttemptMs = 0L)
        => HandUiNodeRepairPolicy.Evaluate(missingCount, handBusy, Now, missingSinceMs, lastAttemptMs);

    [Test]
    public void 缺一张且一切空闲_允许补建()
    {
        Assert.That(Decide(), Is.EqualTo(HandUiNodeRepairDecision.Repair));
        Assert.That(
            HandUiNodeRepairPolicy.ShouldRepair(1, false, Now, Now - 2_000L, 0L),
            Is.True);
    }

    [Test]
    public void 没有缺失_不动()
    {
        Assert.That(Decide(missingCount: 0), Is.EqualTo(HandUiNodeRepairDecision.NoMissing));
    }

    [Test]
    public void 手牌忙_不动()
    {
        // 出牌中 / 选牌中 / 非 Play 模式 —— 此时内容不可比，补节点会插进出牌链
        Assert.That(Decide(handBusy: true), Is.EqualTo(HandUiNodeRepairDecision.HandBusy));
    }

    [Test]
    public void 差异还没持续够久_不动()
    {
        // 原版手牌变换的视觉更新故意延迟约 0.9s，窗口内"数据新、UI 旧"是正常态
        Assert.That(Decide(missingSinceMs: Now - 100L), Is.EqualTo(HandUiNodeRepairDecision.PersistPending));
        Assert.That(
            Decide(missingSinceMs: Now - HandUiNodeRepairPolicy.MinPersistMs + 1L),
            Is.EqualTo(HandUiNodeRepairDecision.PersistPending));
        Assert.That(
            Decide(missingSinceMs: Now - HandUiNodeRepairPolicy.MinPersistMs),
            Is.EqualTo(HandUiNodeRepairDecision.Repair));
    }

    [Test]
    public void 还没开始计时_不动()
    {
        Assert.That(Decide(missingSinceMs: 0L), Is.EqualTo(HandUiNodeRepairDecision.PersistPending));
    }

    [Test]
    public void 上次刚试过_冷却期内不动_冷却过后再试()
    {
        Assert.That(Decide(lastAttemptMs: Now - 1_000L), Is.EqualTo(HandUiNodeRepairDecision.Cooldown));
        Assert.That(
            Decide(lastAttemptMs: Now - HandUiNodeRepairPolicy.RetryCooldownMs + 1L),
            Is.EqualTo(HandUiNodeRepairDecision.Cooldown));
        Assert.That(
            Decide(lastAttemptMs: Now - HandUiNodeRepairPolicy.RetryCooldownMs),
            Is.EqualTo(HandUiNodeRepairDecision.Repair));
        Assert.That(Decide(lastAttemptMs: 0L), Is.EqualTo(HandUiNodeRepairDecision.Repair), "从没试过 ⇒ 不受冷却限制");
    }

    [Test]
    public void 别席位正在出牌不影响补建_r390()
    {
        // 2026-11-22 实机：153 次差异、补建 0 次 —— 旧闸门要求"没有任何托管出牌作用域在飞"，
        // 而这种缺节点恰恰只发生在**别的席位正在出牌**的窗口里 ⇒ 全部被挡。现在不再看这个信号。
        Assert.That(Decide(), Is.EqualTo(HandUiNodeRepairDecision.Repair));
    }
}
