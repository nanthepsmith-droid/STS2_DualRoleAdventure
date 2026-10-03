using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 变换视觉门「这张牌算不算我的」托管席位判定（r203 / BUG-29 第五轮）纯逻辑测试。
///
/// 该判定由 transpiler 换掉原版视觉阶段那句 <c>LocalContext.IsMine(cardAdded2)</c> 的调用点，
/// 因此**不依赖**任何时刻的 <c>LocalContext.NetId</c>，也不怕 JIT 内联 —— 这正是 r199~r202 四轮都失效的原因所在。
/// </summary>
[TestFixture]
public class AutomatedSeatTransformVisualPolicyTests
{
    [Test]
    public void 托管席位的牌_一律不算我的牌()
    {
        // 两种情况都要跳过视觉：原版可能因为上下文被钉回牌主人而判 true，也可能因别的原因判 false。
        Assert.Multiple(() =>
        {
            Assert.That(AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(true, ownerIsAutomatedSeat: true), Is.False);
            Assert.That(AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(false, ownerIsAutomatedSeat: true), Is.False);
        });
    }

    [Test]
    public void 非托管席位的牌_完全按原版口径()
    {
        // 真人自己（或第三方席位）的牌：原版怎么判就怎么判，行为零变化。
        Assert.Multiple(() =>
        {
            Assert.That(AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(true, ownerIsAutomatedSeat: false), Is.True);
            Assert.That(AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(false, ownerIsAutomatedSeat: false), Is.False);
        });
    }

    [Test]
    public void 真值表_逐格期望成立()
    {
        foreach (bool baseResult in new[] { true, false })
        {
            foreach (bool ownerIsAutomated in new[] { true, false })
            {
                bool expected = baseResult && !ownerIsAutomated;
                Assert.That(
                    AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(baseResult, ownerIsAutomated),
                    Is.EqualTo(expected),
                    $"真值表漂移: base={baseResult}, ownerIsAutomated={ownerIsAutomated}");
            }
        }
    }
}
