using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库抓牌形态档位（用户 2026-11-19 口径）：
/// 小卡组 = 中卡组规则 + 上限降到 30；中卡组 = 三张都烂不抓 + ≥40 抬门槛 + 前三次不许跳；
/// 大卡组 = 永不跳 + 删牌只删垃圾。二/三层不单独判过渡牌，而是**提高抓取率要求**。
/// </summary>
[TestFixture]
public class WakuuCardPickPolicyTests
{
    [Test]
    public void 档位归一_未知回落到中卡组()
    {
        Assert.That(WakuuCardPickPolicy.NormalizeTier(null), Is.EqualTo(WakuuCardPickPolicy.NormalTier));
        Assert.That(WakuuCardPickPolicy.NormalizeTier(""), Is.EqualTo(WakuuCardPickPolicy.NormalTier));
        Assert.That(WakuuCardPickPolicy.NormalizeTier("BOGUS"), Is.EqualTo(WakuuCardPickPolicy.NormalTier));
        Assert.That(WakuuCardPickPolicy.NormalizeTier(" SMALL "), Is.EqualTo(WakuuCardPickPolicy.SmallTier));
        Assert.That(WakuuCardPickPolicy.ParseTier("big"), Is.EqualTo(WakuuCardPickTier.Big));
    }

    [Test]
    public void 抓取率要求_按幕递增()
    {
        double act1 = WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Normal, act: 1, deckSize: 10);
        double act2 = WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Normal, act: 2, deckSize: 10);
        double act3 = WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Normal, act: 3, deckSize: 10);

        Assert.That(act1, Is.EqualTo(0.30).Within(1e-9));
        Assert.That(act2, Is.EqualTo(0.40).Within(1e-9), "二层要求提高 10 个点");
        Assert.That(act3, Is.EqualTo(0.50).Within(1e-9), "三层再提高 10 个点");
    }

    [Test]
    public void 抓取率要求_牌组到上限再抬一档()
    {
        Assert.That(
            WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Normal, act: 1, deckSize: 39),
            Is.EqualTo(0.30).Within(1e-9));
        Assert.That(
            WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Normal, act: 1, deckSize: 40),
            Is.EqualTo(0.45).Within(1e-9), "中卡组 ≥40 抬门槛");

        Assert.That(
            WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Small, act: 1, deckSize: 29),
            Is.EqualTo(0.30).Within(1e-9));
        Assert.That(
            WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Small, act: 1, deckSize: 30),
            Is.EqualTo(0.45).Within(1e-9), "小卡组门槛提前到 30 张");
    }

    [Test]
    public void 抓取率要求_大卡组恒为零_永不跳()
    {
        Assert.That(
            WakuuCardPickPolicy.RequiredPickRate(WakuuCardPickTier.Big, act: 3, deckSize: 200),
            Is.EqualTo(0.0));

        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Big, act: 3, deckSize: 200, rewardOrdinal: 99,
                bestPickRate: 0.0, hasAnySignal: true, canSacrifice: true, canReroll: true),
            Is.EqualTo(WakuuCardTakeDecision.Take));
    }

    [Test]
    public void 无任何数据时不瞎跳_照抓()
    {
        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Small, act: 2, deckSize: 35, rewardOrdinal: 9,
                bestPickRate: 0.0, hasAnySignal: false, canSacrifice: false, canReroll: false),
            Is.EqualTo(WakuuCardTakeDecision.Take));
    }

    [Test]
    public void 中卡组前三次不跳_第四次起才判()
    {
        foreach (int ordinal in new[] { 1, 2, 3 })
        {
            Assert.That(
                WakuuCardPickPolicy.Decide(
                    WakuuCardPickTier.Normal, act: 1, deckSize: 20, rewardOrdinal: ordinal,
                    bestPickRate: 0.0, hasAnySignal: true, canSacrifice: false, canReroll: false),
                Is.EqualTo(WakuuCardTakeDecision.Take),
                $"第 {ordinal} 次卡牌奖励不该跳");
        }

        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Normal, act: 1, deckSize: 20, rewardOrdinal: 4,
                bestPickRate: 0.0, hasAnySignal: true, canSacrifice: false, canReroll: false),
            Is.EqualTo(WakuuCardTakeDecision.Skip));
    }

    [Test]
    public void 三张都烂时_献祭优先于重掷()
    {
        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Normal, act: 2, deckSize: 25, rewardOrdinal: 5,
                bestPickRate: 0.05, hasAnySignal: true, canSacrifice: true, canReroll: true),
            Is.EqualTo(WakuuCardTakeDecision.Sacrifice));

        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Normal, act: 2, deckSize: 25, rewardOrdinal: 5,
                bestPickRate: 0.05, hasAnySignal: true, canSacrifice: false, canReroll: true),
            Is.EqualTo(WakuuCardTakeDecision.Reroll));

        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Normal, act: 2, deckSize: 25, rewardOrdinal: 5,
                bestPickRate: 0.05, hasAnySignal: true, canSacrifice: false, canReroll: false),
            Is.EqualTo(WakuuCardTakeDecision.Skip));
    }

    [Test]
    public void 达标就抓_哪怕手里有遗物()
    {
        // 二层门槛 0.40，最高 0.55 ⇒ 抓，不该为了献祭/重掷放弃好牌。
        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Normal, act: 2, deckSize: 25, rewardOrdinal: 5,
                bestPickRate: 0.55, hasAnySignal: true, canSacrifice: true, canReroll: true),
            Is.EqualTo(WakuuCardTakeDecision.Take));
    }

    [Test]
    public void 小卡组没有前三次保护()
    {
        Assert.That(
            WakuuCardPickPolicy.Decide(
                WakuuCardPickTier.Small, act: 1, deckSize: 31, rewardOrdinal: 1,
                bestPickRate: 0.10, hasAnySignal: true, canSacrifice: false, canReroll: false),
            Is.EqualTo(WakuuCardTakeDecision.Skip));
    }

    [Test]
    public void 大卡组删牌只删垃圾()
    {
        Assert.That(WakuuCardPickPolicy.IsRemovalJunkOnly(WakuuCardPickTier.Big), Is.True);
        Assert.That(WakuuCardPickPolicy.IsRemovalJunkOnly(WakuuCardPickTier.Normal), Is.False);
        Assert.That(WakuuCardPickPolicy.IsRemovalJunkOnly(WakuuCardPickTier.Small), Is.False);

        foreach (WakuuCardKind junk in new[]
                 {
                     WakuuCardKind.Curse,
                     WakuuCardKind.Status,
                     WakuuCardKind.BasicStrike,
                     WakuuCardKind.BasicDefend,
                 })
        {
            Assert.That(WakuuCardPickPolicy.IsRemovableInJunkOnly(junk), Is.True, $"{junk} 应可删");
        }

        foreach (WakuuCardKind keep in new[]
                 {
                     WakuuCardKind.Other,
                     WakuuCardKind.Quest,
                     WakuuCardKind.Sly,
                 })
        {
            Assert.That(WakuuCardPickPolicy.IsRemovableInJunkOnly(keep), Is.False, $"{keep} 不该删");
        }
    }
}
