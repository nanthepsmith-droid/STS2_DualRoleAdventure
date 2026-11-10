using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 商店采购顺序纯逻辑（2026-11-10 用户口径）：
/// ① 有诅咒且删牌买得起 ⇒ 先删诅咒；
/// ② 卡组 ≥ 40 且店里没有"很好的牌" ⇒ 先遗物、再删牌；
/// ③ 其余保持「卡 → 遗物 → 药水 → 删牌」。
/// </summary>
[TestFixture]
public class WakuuShopOrderPolicyTests
{
    [Test]
    public void 常态_卡先遗物再药水最后删牌()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: 20, curseCount: 0, hasGoodCard: false, removalAffordable: true);

        Assert.That(order, Is.EqualTo(new[]
        {
            WakuuShopStep.Cards, WakuuShopStep.Relics, WakuuShopStep.Potions, WakuuShopStep.Removal,
        }));
    }

    [Test]
    public void 有诅咒_先删诅咒再买东西()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: 20, curseCount: 2, hasGoodCard: true, removalAffordable: true);

        Assert.That(order.First(), Is.EqualTo(WakuuShopStep.Removal), "有诅咒时删牌必须先做");
        Assert.That(order.Count, Is.EqualTo(4));
        Assert.That(order, Is.EqualTo(new[]
        {
            WakuuShopStep.Removal, WakuuShopStep.Cards, WakuuShopStep.Relics, WakuuShopStep.Potions,
        }));
    }

    [Test]
    public void 有诅咒但删不起_回退常态顺序()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: 20, curseCount: 3, hasGoodCard: false, removalAffordable: false);

        Assert.That(order.First(), Is.EqualTo(WakuuShopStep.Cards));
        Assert.That(order.Last(), Is.EqualTo(WakuuShopStep.Removal));
    }

    [Test]
    public void 大卡组且没好牌_先遗物再删牌()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: WakuuShopOrderPolicy.BigDeckThreshold, curseCount: 0, hasGoodCard: false,
            removalAffordable: true);

        Assert.That(order, Is.EqualTo(new[]
        {
            WakuuShopStep.Relics, WakuuShopStep.Removal, WakuuShopStep.Cards, WakuuShopStep.Potions,
        }));
    }

    [Test]
    public void 大卡组但店里有好牌_恢复常态顺序()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: 45, curseCount: 0, hasGoodCard: true, removalAffordable: true);

        Assert.That(order, Is.EqualTo(new[]
        {
            WakuuShopStep.Cards, WakuuShopStep.Relics, WakuuShopStep.Potions, WakuuShopStep.Removal,
        }));
    }

    [Test]
    public void 卡组刚好不到阈值_不触发大卡组规则()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: WakuuShopOrderPolicy.BigDeckThreshold - 1, curseCount: 0, hasGoodCard: false,
            removalAffordable: true);

        Assert.That(order.First(), Is.EqualTo(WakuuShopStep.Cards));
    }

    [Test]
    public void 有诅咒且大卡组_诅咒优先于大卡组规则()
    {
        IReadOnlyList<WakuuShopStep> order = WakuuShopOrderPolicy.Decide(
            deckSize: 50, curseCount: 1, hasGoodCard: false, removalAffordable: true);

        Assert.That(order.First(), Is.EqualTo(WakuuShopStep.Removal));
    }
}
