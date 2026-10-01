using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战斗内"切换操控角色"的目标选择数学（R4 第二刀提纯）测试。
///
/// 这些边界原先只能靠实机看"切人对不对"：找不到当前席位时退化到从头开始、环形顺序怎么绕、
/// 少于 2 席时不出目标 —— 现在逐条钉死，避免以后改切人逻辑时悄悄改变顺序。
/// </summary>
[TestFixture]
public class SwitchTargetPolicyTests
{
    private static readonly List<ulong> ThreeSeats = new() { 10, 11, 12 };

    [Test]
    public void 下标解析_命中则给下标_找不到按0()
    {
        Assert.That(SwitchTargetPolicy.ResolveIndex(ThreeSeats, 11), Is.EqualTo(1));
        Assert.That(SwitchTargetPolicy.ResolveIndex(ThreeSeats, 10), Is.EqualTo(0));
        Assert.That(SwitchTargetPolicy.ResolveIndex(ThreeSeats, 99), Is.EqualTo(0), "找不到当前席位 ⇒ 按 0（与原实现一致）");
        Assert.That(SwitchTargetPolicy.ResolveIndex(new List<ulong>(), 10), Is.EqualTo(0), "空列表不抛异常");
    }

    [Test]
    public void 单步_下一席与上一席_含回绕()
    {
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 10, true), Is.EqualTo(11UL));
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 12, true), Is.EqualTo(10UL), "末席往后回绕到首席");
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 10, false), Is.EqualTo(12UL), "首席往前回绕到末席");
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 11, false), Is.EqualTo(10UL));
    }

    [Test]
    public void 单步_当前席位不在列表时以首席为基准()
    {
        // 原实现：index 找不到按 0 ⇒ 下一席 = 列表第 2 个元素
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 99, true), Is.EqualTo(11UL));
        Assert.That(SwitchTargetPolicy.SingleStep(ThreeSeats, 99, false), Is.EqualTo(12UL));
    }

    [Test]
    public void 单步_少于两席不出目标()
    {
        Assert.That(SwitchTargetPolicy.SingleStep(new List<ulong> { 10 }, 10, true), Is.Null);
        Assert.That(SwitchTargetPolicy.SingleStep(new List<ulong>(), 10, true), Is.Null);
        Assert.That(SwitchTargetPolicy.SingleStep(null!, 10, true), Is.Null);
    }

    [Test]
    public void 环形候选顺序_从当前之后绕一圈_不含当前席位()
    {
        Assert.That(SwitchTargetPolicy.CandidateOrder(ThreeSeats, 10), Is.EqualTo(new ulong[] { 11, 12 }));
        Assert.That(SwitchTargetPolicy.CandidateOrder(ThreeSeats, 11), Is.EqualTo(new ulong[] { 12, 10 }));
        Assert.That(SwitchTargetPolicy.CandidateOrder(ThreeSeats, 12), Is.EqualTo(new ulong[] { 10, 11 }));
        Assert.That(SwitchTargetPolicy.CandidateOrder(new List<ulong> { 10, 11 }, 10), Is.EqualTo(new ulong[] { 11 }));
    }

    [Test]
    public void 环形候选顺序_当前席位不在列表时首席被当作当前而排除()
    {
        // 与原实现逐字等价：index 找不到按 0 ⇒ 候选从下标 1 开始（首席被"当作当前"跳掉）
        Assert.That(SwitchTargetPolicy.CandidateOrder(ThreeSeats, 99), Is.EqualTo(new ulong[] { 11, 12 }));
    }

    [Test]
    public void 环形候选顺序_少于两席为空()
    {
        Assert.That(SwitchTargetPolicy.CandidateOrder(new List<ulong> { 10 }, 10), Is.Empty);
        Assert.That(SwitchTargetPolicy.CandidateOrder(new List<ulong>(), 10), Is.Empty);
        Assert.That(SwitchTargetPolicy.CandidateOrder(null!, 10), Is.Empty);
    }
}
