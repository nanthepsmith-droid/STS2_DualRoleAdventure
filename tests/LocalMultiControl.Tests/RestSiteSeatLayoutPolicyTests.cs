using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// BUG-14（火堆最多只显示 4 个玩家）摆位判据的纯逻辑测试。
///
/// 两条契约必须钉死：
/// ① **4 人及以下与原版逐值一致**（值直接抄自 `scenes/rooms/rest_site_room.tscn` 的
///    `BgContainer/Character_1..4` 的 `offset_left/top` 与 `scale`）—— 玩家不超过 4 个时
///    我们**一个值都不动**；
/// ② 5 人以上补建的席位必须落在屏幕内、同排等距、且不会把 scale 缩到看不见。
/// </summary>
[TestFixture]
public class RestSiteSeatLayoutPolicyTests
{
    // 原版场景真值（Character_1 / Character_2 前排，Character_3 / Character_4 后排）
    private static readonly (float X, float Y)[] VanillaXy =
    [
        (624.775f, 641.775f),
        (1238.78f, 648.775f),
        (749.775f, 584.775f),
        (1091.78f, 582.775f),
    ];

    [Test]
    public void 空席位表返回空()
    {
        Assert.That(RestSiteSeatLayoutPolicy.Compute(0), Is.Empty);
        Assert.That(RestSiteSeatLayoutPolicy.Compute(-3), Is.Empty);
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void 四人及以下与原版逐值一致(int playerCount)
    {
        IReadOnlyList<RestSiteSeatPlacement> placements = RestSiteSeatLayoutPolicy.Compute(playerCount);

        Assert.That(placements, Has.Count.EqualTo(playerCount));
        for (int seat = 0; seat < playerCount; seat++)
        {
            Assert.That(placements[seat].OffsetX, Is.EqualTo(VanillaXy[seat].X).Within(0.001f), $"seat={seat} x");
            Assert.That(placements[seat].OffsetY, Is.EqualTo(VanillaXy[seat].Y).Within(0.001f), $"seat={seat} y");
            Assert.That(placements[seat].Scale, Is.EqualTo(RestSiteSeatLayoutPolicy.VanillaScale), $"seat={seat} scale");
            Assert.That(placements[seat].IsFrontRow, Is.EqualTo(seat < 2), $"seat={seat} 排（原版前 2 后 2）");
        }
    }

    [Test]
    public void 只有超过四人才需要补容器()
    {
        Assert.That(RestSiteSeatLayoutPolicy.NeedsBackfill(4), Is.False);
        Assert.That(RestSiteSeatLayoutPolicy.NeedsBackfill(5), Is.True);
        Assert.That(RestSiteSeatLayoutPolicy.NeedsBackfill(12), Is.True);
    }

    [Test]
    public void 五人时前排三个后排两个且仍是原尺寸()
    {
        IReadOnlyList<RestSiteSeatPlacement> placements = RestSiteSeatLayoutPolicy.Compute(5);

        Assert.That(placements, Has.Count.EqualTo(5));
        Assert.That(placements.Count(p => p.IsFrontRow), Is.EqualTo(3));
        Assert.That(placements.Count(p => !p.IsFrontRow), Is.EqualTo(2));

        // 三席一排仍用原版间距（614.005），所以 scale 不需要缩
        Assert.That(placements[0].Scale, Is.EqualTo(RestSiteSeatLayoutPolicy.VanillaScale));

        Assert.That(placements[1].OffsetX - placements[0].OffsetX,
            Is.EqualTo(RestSiteSeatLayoutPolicy.BasePitch).Within(0.01f));
        Assert.That(placements[2].OffsetX - placements[1].OffsetX,
            Is.EqualTo(RestSiteSeatLayoutPolicy.BasePitch).Within(0.01f));
    }

    [TestCase(5)]
    [TestCase(8)]
    [TestCase(12)]
    public void 每排等距且不跑出屏幕(int playerCount)
    {
        IReadOnlyList<RestSiteSeatPlacement> placements = RestSiteSeatLayoutPolicy.Compute(playerCount);

        Assert.That(placements, Has.Count.EqualTo(playerCount));

        foreach (IGrouping<bool, RestSiteSeatPlacement> row in placements.GroupBy(p => p.IsFrontRow))
        {
            List<RestSiteSeatPlacement> seats = row.OrderBy(p => p.OffsetX).ToList();
            Assert.That(seats, Has.Count.GreaterThan(0));
            Assert.That(seats.Select(p => p.OffsetY).Distinct().Count(), Is.EqualTo(1), "同一排 y 必须一致");

            for (int i = 1; i < seats.Count; i++)
            {
                Assert.That(seats[i].OffsetX - seats[i - 1].OffsetX,
                    Is.EqualTo(seats[1].OffsetX - seats[0].OffsetX).Within(0.01f), "同排必须等距");
            }

            // 跨度不超过上限（1920 宽上留边）；offsets 空间里 ±960 是屏幕边缘
            Assert.That(seats[^1].OffsetX - seats[0].OffsetX,
                Is.LessThanOrEqualTo(RestSiteSeatLayoutPolicy.MaxRowSpan + 0.01f));
            Assert.That(seats[0].OffsetX, Is.GreaterThan(-960f), "最左不能跑出屏幕");
            Assert.That(seats[^1].OffsetX, Is.LessThan(1894f), "最右不能跑出屏幕");
        }
    }

    [Test]
    public void 十二人时缩放到下限之上且前后排分明()
    {
        IReadOnlyList<RestSiteSeatPlacement> placements = RestSiteSeatLayoutPolicy.Compute(12);

        Assert.That(placements.Count(p => p.IsFrontRow), Is.EqualTo(6));
        Assert.That(placements.Count(p => !p.IsFrontRow), Is.EqualTo(6));
        Assert.That(placements[0].Scale, Is.LessThan(RestSiteSeatLayoutPolicy.VanillaScale));
        Assert.That(placements[0].Scale, Is.GreaterThanOrEqualTo(RestSiteSeatLayoutPolicy.MinScale));

        // 后排（远处）画在更靠上的位置
        foreach (RestSiteSeatPlacement seat in placements)
        {
            Assert.That(seat.OffsetY,
                Is.EqualTo(seat.IsFrontRow ? RestSiteSeatLayoutPolicy.FrontRowY : RestSiteSeatLayoutPolicy.BackRowY));
        }

        Assert.That(RestSiteSeatLayoutPolicy.BackRowY, Is.LessThan(RestSiteSeatLayoutPolicy.FrontRowY));
    }

    [Test]
    public void 同一玩家数量两次计算完全一致()
    {
        IReadOnlyList<RestSiteSeatPlacement> first = RestSiteSeatLayoutPolicy.Compute(9);
        IReadOnlyList<RestSiteSeatPlacement> second = RestSiteSeatLayoutPolicy.Compute(9);

        Assert.That(second, Is.EqualTo(first));
    }
}
