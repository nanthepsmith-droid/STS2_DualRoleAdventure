#nullable enable
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r404：出牌取消噪声过滤的判据（只压「PlayCardAction + 那条精确措辞」，别的动作类型一律放行）。
/// </summary>
[TestFixture]
public class GameActionCanceledNoisePolicyTests
{
    private const string RealMessage =
        "GameAction PlayCardAction card: CARD.STRIKE_DEFECT (20012790) index: 48 targetid: 4 "
        + "finished execution, but was in state Canceled! The task probably kept executing in a paused "
        + "state without properly resuming.";

    [Test]
    public void 真实的出牌取消告警要过滤()
    {
        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(RealMessage), Is.True);
    }

    [Test]
    public void 别的动作类型不过滤()
    {
        string potion = RealMessage.Replace("PlayCardAction", "UsePotionAction");
        string endTurn = RealMessage.Replace("PlayCardAction", "EndPlayerTurnAction");

        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(potion), Is.False);
        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(endTurn), Is.False);
    }

    [Test]
    public void 措辞不同则不过滤()
    {
        string other = "GameAction PlayCardAction card: CARD.STRIKE_DEFECT (1) index: 1 targetid: 4 "
            + "paused execution";

        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(other), Is.False);
        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(null), Is.False);
        Assert.That(GameActionCanceledNoisePolicy.ShouldFilter(string.Empty), Is.False);
    }

    [Test]
    public void 能抠出牌名供日志留证()
    {
        Assert.That(GameActionCanceledNoisePolicy.ExtractCardEntry(RealMessage), Is.EqualTo("CARD.STRIKE_DEFECT"));
        Assert.That(GameActionCanceledNoisePolicy.ExtractCardEntry("no card here"), Is.EqualTo("?"));
        Assert.That(GameActionCanceledNoisePolicy.ExtractCardEntry(null), Is.EqualTo("?"));
    }
}
