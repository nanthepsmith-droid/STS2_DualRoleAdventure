using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 极速模式宝箱「每人 2 件」的派发判据（r352）。
///
/// 契约：① 每人最多 2 件；② 玩家点过的优先；
/// ③ 抢同一件按席位顺序先到先得（本地多控同机，不要石头剪刀布）；
/// ④ 有空位就用没人选的遗物补齐，遗物不够就少拿 —— 不凭空造。
/// </summary>
[TestFixture]
public class BlitzTreasurePickPolicyTests
{
    [Test]
    public void 两人各点两件时每人拿两件()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 2,
            relicCount: 4,
            firstPhasePicks: new int?[] { 0, 1 },
            secondPhasePicks: new int?[] { 2, 3 });

        Assert.That(assignments, Has.Count.EqualTo(4));
        Assert.That(CountOf(assignments, 0), Is.EqualTo(2));
        Assert.That(CountOf(assignments, 1), Is.EqualTo(2));
        Assert.That(assignments.All(a => a.FromPick), Is.True);
        Assert.That(Owned(assignments, 0), Is.EquivalentTo(new[] { 0, 2 }));
        Assert.That(Owned(assignments, 1), Is.EquivalentTo(new[] { 1, 3 }));
    }

    [Test]
    public void 抢同一件时按席位顺序先到先得并给没抢到的补位()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 2,
            relicCount: 4,
            firstPhasePicks: new int?[] { 0, 0 },
            secondPhasePicks: new int?[] { 1, 1 });

        Assert.That(CountOf(assignments, 0), Is.EqualTo(2));
        Assert.That(CountOf(assignments, 1), Is.EqualTo(2));
        Assert.That(Owned(assignments, 0), Is.EquivalentTo(new[] { 0, 1 }), "先到先得");
        Assert.That(Owned(assignments, 1), Is.EquivalentTo(new[] { 2, 3 }), "没抢到的用剩余遗物补齐");
        Assert.That(assignments.Count(a => a.FromPick), Is.EqualTo(2));
    }

    [Test]
    public void 有人跳过时用没人选的遗物补到两件()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 2,
            relicCount: 4,
            firstPhasePicks: new int?[] { 0, null },
            secondPhasePicks: new int?[] { null, 2 });

        Assert.That(CountOf(assignments, 0), Is.EqualTo(2), "席位 0 只点了 1 次，要补到 2");
        Assert.That(CountOf(assignments, 1), Is.EqualTo(2), "席位 1 只点了 1 次，要补到 2");
        Assert.That(Owned(assignments, 0), Contains.Item(0));
        Assert.That(Owned(assignments, 1), Contains.Item(2));
        Assert.That(assignments.Count(a => !a.FromPick), Is.EqualTo(2));
    }

    [Test]
    public void 遗物不够时靠后的席位少拿而不重复派发()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 3,
            relicCount: 2,
            firstPhasePicks: new int?[] { 0, 1, null },
            secondPhasePicks: new int?[] { null, null, null });

        Assert.That(assignments, Has.Count.EqualTo(2));
        Assert.That(assignments.Select(a => a.RelicIndex).Distinct().Count(), Is.EqualTo(2), "同一件遗物只能派一次");
        Assert.That(CountOf(assignments, 2), Is.EqualTo(0));
    }

    [Test]
    public void 每人最多两件()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 1,
            relicCount: 6,
            firstPhasePicks: new int?[] { 0 },
            secondPhasePicks: new int?[] { 1 });

        Assert.That(assignments, Has.Count.EqualTo(2));
        Assert.That(assignments.All(a => a.PlayerSlot == 0), Is.True);
    }

    [Test]
    public void 没派出去的遗物会被列出来给跳过结果()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 1,
            relicCount: 5,
            firstPhasePicks: new int?[] { 0 },
            secondPhasePicks: new int?[] { 1 });

        List<int> unused = BlitzTreasurePickPolicy.ResolveUnusedRelicIndexes(5, assignments);

        Assert.That(unused, Is.EquivalentTo(new[] { 2, 3, 4 }));
    }

    [Test]
    public void 结果按遗物序号排序保证同手抓取的间隔()
    {
        List<BlitzTreasureAssignment> assignments = BlitzTreasurePickPolicy.BuildAssignments(
            playerCount: 2,
            relicCount: 4,
            firstPhasePicks: new int?[] { 3, 0 },
            secondPhasePicks: new int?[] { 2, 1 });

        Assert.That(assignments.Select(a => a.RelicIndex), Is.Ordered);
    }

    private static int CountOf(List<BlitzTreasureAssignment> assignments, int slot)
    {
        return assignments.Count(a => a.PlayerSlot == slot);
    }

    private static IEnumerable<int> Owned(List<BlitzTreasureAssignment> assignments, int slot)
    {
        return assignments.Where(a => a.PlayerSlot == slot).Select(a => a.RelicIndex);
    }
}
