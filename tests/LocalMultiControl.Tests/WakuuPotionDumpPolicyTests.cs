using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库自动用药的「遗物驱动腾栏」纯逻辑（2026-11-10 用户口径）：
/// 白兽雕像 / 娇嫩蕨草 / 石化蟾蜍 会让空药水栏产生价值 ⇒ 忽略规则表条件，按稀有度从低到高用掉。
/// </summary>
[TestFixture]
public class WakuuPotionDumpPolicyTests
{
    [Test]
    public void 没有相关遗物_计划不激活()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(false, false, false);

        Assert.That(plan.IsActive, Is.False);
        Assert.That(plan.Cause, Is.EqualTo(WakuuPotionDumpCause.None));
        Assert.That(WakuuPotionDumpPolicy.ResolveNeededSlots(plan, 3), Is.EqualTo(0));
    }

    [Test]
    public void 白兽雕像_只腾一个空栏_允许用到稀有()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(true, false, false);

        Assert.That(plan.IsActive, Is.True);
        Assert.That(plan.Cause, Is.EqualTo(WakuuPotionDumpCause.WhiteBeastStatue));
        Assert.That(plan.DumpAll, Is.False);
        Assert.That(WakuuPotionDumpPolicy.ResolveNeededSlots(plan, 3), Is.EqualTo(1));
        Assert.That(plan.MaxRarity, Is.EqualTo(WakuuPotionRarity.Rare));
    }

    [Test]
    public void 娇嫩蕨草_尽量腾空_只动普通与罕见()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(false, true, false);

        Assert.That(plan.Cause, Is.EqualTo(WakuuPotionDumpCause.DelicateFrond));
        Assert.That(plan.DumpAll, Is.True);
        Assert.That(plan.MaxRarity, Is.EqualTo(WakuuPotionRarity.Uncommon));
        // DumpAll ⇒ 目标 = 全部栏位（空到满栏为止）
        Assert.That(WakuuPotionDumpPolicy.ResolveNeededSlots(plan, 4), Is.EqualTo(4));
    }

    [Test]
    public void 石化蟾蜍_只动普通_腾一个空栏()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(false, false, true);

        Assert.That(plan.Cause, Is.EqualTo(WakuuPotionDumpCause.PetrifiedToad));
        Assert.That(plan.DumpAll, Is.False);
        Assert.That(plan.MaxRarity, Is.EqualTo(WakuuPotionRarity.Common));
        Assert.That(WakuuPotionDumpPolicy.ResolveNeededSlots(plan, 3), Is.EqualTo(1));
    }

    [Test]
    public void 三件齐全_取最强的蕨草档()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(true, true, true);

        Assert.That(plan.Cause, Is.EqualTo(WakuuPotionDumpCause.DelicateFrond));
        Assert.That(plan.DumpAll, Is.True);
    }

    [Test]
    public void 白兽雕像_三档都参与腾栏()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(true, false, false);

        // 优先级 = 稀有度序号（越小越先被用掉）
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Common), Is.EqualTo(1));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Uncommon), Is.EqualTo(2));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Rare), Is.EqualTo(3));
    }

    [Test]
    public void 白兽雕像_事件与token不参与腾栏()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(true, false, false);

        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Event), Is.EqualTo(0));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Token), Is.EqualTo(0));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.None), Is.EqualTo(0));
    }

    [Test]
    public void 石化蟾蜍_罕见与稀有都不参与腾栏()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(false, false, true);

        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Common), Is.EqualTo(1));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Uncommon), Is.EqualTo(0));
        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Rare), Is.EqualTo(0));
    }

    [Test]
    public void 计划不激活_任何稀有度都不参与()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPlan.Inactive;

        Assert.That(WakuuPotionDumpPolicy.RankOf(plan, WakuuPotionRarity.Common), Is.EqualTo(0));
        Assert.That(WakuuPotionDumpPolicy.NeedsDump(plan, 0, 1), Is.False);
    }

    [Test]
    public void 已经有空栏_就不再腾栏()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(true, false, false);

        Assert.That(WakuuPotionDumpPolicy.NeedsDump(plan, 1, 1), Is.False);
        Assert.That(WakuuPotionDumpPolicy.NeedsDump(plan, 0, 1), Is.True);
    }

    [Test]
    public void 优先级为0的候选_条件满足也不腾()
    {
        WakuuPotionDumpPlan plan = WakuuPotionDumpPolicy.Resolve(false, false, true);

        Assert.That(WakuuPotionDumpPolicy.ShouldDump(plan, 0, 0, 1), Is.False);
        Assert.That(WakuuPotionDumpPolicy.ShouldDump(plan, 1, 0, 1), Is.True);
    }

    [Test]
    public void 候选排序_稀有度升序_同档按栏位顺序()
    {
        List<(int Slot, int Rank)> candidates = new()
        {
            (2, 3),
            (0, 1),
            (3, 2),
            (1, 1),
        };

        IReadOnlyList<int> order = WakuuPotionDumpPolicy.OrderCandidates(candidates);

        Assert.That(order, Is.EqualTo(new[] { 0, 1, 3, 2 }));
    }

    [Test]
    public void 候选排序_忽略不参与的项()
    {
        List<(int Slot, int Rank)> candidates = new()
        {
            (0, 0),
            (1, 2),
            (2, 0),
        };

        IReadOnlyList<int> order = WakuuPotionDumpPolicy.OrderCandidates(candidates);

        Assert.That(order.Count, Is.EqualTo(1));
        Assert.That(order.Single(), Is.EqualTo(1));
    }
}
