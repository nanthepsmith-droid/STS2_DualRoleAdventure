using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 宝箱遗物格摆位判据的纯逻辑测试（12 人扩展）。
///
/// 两条契约：① **4 个遗物及以内与原版逐值一致**（值抄自 `scenes/rooms/treasure_room.tscn` 的
/// `MultiplayerRelicHolder1..4`），② 5~12 个时格子必须全部落在屏幕内、同排等距、互不重叠。
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
        }
    }

    [Test]
    public void 只有超过四格才需要补建()
    {
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(4), Is.False);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(5), Is.True);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(12), Is.True);
        Assert.That(TreasureRelicHolderLayoutPolicy.NeedsBackfill(13), Is.False, "超过席位上限不接（走原版 4 格 + 复制兜底）");
    }

    [TestCase(5)]
    [TestCase(6)]
    public void 五个六个排一排且等距(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        Assert.That(placements, Has.Count.EqualTo(relicCount));
        Assert.That(placements.Select(p => p.CenterY).Distinct().Count(), Is.EqualTo(1), "一排时 y 必须一致");
        for (int i = 1; i < placements.Count; i++)
        {
            Assert.That(placements[i].CenterX - placements[i - 1].CenterX,
                Is.EqualTo(TreasureRelicHolderLayoutPolicy.GridPitchX).Within(0.01f));
        }
    }

    [TestCase(7)]
    [TestCase(9)]
    [TestCase(12)]
    public void 七个以上排两排且都在屏幕内(int relicCount)
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(relicCount);

        Assert.That(placements, Has.Count.EqualTo(relicCount));
        Assert.That(placements.Select(p => p.CenterY).Distinct().Count(), Is.EqualTo(2), "必须是两排");

        int firstRow = placements.Count(p => p.CenterY < 0);
        Assert.That(firstRow, Is.EqualTo(Math.Min(TreasureRelicHolderLayoutPolicy.MaxPerRow, relicCount)));
        Assert.That(placements.Count(p => p.CenterY > 0), Is.EqualTo(relicCount - firstRow));

        foreach (IGrouping<float, TreasureRelicHolderPlacement> row in placements.GroupBy(p => p.CenterY))
        {
            List<TreasureRelicHolderPlacement> seats = row.OrderBy(p => p.CenterX).ToList();
            for (int i = 1; i < seats.Count; i++)
            {
                Assert.That(seats[i].CenterX - seats[i - 1].CenterX,
                    Is.EqualTo(TreasureRelicHolderLayoutPolicy.GridPitchX).Within(0.01f), "同排必须等距");
            }

            // 1920 宽（中心 ±960）、1080 高；牌桌区域留边 ⇒ ±760 / ±200 必须装得下
            Assert.That(Math.Abs(seats[0].CenterX), Is.LessThan(760f), "最左不能跑出屏幕");
            Assert.That(Math.Abs(seats[^1].CenterX), Is.LessThan(760f), "最右不能跑出屏幕");
        }
    }

    [TestCase(5)]
    [TestCase(12)]
    public void 每个格子都是原版尺寸(int relicCount)
    {
        foreach (TreasureRelicHolderPlacement placement in TreasureRelicHolderLayoutPolicy.Compute(relicCount))
        {
            Assert.That(placement.Right - placement.Left, Is.EqualTo(TreasureRelicHolderLayoutPolicy.HolderSize));
            Assert.That(placement.Bottom - placement.Top, Is.EqualTo(TreasureRelicHolderLayoutPolicy.HolderSize));
        }
    }

    [Test]
    public void 末排不满时逐排居中()
    {
        IReadOnlyList<TreasureRelicHolderPlacement> placements = TreasureRelicHolderLayoutPolicy.Compute(7);

        List<TreasureRelicHolderPlacement> secondRow = placements.Where(p => p.CenterY > 0).ToList();
        Assert.That(secondRow, Has.Count.EqualTo(1));
        Assert.That(secondRow[0].CenterX, Is.EqualTo(0f).Within(0.01f), "只有一个时居中");
    }
}
