using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>瓦库商店自动购买决策纯函数测试（Phase 4 §9.3）：买卡 / 遗物 / 药水。</summary>
[TestFixture]
public class WakuuMerchantPickingTests
{
    private static WakuuMerchantCardCandidate Card(string id, int price, double? winRate)
    {
        return new WakuuMerchantCardCandidate(id, price, winRate);
    }

    [Test]
    public void 只买胜率达到门槛且付完仍保留保底金的卡()
    {
        // 默认门槛 0.2（2026-09-06 从 0.5 下调：社区多数牌胜率集中在 20%~30%）。
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("CLASH", 50, 0.25),   // 达标 → 买
            Card("STRIKE", 50, 0.15),  // 胜率不够（<0.2）→ 不买
            Card("CARD_NO_DATA", 50, null), // 无数据 → 不买
            Card("CORPSE_EXPLOSION", 150, 0.35), // 达标但买完剩 0 < 保底 50 → 不买
            Card("IMPERVIOUS", 50, 0.21), // 达标 → 买
        };

        List<int> picks = WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200);

        Assert.Multiple(() =>
        {
            Assert.That(picks, Is.EqualTo(new[] { 0, 4 }));
        });
    }

    [Test]
    public void 默认门槛为02仅拦胜率不足02的卡()
    {
        // 回归验证门槛确实为 0.2：0.2 达标，0.19 不足。
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("CLASH", 50, 0.2),
            Card("STRIKE", 50, 0.19),
        };
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void 无数据卡默认跳过开启buyNoData后按金币保底买入()
    {
        // buyNoData=false（默认）：无数据卡不买
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("MOD_CARD_NO_DATA", 50, null),  // 无数据 → 默认不买
            Card("CLASH", 50, 0.25),             // 有数据达标 → 买
        };
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200), Is.EqualTo(new[] { 1 }));

        // buyNoData=true：无数据卡也进入金币保底判定
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200, buyNoData: true), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void buyNoData开启后无数据卡仍受金币保底约束()
    {
        // 无数据但买完剩 0 < 保底 50 → 即使 buyNoData 也不买
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("MOD_CARD_PRICEY", 160, null),
        };
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200, buyNoData: true), Is.Empty);
        // 便宜的无数据卡可买（200-50=150 ≥ 50）
        List<WakuuMerchantCardCandidate> cheap = new()
        {
            Card("MOD_CARD_CHEAP", 50, null),
        };
        Assert.That(WakuuMerchantPicking.SelectCardBuys(cheap, gold: 200, buyNoData: true), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void 游戏内置Null占位卡一律不买()
    {
        // Null 卡在社区统计里有数据（能被 0.2 门槛选中），但不是真牌，买了纯浪费金币
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("NULL", 36, 0.25),       // 占位卡 → 不买
            Card("BEAM_CELL", 49, 0.25),  // 正常卡 → 买
        };
        Assert.Multiple(() =>
        {
            Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200), Is.EqualTo(new[] { 1 }));
            // buyNoData 放开时同样不买占位卡
            Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 200, buyNoData: true), Is.EqualTo(new[] { 1 }));
            Assert.That(WakuuMerchantPicking.IsPlaceholderCardId("null"), Is.True);
            Assert.That(WakuuMerchantPicking.IsPlaceholderCardId(""), Is.True);
            Assert.That(WakuuMerchantPicking.IsPlaceholderCardId("BEAM_CELL"), Is.False);
        });
    }

    [Test]
    public void 金币不够一律不买()
    {
        List<WakuuMerchantCardCandidate> candidates = new()
        {
            Card("CLASH", 50, 0.9),
        };
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 40), Is.Empty);
        // 买完低于保底（60 - 50 = 10 < 50）也不买
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 60), Is.Empty);
        // 111 够（111-50=61 ≥ 50）
        Assert.That(WakuuMerchantPicking.SelectCardBuys(candidates, gold: 111), Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void 空候选与负数金币返回空()
    {
        Assert.That(WakuuMerchantPicking.SelectCardBuys(new List<WakuuMerchantCardCandidate>(), 500), Is.Empty);
        Assert.That(WakuuMerchantPicking.SelectCardBuys(null!, 500), Is.Empty);
        Assert.That(WakuuMerchantPicking.SelectCardBuys(
            new List<WakuuMerchantCardCandidate> { Card("CLASH", 50, 0.9) }, -10), Is.Empty);
    }

    private static WakuuMerchantPricedItem Priced(string id, int price)
    {
        return new WakuuMerchantPricedItem(id, price);
    }

    [Test]
    public void 遗物与药水只按价格与金币保底购买()
    {
        // 遗物价位 175 / 225 / 275（RelicModel.MerchantCost）；金币 500、保底 50：
        // 买 175 后剩 325 → 再买 225 后剩 100 → 第三件 275 会跌破保底，停手。
        List<WakuuMerchantPricedItem> items = new()
        {
            Priced("RELIC_COMMON", 175),
            Priced("RELIC_UNCOMMON", 225),
            Priced("RELIC_RARE", 275),
        };

        Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 500), Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void 商品价格恰好等于金币减保底时可买()
    {
        // 边界取「买完 >= 保底」（与买卡同口径）：225 - 175 = 50，不低于保底 50 → 买
        List<WakuuMerchantPricedItem> items = new() { Priced("RELIC_COMMON", 175) };
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 225), Is.EqualTo(new[] { 0 }));
        // 差 1 金就不买
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 224), Is.Empty);
        // 金币本身低于保底：一件都不买（余额 - 保底 为负，任何正价都比它大）
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(
            new List<WakuuMerchantPricedItem> { Priced("POTION_COMMON", 50) }, gold: 30), Is.Empty);
    }

    [Test]
    public void 商品id为空或价格非正一律跳过()
    {
        List<WakuuMerchantPricedItem> items = new()
        {
            Priced("", 50),                  // 未上架 / 空 id（运行层用空 id 表示跳过项）
            Priced("POTION_COMMON", 0),      // 价格异常
            Priced("POTION_UNCOMMON", -1),   // 价格异常
            Priced("POTION_RARE", 100),      // 正常 → 买
        };

        Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 500), Is.EqualTo(new[] { 3 }));
    }

    [Test]
    public void 商品读不到价格时被金币保底拦下()
    {
        // 运行层 SafeCost 读价异常时给 int.MaxValue：必须不买（且不能因减法溢出而误判）
        List<WakuuMerchantPricedItem> items = new() { Priced("RELIC_UNKNOWN", int.MaxValue) };
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 999), Is.Empty);
    }

    [Test]
    public void 商品空候选与负数金币返回空()
    {
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(new List<WakuuMerchantPricedItem>(), 500), Is.Empty);
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(null!, 500), Is.Empty);
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(
            new List<WakuuMerchantPricedItem> { Priced("POTION_COMMON", 50) }, -10), Is.Empty);
    }

    private static WakuuShopSignal Signal(string kind, string item, long bought, double held, long baseline, double baseRate)
    {
        return new WakuuShopSignal(kind, item, bought, held, baseline, baseRate);
    }

    private static WakuuMerchantPricedItem PricedWithSignal(string id, int price, WakuuShopSignal signal)
    {
        return new WakuuMerchantPricedItem(id, price, signal);
    }

    [Test]
    public void 个人统计负面否决时不买()
    {
        // 买过它 4 局、胜率 0.25 vs 基准 0.50 ⇒ 增益 -0.25 < 0 ⇒ 否决
        WakuuShopSignal negative = Signal("relic", "RELIC_BAD", 4, 0.25, 100, 0.50);
        // 买过它 5 局、胜率 0.70 vs 0.50 ⇒ 增益 +0.20 ⇒ 不否决
        WakuuShopSignal positive = Signal("relic", "RELIC_GOOD", 5, 0.70, 100, 0.50);

        List<WakuuMerchantPricedItem> items = new()
        {
            PricedWithSignal("RELIC_BAD", 175, negative),
            PricedWithSignal("RELIC_GOOD", 175, positive),
            Priced("RELIC_NO_DATA", 175),
        };

        Assert.Multiple(() =>
        {
            // 未启用统计（门槛 0）⇒ 三件都按价格规则买
            Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 1000), Is.EqualTo(new[] { 0, 1, 2 }));
            // 启用统计且样本达标 ⇒ 只否决负面那件
            Assert.That(
                WakuuMerchantPicking.SelectPricedBuys(items, gold: 1000, personalMinSample: 3),
                Is.EqualTo(new[] { 1, 2 }));
        });
    }

    [Test]
    public void 个人统计样本不足或增益不为负时不否决()
    {
        // 只有 2 局样本、门槛 3 ⇒ 不否决（宁可不干预）
        List<WakuuMerchantPricedItem> thin = new()
        {
            PricedWithSignal("POTION_THIN", 50, Signal("potion", "POTION_THIN", 2, 0.0, 50, 0.60)),
        };
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(thin, gold: 500, personalMinSample: 3),
            Is.EqualTo(new[] { 0 }));

        // 样本够、增益正好 0（不低于门槛 0）⇒ 不否决
        List<WakuuMerchantPricedItem> flat = new()
        {
            PricedWithSignal("POTION_FLAT", 50, Signal("potion", "POTION_FLAT", 3, 0.60, 50, 0.60)),
        };
        Assert.That(WakuuMerchantPicking.SelectPricedBuys(flat, gold: 500, personalMinSample: 3),
            Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void 统计否决与金币保底各自独立生效()
    {
        // 否决优先于金币判定：即使钱多得花不完也不买
        List<WakuuMerchantPricedItem> items = new()
        {
            PricedWithSignal("RELIC_BAD", 100, Signal("relic", "RELIC_BAD", 3, 0.1, 20, 0.6)),
        };
        Assert.Multiple(() =>
        {
            Assert.That(WakuuMerchantPicking.SelectPricedBuys(items, gold: 10000, personalMinSample: 3), Is.Empty);
            // 纯函数判据本身的三条边界
            Assert.That(
                WakuuMerchantPicking.IsPersonalStatsVeto(Signal("relic", "X", 3, 0.1, 20, 0.6), minSample: 3),
                Is.True);
            Assert.That(WakuuMerchantPicking.IsPersonalStatsVeto(null, minSample: 3), Is.False);
            Assert.That(
                WakuuMerchantPicking.IsPersonalStatsVeto(Signal("relic", "X", 3, 0.1, 20, 0.6), minSample: 0),
                Is.False);
        });
    }
}
