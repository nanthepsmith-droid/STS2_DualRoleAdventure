using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 药水规则判定纯函数测试（WakuuPotionDecision.ShouldUseRule）。
/// 覆盖相位/范围/首回合/条件/昏眩五类过滤及组合场景。
/// </summary>
[TestFixture]
public class PotionRuleDecisionTests
{
    // ---------------------------------------------------------------
    // 相位（Phases）过滤
    // ---------------------------------------------------------------

    [Test]
    public void 相位不含当前相位_返回false()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.StartOfTurn, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.EndOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.False);
    }

    [Test]
    public void 相位含当前相位_返回true()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.EndOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    [Test]
    public void EndOfTurn规则_在StartOfTurn评估_返回false()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.EndOfTurn, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.False);
    }

    // ---------------------------------------------------------------
    // 战斗范围（Scope）过滤
    // ---------------------------------------------------------------

    [Test]
    public void 范围AnyCombat_普通战斗也可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    [TestCase(true, false, true)]  // 精英战斗：hardFight=true → 可用
    [TestCase(true, true, true)]   // Boss 战斗：hardFight 已含 Boss → 可用
    [TestCase(false, false, false)] // 普通战斗：不可用
    public void 范围HardFight_仅精英或Boss可用(bool hardFight, bool bossFight, bool expected)
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.HardFight,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: hardFight, bossFight: bossFight);

        Assert.That(use, Is.EqualTo(expected));
    }

    [TestCase(true, true, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    public void 范围BossFight_仅Boss可用(bool hardFight, bool bossFight, bool expected)
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.BossFight,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: hardFight, bossFight: bossFight);

        Assert.That(use, Is.EqualTo(expected));
    }

    // ---------------------------------------------------------------
    // 首回合（FirstRoundOnly）过滤
    // ---------------------------------------------------------------

    [Test]
    public void 首回合规则_第1回合可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: true, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    [TestCase(2)]
    [TestCase(3)]
    [TestCase(10)]
    public void 首回合规则_第2回合起不可用(int round)
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: true, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: round, hardFight: false, bossFight: false);

        Assert.That(use, Is.False);
    }

    [Test]
    public void 非首回合规则_任意回合可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 5, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    // ---------------------------------------------------------------
    // 附加条件（Condition）过滤
    // ---------------------------------------------------------------

    [Test]
    public void 有条件且条件满足_可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: true, conditionMet: true,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    [Test]
    public void 有条件但条件不满足_不可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: true, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.False);
    }

    [Test]
    public void 无条件规则_不受条件参数影响()
    {
        // 无条件规则（如果汁随时喝）：即使 conditionMet=false 也应可用
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    // ---------------------------------------------------------------
    // 昏眩（SkipWhenStunned）过滤
    // ---------------------------------------------------------------

    [Test]
    public void 昏眩跳过规则_当前昏眩_不可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: true, stunned: true,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.False);
    }

    [Test]
    public void 昏眩跳过规则_未昏眩_可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: true, stunned: false,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    [Test]
    public void 非昏眩规则_当前昏眩_仍可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.Both, scope: WakuuPotionFightScope.AnyCombat,
            firstRoundOnly: false, hasCondition: false, conditionMet: false,
            skipWhenStunned: false, stunned: true,
            phase: WakuuPotionPhase.StartOfTurn, round: 1, hardFight: false, bossFight: false);

        Assert.That(use, Is.True);
    }

    // ---------------------------------------------------------------
    // 组合场景
    // ---------------------------------------------------------------

    [Test]
    public void 组合_EndOfTurn_硬仗_条件满足_昏眩跳过_全通过才可用()
    {
        // 模拟"能量药水救高费牌"：EndOfTurn、HardFight、有条件、昏眩跳过
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.EndOfTurn, scope: WakuuPotionFightScope.HardFight,
            firstRoundOnly: false, hasCondition: true, conditionMet: true,
            skipWhenStunned: true, stunned: false,
            phase: WakuuPotionPhase.EndOfTurn, round: 3, hardFight: true, bossFight: false);

        Assert.That(use, Is.True);
    }

    [Test]
    public void 组合_任一条件不满足_整体不可用()
    {
        // 同一规则但昏眩中 → 不可用
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.EndOfTurn, scope: WakuuPotionFightScope.HardFight,
            firstRoundOnly: false, hasCondition: true, conditionMet: true,
            skipWhenStunned: true, stunned: true,
            phase: WakuuPotionPhase.EndOfTurn, round: 3, hardFight: true, bossFight: false);

        Assert.That(use, Is.False);
    }

    [Test]
    public void 组合_Boss战EndOfTurn首回合_龙涎香规则_可用()
    {
        // 模拟"龙涎香Boss残血"：EndOfTurn、BossFight、低血条件
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.EndOfTurn, scope: WakuuPotionFightScope.BossFight,
            firstRoundOnly: false, hasCondition: true, conditionMet: true,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.EndOfTurn, round: 1, hardFight: true, bossFight: true);

        Assert.That(use, Is.True);
    }

    [Test]
    public void 组合_非Boss战评估BossFight规则_不可用()
    {
        bool use = WakuuPotionDecision.ShouldUseRule(
            phases: WakuuPotionPhase.EndOfTurn, scope: WakuuPotionFightScope.BossFight,
            firstRoundOnly: false, hasCondition: true, conditionMet: true,
            skipWhenStunned: false, stunned: false,
            phase: WakuuPotionPhase.EndOfTurn, round: 1, hardFight: true, bossFight: false);

        Assert.That(use, Is.False);
    }
}
