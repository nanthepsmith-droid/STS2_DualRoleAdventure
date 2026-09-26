using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 卡牌经济学基线测试（M1 知识层，总规范 v2 §20~§23/§76）。
///
/// 要钉死的语义：
/// ① 原版锚点值就是可核对的那两个（基础打击 1 费 6 伤、基础防御 1 费 5 挡）；
/// ② 换算函数对**异常输入不产生 NaN/负数**（评分上游最怕这个）；
/// ③ 未知潜能**永远为正且收敛**（v2 §19/§78：未知 ≠ 0；同时不许爆表）。
/// </summary>
[TestFixture]
public class WakuuCardBaselineTests
{
    [Test]
    public void 原版锚点_基础打击与基础防御()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardBaseline.BasicStrikeDamage1E, Is.EqualTo(6f));
            Assert.That(WakuuCardBaseline.BasicDefendBlock1E, Is.EqualTo(5f));
            Assert.That(WakuuCardBaseline.HealthyAttack1E, Is.EqualTo(14f));
            Assert.That(WakuuCardBaseline.BaselineEnergyPerTurn, Is.EqualTo(3f));
        });
    }

    [Test]
    public void 期望攻击与格挡_按费用线性放大_且费用下限为一()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardBaseline.ExpectedAttackAtCost(0), Is.EqualTo(14f));
            Assert.That(WakuuCardBaseline.ExpectedAttackAtCost(1), Is.EqualTo(14f));
            Assert.That(WakuuCardBaseline.ExpectedAttackAtCost(2), Is.EqualTo(28f));
            Assert.That(WakuuCardBaseline.ExpectedBlockAtCost(1), Is.EqualTo(10f));
        });
    }

    [Test]
    public void 端口折算_健康线卡牌得满分_超模卡超过一()
    {
        Assert.Multiple(() =>
        {
            // 1 费 14 伤 = 恰好健康线 ⇒ 伤害端口 1.0
            Assert.That(WakuuCardBaseline.DamagePort(14, 1), Is.EqualTo(1f).Within(0.0001f));

            // 1 费 7 伤 = 半张健康牌 ⇒ 0.5
            Assert.That(WakuuCardBaseline.DamagePort(7, 1), Is.EqualTo(0.5f).Within(0.0001f));

            // 2 费 28 伤（= 2× 健康线）同样归一到 1.0，避免"高费牌端口爆表"
            Assert.That(WakuuCardBaseline.DamagePort(28, 2), Is.EqualTo(1f).Within(0.0001f));
        });
    }

    [Test]
    public void 换算函数_对负数与NaN一律零_不产生NaN()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardBaseline.DamagePort(-5, 1), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.DefensePort(-5, 1), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.DrawPort(-1), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.EnergyPort(-1), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.DamageOverTimePort(-3), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.Sanitize(float.NaN), Is.EqualTo(0f));
            Assert.That(WakuuCardBaseline.Sanitize(float.PositiveInfinity), Is.EqualTo(0f));
        });
    }

    [Test]
    public void 未知潜能_零信号为零_其余恒正且收敛()
    {
        float none = WakuuCardBaseline.UnknownPotential(0);
        float one = WakuuCardBaseline.UnknownPotential(1);
        float two = WakuuCardBaseline.UnknownPotential(2);
        float nine = WakuuCardBaseline.UnknownPotential(9);

        Assert.Multiple(() =>
        {
            Assert.That(none, Is.EqualTo(0f));
            Assert.That(one, Is.EqualTo(WakuuCardBaseline.UnknownEffectEquivalent).Within(0.0001f));
            Assert.That(two, Is.GreaterThan(one), "两个读不懂的效果应当比一个好（但不能线性翻倍）");
            Assert.That(two, Is.LessThan(one * 2f), "平方根抑制：两个未知不应等价于两张满额未知牌");

            // 无论多少条未知，都不该超过"三张健康牌"的量级（防爆表）
            Assert.That(nine, Is.LessThan(3f));
            Assert.That(WakuuCardBaseline.UnknownPotential(-1), Is.EqualTo(0f));
        });
    }
}
