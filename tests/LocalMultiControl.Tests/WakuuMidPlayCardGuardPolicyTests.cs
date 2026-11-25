#nullable enable
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r405：选牌候选里剔除「正在打出的那张牌」（避免变化/消耗/丢弃选到它自己）。
/// </summary>
[TestFixture]
public class WakuuMidPlayCardGuardPolicyTests
{
    [Test]
    public void 打出中的那张会被剔除且其它保持原序()
    {
        List<string> candidates = new() { "strike", "begone", "defend" };

        List<string> kept = WakuuMidPlayCardGuardPolicy.WithoutMidPlayCards(
            candidates,
            candidate => candidate == "begone",
            out List<string> removed);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(new[] { "strike", "defend" }));
            Assert.That(removed, Is.EqualTo(new[] { "begone" }));
        });
    }

    [Test]
    public void 没有打出中的牌时原样返回()
    {
        List<int> kept = WakuuMidPlayCardGuardPolicy.WithoutMidPlayCards(
            new[] { 1, 2, 3 },
            _ => false,
            out List<int> removed);

        Assert.Multiple(() =>
        {
            Assert.That(kept, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(removed, Is.Empty);
        });
    }

    [Test]
    public void 空候选或null不抛()
    {
        Assert.That(
            WakuuMidPlayCardGuardPolicy.WithoutMidPlayCards(new int[0], _ => true, out List<int> r1),
            Is.Empty);
        Assert.That(r1, Is.Empty);

        Assert.That(
            WakuuMidPlayCardGuardPolicy.WithoutMidPlayCards<int>(null, _ => true, out List<int> r2),
            Is.Empty);
        Assert.That(r2, Is.Empty);
    }
}
