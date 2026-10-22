using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// ⑱ 战神血条（顶部状态条旁的血条面板）行规划纯逻辑。
/// </summary>
[TestFixture]
public class WarGodHpBadgePolicyTests
{
    private static HpBadgeEntry Pet(string name, int cur, int max, bool alive = true)
        => new(HpBadgeEntryKind.Pet, name, cur, max, alive);

    private static HpBadgeEntry WarGod(HellwarWarGodTier tier, int cur, int max, bool alive = true)
        => new(HpBadgeEntryKind.WarGod, WarGodHpBadgePolicy.DescribeWarGod(tier), cur, max, alive);

    [Test]
    public void 都没有_不产生任何行()
    {
        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry>(),
            new List<HpBadgeEntry>());

        Assert.That(rows, Is.Empty);
    }

    [Test]
    public void 召唤物文案与r86完全一致()
    {
        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry> { Pet("Osty", 12, 30) },
            new List<HpBadgeEntry>());

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Label, Is.EqualTo("Osty 12/30"));
        Assert.That(rows[0].Ratio, Is.EqualTo(0.4f).Within(0.001f));
        Assert.That(rows[0].IsOverflow, Is.False);
    }

    [Test]
    public void 战神文案带档位前缀()
    {
        Assert.That(WarGodHpBadgePolicy.DescribeWarGod(HellwarWarGodTier.Common), Is.EqualTo("战神·普通"));
        Assert.That(WarGodHpBadgePolicy.DescribeWarGod(HellwarWarGodTier.Rare), Is.EqualTo("战神·罕见"));
        Assert.That(WarGodHpBadgePolicy.DescribeWarGod(HellwarWarGodTier.Epic), Is.EqualTo("战神·稀有"));
        Assert.That(WarGodHpBadgePolicy.DescribeWarGod(HellwarWarGodTier.Ancient), Is.EqualTo("战神·先古"));
    }

    [Test]
    public void 未知档位兜底为未知()
    {
        Assert.That(WarGodHpBadgePolicy.DescribeTier((HellwarWarGodTier)99), Is.EqualTo("未知"));
    }

    [Test]
    public void 召唤物在前战神在后_顺序稳定()
    {
        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry> { Pet("Osty", 5, 10) },
            new List<HpBadgeEntry> { WarGod(HellwarWarGodTier.Ancient, 75, 75), WarGod(HellwarWarGodTier.Common, 40, 40) });

        Assert.That(rows, Has.Count.EqualTo(3));
        Assert.That(rows[0].Label, Is.EqualTo("Osty 5/10"));
        Assert.That(rows[1].Label, Is.EqualTo("战神·先古 75/75"));
        Assert.That(rows[2].Label, Is.EqualTo("战神·普通 40/40"));
    }

    [Test]
    public void 超过上限_压成折叠行且计数正确()
    {
        List<HpBadgeEntry> warGods = new();
        for (int i = 0; i < 9; i++)
        {
            warGods.Add(WarGod(HellwarWarGodTier.Ancient, 75, 75));
        }

        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry> { Pet("Osty", 5, 10) },
            warGods);

        // 默认上限 8 行 = 7 行内容 + 1 行折叠
        Assert.That(rows, Has.Count.EqualTo(WarGodHpBadgePolicy.DefaultMaxRows));
        Assert.That(rows[WarGodHpBadgePolicy.DefaultMaxRows - 1].IsOverflow, Is.True);
        Assert.That(rows[WarGodHpBadgePolicy.DefaultMaxRows - 1].Label, Is.EqualTo("还有 3 个（已折叠）"));
        Assert.That(rows[WarGodHpBadgePolicy.DefaultMaxRows - 1].Ratio, Is.Zero);
    }

    [Test]
    public void 恰好等于上限_不折叠()
    {
        List<HpBadgeEntry> warGods = new();
        for (int i = 0; i < 8; i++)
        {
            warGods.Add(WarGod(HellwarWarGodTier.Common, 10, 10));
        }

        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(new List<HpBadgeEntry>(), warGods);

        Assert.That(rows, Has.Count.EqualTo(8));
        Assert.That(rows.TrueForAll((row) => !row.IsOverflow), Is.True);
    }

    [Test]
    public void 上限为1或0时按下限2处理()
    {
        List<HpBadgeEntry> warGods = new()
        {
            WarGod(HellwarWarGodTier.Common, 10, 10),
            WarGod(HellwarWarGodTier.Rare, 10, 10),
            WarGod(HellwarWarGodTier.Ancient, 10, 10),
        };

        foreach (int maxRows in new[] { 0, 1 })
        {
            List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(new List<HpBadgeEntry>(), warGods, maxRows);

            Assert.That(rows, Has.Count.EqualTo(2));
            Assert.That(rows[0].IsOverflow, Is.False);
            Assert.That(rows[1].IsOverflow, Is.True);
            Assert.That(rows[1].Label, Is.EqualTo("还有 2 个（已折叠）"));
        }
    }

    [Test]
    public void 死亡行_比例为0且置灰标记()
    {
        List<HpBadgeRow> rows = WarGodHpBadgePolicy.BuildRows(
            new List<HpBadgeEntry>(),
            new List<HpBadgeEntry> { WarGod(HellwarWarGodTier.Ancient, 0, 75, alive: false) });

        Assert.That(rows, Has.Count.EqualTo(1));
        Assert.That(rows[0].Alive, Is.False);
        Assert.That(rows[0].Ratio, Is.Zero);
        Assert.That(rows[0].Label, Is.EqualTo("战神·先古 0/75"));
    }

    [Test]
    public void 血量异常时兜底_上限至少1且当前不为负()
    {
        HpBadgeEntry entry = Pet("Osty", -5, 0);

        Assert.That(entry.MaxHp, Is.EqualTo(1));
        Assert.That(entry.CurrentHp, Is.Zero);
        Assert.That(entry.Ratio, Is.Zero);
    }

    [Test]
    public void 名字为空时兜底为Pet()
    {
        HpBadgeEntry entry = new(HpBadgeEntryKind.Pet, "   ", 3, 5, alive: true);

        Assert.That(entry.Name, Is.EqualTo("Pet"));
    }
}
