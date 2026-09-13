using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 「一批真人可见的卡牌候选 → 点中的进牌组」的共用快照/差分工具。
/// 卡牌奖励（CardReward.OnSelect）与事件网格选 N 张入卡组（EventModel.SelectCardsToAddToDeckFromGrid）
/// 两处同构：前缀快照候选 + 牌组构成，结算完成后用牌组前后对比算出点中的卡——
/// 牌组对比才是 ground truth（`_cards`/候选 list 不一定被剪掉，差分法不可靠，r58/r60 教训）。
/// </summary>
internal static class PersonalCardBatchTracker
{
    internal sealed class Snapshot
    {
        public Player? Owner;

        public List<string> Offered = new();

        public List<bool> RepeatFlags = new();

        /// <summary>选牌前主牌组中各卡 id 的数量（用于结算后对比出点中的卡）。</summary>
        public Dictionary<string, int> DeckCounts = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>取快照；owner 为空 / 候选全空返回 null（不记录）。cardIds 为候选卡的裸 id。</summary>
    internal static Snapshot? TakeSnapshot(Player? owner, IEnumerable<string?> cardIds)
    {
        if (owner == null)
        {
            return null;
        }

        Snapshot snapshot = new() { Owner = owner };
        foreach (string? raw in cardIds)
        {
            if (string.IsNullOrEmpty(raw))
            {
                continue;
            }

            string cardId = raw!;
            snapshot.Offered.Add(cardId);
            snapshot.RepeatFlags.Add(DeckContainsSame(owner, cardId));
        }

        if (snapshot.Offered.Count == 0)
        {
            return null;
        }

        CountDeck(owner, snapshot.DeckCounts);
        return snapshot;
    }

    /// <summary>
    /// 结算完成后按牌组前后对比算点中的卡。picked 为空（整批跳过 / 选了替代项 / 没有真取舍）返回 false。
    /// </summary>
    internal static bool TryDiffPicked(
        Snapshot snapshot,
        out List<(string CardId, bool IsRepeat)> offers,
        out List<string> picked)
    {
        offers = new List<(string, bool)>();
        picked = new List<string>();
        if (snapshot.Owner == null || snapshot.Offered.Count == 0)
        {
            return false;
        }

        Dictionary<string, int> deckNow = new(StringComparer.OrdinalIgnoreCase);
        CountDeck(snapshot.Owner, deckNow);

        for (int i = 0; i < snapshot.Offered.Count; i++)
        {
            string offeredId = snapshot.Offered[i];
            offers.Add((offeredId, snapshot.RepeatFlags[i]));

            int before = snapshot.DeckCounts.TryGetValue(offeredId, out int b) ? b : 0;
            int after = deckNow.TryGetValue(offeredId, out int a) ? a : 0;
            if (after > before)
            {
                picked.Add(offeredId);
            }
        }

        return picked.Count > 0;
    }

    /// <summary>牌组（PileType.Deck，房与房之间持有的主牌堆）里是否已有同 id 卡（首次/重复判定）。</summary>
    private static bool DeckContainsSame(Player owner, string cardId)
    {
        try
        {
            return owner.Deck.Cards.Any((c) => c?.Id != null
                                               && string.Equals(c.Id.Entry, cardId, StringComparison.OrdinalIgnoreCase));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>统计主牌组各卡 id 数量（忽略大小写，写入 target）。</summary>
    private static void CountDeck(Player owner, Dictionary<string, int> target)
    {
        try
        {
            foreach (CardModel card in owner.Deck.Cards)
            {
                if (card?.Id == null)
                {
                    continue;
                }

                string id = card.Id.Entry;
                target[id] = target.TryGetValue(id, out int count) ? count + 1 : 1;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-读取牌组失败(忽略): {exception.Message}");
        }
    }
}

/// <summary>
/// 个人偏好记录器的「整局结束」钩子：RunManager.OnEnded 是整个 run 的终点，
/// 前缀时 State/种子都还在，据此给本局补写 win/loss 结果（abandon 局数据直接丢弃）。
/// 记录器开关（personalRecorder）关闭时不做任何事。
/// </summary>
[HarmonyPatch(typeof(RunManager), nameof(RunManager.OnEnded))]
internal static class PersonalRunEndPatch
{
    /// <summary>
    /// 原版 OnEnded 的重入护栏：WinRun 在 OnEnded(true) 后还会 GuaranteeKillAllPlayers()，
    /// 触发 CreatureCmd.Kill → 第二次 OnEnded(false)；原版靠 _runHistoryWasUploaded 第二次直接早退。
    /// 我们的前缀跑在护栏之前，必须自己守住同一语义，否则一局会被记成 win+loss 两条。
    /// </summary>
    private static readonly FieldInfo? RunHistoryUploadedField = AccessTools.Field(typeof(RunManager), "_runHistoryWasUploaded");

    [HarmonyPrefix]
    private static void Prefix(bool isVictory)
    {
        if (!LocalPersonalRecorder.IsEnabled)
        {
            return;
        }

        try
        {
            RunManager? runManager = RunManager.Instance;
            if (runManager == null)
            {
                return;
            }

            // 重入（本局结果已记过）→ 跳过，与原版 _runHistoryWasUploaded 早退语义一致
            if (RunHistoryUploadedField?.GetValue(runManager) is true)
            {
                return;
            }

            bool abandoned = runManager.IsAbandoned;
            LocalPersonalRecorder.RecordRunEnded(isVictory, abandoned);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-整局结束钩子异常(忽略): {exception.Message}");
        }
    }
}

/// <summary>
/// 个人偏好记录器的「真人点选卡牌奖励」钩子。
///
/// 为什么挂在 CardReward.OnSelect（而不是 RewardsSetSynchronizer.SelectLocalReward）：
/// ① 本 mod 的本地多控合并领奖屏，真人点按钮走 NRewardButton.GetReward →
///    NRewardButtonMergedRewardSelectPatch → 直调 reward.SelectUnsynchronized()，
///    **完全绕过 RewardsSetSynchronizer**，挂同步器会一条都记不到（2026-09-05 双人局实测：
///    拿到 27 张卡，cardOffers 落盘 0 条）。两条路径的公共下游就是 Reward.SelectUnsynchronized
///    → CardReward.OnSelect，挂这里单机/合并/事件自定义奖励全覆盖。
/// ② SelectLocalReward 是 async 方法，Harmony 后缀在第一个 await 就返回，那时玩家还没选牌、
///    牌组也没新增卡，前后对比恒为空——即使在单机路径下同样记不到。
///
/// 瓦库数据的排除：自动领奖走 LocalWakuuRewardAutoClaim.TrySettleAsync，期间
/// AutoClaimCardOwnerId 置位（AsyncLocal 作用域），据此跳过；真人点选天然不在该作用域内。
/// </summary>
[HarmonyPatch(typeof(CardReward), "OnSelect")]
internal static class PersonalCardRewardPickPatch
{
    [HarmonyPrefix]
    private static void Prefix(CardReward __instance, ref PersonalCardBatchTracker.Snapshot? __state)
    {
        if (!LocalPersonalRecorder.IsEnabled || TestMode.IsOn)
        {
            return;
        }

        try
        {
            Player? rewardOwner = __instance.Player;
            if (rewardOwner == null)
            {
                return;
            }

            // 瓦库自动领取（Selector 自动作答、不弹屏）不是真人决策 —— **按归属者比较**（r130）：
            // 只看"作用域非空"会把真人自己的奖励一起漏记（AsyncLocal 会沿异步链残留，
            // 详见 WakuuRecordScopePolicy）。
            if (WakuuRecordScopePolicy.IsAutoScopeOwnedBy(
                    LocalWakuuRewardAutoClaim.AutoClaimCardOwnerId, rewardOwner.NetId))
            {
                return;
            }

            __state = PersonalCardBatchTracker.TakeSnapshot(
                rewardOwner,
                __instance.Cards.Select((c) => c?.Id?.Entry));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-卡牌奖励快照异常(忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// OnSelect 是 async 方法，本后缀在它交出 Task 时就执行（玩家还没选完）。
    /// 因此不能直接在这里算牌组差分，必须把记录挂到返回 Task 的续体上，
    /// 等 OnSelect 整条（含 CardPileCmd.Add 落袋）跑完再结算。
    /// </summary>
    [HarmonyPostfix]
    private static void Postfix(ref Task<bool> __result, ref PersonalCardBatchTracker.Snapshot? __state)
    {
        if (__state == null || __state.Owner == null)
        {
            return;
        }

        PersonalCardBatchTracker.Snapshot snapshot = __state;
        Task<bool> original = __result;
        if (original == null)
        {
            return;
        }

        __result = RecordAfterSelectionAsync(original, snapshot);
    }

    private static async Task<bool> RecordAfterSelectionAsync(Task<bool> original, PersonalCardBatchTracker.Snapshot snapshot)
    {
        bool success = false;
        try
        {
            success = await original;
        }
        finally
        {
            // 选牌失败/取消也要走这里，但牌组没有新增时不会落账
            try
            {
                if (PersonalCardBatchTracker.TryDiffPicked(snapshot, out List<(string, bool)> offers, out List<string> picked))
                {
                    LocalPersonalRecorder.RecordHumanCardRewardBatch(snapshot.Owner!, offers, picked);
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人记录-卡牌奖励结算异常(忽略): {exception.Message}");
            }
        }

        return success;
    }
}

/// <summary>
/// 个人偏好记录器的「真人点选事件网格、选 N 张入卡组」钩子（Phase 1.5 覆盖缺口，
/// 脑蛭「分享知识」等不可跳过的 FromSimpleGridForRewards 路径——此前不记会系统性漏掉
/// 这批"入卡组"决策，且其中"展示了但没选"的候选是高质量负信号）。
///
/// 锚点：EventModel.SelectCardsToAddToDeckFromGrid 是「网格选完 → 加入主牌组」的公共封装，
/// 挂它能在选牌真正进牌组之后再结算（牌组差分 ground truth），并且天然只覆盖"选牌入卡组"
/// 类事件（遗物 SeaGlass / SealedDeck 那种直调 FromSimpleGridForRewards 的不进这张表）。
///
/// 排除：瓦库自动选事件期间直调 option.Chosen()，InEventAutoChoiceScope 置位 → 跳过；
/// 事件给的就一张、无条件入组（无真实取舍）→ 跳过。
/// </summary>
[HarmonyPatch(typeof(EventModel), "SelectCardsToAddToDeckFromGrid")]
internal static class PersonalGridCardPickPatch
{
    [HarmonyPrefix]
    private static void Prefix(
        EventModel __instance,
        List<CardCreationResult> cards,
        CardSelectorPrefs prefs,
        ref PersonalCardBatchTracker.Snapshot? __state)
    {
        if (!LocalPersonalRecorder.IsEnabled || TestMode.IsOn)
        {
            return;
        }

        try
        {
            // 瓦库事件自动选择作用域内（直调 Chosen）不是真人决策 —— 按归属者比较（r130）
            if (LocalWakuuEventAutoChoice.IsAutoChoosingFor(__instance.Owner))
            {
                return;
            }

            if (cards == null || cards.Count == 0)
            {
                return;
            }

            // 无真实取舍：只有 ≤ 最少可选张且不要求确认 → 自动全选入组，不是"从 N 张里挑"
            if (!prefs.RequireManualConfirmation && cards.Count <= prefs.MinSelect)
            {
                return;
            }

            __state = PersonalCardBatchTracker.TakeSnapshot(
                __instance.Owner,
                cards.Select((c) => c?.Card?.Id?.Entry));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-网格入卡组快照异常(忽略): {exception.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task __result, ref PersonalCardBatchTracker.Snapshot? __state)
    {
        if (__state == null || __state.Owner == null)
        {
            return;
        }

        PersonalCardBatchTracker.Snapshot snapshot = __state;
        Task original = __result;
        if (original == null)
        {
            return;
        }

        __result = RecordAfterSelectionAsync(original, snapshot);
    }

    private static async Task RecordAfterSelectionAsync(Task original, PersonalCardBatchTracker.Snapshot snapshot)
    {
        try
        {
            await original;
        }
        finally
        {
            try
            {
                if (PersonalCardBatchTracker.TryDiffPicked(snapshot, out List<(string, bool)> offers, out List<string> picked))
                {
                    LocalPersonalRecorder.RecordHumanGridCardBatch(snapshot.Owner!, offers, picked);
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人记录-网格入卡组结算异常(忽略): {exception.Message}");
            }
        }
    }
}

/// <summary>
/// 个人偏好记录器的「真人商店购买」钩子（用户口径：只记「买了」这一种真人决策）。
///
/// 锚点：MerchantEntry.OnTryPurchaseWrapper 是所有商店条目（卡/遗物/药水）购买成功后的公共出口；
/// 删卡服务（MerchantCardRemovalEntry）用三参重载自己走另一条链，天然不进这里。
/// 前缀在扣款/清栏之前快照"这一格卖的是什么 + 应付金币"（购买成功后条目会被 Clear/Restock，
/// 后缀再读就拿不到了），后缀把记录挂到返回 Task 上、购买成功才入账。
///
/// 目前商店只有真人会买（瓦库商店自动化是 Phase 4，届时瓦库自动购买需在此处加作用域排除）。
/// </summary>
[HarmonyPatch(typeof(MerchantEntry), nameof(MerchantEntry.OnTryPurchaseWrapper))]
internal static class PersonalShopPurchasePatch
{
    private sealed class Snapshot
    {
        public Player? Owner;

        public string Kind = string.Empty;

        public string Item = string.Empty;

        public int GoldSpent;
    }

    private static readonly FieldInfo? EntryPlayerField = AccessTools.Field(typeof(MerchantEntry), "_player");

    [HarmonyPrefix]
    private static void Prefix(MerchantEntry __instance, bool ignoreCost, ref Snapshot? __state)
    {
        if (!LocalPersonalRecorder.IsEnabled || TestMode.IsOn)
        {
            return;
        }

        try
        {
            Player? owner = EntryPlayerField?.GetValue(__instance) as Player;
            if (owner == null)
            {
                return;
            }

            // 瓦库商店自动采购（shopAssist）不是真人决策 —— **按归属者比较**（r130）：
            // 只看"作用域非空"会把真人自己的购买一起吞掉（AsyncLocal 会沿异步链残留，
            // 这正是商店购买记录长期只有 2 行的原因；详见 WakuuRecordScopePolicy）。
            if (WakuuRecordScopePolicy.IsAutoScopeOwnedBy(
                    LocalWakuuMerchantAuto.PurchaseOwnerId, owner.NetId))
            {
                return;
            }

            string kind;
            string? itemId;
            switch (__instance)
            {
                case MerchantCardEntry cardEntry when cardEntry.CreationResult?.Card?.Id != null:
                    kind = WakuuPersonalQuery.ShopKindCard;
                    itemId = cardEntry.CreationResult!.Card!.Id.Entry;
                    break;
                case MerchantRelicEntry relicEntry when relicEntry.Model?.Id != null:
                    kind = WakuuPersonalQuery.ShopKindRelic;
                    itemId = relicEntry.Model!.Id.Entry;
                    break;
                case MerchantPotionEntry potionEntry when potionEntry.Model?.Id != null:
                    kind = WakuuPersonalQuery.ShopKindPotion;
                    itemId = potionEntry.Model!.Id.Entry;
                    break;
                default:
                    // 删卡服务等不记（花钱删牌不是"买了什么"）
                    return;
            }

            if (string.IsNullOrEmpty(itemId))
            {
                return;
            }

            __state = new Snapshot
            {
                Owner = owner,
                Kind = kind,
                Item = itemId,
                GoldSpent = ignoreCost ? 0 : __instance.Cost,
            };
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-商店购买快照异常(忽略): {exception.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task<bool> __result, ref Snapshot? __state)
    {
        if (__state == null || __state.Owner == null)
        {
            return;
        }

        Snapshot snapshot = __state;
        Task<bool> original = __result;
        if (original == null)
        {
            return;
        }

        __result = RecordIfPurchasedAsync(original, snapshot);
    }

    private static async Task<bool> RecordIfPurchasedAsync(Task<bool> original, Snapshot snapshot)
    {
        bool success = false;
        try
        {
            success = await original;
        }
        finally
        {
            try
            {
                if (success)
                {
                    LocalPersonalRecorder.RecordShopPurchase(snapshot.Owner!, snapshot.Kind, snapshot.Item, snapshot.GoldSpent);
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人记录-商店购买结算异常(忽略): {exception.Message}");
            }
        }

        return success;
    }
}

/// <summary>
/// 个人偏好记录器的「真人删牌」钩子（删牌统计，Phase 4）：事件删牌 / 营地删牌 / 商店删牌服务 / 删牌遗物
/// 最终都会走到 <c>CardSelectCmd.FromDeckGeneric</c>，返回的正是真人挑出来要删的牌
/// （调用方随后 <c>CardPileCmd.RemoveFromDeck</c>）。
/// 一次选择可能删多张（各记一行）；取消返回空 → 不记。
///
/// r128 → r129 → r130 两次实机定位（都在 2026-09-13）：
/// ① **现象**：商店删牌走完、选牌链路正常结束（`Player …326 chose cards [WATCHER-DEFEND_WATCHER]`），
///    但 `个人记录-删牌` 一条没有、落盘 `personal_stats.json` 的 **cardRemovals 全历史 0 行**，
///    而同局卡牌奖励 / 事件点选记录都正常。
/// ② **真正根因（r130 定位）= 守卫写错了**：原守卫里
///    `… && LocalWakuuMerchantAuto.PurchaseOwnerId == null` 的 `PurchaseOwnerId` 当时是
///    **AsyncLocal 字段本身**（恒非 null）⇒ 该条件恒 false ⇒ **整块守卫恒不成立、永远不记录**。
///    已把该字段收私有、改经值属性 `PurchaseOwnerId` 暴露（与 `AutoClaimCardOwnerId` 同套写法），
///    调用方不再可能把字段当值来比较。⚠ r129 那次我把 `== null` 误翻成 `!= null`（同样的字段/值混淆，
///    方向相反 ⇒ 恒真 ⇒ 每次都跳过），实机日志里的 `跳过: 瓦库商店自动采购作用域内` 就是这么来的。
/// ③ **顺带加固**（不是根因）：
///    - 锚点从入口 `CardSelectCmd.FromDeckForRemoval` 移到 <c>FromDeckGeneric</c>：前者只有 4 行、
///      直接转调后者，属于有被 JIT 内联风险的纯包装方法——「补丁在启动审计里显示挂上了 `[P2Po1T0F0]`」
///      只说明挂上了，不代表会被调用。后者是 async、函数体大，r129 实机已证明它会触发。
///      代价：它同时被 DollysMirror（复制）/ WoodCarvings（变化）复用 ⇒ 必须按 prefs 提示键过滤出
///      「删除」语义（<see cref="WakuuRecordScopePolicy.IsDeckRemovalPrompt"/>）。
///    - 自动化作用域一律**按归属者比较**，不能只判"非空"（AsyncLocal 会沿异步链残留）——
///      见 <see cref="WakuuRecordScopePolicy"/>。
///
/// 排除：瓦库自己的事件自动选择 / 奖励自动领取 / 商店自动采购 / 瓦库形态角色的托管选牌。
/// 每次命中都打一条 INFO（含逐条跳过原因）——避免再出现「没生效」与「没数据」分不清（r118 教训）。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric), new[]
{
    typeof(Player),
    typeof(CardSelectorPrefs),
    typeof(Func<CardModel, bool>),
    typeof(Func<CardModel, int>),
})]
internal static class PersonalDeckRemovalPatch
{
    [HarmonyPostfix]
    private static void Postfix(Player player, CardSelectorPrefs prefs, ref Task<IEnumerable<CardModel>> __result)
    {
        if (__result == null || player == null)
        {
            return;
        }

        // 通用入口：只认「删除」语义（复制 / 变化共用同一条路，不能误记）
        if (!WakuuRecordScopePolicy.IsDeckRemovalPrompt(prefs.Prompt?.LocEntryKey))
        {
            return;
        }

        Task<IEnumerable<CardModel>> original = __result;
        __result = RecordAfterRemovalAsync(original, player, prefs);
    }

    private static async Task<IEnumerable<CardModel>> RecordAfterRemovalAsync(
        Task<IEnumerable<CardModel>> original, Player player, CardSelectorPrefs prefs)
    {
        IEnumerable<CardModel> selected = Array.Empty<CardModel>();
        try
        {
            selected = await original;
        }
        catch
        {
            return Array.Empty<CardModel>();
        }

        if (selected == null)
        {
            return Array.Empty<CardModel>();
        }

        List<CardModel> cards = selected.Where((card) => card?.Id != null).ToList();
        string? blockReason = RemovalRecordBlockReason(player);
        LocalMultiControlLogger.Info(
            $"个人记录-删牌钩子命中: owner={player.NetId}, prompt={prefs.Prompt?.LocEntryKey ?? "?"}, "
            + $"min={prefs.MinSelect}, max={prefs.MaxSelect}, 选中={cards.Count}, "
            + $"瓦库形态={LocalWakuuRelicRuntime.IsVakuuFormMode(player)}, "
            + $"{(blockReason == null ? "记录" : "跳过: " + blockReason)}");

        if (blockReason == null)
        {
            try
            {
                foreach (CardModel card in cards)
                {
                    if (card?.Id == null)
                    {
                        continue;
                    }

                    LocalPersonalRecorder.RecordHumanCardRemoval(player, card.Id.Entry);
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"个人记录-删牌记录异常(忽略): {exception.Message}");
            }
        }

        return selected;
    }

    /// <summary>
    /// 不该记录的原因（null = 应记录）。逐条写进日志，跳过时也能一眼看出卡在哪一步
    /// —— 原实现是整块 `if (...) { 记录 }`，任一条件不满足就静默返回，这是"没生效"与"没数据"分不清的根源。
    ///
    /// ⚠ r130（r129 实机反馈修复）：**自动化作用域一律按归属者比较**，不能只判"非空"。
    /// 这些作用域是 AsyncLocal、会沿异步链残留；2026-09-13 实机（marker r129）里真人自己的
    /// 商店删牌连续两次都被 `跳过: 瓦库商店自动采购作用域内` 吞掉（`PurchaseOwnerId` 是那个**别的角色**
    /// 的自动采购残留值），于是"连删 2 张同名牌"一条都没记上。
    /// </summary>
    private static string? RemovalRecordBlockReason(Player player)
    {
        if (!LocalPersonalRecorder.IsEnabled)
        {
            return "记录器关闭(personalRecorder=off)";
        }

        if (TestMode.IsOn)
        {
            return "TestMode 开启";
        }

        if (LocalWakuuEventAutoChoice.IsAutoChoosingFor(player))
        {
            return "瓦库事件自动选择作用域内（归属者=本人）";
        }

        if (WakuuRecordScopePolicy.IsAutoScopeOwnedBy(
                LocalWakuuRewardAutoClaim.AutoClaimCardOwnerId, player.NetId))
        {
            return "瓦库奖励自动领取作用域内（归属者=本人）";
        }

        if (WakuuRecordScopePolicy.IsAutoScopeOwnedBy(
                LocalWakuuMerchantAuto.PurchaseOwnerId, player.NetId))
        {
            return "瓦库商店自动采购作用域内（归属者=本人）";
        }

        // 瓦库形态角色的一切选牌都是托管产生的（含「获得遗物时删一张」这类**没有作用域标记**的路径），
        // 不可能是真人决策。判据与 WakuuEventEnchantAutoAnswerPatch 同一套。
        if (LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return "瓦库形态角色（托管决策，不是真人）";
        }

        return null;
    }
}

/// <summary>
/// 个人偏好记录器的「真人点选事件选项」钩子。
///
/// 为什么挂 NEventRoom.OptionButtonClicked：真人点事件按钮（OnRelease）最终都会调到这里；
/// 瓦库自动选事件直调 option.Chosen() 不经 UI → 天然排除瓦库数据。
/// 前缀时页面还没清空，能拿到当前页全部选项（offers）与点中的那个（chosen）。
/// 跳过：锁定项 / 继续（proceed，翻页不算决策）/ 共享事件（本地多控里共享事件是群体投票，
/// 不算单人的稳定偏好）。
/// </summary>
[HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.OptionButtonClicked))]
internal static class PersonalEventClickPatch
{
    private static readonly FieldInfo? EventField = AccessTools.Field(typeof(NEventRoom), "_event");

    [HarmonyPrefix]
    private static void Prefix(NEventRoom __instance, EventOption option)
    {
        if (!LocalPersonalRecorder.IsEnabled || TestMode.IsOn)
        {
            return;
        }

        try
        {
            if (option == null || option.IsLocked || option.IsProceed)
            {
                return;
            }

            EventModel? eventModel = EventField?.GetValue(__instance) as EventModel;
            if (eventModel == null || eventModel.Owner == null || eventModel.IsShared || eventModel.IsFinished)
            {
                return;
            }

            IReadOnlyList<EventOption> page = eventModel.CurrentOptions;
            if (page == null || page.Count == 0)
            {
                return;
            }

            // 只记有真实取舍的页：过滤掉锁定/纯"继续"后仍 ≥2 个可选才算决策——
            // 单选项/对话推进页（如 THE_ARCHITECT 结局对话、探索者等待/前进）没有偏好信号，跳过防污染。
            List<EventOption> selectable = page
                .Where((o) => o != null && !o.IsLocked && !o.IsProceed)
                .ToList();
            if (selectable.Count < 2 || !selectable.Contains(option))
            {
                return;
            }

            LocalPersonalRecorder.RecordHumanEventPage(eventModel, option, selectable);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"个人记录-事件点选钩子异常(忽略): {exception.Message}");
        }
    }
}
