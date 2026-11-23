using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 变化类选牌的「结果形态」判据（2026-11-23）：固定目标 ⇒ 先变烂牌；随机 / 未登记 ⇒ 先变打击防御。
///
/// 登记表必须逐条对照反编译源码（"变完是不是 CreateCard&lt;T&gt;() 造的固定牌"），
/// 所以这里用显式断言钉住每个已知来源，避免日后被顺手改写成"全部随机"。
/// </summary>
[TestFixture]
public class WakuuTransformTargetPolicyTests
{
    // 手牌 / 抽牌堆选牌后固定变成某一张牌的来源（Begone=仆从打击、Charge=仆从俯冲弹、Seance=魂）
    [TestCase("Begone")]
    [TestCase("Charge")]
    [TestCase("Seance")]
    public void 已知固定目标来源判定为固定(string sourceTypeName)
    {
        Assert.That(WakuuTransformTargetPolicy.IsFixedTarget(sourceTypeName), Is.True);
        Assert.That(WakuuTransformTargetPolicy.Resolve(sourceTypeName), Is.EqualTo(WakuuTransformTargetKind.Fixed));
    }

    [Test]
    public void 熵是随机变化_不能被当成固定目标()
    {
        // EntropyPower：回合开始手牌选 N 张 → TransformToRandom（同角色随机）
        Assert.That(WakuuTransformTargetPolicy.IsFixedTarget("EntropyPower"), Is.False);
        Assert.That(
            WakuuTransformTargetPolicy.Resolve("EntropyPower"),
            Is.EqualTo(WakuuTransformTargetKind.Random));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("SomeModTransformCard")]
    [TestCase("begone")] // 大小写不敏感**不**做：mod 卡可能只想精确登记
    public void 未登记来源一律按未知处理(string? sourceTypeName)
    {
        Assert.That(WakuuTransformTargetPolicy.IsFixedTarget(sourceTypeName), Is.False);
        Assert.That(
            WakuuTransformTargetPolicy.Resolve(sourceTypeName),
            Is.EqualTo(WakuuTransformTargetKind.Unknown));
    }

    [Test]
    public void 前后空白被裁掉()
    {
        Assert.That(WakuuTransformTargetPolicy.IsFixedTarget(" Begone "), Is.True);
    }
}
