using LocalMultiControl.Scripts.Runtime;

using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r358 回归：**战神调度被挡太久必须兜底收口**，否则真人「结束回合」永远推不动回合。
///
/// 实机事故（2026-11-15）：战神 4096 连刷 `rejected=selector-scope-busy:19`，手上有托管能打的牌
/// ⇒ 空闲收口不敢替它 ready ⇒ `PlayersReadyToEndTurn.Count` 只有 2、分母 3 ⇒ 回合链停住。
/// </summary>
[TestFixture]
public class TempSeatSchedulingStallPolicyTests
{
    [Test]
    public void 只有等下去也不会自己好的两种拒绝算卡住()
    {
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("selector-scope-busy"), Is.True);
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("watchdog-in-flight"), Is.True);
    }

    [Test]
    public void 别的拒绝不参与兜底计时()
    {
        // 这些都有明确的其它收口路径：无牌可打（空闲收口会 ready）/ 已 ready / 弹层挡住（等人关）
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("no-playable-cards"), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("player-ready-to-end-turn"), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("overlay-open"), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection("not-part-of-this-turn"), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.IsBlockingRejection(null), Is.False);
    }

    [Test]
    public void 未到宽限不兜底_刚到宽限即兜底()
    {
        long since = 10_000;

        Assert.That(TempSeatSchedulingStallPolicy.ShouldForceEnd(since, nowMs: 17_999, graceMs: 8_000), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.ShouldForceEnd(since, nowMs: 18_000, graceMs: 8_000), Is.True);
    }

    [Test]
    public void 没开始计时或时间倒流都不兜底()
    {
        Assert.That(TempSeatSchedulingStallPolicy.ShouldForceEnd(blockedSinceMs: 0, nowMs: 999_999, graceMs: 8_000), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.ShouldForceEnd(blockedSinceMs: 50_000, nowMs: 10_000, graceMs: 8_000), Is.False);
        Assert.That(TempSeatSchedulingStallPolicy.ShouldForceEnd(10_000, 999_999, graceMs: 0), Is.False);
    }
}
