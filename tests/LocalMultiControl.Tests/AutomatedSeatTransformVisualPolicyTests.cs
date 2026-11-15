using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 变换视觉门「这张牌算不算我的」托管席位判定（r203 / BUG-29 第五轮；r360 追加前台托管席位）。
///
/// 该判定由 transpiler 换掉原版视觉阶段那句 <c>LocalContext.IsMine(cardAdded2)</c> 的调用点，
/// 因此**不依赖**任何时刻的 <c>LocalContext.NetId</c>，也不怕 JIT 内联 —— 这正是 r199~r202 四轮都失效的原因所在。
///
/// r360（2026-11-15 用户实机报「瓦库打牌手牌视觉与实际不同步」）：原判据假设"托管席位的手牌 UI 不存在"，
/// 但**全瓦库**（连前台角色也托管）时屏幕上的手牌就是它那份 ⇒ 一律跳过视觉会让 UI 与数据永久不一致。
/// 现在按「后台 / 前台」分流。
/// </summary>
[TestFixture]
public class AutomatedSeatTransformVisualPolicyTests
{
    [Test]
    public void 后台托管席位的牌_一律不算我的牌()
    {
        // 后台托管席位的手牌 UI 不存在：原版视觉必然去前台手牌找节点并抛 ⇒ 一律不算。
        Assert.Multiple(() =>
        {
            Assert.That(
                AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(true, ownerIsAutomatedSeat: true, ownerIsForeground: false),
                Is.False);
            Assert.That(
                AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(false, ownerIsAutomatedSeat: true, ownerIsForeground: false),
                Is.False);
        });
    }

    [Test]
    public void 前台托管席位的牌_节点在场时按原版播视觉()
    {
        // 全瓦库：手牌 UI 就是它这份 ⇒ 与真人前台同口径，否则数据换了牌 UI 还画旧牌（r360 实机根因）。
        Assert.That(
            AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(
                true, ownerIsAutomatedSeat: true, ownerIsForeground: true, handNodesAvailable: true),
            Is.True);
    }

    [Test]
    public void 前台托管席位的牌_节点不在场时仍跳过视觉()
    {
        // 宁可丢一次换牌动画，也不要 Couldn't get hand node 抛穿调用链（BUG-29 老症状 = 牌停屏）。
        Assert.That(
            AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(
                true, ownerIsAutomatedSeat: true, ownerIsForeground: true, handNodesAvailable: false),
            Is.False);
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
                foreach (bool ownerIsForeground in new[] { true, false })
                {
                    foreach (bool handNodesAvailable in new[] { true, false })
                    {
                        bool expected = baseResult
                                        && (!ownerIsAutomated || (ownerIsForeground && handNodesAvailable));
                        Assert.That(
                            AutomatedSeatTransformVisualPolicy.ShouldTreatAsMine(
                                baseResult, ownerIsAutomated, ownerIsForeground, handNodesAvailable),
                            Is.EqualTo(expected),
                            $"真值表漂移: base={baseResult}, ownerIsAutomated={ownerIsAutomated}, "
                            + $"ownerIsForeground={ownerIsForeground}, handNodesAvailable={handNodesAvailable}");
                    }
                }
            }
        }
    }
}
