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

    // ② 预设遗物（r255）：件数阶梯 = 成本 − 1（单一事实源 = 本函数；内容表在启动自检里与它对账）。
    [TestCase(0, 0)] // Common：不发预设遗物（只保留它自己角色的初始遗物）
    [TestCase(1, 1)] // Rare
    [TestCase(2, 2)] // Epic
    [TestCase(3, 3)] // Ancient
    public void 预设遗物件数等于成本减一(int tierValue, int expectedCount)
    {
        Assert.That(HellwarSummonPolicy.ResolvePresetRelicCount((HellwarWarGodTier)tierValue), Is.EqualTo(expectedCount));
    }

    [Test]
    public void 预设遗物件数随档位递增且普通档为零()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.ResolvePresetRelicCount(HellwarWarGodTier.Common), Is.EqualTo(0),
                "普通档不发预设遗物（最便宜那档只拿角色初始遗物 + 角色初始卡组）");
            for (int i = 1; i < HellwarSummonPolicy.SelectableTiers.Length; i++)
            {
                Assert.That(
                    HellwarSummonPolicy.ResolvePresetRelicCount(HellwarSummonPolicy.SelectableTiers[i]),
                    Is.GreaterThan(HellwarSummonPolicy.ResolvePresetRelicCount(HellwarSummonPolicy.SelectableTiers[i - 1])),
                    "遗物件数必须随档位递增（否则档位强度阶梯被抹平）");
            }
        });
    }

    // R9（Phase 4）：复活花费 = 该战神**原档位**的召唤成本（本场预算）。
    [Test]
    public void 复活花费等于该档位的召唤成本()
    {
        Assert.Multiple(() =>
        {
            foreach (HellwarWarGodTier tier in HellwarSummonPolicy.SelectableTiers)
            {
                Assert.That(
                    HellwarSummonPolicy.ResolveReviveCost(tier),
                    Is.EqualTo(HellwarSummonPolicy.ResolveSummonCost(tier)),
                    $"档位 {tier} 的复活花费应等于它的召唤成本（复活不该比重新召一个更贵/更便宜）");
            }

            Assert.That(
                HellwarSummonPolicy.ResolveReviveCost((HellwarWarGodTier)99),
                Is.EqualTo(HellwarSummonPolicy.ResolveSummonCost(HellwarWarGodTier.Common)),
                "非法档位兜普通档");
        });
    }

    // ─────────────── R10（Phase 4）：战灵守卫（D4 = 玩家零伤害 + 战神吃一半）───────────────

    [TestCase(true, true, true, true)]     // 攻击伤害 + 打的是绑主 + 有存活战神 ⇒ 拦
    [TestCase(true, true, false, false)]   // **没有存活战神 ⇒ 一点都不该改否则绑主白赚 50% 减伤**
    [TestCase(true, false, true, false)]   // 不是打绑主（战神自己挨打 / 别人挨打）⇒ 不拦
    [TestCase(false, true, true, false)]   // 非攻击伤害（中毒 / 直接掉血）⇒ 不拦
    public void 战灵守卫只在攻击打绑主且有存活战神时生效(
        bool isPoweredAttack, bool targetIsSummoner, bool hasAliveGuard, bool expected)
    {
        Assert.That(
            HellwarSummonPolicy.ShouldGuardIncomingDamage(isPoweredAttack, targetIsSummoner, hasAliveGuard),
            Is.EqualTo(expected));
    }

    [TestCase(10d, 5d)]
    [TestCase(1d, 0.5d)]
    [TestCase(0d, 0d)]
    public void 守卫把绑主要掉的血减半(double amount, double expected)
    {
        // 减半后的量紧接着被转给战神（另一半消失 = 旧【我挡】"你只承受一半"的语义）。
        Assert.That(
            (double)HellwarSummonPolicy.ResolveGuardedHpLoss((decimal)amount),
            Is.EqualTo(expected));
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

    // ─────────────────────── Phase 2：战斗内计数预算（R3 口径 A） ───────────────────────
    // 计数是"每场战斗的预算"：休息处 / 每幕累计出来的是上限（存档），战斗中花的是"本场已花"（不存档），
    // 战后把已花清零 = 自动补满。可用 = 上限 − 已花。

    [TestCase(0, 0, 0)]
    [TestCase(5, 0, 5)]
    [TestCase(5, 2, 3)]
    [TestCase(5, 5, 0)]
    [TestCase(5, 9, 0)]     // 已花超过上限 ⇒ 钳到 0（不出现负数）
    [TestCase(-3, 0, 0)]    // 上限非法 ⇒ 0
    [TestCase(5, -2, 5)]    // 已花非法 ⇒ 按 0
    public void 可用计数是上限减本场已花(int cap, int spent, int expected)
    {
        Assert.That(HellwarSummonPolicy.ResolveAvailableSpirit(cap, spent), Is.EqualTo(expected));
    }

    [TestCase(5, 4, 1, true)]
    [TestCase(5, 5, 1, false)]
    [TestCase(5, 0, 4, true)]
    [TestCase(5, 2, 4, false)]
    [TestCase(5, 0, 0, false)]  // 非法成本
    public void 还够不够花(int cap, int spent, int cost, bool expected)
    {
        Assert.That(HellwarSummonPolicy.CanSpendSpirit(cap, spent, cost), Is.EqualTo(expected));
    }

    [TestCase(0, 1, 1)]
    [TestCase(2, 3, 5)]
    [TestCase(-4, 1, 1)]        // 已花非法 ⇒ 当 0
    [TestCase(2, -3, 2)]        // 成本非法 ⇒ 不加
    public void 已花可以累加(int spent, int cost, int expected)
    {
        Assert.That(HellwarSummonPolicy.AddSpentSpirit(spent, cost), Is.EqualTo(expected));
    }

    [Test]
    public void 召唤预算是先花后补满的闭环()
    {
        int cap = 3;
        int spent = 0;
        // 花 1 次（普通档）⇒ 可用 2。
        Assert.That(HellwarSummonPolicy.CanSpendSpirit(cap, spent, HellwarSummonPolicy.CommonCost), Is.True);
        spent = HellwarSummonPolicy.AddSpentSpirit(spent, HellwarSummonPolicy.CommonCost);
        Assert.That(HellwarSummonPolicy.ResolveAvailableSpirit(cap, spent), Is.EqualTo(2));
        // 花光 ⇒ 不能再花。
        spent = HellwarSummonPolicy.AddSpentSpirit(spent, 2);
        Assert.That(HellwarSummonPolicy.CanSpendSpirit(cap, spent, 1), Is.False);
        // 战后补满（spent 清零）⇒ 上限不变、可用回满（计数本身只增不减，R3 口径 A）。
        Assert.That(HellwarSummonPolicy.ResolveAvailableSpirit(cap, 0), Is.EqualTo(cap));
    }

    // ─────────────────────── Phase 2：回合开始是否弹"召唤选择" ───────────────────────

    [TestCase(true, true, true, false, 1, 1, 2, 12, true)]
    [TestCase(false, true, true, false, 1, 1, 2, 12, false)]   // 开关关
    [TestCase(true, false, true, false, 1, 1, 2, 12, false)]   // 非真人席位（瓦库 / 第三方）
    [TestCase(true, true, false, false, 1, 1, 2, 12, false)]   // 没有耳环
    [TestCase(true, true, true, true, 1, 1, 2, 12, false)]     // 本回合已弹过（D6 每回合 1 个）
    [TestCase(true, true, true, false, 0, 1, 2, 12, false)]    // 计数不够最低档 ⇒ 安静跳过
    [TestCase(true, true, true, false, 1, 1, 12, 12, false)]   // 已满 12 席（R6）
    [TestCase(true, true, true, false, 9, 1, 11, 12, true)]    // 还差一席 ⇒ 可以
    public void 回合开始弹召唤选择的判据(
        bool featureEnabled, bool humanSeat, bool hasEarring, bool alreadyOffered,
        int available, int minCost, int combatCount, int maxCount, bool expected)
    {
        Assert.That(
            HellwarSummonPolicy.ShouldOfferSummon(
                featureEnabled, humanSeat, hasEarring, alreadyOffered, available, minCost, combatCount, maxCount),
            Is.EqualTo(expected));
    }

    [Test]
    public void 不弹召唤选择的原因可读()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, true, true, false, 1, 1, 2, 12), Is.EqualTo("offer"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(false, true, true, false, 1, 1, 2, 12), Does.Contain("feature-disabled"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, false, true, false, 1, 1, 2, 12), Does.Contain("not-human-seat"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, true, false, false, 1, 1, 2, 12), Does.Contain("no-earring"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, true, true, true, 1, 1, 2, 12), Does.Contain("already-offered"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, true, true, false, 0, 1, 2, 12), Does.Contain("not-enough-spirit"));
            Assert.That(HellwarSummonPolicy.DescribeOfferSkipReason(true, true, true, false, 1, 1, 12, 12), Does.Contain("party-full"));
        });
    }

    [Test]
    public void Phase2b起四档都可选()
    {
        // Phase 2 只有普通档；Phase 2b 补齐罕见 / 稀有 / 先古（预设档见 HellwarWarGodPresets）。
        Assert.That(
            HellwarSummonPolicy.SelectableTiers,
            Is.EqualTo(new[]
            {
                HellwarWarGodTier.Common,
                HellwarWarGodTier.Rare,
                HellwarWarGodTier.Epic,
                HellwarWarGodTier.Ancient,
            }));

        // 成本必须随档位递增（R5）——顺带钉住"选择列表顺序 = 成本顺序"。
        for (int i = 1; i < HellwarSummonPolicy.SelectableTiers.Length; i++)
        {
            Assert.That(
                HellwarSummonPolicy.ResolveSummonCost(HellwarSummonPolicy.SelectableTiers[i]),
                Is.GreaterThan(HellwarSummonPolicy.ResolveSummonCost(HellwarSummonPolicy.SelectableTiers[i - 1])));
        }
    }

    // ─────────────── Phase 2b：两个大招（R16【无尽战神】/ R17【终极九连环】）───────────────

    [TestCase(12, 2, 10)]   // 2 真人 ⇒ 补 10 个
    [TestCase(12, 3, 9)]
    [TestCase(12, 12, 0)]   // 已满 ⇒ 0 个（按钮不会出现）
    [TestCase(12, 20, 0)]   // 越界也不为负
    [TestCase(12, 11, 1)]
    public void 大招按剩余栏位截断(int maxPlayers, int combatPlayers, int expected)
    {
        Assert.That(HellwarSummonPolicy.ResolveFreeSlots(combatPlayers, maxPlayers), Is.EqualTo(expected));
        Assert.That(HellwarSummonPolicy.ResolveEndlessSummonCount(combatPlayers, maxPlayers), Is.EqualTo(expected));
        Assert.That(HellwarSummonPolicy.ResolveTruncatedSummonCount(9, expected), Is.EqualTo(Math.Min(9, expected)));
    }

    [TestCase(10, 9)]    // 位够 ⇒ **固定 9 个**
    [TestCase(20, 9)]
    [TestCase(9, 9)]
    [TestCase(4, 4)]     // 只有剩余栏位不足才截断
    [TestCase(1, 1)]
    [TestCase(0, 0)]
    public void 九连环固定九只受栏位截断(int freeSlots, int expected)
    {
        Assert.That(HellwarSummonPolicy.ResolveNineChainSummonCount(freeSlots), Is.EqualTo(expected));
    }

    [TestCase(9, 12)]    // 满编 ⇒ 整包价 12
    [TestCase(8, 11)]    // round(12×8/9 = 10.67) = 11
    [TestCase(7, 9)]     // round(9.33) = 9
    [TestCase(6, 8)]
    [TestCase(5, 7)]     // round(6.67) = 7
    [TestCase(4, 5)]     // round(5.33) = 5
    [TestCase(3, 4)]
    [TestCase(2, 3)]     // round(2.67) = 3
    [TestCase(1, 1)]     // round(1.33) = 1
    [TestCase(0, 0)]
    [TestCase(-3, 0)]
    [TestCase(99, 12)]   // 越界按满编算，不超过整包价
    public void 九连环按实际召唤数折算消耗(int summoned, int expected)
    {
        Assert.That(HellwarSummonPolicy.ResolveNineChainSpiritCost(summoned), Is.EqualTo(expected));
        Assert.That(HellwarSummonPolicy.ResolveNineChainSpiritCost(summoned),
            Is.LessThanOrEqualTo(HellwarSummonPolicy.UltimateNineChainBundleCost));
    }

    [Test]
    public void 九连环满编正好整包价()
    {
        // "消耗按实际数量折算"必须与整包价自洽：9 个 = 12 点。
        Assert.That(
            HellwarSummonPolicy.ResolveNineChainSpiritCost(HellwarSummonPolicy.NineChainMaxAncients),
            Is.EqualTo(HellwarSummonPolicy.UltimateNineChainBundleCost));
    }

    [Test]
    public void 九连环的门槛是整包价()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.UltimateNineChainBundleCost, Is.EqualTo(12), "整包价 12（D16 乙）");
            Assert.That(HellwarSummonPolicy.NineChainMinSpirit, Is.EqualTo(12),
                "2026-10-09 三轮修订：门槛 = 整包价（\"计数至少能召唤 9 个\"）");
            Assert.That(HellwarSummonPolicy.NineChainMaxAncients, Is.EqualTo(9));
            Assert.That(HellwarSummonPolicy.EndlessWarGodBundleCost, Is.EqualTo(8), "整包价 8（D16 乙）");
        });
    }

    [TestCase(8, 10, false, true)]    // 正好够、有空位、没用过 ⇒ 可开
    [TestCase(8, 10, true, false)]    // 本场已用过（D15）
    [TestCase(7, 10, false, false)]   // 差一点
    [TestCase(8, 0, false, false)]    // 没空位
    [TestCase(99, 1, false, true)]
    public void 无尽战神的开启判据(int available, int freeSlots, bool used, bool expected)
    {
        Assert.That(HellwarSummonPolicy.CanOpenEndlessWarGod(available, freeSlots, used), Is.EqualTo(expected));
    }

    [TestCase(12, 10, false, true)]   // 门槛 = 整包价 12（**上限**口径，不是本场可用）
    [TestCase(20, 10, false, true)]
    [TestCase(11, 10, false, false)]  // 差一点就开不出来（三轮修订：不再"部分成功"）
    [TestCase(3, 10, false, false)]
    [TestCase(12, 0, false, false)]   // 没空位
    [TestCase(12, 10, true, false)]   // 本场已用过（D15）
    public void 九连环的开启判据(int cap, int freeSlots, bool used, bool expected)
    {
        Assert.That(HellwarSummonPolicy.CanOpenNineChain(cap, freeSlots, used), Is.EqualTo(expected));
    }

    [Test]
    public void 大招不能开的原因可读()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarSummonPolicy.DescribeUltimateBlockReason(false, 8, 3, false), Is.EqualTo("ok"));
            Assert.That(HellwarSummonPolicy.DescribeUltimateBlockReason(false, 8, 3, true), Does.Contain("already-used"));
            Assert.That(HellwarSummonPolicy.DescribeUltimateBlockReason(false, 8, 0, false), Does.Contain("party-full"));
            Assert.That(HellwarSummonPolicy.DescribeUltimateBlockReason(false, 7, 3, false), Does.Contain("not-enough-spirit(7<8)"));
            Assert.That(HellwarSummonPolicy.DescribeUltimateBlockReason(true, 11, 3, false), Does.Contain("not-enough-spirit(11<12)"));
        });
    }
}
