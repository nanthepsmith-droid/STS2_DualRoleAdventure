using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r283：额外回合（原版帕尔之眼 / 新三国「天意」这类**接管回合**效果）里"这一席还能不能行动"。
///
/// 语义对齐原版 <c>CombatManager.IsPartOfPlayerTurn</c>：普通回合全员算在回合里；
/// 一旦有 `PlayersTakingExtraTurn` 名单，就只有名单内的席位有行动权。
/// </summary>
[TestFixture]
public class TurnParticipationPolicyTests
{
    [Test]
    public void 玩家侧且属于本回合_可以驱动()
    {
        Assert.That(
            TurnParticipationPolicy.CanDriveSeat(currentSideIsPlayer: true, isPartOfPlayerTurn: true),
            Is.True);
    }

    [Test]
    public void 敌方回合_一律不驱动()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                TurnParticipationPolicy.CanDriveSeat(currentSideIsPlayer: false, isPartOfPlayerTurn: true),
                Is.False);
            Assert.That(
                TurnParticipationPolicy.CanDriveSeat(currentSideIsPlayer: false, isPartOfPlayerTurn: false),
                Is.False);
        });
    }

    [Test]
    public void 额外回合的非参与者_不能驱动()
    {
        // 关键回归点：别人的额外回合里，瓦库/战神没有行动权 —— 不能替它出牌、也不能替它结束回合。
        Assert.That(
            TurnParticipationPolicy.CanDriveSeat(currentSideIsPlayer: true, isPartOfPlayerTurn: false),
            Is.False);
    }
}
