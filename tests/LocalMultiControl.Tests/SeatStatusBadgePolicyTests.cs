using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// QOL-3：顶部玩家状态条上的「污染 / 沙坑」状态角标（纯逻辑）。
/// </summary>
[TestFixture]
public class SeatStatusBadgePolicyTests
{
    [Test]
    public void 都没有_不产生任何行()
    {
        Assert.That(SeatStatusBadgePolicy.BuildLabels(0, 0), Is.Empty);
    }

    [Test]
    public void 只有污染()
    {
        List<string> labels = SeatStatusBadgePolicy.BuildLabels(3, 0);

        Assert.That(labels, Has.Count.EqualTo(1));
        Assert.That(labels[0], Is.EqualTo("污染 3"));
    }

    [Test]
    public void 只有沙坑()
    {
        List<string> labels = SeatStatusBadgePolicy.BuildLabels(0, 5);

        Assert.That(labels, Has.Count.EqualTo(1));
        Assert.That(labels[0], Is.EqualTo("沙坑 5"));
    }

    [Test]
    public void 两个都有_顺序固定为污染在前沙坑在后()
    {
        List<string> labels = SeatStatusBadgePolicy.BuildLabels(2, 4);

        Assert.That(labels, Has.Count.EqualTo(2));
        Assert.That(labels[0], Is.EqualTo("污染 2"));
        Assert.That(labels[1], Is.EqualTo("沙坑 4"));
    }

    [Test]
    public void 负数按零处理_不显示()
    {
        Assert.That(SeatStatusBadgePolicy.BuildLabels(-1, -8), Is.Empty);
    }

    [Test]
    public void 行规划_状态行追加在召唤物与战神之后_且不画血条()
    {
        List<HpBadgeEntry> pets = new()
        {
            new HpBadgeEntry(HpBadgeEntryKind.Pet, "Osty", 12, 30, true),
        };
        List<HpBadgeEntry> warGods = new()
        {
            new HpBadgeEntry(HpBadgeEntryKind.WarGod, "战神·普通", 40, 40, true),
        };

        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            pets,
            warGods,
            WarGodHpBadgePolicy.DefaultMaxRows,
            SeatStatusBadgePolicy.BuildLabels(3, 5));

        Assert.That(rows, Has.Count.EqualTo(4));
        Assert.That(rows[0].Label, Is.EqualTo("Osty 12/30"));
        Assert.That(rows[1].Label, Is.EqualTo("战神·普通 40/40"));
        Assert.That(rows[2].Label, Is.EqualTo("污染 3"));
        Assert.That(rows[2].IsStatus, Is.True);
        Assert.That(rows[2].Ratio, Is.Zero);
        Assert.That(rows[3].Label, Is.EqualTo("沙坑 5"));
        Assert.That(rows[3].IsStatus, Is.True);
    }

    [Test]
    public void 行规划_只有状态角标也能显示()
    {
        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry>(),
            new List<HpBadgeEntry>(),
            WarGodHpBadgePolicy.DefaultMaxRows,
            SeatStatusBadgePolicy.BuildLabels(1, 0));

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Label, Is.EqualTo("污染 1"));
        Assert.That(rows[0].IsStatus, Is.True);
        Assert.That(rows[0].IsOverflow, Is.False);
    }

    [Test]
    public void 行规划_状态行不参与折叠计数()
    {
        List<HpBadgeEntry> warGods = new();
        for (int i = 0; i < 9; i++)
        {
            warGods.Add(new HpBadgeEntry(HpBadgeEntryKind.WarGod, "战神·普通", 10, 10, true));
        }

        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry>(),
            warGods,
            WarGodHpBadgePolicy.DefaultMaxRows,
            SeatStatusBadgePolicy.BuildLabels(2, 3));

        // 8 行（7 内容 + 1 折叠）+ 2 行状态
        Assert.That(rows, Has.Count.EqualTo(WarGodHpBadgePolicy.DefaultMaxRows + 2));
        Assert.That(rows[WarGodHpBadgePolicy.DefaultMaxRows - 1].IsOverflow, Is.True);
        Assert.That(rows[^2].Label, Is.EqualTo("污染 2"));
        Assert.That(rows[^1].Label, Is.EqualTo("沙坑 3"));
    }
}
