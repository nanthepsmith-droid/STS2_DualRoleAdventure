using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「瓦库的爹」三张牌**效果**的纯判定测试（r228 实装）。
///
/// 口径（提案 §5.1）：
/// 我挡 = 只能点队友（不能点自己）；你攻 = 只影响瓦库席位；合体 = 只能点瓦库队友，
/// 混抽在两个抽牌堆之间等概率、混弃在两个弃牌堆之间等概率。
/// </summary>
[TestFixture]
public class WakuuDaddyCardPolicyTests
{
    [Test]
    public void 我挡_不能点自己()
    {
        Assert.That(WakuuDaddyCardPolicy.CanShieldTarget(targetIsSelf: true), Is.False);
        Assert.That(WakuuDaddyCardPolicy.CanShieldTarget(targetIsSelf: false), Is.True);
    }

    [Test]
    public void 合体_只认瓦库队友()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuDaddyCardPolicy.CanMergeTarget(targetIsWakuuSeat: true, targetIsSelf: false), Is.True);
            // 点在自己身上：AnyAlly 允许，但"合体自己"没有意义
            Assert.That(WakuuDaddyCardPolicy.CanMergeTarget(targetIsWakuuSeat: true, targetIsSelf: true), Is.False);
            // 点在真人队友身上：不发动（只抽 1 张）
            Assert.That(WakuuDaddyCardPolicy.CanMergeTarget(targetIsWakuuSeat: false, targetIsSelf: false), Is.False);
        });
    }

    [Test]
    public void 合体混抽_两边都有牌时两个来源都在()
    {
        IReadOnlyList<int> sources = WakuuDaddyCardPolicy.ResolveMergeDrawSources(true, true);
        Assert.That(sources, Is.EqualTo(new[] { WakuuDaddyCardPolicy.MergeDrawOwn, WakuuDaddyCardPolicy.MergeDrawOther }));
    }

    [Test]
    public void 合体混抽_只有一边有牌时只留那一边()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuDaddyCardPolicy.ResolveMergeDrawSources(true, false),
                Is.EqualTo(new[] { WakuuDaddyCardPolicy.MergeDrawOwn }));
            Assert.That(
                WakuuDaddyCardPolicy.ResolveMergeDrawSources(false, true),
                Is.EqualTo(new[] { WakuuDaddyCardPolicy.MergeDrawOther }));
            Assert.That(WakuuDaddyCardPolicy.ResolveMergeDrawSources(false, false), Is.Empty);
        });
    }

    [Test]
    public void 合体混弃_掷骰各半()
    {
        // 0/1 掷骰：偶数留在自己弃牌堆，奇数进瓦库弃牌堆
        Assert.Multiple(() =>
        {
            Assert.That(WakuuDaddyCardPolicy.ShouldDiscardToOther(0), Is.False);
            Assert.That(WakuuDaddyCardPolicy.ShouldDiscardToOther(1), Is.True);
            Assert.That(WakuuDaddyCardPolicy.ShouldDiscardToOther(2), Is.False);
            Assert.That(WakuuDaddyCardPolicy.ShouldDiscardToOther(3), Is.True);
        });
    }
}
