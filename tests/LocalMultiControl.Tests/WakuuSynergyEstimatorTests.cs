using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 协同估计测试（M1 知识层，总规范 v2 §37/§38/§39/§40）。
///
/// 要钉死的语义：
/// ① 没有任何来源 ⇒ 协同价值 **0**，并标记为孤儿组件（**不许**因为"理论上限大"而加分，v2 §39/§40）；
/// ② 来源越多、抽牌越足 ⇒ 条件上线概率越高，但**永远到不了 1**（牌序/耗牌/被清除）；
/// ③ 看不懂的联动（低置信度）不许给出高期望（v2 §40）。
/// </summary>
[TestFixture]
public class WakuuSynergyEstimatorTests
{
    [Test]
    public void 无来源_协同为零并标记孤儿组件()
    {
        WakuuSynergyContext context = new(sourceCount: 0, drawSupport: 5f, deckSize: 30, confidence: 1f);
        WakuuSynergyEstimate estimate = WakuuSynergyEstimator.Estimate(context, payoff: 5f);

        Assert.Multiple(() =>
        {
            Assert.That(estimate.Value, Is.EqualTo(0f));
            Assert.That(estimate.IsOrphanComponent, Is.True);
        });
    }

    [Test]
    public void 来源越多_条件上线概率越高()
    {
        float one = Value(1, drawSupport: 0f);
        float two = Value(2, drawSupport: 0f);
        float three = Value(3, drawSupport: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(two, Is.GreaterThan(one));
            Assert.That(three, Is.GreaterThan(two));
        });
    }

    [Test]
    public void 概率存在上限_永远到不了一()
    {
        WakuuSynergyContext context = new(sourceCount: 20, drawSupport: 100f, deckSize: 10, confidence: 1f);

        Assert.That(WakuuSynergyEstimator.Estimate(context, payoff: 1f).Probability,
            Is.EqualTo(WakuuSynergyEstimator.MaxProbability).Within(0.0001f));
    }

    [Test]
    public void 抽牌支持_带来概率加成但封顶()
    {
        float none = Value(2, drawSupport: 0f);
        float some = Value(2, drawSupport: 1f);
        float lots = Value(2, drawSupport: 100f);

        Assert.Multiple(() =>
        {
            Assert.That(some, Is.GreaterThan(none));
            Assert.That(lots, Is.GreaterThan(some));
            Assert.That(lots - none, Is.LessThanOrEqualTo(WakuuSynergyEstimator.MaxDrawBonus + 0.0001f));
        });
    }

    [Test]
    public void 牌组越厚_条件越难上线()
    {
        WakuuSynergyContext thin = new(sourceCount: 1, drawSupport: 0f, deckSize: 20, confidence: 1f);
        WakuuSynergyContext thick = new(sourceCount: 1, drawSupport: 0f, deckSize: 80, confidence: 1f);

        Assert.That(WakuuSynergyEstimator.Estimate(thick, 1f).Probability,
            Is.LessThan(WakuuSynergyEstimator.Estimate(thin, 1f).Probability));
    }

    [Test]
    public void 置信度_直接压低期望_看不懂就不许满概率()
    {
        WakuuSynergyContext sure = new(sourceCount: 3, drawSupport: 1f, deckSize: 30, confidence: 1f);
        WakuuSynergyContext unsure = new(sourceCount: 3, drawSupport: 1f, deckSize: 30, confidence: 0f);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuSynergyEstimator.Estimate(unsure, 1f).Value, Is.EqualTo(0f));
            Assert.That(WakuuSynergyEstimator.Estimate(sure, 1f).Value, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void 负收益与异常入参_被夹到零()
    {
        WakuuSynergyContext context = new(sourceCount: 2, drawSupport: 1f, deckSize: 30, confidence: 1f);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuSynergyEstimator.Estimate(context, payoff: -5f).Payoff, Is.EqualTo(0f));
            Assert.That(WakuuSynergyEstimator.Estimate(context, payoff: float.NaN).Payoff, Is.EqualTo(0f));
            Assert.That(new WakuuSynergyContext(-3, -1f, -1, 5f).SourceCount, Is.EqualTo(0));
        });
    }

    private static float Value(int sourceCount, float drawSupport)
    {
        WakuuSynergyContext context = new(sourceCount, drawSupport, deckSize: 30, confidence: 1f);
        return WakuuSynergyEstimator.Estimate(context, payoff: 1f).Value;
    }
}
