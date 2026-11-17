using LocalMultiControl.Scripts.Runtime;

using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r364「强行结束回合」按钮的判据：门（能不能点）+ 逐席位（该结束谁）。
///
/// 两个硬约束都在这里钉住：
/// ① 有出牌 / 选牌在飞必须拒绝（r263 / r356 两次软锁都是"回合链被从中间切开"）；
/// ② 只结束托管席位（战神 / 瓦库形态），真人席位一律不碰。
/// </summary>
[TestFixture]
public class ForceEndTurnPolicyTests
{
    private static ForceEndTurnBlockReason Gate(
        bool localMulti = true,
        bool run = true,
        bool combat = true,
        bool playerSide = true,
        bool playPhase = true,
        bool selectionBusy = false,
        int endable = 1)
    {
        return ForceEndTurnPolicy.EvaluateGate(localMulti, run, combat, playerSide, playPhase, selectionBusy, endable);
    }

    [Test]
    public void 环境不满足时逐个拒绝()
    {
        Assert.That(Gate(localMulti: false), Is.EqualTo(ForceEndTurnBlockReason.LocalMultiDisabled));
        Assert.That(Gate(run: false), Is.EqualTo(ForceEndTurnBlockReason.RunNotInProgress));
        Assert.That(Gate(combat: false), Is.EqualTo(ForceEndTurnBlockReason.CombatNotInProgress));
        Assert.That(Gate(playerSide: false), Is.EqualTo(ForceEndTurnBlockReason.NotPlayerSide));
        Assert.That(Gate(playPhase: false), Is.EqualTo(ForceEndTurnBlockReason.NotPlayPhase));
    }

    [Test]
    public void 出牌或选牌在飞时拒绝_且优先于没有可结束的席位()
    {
        // 两条同时成立时必须报 SelectionBusy —— 它是"危险"那一类，日志/提示要能分开。
        Assert.That(Gate(selectionBusy: true, endable: 0), Is.EqualTo(ForceEndTurnBlockReason.SelectionBusy));
        Assert.That(Gate(selectionBusy: true, endable: 3), Is.EqualTo(ForceEndTurnBlockReason.SelectionBusy));
    }

    [Test]
    public void 托管席位都已结束回合时没有可结束项()
    {
        Assert.That(Gate(endable: 0), Is.EqualTo(ForceEndTurnBlockReason.NothingToForce));
    }

    [Test]
    public void 条件齐备就放行()
    {
        ForceEndTurnBlockReason reason = Gate();
        Assert.That(reason, Is.EqualTo(ForceEndTurnBlockReason.None));
        Assert.That(ForceEndTurnPolicy.IsAllowed(reason), Is.True);
        Assert.That(ForceEndTurnPolicy.IsAllowed(ForceEndTurnBlockReason.SelectionBusy), Is.False);
    }

    [Test]
    public void 有选牌在飞时按钮仍然显示_点了才提示()
    {
        // 显示与否只看"有没有该结束的托管席位"：按钮忽然消失比"点了得到提示"更难定位。
        Assert.That(
            ForceEndTurnPolicy.ShouldShowButton(true, true, true, true, true, endableSeatCount: 1),
            Is.True);
        Assert.That(
            ForceEndTurnPolicy.ShouldShowButton(true, true, true, true, true, endableSeatCount: 0),
            Is.False);
        Assert.That(
            ForceEndTurnPolicy.ShouldShowButton(true, true, true, sideIsPlayer: false, playPhase: true, endableSeatCount: 1),
            Is.False);
    }

    [Test]
    public void 只结束存活且在回合里且未结束回合的托管席位()
    {
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(true, true, true, true, false), Is.True);

        // 已 ready / 死了 / 没实体 / 不在这一段回合里（额外回合的非参与者）/ 不是托管席位 ⇒ 都不碰。
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(true, true, true, true, true), Is.False);
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(true, true, false, true, false), Is.False);
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(true, false, true, true, false), Is.False);
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(true, true, true, false, false), Is.False);
        Assert.That(ForceEndTurnPolicy.ShouldEndSeat(false, true, true, true, false), Is.False);
    }
}
