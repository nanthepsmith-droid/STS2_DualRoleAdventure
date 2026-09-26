using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 机会成本测试（M1 知识层，总规范 v2 §42/§43）。
///
/// 要钉死的语义：**牌组越大、越健康 ⇒ 再加一张越贵**（v2 §43 的原话是"牌组越健康 OpportunityCost ↑"），
/// 这正是 M2 里"好牌组也应当会 Skip"的数学来源（v2 §23：`MarginalValue ≠ RawValue`）。
/// </summary>
[TestFixture]
public class WakuuOpportunityCostTests
{
    [Test]
    public void 牌组越大_机会成本越高()
    {
        float small = WakuuOpportunityCost.OfAddingOne(10, uniformity: 0.5f);
        float reference = WakuuOpportunityCost.OfAddingOne(30, uniformity: 0.5f);
        float large = WakuuOpportunityCost.OfAddingOne(40, uniformity: 0.5f);

        Assert.Multiple(() =>
        {
            Assert.That(small, Is.LessThan(reference));
            Assert.That(large, Is.GreaterThan(reference));
        });
    }

    [Test]
    public void 牌组越健康_机会成本越高()
    {
        float weak = WakuuOpportunityCost.OfAddingOne(30, uniformity: 0f);
        float healthy = WakuuOpportunityCost.OfAddingOne(30, uniformity: 1f);

        Assert.That(healthy, Is.GreaterThan(weak));
    }

    [Test]
    public void 参考规模下_成本等于基准稀释值()
    {
        // 30 张、均卡度 0 ⇒ 规模因子 1、健康因子 1 ⇒ 恰为 BaseDilutionPerCard
        Assert.That(WakuuOpportunityCost.OfAddingOne(30, 0f),
            Is.EqualTo(WakuuOpportunityCost.BaseDilutionPerCard).Within(0.0001f));
    }

    [Test]
    public void 净价值_等于卡价值减机会成本_且可为负()
    {
        float net = WakuuOpportunityCost.NetValue(cardValue: 1f, deckSizeNow: 30, uniformity: 1f);

        Assert.Multiple(() =>
        {
            // 1 − 0.5×1×2 = 0 ⇒ 健康牌组里"一张健康牌"只是打平
            Assert.That(net, Is.EqualTo(0f).Within(0.0001f));

            // 一张平庸牌（0.4）在健康牌组里是负收益 ⇒ 该 Skip
            Assert.That(WakuuOpportunityCost.NetValue(0.4f, 30, 1f), Is.LessThan(0f));
        });
    }

    [Test]
    public void 异常入参_不产生负数成本与NaN()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuOpportunityCost.OfAddingOne(-10, 0f), Is.EqualTo(0f));
            Assert.That(WakuuOpportunityCost.OfAddingOne(30, float.NaN), Is.GreaterThan(0f));
            Assert.That(float.IsNaN(WakuuOpportunityCost.NetValue(float.NaN, 30, 0.5f)), Is.False);
        });
    }
}
