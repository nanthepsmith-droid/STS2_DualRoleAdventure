using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>瓦库商店自动买卡决策纯函数测试（Phase 4 §9.3）。</summary>
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
}
