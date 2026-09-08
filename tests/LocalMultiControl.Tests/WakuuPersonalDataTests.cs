using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 个人偏好记录器纯逻辑测试：
/// offer/pick 计数（mode/角色/act/首次重复切片）、胜负归因（held/skipped、abandon 剔除）、
/// 决策信号偏好链（多人优先多人）、JSON 往返。
/// </summary>
[TestFixture]
public class WakuuPersonalDataTests
{
    private static PersonalStore MakeStore()
    {
        return new PersonalStore();
    }

    private static PersonalRunRecord Run(string runKey, bool isMulti, string result)
    {
        return new PersonalRunRecord { runKey = runKey, isMulti = isMulti, result = result, ts = 0 };
    }

    private static PersonalCardOfferRecord Card(
        string runKey, bool isMulti, string character, int act, string card, bool isRepeat, bool picked)
    {
        return new PersonalCardOfferRecord
        {
            runKey = runKey, isMulti = isMulti, character = character, act = act,
            card = card, isRepeat = isRepeat, picked = picked, ts = 0,
        };
    }

    private static PersonalEventOptionRecord Evt(
        string runKey, bool isMulti, string character, int act, string eventId, string optionKey, bool chosen)
    {
        return new PersonalEventOptionRecord
        {
            runKey = runKey, isMulti = isMulti, character = character, act = act,
            eventId = eventId, optionKey = optionKey, optionText = optionKey, chosen = chosen, ts = 0,
        };
    }

    private static PersonalShopPurchaseRecord Shop(
        string runKey, bool isMulti, string character, int act, string kind, string item, int gold)
    {
        return new PersonalShopPurchaseRecord
        {
            runKey = runKey, isMulti = isMulti, character = character, act = act,
            kind = kind, item = item, goldSpent = gold, ts = 0,
        };
    }

    private static PersonalCardRemovalRecord Rem(
        string runKey, bool isMulti, string character, int act, string card)
    {
        return new PersonalCardRemovalRecord
        {
            runKey = runKey, isMulti = isMulti, character = character, act = act,
            card = card, ts = 0,
        };
    }

    [Test]
    public void 卡牌切片_按模式角色act首次重复过滤()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "win"));
        store.runs.Add(Run("r2", true, "win"));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "STRIKE", false, true));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "STRIKE", false, false));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "STRIKE", true, false));
        store.cardOffers.Add(Card("r2", true, "IRONCLAD", 2, "STRIKE", false, false));
        store.cardOffers.Add(Card("r2", true, "SILENT", 1, "STRIKE", false, true));

        // 单人+铁甲+第1幕+首次：offer2 pick1
        PersonalCardSlice slice = WakuuPersonalQuery.CountCardOffers(
            store, "strike", isMulti: false, character: "IRONCLAD", act: 1, isRepeat: false);
        Assert.Multiple(() =>
        {
            Assert.That(slice.Offered, Is.EqualTo(2));
            Assert.That(slice.Picked, Is.EqualTo(1));
            Assert.That(slice.PickRate, Is.EqualTo(0.5));
        });

        // 全量（不分模式角色）：offer5
        PersonalCardSlice all = WakuuPersonalQuery.CountCardOffers(store, "STRIKE");
        Assert.That(all.Offered, Is.EqualTo(5));
    }

    [Test]
    public void 胜负归因_点过为held_整局未点但展示过为skipped()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "win"));
        store.runs.Add(Run("r2", false, "loss"));
        store.runs.Add(Run("r3", false, "win"));
        // r1 点了（held, win）
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "CLASH", false, true));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "CLASH", true, false));
        // r2 展示了但整局没点（skipped, loss）
        store.cardOffers.Add(Card("r2", false, "IRONCLAD", 1, "CLASH", false, false));
        // r3 点过两次（held, win，同局去重）
        store.cardOffers.Add(Card("r3", false, "IRONCLAD", 1, "CLASH", false, true));
        store.cardOffers.Add(Card("r3", false, "IRONCLAD", 1, "CLASH", true, true));

        PersonalWinSlice win = WakuuPersonalQuery.CountCardWinSlice(store, "CLASH", isMulti: false, character: "IRONCLAD");
        Assert.Multiple(() =>
        {
            Assert.That(win.HeldRuns, Is.EqualTo(2));   // r1、r3
            Assert.That(win.HeldWins, Is.EqualTo(2));   // r1、r3 都赢了
            Assert.That(win.SkippedRuns, Is.EqualTo(1)); // r2
            Assert.That(win.SkippedWins, Is.EqualTo(0));
            Assert.That(win.WinRateHeld, Is.EqualTo(1.0));
            Assert.That(win.WinRateSkipped, Is.EqualTo(0.0));
        });
    }

    [Test]
    public void 胜负归因_abandon局不进胜率分母()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "abandon"));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "CLASH", false, true));

        PersonalWinSlice win = WakuuPersonalQuery.CountCardWinSlice(store, "CLASH");
        Assert.Multiple(() =>
        {
            Assert.That(win.HeldRuns, Is.EqualTo(0)); // abandon 不计 held
            Assert.That(win.HeldWins, Is.EqualTo(0));
        });
        // 统计口径：abandon 局不计入 offer/pick（与胜负归因同一批已结束数据）
        Assert.That(WakuuPersonalQuery.CountCardOffers(store, "CLASH").Offered, Is.EqualTo(0));
    }

    [Test]
    public void 决策信号_多人优先多人切片_样本足够则命中()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("m1", true, "win"));
        store.runs.Add(Run("m2", true, "win"));
        store.runs.Add(Run("s1", false, "loss"));
        // 多人切片 3 offer 且高胜率
        store.cardOffers.Add(Card("m1", true, "IRONCLAD", 1, "PERFECT_STRIKE", false, true));
        store.cardOffers.Add(Card("m2", true, "IRONCLAD", 1, "PERFECT_STRIKE", false, true));
        store.cardOffers.Add(Card("m1", true, "SILENT", 1, "PERFECT_STRIKE", false, false));
        // 单人切片也有（低胜率，不应优先）
        store.cardOffers.Add(Card("s1", false, "IRONCLAD", 1, "PERFECT_STRIKE", false, false));

        // 瓦库在多人局：偏好多人切片命中 → pickRate=2/3
        WakuuCardSignal? signal = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "PERFECT_STRIKE", isMultiPreference: true, characterPreference: "IRONCLAD");
        Assert.That(signal, Is.Not.Null);
        WakuuCardSignal value = signal!.Value;
        Assert.Multiple(() =>
        {
            Assert.That(value.OfferCount, Is.EqualTo(3));
            Assert.That(value.PickRate, Is.EqualTo(2.0 / 3.0));
        });
    }

    [Test]
    public void 决策信号_偏好切片样本不足_放宽到其他模式()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("s1", false, "win"));
        // 只有单人数据 3 条
        store.cardOffers.Add(Card("s1", false, "IRONCLAD", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("s1", false, "IRONCLAD", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("s1", false, "IRONCLAD", 1, "CARD_A", true, false));

        // 在多人局里查（偏好 multi），multi 无数据 → 放宽到全量单人数据命中
        WakuuCardSignal? signal = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: true, characterPreference: null);
        Assert.That(signal, Is.Not.Null);
        Assert.That(signal!.Value.OfferCount, Is.EqualTo(3));
    }

    [Test]
    public void 决策信号_样本不足_返回null()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "win")); // 已结束局
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "CARD_A", false, true));

        WakuuCardSignal? signal = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: false, characterPreference: null);
        Assert.That(signal, Is.Null); // offered=1 < 门槛 3
    }

    [Test]
    public void 进行中的局_不计入统计()
    {
        PersonalStore store = MakeStore();
        // r1 已在 runs 里但结果 ongoing（未结束），r2 有 win 结果
        store.runs.Add(Run("r1", false, "ongoing"));
        store.runs.Add(Run("r2", false, "win"));
        store.cardOffers.Add(Card("r1", false, "IRONCLAD", 1, "CARD_A", false, true)); // 未结束局 offer
        store.cardOffers.Add(Card("r2", false, "IRONCLAD", 1, "CARD_A", false, true)); // 已结束局 offer

        PersonalCardSlice slice = WakuuPersonalQuery.CountCardOffers(store, "CARD_A");
        Assert.Multiple(() =>
        {
            Assert.That(slice.Offered, Is.EqualTo(1)); // 只计 r2
            Assert.That(slice.Picked, Is.EqualTo(1));
        });
    }

    [Test]
    public void 事件切片与归因()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "win"));
        store.runs.Add(Run("r2", false, "loss"));
        // r1 这一页 3 个选项，点了 B
        store.eventChoices.Add(Evt("r1", false, "IRONCLAD", 1, "EVENT1", "OPT_A", false));
        store.eventChoices.Add(Evt("r1", false, "IRONCLAD", 1, "EVENT1", "OPT_B", true));
        store.eventChoices.Add(Evt("r1", false, "IRONCLAD", 1, "EVENT1", "OPT_C", false));
        // r2 这一页点了 C（B 展示过但整局没点）
        store.eventChoices.Add(Evt("r2", false, "IRONCLAD", 1, "EVENT1", "OPT_B", false));
        store.eventChoices.Add(Evt("r2", false, "IRONCLAD", 1, "EVENT1", "OPT_C", true));

        PersonalEventSlice sliceB = WakuuPersonalQuery.CountEventOptionSlice(store, "EVENT1", "OPT_B", isMulti: false);
        Assert.Multiple(() =>
        {
            Assert.That(sliceB.Offered, Is.EqualTo(2));
            Assert.That(sliceB.Chosen, Is.EqualTo(1));
            Assert.That(sliceB.ChosenRate, Is.EqualTo(0.5));
        });

        PersonalWinSlice winB = WakuuPersonalQuery.CountEventWinSlice(store, "EVENT1", "OPT_B", isMulti: false);
        Assert.Multiple(() =>
        {
            Assert.That(winB.HeldRuns, Is.EqualTo(1));   // r1
            Assert.That(winB.HeldWins, Is.EqualTo(1));
            Assert.That(winB.SkippedRuns, Is.EqualTo(1)); // r2
            Assert.That(winB.SkippedWins, Is.EqualTo(0));
        });
    }

    [Test]
    public void JSON往返_字段保留()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", true, "win"));
        store.cardOffers.Add(Card("r1", true, "IRONCLAD", 2, "CLASH", true, true));
        store.eventChoices.Add(Evt("r1", true, "IRONCLAD", 2, "EVENT1", "OPT_A", true));
        store.shopPurchases.Add(Shop("r1", true, "IRONCLAD", 2, WakuuPersonalQuery.ShopKindCard, "PERFECT_STRIKE", 75));
        store.shopPurchases.Add(Shop("r1", true, "IRONCLAD", 2, WakuuPersonalQuery.ShopKindRelic, "MEAT_ON_THE_BONE", 130));
        store.cardRemovals.Add(Rem("r1", true, "IRONCLAD", 2, "CLASH"));

        string json = WakuuPersonalJson.Serialize(store);
        PersonalStore parsed = WakuuPersonalJson.Parse(json)!;

        Assert.Multiple(() =>
        {
            Assert.That(parsed.runs, Has.Count.EqualTo(1));
            Assert.That(parsed.cardOffers, Has.Count.EqualTo(1));
            Assert.That(parsed.eventChoices, Has.Count.EqualTo(1));
            Assert.That(parsed.shopPurchases, Has.Count.EqualTo(2));
            Assert.That(parsed.cardRemovals, Has.Count.EqualTo(1));
            Assert.That(parsed.cardRemovals[0].card, Is.EqualTo("CLASH"));
            Assert.That(parsed.cardOffers[0].card, Is.EqualTo("CLASH"));
            Assert.That(parsed.cardOffers[0].isRepeat, Is.True);
            Assert.That(parsed.cardOffers[0].picked, Is.True);
            Assert.That(parsed.eventChoices[0].optionKey, Is.EqualTo("OPT_A"));
            Assert.That(parsed.runs[0].result, Is.EqualTo("win"));
            Assert.That(parsed.shopPurchases[0].kind, Is.EqualTo(WakuuPersonalQuery.ShopKindCard));
            Assert.That(parsed.shopPurchases[0].item, Is.EqualTo("PERFECT_STRIKE"));
            Assert.That(parsed.shopPurchases[0].goldSpent, Is.EqualTo(75));
            Assert.That(parsed.shopPurchases[1].item, Is.EqualTo("MEAT_ON_THE_BONE"));
        });
    }

    [Test]
    public void 商店购买记录_随局清理_已结束局保留()
    {
        PersonalStore store = MakeStore();
        long now = 200L * 24 * 60 * 60 * 1000;
        // 已结束局里的购买 → 保留
        store.runs.Add(Run("finished", false, "win"));
        store.shopPurchases.Add(Shop("finished", false, "IRONCLAD", 1, WakuuPersonalQuery.ShopKindCard, "CARD_A", 75));
        // 未结束旧局里的购买 → 清理
        store.shopPurchases.Add(Shop("old", false, "IRONCLAD", 1, WakuuPersonalQuery.ShopKindPotion, "POTION_X", 50));
        // 未结束新局里的购买 → 保留（跨会话归因，近期 ts）
        PersonalShopPurchaseRecord fresh = Shop("fresh", false, "IRONCLAD", 1, WakuuPersonalQuery.ShopKindRelic, "RELIC_Y", 120);
        fresh.ts = now;
        store.shopPurchases.Add(fresh);

        WakuuPersonalQuery.PruneStale(store, now, maxAgeDays: 60);

        Assert.Multiple(() =>
        {
            Assert.That(store.shopPurchases.Any((s) => s.item == "CARD_A"), Is.True);
            Assert.That(store.shopPurchases.Any((s) => s.item == "POTION_X"), Is.False);
            Assert.That(store.shopPurchases.Any((s) => s.item == "RELIC_Y"), Is.True);
        });
    }

    [Test]
    public void 默认档位等于角色优先_与现状一致()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", true, "win"));
        store.runs.Add(Run("r2", true, "win"));
        store.runs.Add(Run("r3", true, "win"));
        store.cardOffers.Add(Card("r1", true, "IRONCLAD", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("r2", true, "IRONCLAD", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("r3", true, "IRONCLAD", 1, "CARD_A", false, false));

        // 不带档位参数的调用 = 显式 characterFirst
        WakuuCardSignal? implicitSignal = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: true, characterPreference: "IRONCLAD");
        WakuuCardSignal? explicitSignal = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: true, characterPreference: "IRONCLAD",
            tierPreference: WakuuPersonalQuery.PersonalTierCharacterFirst);
        Assert.Multiple(() =>
        {
            Assert.That(implicitSignal, Is.Not.Null);
            Assert.That(explicitSignal, Is.Not.Null);
            Assert.That(implicitSignal!.Value.OfferCount, Is.EqualTo(explicitSignal!.Value.OfferCount));
            Assert.That(implicitSignal.Value.PickRate, Is.EqualTo(explicitSignal.Value.PickRate));
        });
    }

    [Test]
    public void 只看角色档_不用其他角色数据兜底()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("m1", true, "win"));
        store.runs.Add(Run("m2", true, "win"));
        store.runs.Add(Run("m3", true, "win"));
        // 只有 SILENT 在多人局里大量拿过 CARD_A（≥3），IRONCLAD 自己一条都没有
        store.cardOffers.Add(Card("m1", true, "SILENT", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("m2", true, "SILENT", 1, "CARD_A", false, true));
        store.cardOffers.Add(Card("m3", true, "SILENT", 1, "CARD_A", false, false));

        // 角色优先/总量优先都会放宽到「模式×任意角色」兜底命中
        WakuuCardSignal? relaxed = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: true, characterPreference: "IRONCLAD");
        // 只看角色：IRONCLAD 自身两档都无数据 → null
        WakuuCardSignal? charOnly = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_A", isMultiPreference: true, characterPreference: "IRONCLAD",
            tierPreference: WakuuPersonalQuery.PersonalTierCharacterOnly);
        Assert.Multiple(() =>
        {
            Assert.That(relaxed, Is.Not.Null);
            Assert.That(charOnly, Is.Null);
        });
    }

    [Test]
    public void 总量优先档_跳过跨模式单角色档()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("m1", true, "win"));
        store.runs.Add(Run("m2", true, "win"));
        store.runs.Add(Run("s1", false, "win"));
        store.runs.Add(Run("s2", false, "win"));
        store.runs.Add(Run("s3", false, "win"));
        // 模式×角色（multi×IRONCLAD）只有 2 → 不足
        store.cardOffers.Add(Card("m1", true, "IRONCLAD", 1, "CARD_B", false, true));
        store.cardOffers.Add(Card("m2", true, "IRONCLAD", 1, "CARD_B", false, false));
        // 跨模式本角色（char only）另有 3 条 → 角色优先在第 ③ 档命中
        store.cardOffers.Add(Card("s1", false, "IRONCLAD", 1, "CARD_B", false, true));
        store.cardOffers.Add(Card("s2", false, "IRONCLAD", 1, "CARD_B", false, true));
        store.cardOffers.Add(Card("s3", false, "IRONCLAD", 1, "CARD_B", false, false));
        // 别的角色（SILENT 单人）还贡献 1 条 → 全量比「本角色任意模式」更大
        store.cardOffers.Add(Card("s1", false, "SILENT", 1, "CARD_B", false, true));

        WakuuCardSignal? charFirst = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_B", isMultiPreference: true, characterPreference: "IRONCLAD",
            tierPreference: WakuuPersonalQuery.PersonalTierCharacterFirst);
        WakuuCardSignal? volumeFirst = WakuuPersonalQuery.TryGetCardDecisionSignal(
            store, "CARD_B", isMultiPreference: true, characterPreference: "IRONCLAD",
            tierPreference: WakuuPersonalQuery.PersonalTierVolumeFirst);
        Assert.Multiple(() =>
        {
            Assert.That(charFirst, Is.Not.Null);
            Assert.That(charFirst!.Value.OfferCount, Is.EqualTo(5)); // 本角色任意模式（2 多 + 3 单）
            Assert.That(volumeFirst, Is.Not.Null);
            Assert.That(volumeFirst!.Value.OfferCount, Is.EqualTo(6)); // 直接全量（含 SILENT 的 1 条）
        });
    }

    [Test]
    public void 删牌统计_按卡计数与占比分母()
    {
        PersonalStore store = MakeStore();
        store.runs.Add(Run("r1", false, "win"));
        store.runs.Add(Run("r2", false, "loss"));
        store.runs.Add(Run("r3", true, "abandon")); // abandon 不计
        store.cardRemovals.Add(Rem("r1", false, "IRONCLAD", 1, "CLASH"));
        store.cardRemovals.Add(Rem("r2", false, "IRONCLAD", 2, "CLASH"));
        store.cardRemovals.Add(Rem("r3", true, "IRONCLAD", 1, "CLASH")); // abandon 局 → 不计
        store.cardRemovals.Add(Rem("r2", false, "IRONCLAD", 2, "STRIKE"));
        store.cardRemovals.Add(Rem("r1", false, "SILENT", 1, "CLASH")); // 别的角色 → 不计入 IRONCLAD 切片

        Assert.Multiple(() =>
        {
            Assert.That(WakuuPersonalQuery.CountCardRemovals(store, "CLASH", character: "IRONCLAD"), Is.EqualTo(2));
            Assert.That(WakuuPersonalQuery.CountAllCardRemovals(store, character: "IRONCLAD"), Is.EqualTo(3)); // CLASH×2 + STRIKE×1
            // 全量（含 SILENT）：CLASH 3 次
            Assert.That(WakuuPersonalQuery.CountCardRemovals(store, "CLASH"), Is.EqualTo(3));
        });
    }

    [Test]
    public void 清理过期未结束局_保留近期()
    {
        PersonalStore store = MakeStore();
        long now = 200L * 24 * 60 * 60 * 1000; // 距今约 200 天
        // 未结束的旧局 → 清理
        store.cardOffers.Add(new PersonalCardOfferRecord
        {
            runKey = "old", isMulti = false, character = "IRONCLAD", act = 1,
            card = "CARD_A", isRepeat = false, picked = true, ts = 10L * 24 * 60 * 60 * 1000,
        });
        // 未结束的新局 → 保留
        store.cardOffers.Add(new PersonalCardOfferRecord
        {
            runKey = "new", isMulti = false, character = "IRONCLAD", act = 1,
            card = "CARD_B", isRepeat = false, picked = true, ts = now,
        });
        // 已结束局 → 保留
        store.runs.Add(Run("finished", false, "win"));
        store.cardOffers.Add(new PersonalCardOfferRecord
        {
            runKey = "finished", isMulti = false, character = "IRONCLAD", act = 1,
            card = "CARD_C", isRepeat = false, picked = true, ts = 10L * 24 * 60 * 60 * 1000,
        });

        WakuuPersonalQuery.PruneStale(store, now, maxAgeDays: 60);

        Assert.Multiple(() =>
        {
            Assert.That(store.cardOffers.Any((c) => c.card == "CARD_A"), Is.False); // 旧+未结束 → 删
            Assert.That(store.cardOffers.Any((c) => c.card == "CARD_B"), Is.True);  // 新+未结束 → 留
            Assert.That(store.cardOffers.Any((c) => c.card == "CARD_C"), Is.True);  // 已结束 → 留
        });
    }
}
