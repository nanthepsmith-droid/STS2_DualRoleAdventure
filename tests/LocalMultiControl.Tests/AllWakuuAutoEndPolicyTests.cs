using LocalMultiControl.Scripts.Runtime;

using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r370「全瓦库局托管打光后自动收口」的判据。
///
/// 钉住四条硬口径：① 四项条件缺一不可（无牌可打 / 无在飞租约 / 无待作答 / 没有该打的席位）；
/// ② 有真人席位的局一律不抢；③ "有没有牌可打"必须来自托管口径；④ 同回合不重复收口。
/// </summary>
[TestFixture]
public class AllWakuuAutoEndPolicyTests
{
    private static AllWakuuAutoEndBlockReason Evaluate(
        bool localMulti = true,
        bool run = true,
        bool combat = true,
        bool playerSide = true,
        bool playPhase = true,
        bool manualGuard = false,
        bool selectionBusy = false,
        bool playLoopInFlight = false,
        bool selectorScopeInFlight = false,
        bool allHosted = true,
        bool noPlayable = true,
        bool pendingSeat = true,
        bool alreadyAutoEnded = false)
    {
        return AllWakuuAutoEndPolicy.Evaluate(new AllWakuuAutoEndInputs(
            localMulti,
            run,
            combat,
            playerSide,
            playPhase,
            manualGuard,
            selectionBusy,
            playLoopInFlight,
            selectorScopeInFlight,
            allHosted,
            noPlayable,
            pendingSeat,
            alreadyAutoEnded));
    }

    [Test]
    public void 条件齐备就放行()
    {
        AllWakuuAutoEndBlockReason reason = Evaluate();
        Assert.That(reason, Is.EqualTo(AllWakuuAutoEndBlockReason.None));
        Assert.That(AllWakuuAutoEndPolicy.IsAllowed(reason), Is.True);
    }

    [Test]
    public void 环境不满足时逐个拒绝()
    {
        Assert.That(Evaluate(localMulti: false), Is.EqualTo(AllWakuuAutoEndBlockReason.LocalMultiDisabled));
        Assert.That(Evaluate(run: false), Is.EqualTo(AllWakuuAutoEndBlockReason.RunNotInProgress));
        Assert.That(Evaluate(combat: false), Is.EqualTo(AllWakuuAutoEndBlockReason.CombatNotInProgress));
        Assert.That(Evaluate(playerSide: false), Is.EqualTo(AllWakuuAutoEndBlockReason.NotPlayerSide));
        Assert.That(Evaluate(playPhase: false), Is.EqualTo(AllWakuuAutoEndBlockReason.NotPlayPhase));
    }

    [Test]
    public void 有真人在手动出牌时立刻让开()
    {
        Assert.That(Evaluate(manualGuard: true), Is.EqualTo(AllWakuuAutoEndBlockReason.ManualPlayGuard));
    }

    [Test]
    public void 危险类判据优先于范围与无牌判据()
    {
        // 选牌在飞 / 出牌循环在飞 / 选择器作用域在飞：哪怕别的条件都成立也必须拒绝。
        Assert.That(
            Evaluate(selectionBusy: true, allHosted: false, noPlayable: false),
            Is.EqualTo(AllWakuuAutoEndBlockReason.SelectionBusy));
        Assert.That(
            Evaluate(playLoopInFlight: true, selectionBusy: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.SelectionBusy));
        Assert.That(
            Evaluate(playLoopInFlight: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.PlayLoopInFlight));
        Assert.That(
            Evaluate(selectorScopeInFlight: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.SelectorScopeInFlight));
    }

    [Test]
    public void 有真人席位时绝不抢收()
    {
        Assert.That(
            Evaluate(allHosted: false),
            Is.EqualTo(AllWakuuAutoEndBlockReason.NotAllSeatsHosted));
        // 即使别人都打光了，只要有一席不是托管席位就不许收。
        Assert.That(
            Evaluate(allHosted: false, noPlayable: true, pendingSeat: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.NotAllSeatsHosted));
    }

    [Test]
    public void 还有人托管能打牌就不收()
    {
        Assert.That(
            Evaluate(noPlayable: false),
            Is.EqualTo(AllWakuuAutoEndBlockReason.HostedSeatHasPlayableCard));
    }

    [Test]
    public void 都已就绪或本回合已收口过时不重复收()
    {
        Assert.That(Evaluate(pendingSeat: false), Is.EqualTo(AllWakuuAutoEndBlockReason.NoPendingSeat));
        Assert.That(
            Evaluate(alreadyAutoEnded: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.AlreadyAutoEndedThisRound));
        // 已收口过的判定排在最后：只要环境/在飞条件不成立，报的是更上游的原因（日志要能分开）。
        Assert.That(
            Evaluate(alreadyAutoEnded: true, selectionBusy: true),
            Is.EqualTo(AllWakuuAutoEndBlockReason.SelectionBusy));
    }

    [Test]
    public void 观察窗未到期不收_到期才收()
    {
        const long window = AllWakuuAutoEndPolicy.QuietWindowMs;

        // 还没开始计时。
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(0L, 10_000L, window), Is.False);
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(-1L, 10_000L, window), Is.False);
        // 刚成立 / 差 1ms。
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(10_000L, 10_000L, window), Is.False);
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(10_000L, 10_000L + window - 1, window), Is.False);
        // 正好到期 / 早已超时。
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(10_000L, 10_000L + window, window), Is.True);
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(10_000L, 60_000L, window), Is.True);
        // 时钟倒流（起点比现在还晚）⇒ 视为未到期。
        Assert.That(AllWakuuAutoEndPolicy.ShouldFire(20_000L, 10_000L, window), Is.False);
    }
}
