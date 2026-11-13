using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 宝箱遗物格摆位判据的纯逻辑测试（12 人扩展）。
///
/// 三条契约：① **4 个遗物及以内与原版逐值一致**（值抄自 `scenes/rooms/treasure_room.tscn` 的
/// `MultiplayerRelicHolder1..4`，含 scale=1）；② 5~12 个时按**方阵**铺开（列数 = ceil(sqrt(n)) 且 ≤4，
/// 每行件数平均，整块宽度落在原版面板内、**不会伸到屏幕左侧被玩家状态条挡住**）；
/// ③ 补出来的格子统一缩放（图标放大 2 倍后不压邻格）。
/// </summary>
[TestFixture]
public class TreasureRelicHolderLayoutPolicyTests
{
    // 原版场景真值：left, top, right, bottom
    private static readonly (float Left, float Top, float Right, float Bottom)[] VanillaBoxes =
    [
        (-148f, -180f, -12f, -44f),
        (12f, 44f, 148f, 180f),
        (-388f, -68f, -252f, 68f),
        (252f, -68f, 388f, 68f),
    ];

    [Test]
    public void 空遗物表返回空()
    {
        Assert.That(TreasureRelicHolderLayoutPolicy.Compute(0), Is.Empty);
        Assert.That(TreasureRelicHolderLayoutPolicy.Compute(-2), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void 四个及以内与原版逐值一致(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        Assert.That(placements, Has.Count.EqualTo(relicCount));
        for (int i = 0; i < relicCount; i++)
        {
            Assert.That(placements[i].Left, Is.EqualTo(VanillaBoxes[i].Left), $"holder{i + 1} left");
            Assert.That(placements[i].Top, Is.EqualTo(VanillaBoxes[i].Top), $"holder{i + 1} top");
            Assert.That(placements[i].Right, Is.EqualTo(VanillaBoxes[i].Right), $"holder{i + 1} right");
            Assert.That(placements[i].Bottom, Is.EqualTo(VanillaBoxes[i].Bottom), $"holder{i + 1} bottom");
            Assert.That(placements[i].Scale, Is.EqualTo(1f), $"holder{i + 1} 原版不缩放");
        }
    }

    [Test]
    public void 只有超过原版六套布局才需要补建()
    {
        // 0.113：treasure_room.tscn 自带 Layout1..Layout6（1~6 格），<= 6 一律交给原版。
        Assert.That(TreasureRelicHolderLayoutPolicy.VanillaLayoutCount, Is.EqualTo(6));
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(4), Is.False);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(6), Is.False);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(7), Is.True, "第 7 件起没有匹配布局 ⇒ 必须自己建");
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(12), Is.True);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(24), Is.True, "12 席 × 每人 2 件的最坏情况也要能排");
    }

    [TestCase(13)]
    [TestCase(16)]
    [TestCase(24)]
    public void 超过十二格时整体缩小仍落在面板内(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        Assert.That(placements, Has.Count.EqualTo(relicCount));
        foreach (TreasureRelicHolderPlacement placement in placements)
        {
            Assert.That(Math.Abs(placement.CenterX), Is.LessThanOrEqualTo(340f), $"x={placement.CenterX}");
            Assert.That(Math.Abs(placement.CenterY), Is.LessThanOrEqualTo(240f), $"y={placement.CenterY}");
            Assert.That(placement.Scale, Is.LessThanOrEqualTo(TreasureRelicHolderLayoutPolicy.BackfillScale));
            Assert.That(placement.Scale, Is.GreaterThan(0f));
        }
    }

    [TestCase(5)]
    [TestCase(7)]
    [TestCase(9)]
    [TestCase(12)]
    public void 五到十二格按方阵铺开(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        Assert.That(placements, Has.Count.EqualTo(relicCount));

        List<IGrouping<float, TreasureRelicHolderPlacement>> rows = placements
            .GroupBy(p => p.CenterY)
            .OrderBy(g => g.Key)
            .ToList();

        int expectedColumns = Math.Min(
            TreasureRelicHolderLayoutPolicy.MaxColumns,
            (int)Math.Ceiling(Math.Sqrt(relicCount)));
        int expectedRows = (int)Math.Ceiling(relicCount / (double)expectedColumns);

        Assert.That(rows, Has.Count.EqualTo(expectedRows), "行数 = ceil(n / 列数)");
        Assert.That(rows.Max(r => r.Count()), Is.LessThanOrEqualTo(expectedColumns), "每行不超过列数上限");
        Assert.That(rows.Min(r => r.Count()), Is.GreaterThanOrEqualTo(rows.Max(r => r.Count()) - 1),
            "每行件数要平均（不能出现只剩 1 个的末行）");

        foreach (IGrouping<float, TreasureRelicHolderPlacement> row in rows)
        {
            List<TreasureRelicHolderPlacement> seats = row.OrderBy(p => p.CenterX).ToList();

            // 同一行 y 必须一致、且等距
            for (int i = 1; i < seats.Count; i++)
            {
                Assert.That(seats[i].CenterX - seats[i - 1].CenterX,
                    Is.EqualTo(TreasureRelicHolderLayoutPolicy.GridPitchX).Within(0.01f), "同排必须等距");
            }

            // 逐行居中（左右对称）
            Assert.That(seats[0].CenterX, Is.EqualTo(-seats[^1].CenterX).Within(0.01f), "每行必须居中");
        }
    }

    [TestCase(5)]
    [TestCase(7)]
    [TestCase(9)]
    [TestCase(12)]
    public void 整块不伸出原版面板也不会压到左侧血条(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        // 原版最左的格子中心在 -320（holder3），原版面板 ±450 宽。
        // 用户实机反馈：第一版铺到 ±750 时左下角的遗物被玩家状态条挡住点不到 ⇒ 这里钉死 ±340 上限。
        foreach (TreasureRelicHolderPlacement placement in placements)
        {
            Assert.That(Math.Abs(placement.CenterX), Is.LessThanOrEqualTo(340f), $"x={placement.CenterX}");
            Assert.That(Math.Abs(placement.CenterY), Is.LessThanOrEqualTo(240f), $"y={placement.CenterY}");
        }
    }

    [TestCase(5)]
    [TestCase(12)]
    public void 补出来的格子统一缩放且保持原版宽高比(int relicCount)
    {
        foreach (TreasureRelicHolderPlacement placement in TreasureRelicHolderLayoutPolicy.Compute(relicCount))
        {
            Assert.That(placement.Scale, Is.EqualTo(TreasureRelicHolderLayoutPolicy.BackfillScale));
            float expected = TreasureRelicHolderLayoutPolicy.HolderSize * TreasureRelicHolderLayoutPolicy.BackfillScale;
            Assert.That(placement.Right - placement.Left, Is.EqualTo(expected).Within(0.01f));
            Assert.That(placement.Bottom - placement.Top, Is.EqualTo(expected).Within(0.01f));
        }
    }

    [Test]
    public void 十二人时是四列三行()
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(12);

        Assert.That(placements.GroupBy(p => p.CenterY).Count(), Is.EqualTo(3));
        Assert.That(placements.GroupBy(p => p.CenterY).All(g => g.Count() == 4), Is.True, "4 × 3 方阵");
        Assert.That(placements.Max(p => Math.Abs(p.CenterX)),
            Is.EqualTo(TreasureRelicHolderLayoutPolicy.GridPitchX * 1.5f).Within(0.01f), "最左/最右 = ±1.5 列距");
    }

    [Test]
    public void 七个时是三两二()
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(7);

        List<int> perRow = placements
            .GroupBy(p => p.CenterY)
            .OrderBy(g => g.Key)
            .Select(g => g.Count())
            .ToList();

        Assert.That(perRow, Is.EqualTo(new[] { 3, 2, 2 }));
    }

    [Test]
    public void 同一遗物数量两次计算完全一致()
    {
        Assert.That(TreasureRelicHolderLayoutPolicy.Compute(11), Is.EqualTo(TreasureRelicHolderLayoutPolicy.Compute(11)));
    }
}
