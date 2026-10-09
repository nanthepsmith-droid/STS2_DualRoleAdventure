using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤（原 ④「地狱战神」，#21）Phase 1 纯逻辑 <see cref="HellwarSummonPolicy"/> 的用例：
/// 遗物发放判据 / 强度→成本（R5·D7）/ 是否可召唤（R6）/ 【分裂精神】扣血档位与 clamp（R2b·D13）。
/// </summary>
[TestFixture]
public class HellwarSummonPolicyTests
{
    [Test]
    public void 耳环只发给真人席位()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ShouldGrantEarring(featureEnabled: true, isLocalSessionSeat: true, isWakuuSeat: false), Is.True);
            Assert.That(HellwarSummonPolicy.ShouldGrantEarring(featureEnabled: true, isLocalSessionSeat: true, isWakuuSeat: true), Is.False,
                "瓦库席位不发（D8：首版瓦库不能玩地狱战神）");
            Assert.That(HellwarSummonPolicy.ShouldGrantEarring(featureEnabled: true, isLocalSessionSeat: false, isWakuuSeat: false), Is.False);
            Assert.That(HellwarSummonPolicy.ShouldGrantEarring(featureEnabled: false, isLocalSessionSeat: true, isWakuuSeat: false), Is.False);
        });
    }

    // 档位用 int 传参（枚举是 internal，public 测试方法不能带 internal 参数类型）。
    [TestCase(0, 1)] // Common
    [TestCase(1, 2)] // Rare
    [TestCase(2, 3)] // Epic
    [TestCase(3, 4)] // Ancient
    public void 强度越高成本越高(int tierValue, int expectedCost)
    {
        Assert.That(HellwarSummonPolicy.ResolveSummonCost((HellwarWarGodTier)tierValue), Is.EqualTo(expectedCost));
    }

    [Test]
    public void 成本随档位递增()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Rare),
                Is.GreaterThan(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Common)));
            Assert.That(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Epic),
                Is.GreaterThan(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Rare)));
            Assert.That(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Ancient),
                Is.GreaterThan(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Epic)));
        });
    }

    [Test]
    public void 计数够且未满员才可召唤()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.CanSummon(spiritCount: 1, cost: 1, combatPlayerCount: 2, maxPlayerCount: 12), Is.True);
            Assert.That(HellwarSummonPolicy.CanSummon(spiritCount: 0, cost: 1, combatPlayerCount: 2, maxPlayerCount: 12), Is.False,
                "计数耗尽不能召唤（R6）");
            Assert.That(HellwarSummonPolicy.CanSummon(spiritCount: 3, cost: 4, combatPlayerCount: 2, maxPlayerCount: 12), Is.False,
                "计数不足一个高档位战神时不能召唤");
            Assert.That(HellwarSummonPolicy.CanSummon(spiritCount: 9, cost: 4, combatPlayerCount: 12, maxPlayerCount: 12), Is.False,
                "玩家已满 12 席不能召唤（R6）");
            Assert.That(HellwarSummonPolicy.CanSummon(spiritCount: 9, cost: 0, combatPlayerCount: 2, maxPlayerCount: 12), Is.False,
                "非法成本（0）不成立");
        });
    }

    [Test]
    public void 不能召唤的原因可读()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.DescribeBlockReason(1, 1, 2, 12), Is.EqualTo("ok"));
            Assert.That(HellwarSummonPolicy.DescribeBlockReason(0, 1, 2, 12), Does.Contain("not-enough-spirit"));
            Assert.That(HellwarSummonPolicy.DescribeBlockReason(9, 4, 12, 12), Does.Contain("party-full"));
            Assert.That(HellwarSummonPolicy.DescribeBlockReason(9, 0, 2, 12), Is.EqualTo("bad-cost"));
        });
    }

    [TestCase(null, "15")]
    [TestCase("", "15")]
    [TestCase("0", "0")]
    [TestCase("10", "10")]
    [TestCase("15", "15")]
    [TestCase("20", "20")]
    [TestCase(" 20 ", "20")]
    [TestCase("25", "15")]
    [TestCase("abc", "15")]
    public void 扣血档位宽松兜默认(string? raw, string expected)
    {
        Assert.That(HellwarSummonPolicy.NormalizeSplitHpRatio(raw), Is.EqualTo(expected));
    }

    [TestCase("0", true)]
    [TestCase("10", true)]
    [TestCase("15", true)]
    [TestCase("20", true)]
    [TestCase(" 20 ", true)]
    [TestCase("25", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void 设置页写入口严格拒绝非法档位(string? raw, bool expected)
    {
        Assert.That(HellwarSummonPolicy.TryNormalizeSplitHpRatio(raw, out _), Is.EqualTo(expected));
    }

    [TestCase("0", 0)]
    [TestCase("10", 10)]
    [TestCase("15", 15)]
    [TestCase("20", 20)]
    [TestCase("非法", 15)]
    public void 档位换算成百分比(string ratioKey, decimal expectedPercent)
    {
        Assert.That(HellwarSummonPolicy.ResolveSplitHpPercent(ratioKey), Is.EqualTo(expectedPercent));
    }

    [TestCase("0", true)]
    [TestCase("10", false)]
    [TestCase("15", false)]
    [TestCase("20", false)]
    [TestCase("非法", false)]
    public void 不扣血档位判定(string ratioKey, bool isNoCost)
    {
        Assert.That(HellwarSummonPolicy.IsNoCostSplitRatio(ratioKey), Is.EqualTo(isNoCost));
    }

    [TestCase(70, "0")]
    [TestCase(1, "0")]
    [TestCase(999, "0")]
    public void 不扣血档位一点都不掉血(decimal currentHp, string ratioKey)
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ResolveSplitHpLoss(currentHp, ratioKey), Is.EqualTo(0m));
            Assert.That(HellwarSummonPolicy.ResolveHpAfterSplit(currentHp, ratioKey), Is.EqualTo(currentHp));
        });
    }

    [TestCase(null, "2")]
    [TestCase("", "2")]
    [TestCase("1", "1")]
    [TestCase("2", "2")]
    [TestCase("3", "3")]
    [TestCase(" 3 ", "3")]
    [TestCase("0", "2")]
    [TestCase("4", "2")]
    [TestCase("abc", "2")]
    public void 每幕获得数量档位宽松兜默认(string? raw, string expected)
    {
        Assert.That(HellwarSummonPolicy.NormalizeActGainAmount(raw), Is.EqualTo(expected));
    }

    [TestCase("1", true)]
    [TestCase("2", true)]
    [TestCase("3", true)]
    [TestCase("0", false)]
    [TestCase("4", false)]
    [TestCase(null, false)]
    public void 每幕获得数量写入口严格拒绝(string? raw, bool expected)
    {
        Assert.That(HellwarSummonPolicy.TryNormalizeActGainAmount(raw, out _), Is.EqualTo(expected));
    }

    [TestCase("1", 1)]
    [TestCase("2", 2)]
    [TestCase("3", 3)]
    [TestCase("9", 2)]
    [TestCase(null, 2)]
    public void 每幕获得数量换算(string? raw, int expected)
    {
        Assert.That(HellwarSummonPolicy.ResolveActGainAmount(raw), Is.EqualTo(expected));
        Assert.That(expected, Is.InRange(HellwarSummonPolicy.ActGainMin, HellwarSummonPolicy.ActGainMax));
    }

    [TestCase(true, 0, -1, true)]   // 第 1 幕、没发过 ⇒ 发
    [TestCase(true, 2, 1, true)]    // 进第 3 幕 ⇒ 发
    [TestCase(true, 1, 1, false)]   // 这一幕发过了（读档重进）⇒ 不发
    [TestCase(true, 0, 0, false)]   // 第 1 幕发过了 ⇒ 不发
    [TestCase(true, 2, 2, false)]   // 同幕重复触发 ⇒ 不发
    [TestCase(false, 2, 1, false)]  // 开关关 ⇒ 不发
    [TestCase(true, -1, -1, false)] // 幕号无效 ⇒ 不发
    public void 每幕只发一次计数(bool enabled, int actIndex, int lastRewarded, bool expected)
    {
        Assert.That(HellwarSummonPolicy.ShouldGrantActCharges(enabled, actIndex, lastRewarded), Is.EqualTo(expected));
    }

    [TestCase(70, "15", 10)]   // 70 × 15% = 10.5 ⇒ 向下取整 10
    [TestCase(100, "10", 10)]
    [TestCase(100, "15", 15)]
    [TestCase(100, "20", 20)]
    [TestCase(71, "20", 14)]   // 71 × 20% = 14.2 ⇒ 14
    [TestCase(3, "20", 0)]     // 3 × 20% = 0.6 ⇒ 0（向下取整）
    public void 扣血基数与取整(decimal currentHp, string ratioKey, decimal expectedLoss)
    {
        Assert.That(HellwarSummonPolicy.ResolveSplitHpLoss(currentHp, ratioKey), Is.EqualTo(expectedLoss));
    }

    [TestCase(1, "10")]
    [TestCase(1, "15")]
    [TestCase(1, "20")]
    [TestCase(2, "20")]
    [TestCase(7, "20")]
    [TestCase(70, "20")]
    public void 扣血后血量仍不低于1(decimal currentHp, string ratioKey)
    {
        Assert.That(HellwarSummonPolicy.ResolveHpAfterSplit(currentHp, ratioKey),
            Is.GreaterThanOrEqualTo(HellwarSummonPolicy.MinHpAfterSplit),
            "D13：clamp ≥1，休息处自杀很怪");
    }

    [TestCase(1, "20")]   // 血量已在下限
    [TestCase(1, "10")]
    [TestCase(2, "20")]   // 2 × 20% = 0.4 ⇒ 向下取整 0，扣不动
    [TestCase(3, "20")]
    [TestCase(0, "20")]
    [TestCase(-5, "20")]
    public void 血量低于下限时不再扣(decimal currentHp, string ratioKey)
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ResolveSplitHpLoss(currentHp, ratioKey), Is.EqualTo(0m));
            Assert.That(HellwarSummonPolicy.ResolveHpAfterSplit(currentHp, ratioKey), Is.EqualTo(currentHp));
        });
    }

    [Test]
    public void 小血量按档位仍会被扣到只剩1()
    {
        // 5 × 20% = 1 ⇒ 恰好到下限；6 × 20% = 1.2 ⇒ 1 ⇒ 剩 5。
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ResolveHpAfterSplit(5m, "20"), Is.EqualTo(4m));
            Assert.That(HellwarSummonPolicy.ResolveHpAfterSplit(6m, "20"), Is.EqualTo(5m));
        });
    }

    [Test]
    public void 分裂精神每次只加一个计数()
    {
        Assert.That(HellwarSummonPolicy.SplitSpiritGain, Is.EqualTo(1));
    }

    [Test]
    public void 默认档与默认计数()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.DefaultSplitHpRatio, Is.EqualTo("15"), "D13：默认 15%");
            Assert.That(HellwarSummonPolicy.InitialSpiritCount, Is.EqualTo(0), "R2：计数初始 0");
        });
    }
}
