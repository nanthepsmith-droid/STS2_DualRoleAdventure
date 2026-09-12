using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 个人记录器「写时幂等」去重纯逻辑测试（r120）。
///
/// 核心场景：SL（读档重玩）会让同一次抉择被记录多次。这里钉死"同一抉择只保留最后一次"的口径，
/// 并模拟"SL 前选 A、SL 后改选 B"——要求旧页（含 A 的 chosen=true）被整页替换掉。
/// </summary>
[TestFixture]
public class WakuuPersonalDedupeTests
{
    private const string Run = "SEED:2";

    // ---------------- BuildBatchKey ----------------

    [Test]
    public void 批次键_顺序无关且大小写无关()
    {
        Assert.That(
            WakuuPersonalDedupe.BuildBatchKey(new[] { "Strike", "DEFEND", "Bash" }),
            Is.EqualTo(WakuuPersonalDedupe.BuildBatchKey(new[] { "defend", "BASH", "strike" })));
    }

    [Test]
    public void 批次键_去重并排序()
    {
        Assert.That(
            WakuuPersonalDedupe.BuildBatchKey(new[] { "B", "A", "B", "C" }),
            Is.EqualTo("A|B|C"));
    }

    [Test]
    public void 批次键_空与全空项返回空串()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuPersonalDedupe.BuildBatchKey(null), Is.Empty);
            Assert.That(WakuuPersonalDedupe.BuildBatchKey(new List<string>()), Is.Empty);
            Assert.That(WakuuPersonalDedupe.BuildBatchKey(new[] { "", null! }), Is.Empty);
        });
    }

    // ---------------- 事件页 ----------------

    [Test]
    public void 事件页_同局同事件只保留最后一次_旧页整页替换()
    {
        PersonalStore store = new();
        // SL 前：整页 3 项，选了 A
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "A", chosen = true });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "B", chosen = false });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "C", chosen = false });

        int replaced = WakuuPersonalDedupe.RemoveEventPage(store, Run, "E1");

        // SL 后：改选 B
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "A", chosen = false });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "B", chosen = true });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "C", chosen = false });

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.EqualTo(3));
            Assert.That(store.eventChoices, Has.Count.EqualTo(3), "重复不会累积");
            Assert.That(store.eventChoices.FindAll(r => r.optionKey == "A")[0].chosen, Is.False,
                "SL 前选过的 A 不再残留 chosen=true");
            Assert.That(store.eventChoices.FindAll(r => r.optionKey == "B")[0].chosen, Is.True);
        });
    }

    [Test]
    public void 事件页_其它事件与其它局不受影响()
    {
        PersonalStore store = new();
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E1", optionKey = "A" });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = Run, eventId = "E2", optionKey = "A" });
        store.eventChoices.Add(new PersonalEventOptionRecord { runKey = "OTHER:2", eventId = "E1", optionKey = "A" });

        Assert.That(WakuuPersonalDedupe.RemoveEventPage(store, Run, "E1"), Is.EqualTo(1));
        Assert.That(store.eventChoices, Has.Count.EqualTo(2));
    }

    // ---------------- 卡牌批次 ----------------

    [Test]
    public void 卡牌批次_同批次覆盖_不同批次共存()
    {
        PersonalStore store = new();
        store.cardOffers.Add(new PersonalCardOfferRecord { runKey = Run, card = "A", batch = "A|B|C", picked = true });
        store.cardOffers.Add(new PersonalCardOfferRecord { runKey = Run, card = "B", batch = "A|B|C", picked = false });

        int sameBatch = WakuuPersonalDedupe.RemoveCardBatch(store, Run, "A|B|C");
        int otherBatch = WakuuPersonalDedupe.RemoveCardBatch(store, Run, "X|Y|Z");

        Assert.Multiple(() =>
        {
            Assert.That(sameBatch, Is.EqualTo(2), "同批次整体被替换");
            Assert.That(otherBatch, Is.EqualTo(0), "重 roll 出的不同批次不该被删");
            Assert.That(store.cardOffers, Is.Empty);
        });
    }

    [Test]
    public void 卡牌批次_旧数据无批次字段时不参与去重()
    {
        PersonalStore store = new();
        store.cardOffers.Add(new PersonalCardOfferRecord { runKey = Run, card = "A", batch = string.Empty });

        Assert.Multiple(() =>
        {
            Assert.That(WakuuPersonalDedupe.RemoveCardBatch(store, Run, string.Empty), Is.EqualTo(0));
            Assert.That(store.cardOffers, Has.Count.EqualTo(1), "无法判断的旧数据保留");
        });
    }

    // ---------------- 商店 / 删牌 ----------------

    [Test]
    public void 商店购买_同局同幕同类别同物品覆盖()
    {
        PersonalStore store = new();
        store.shopPurchases.Add(new PersonalShopPurchaseRecord { runKey = Run, act = 1, kind = "card", item = "BASH" });
        store.shopPurchases.Add(new PersonalShopPurchaseRecord { runKey = Run, act = 2, kind = "card", item = "BASH" });

        int replaced = WakuuPersonalDedupe.RemoveShopPurchase(store, Run, act: 1, kind: "card", item: "BASH");

        Assert.Multiple(() =>
        {
            Assert.That(replaced, Is.EqualTo(1));
            Assert.That(store.shopPurchases, Has.Count.EqualTo(1));
            Assert.That(store.shopPurchases[0].act, Is.EqualTo(2), "不同幕的不受影响");
        });
    }

    [Test]
    public void 删牌_同局同一张牌覆盖()
    {
        PersonalStore store = new();
        store.cardRemovals.Add(new PersonalCardRemovalRecord { runKey = Run, card = "CLASH" });
        store.cardRemovals.Add(new PersonalCardRemovalRecord { runKey = "$其他", card = "CLASH" });

        Assert.That(WakuuPersonalDedupe.RemoveCardRemoval(store, Run, "CLASH"), Is.EqualTo(1));
        Assert.That(store.cardRemovals, Has.Count.EqualTo(1));
    }

    [Test]
    public void 空输入安全返回零()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuPersonalDedupe.RemoveEventPage(null!, Run, "E1"), Is.EqualTo(0));
            Assert.That(WakuuPersonalDedupe.RemoveEventPage(new PersonalStore(), string.Empty, "E1"), Is.EqualTo(0));
            Assert.That(WakuuPersonalDedupe.RemoveCardBatch(new PersonalStore(), Run, string.Empty), Is.EqualTo(0));
            Assert.That(WakuuPersonalDedupe.RemoveShopPurchase(new PersonalStore(), Run, 1, string.Empty, "X"), Is.EqualTo(0));
            Assert.That(WakuuPersonalDedupe.RemoveCardRemoval(null!, Run, "X"), Is.EqualTo(0));
        });
    }
}
