using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库商店自动化（Phase 4，可行性分析 §9.3；总开关 shopAssist，默认关）：
/// 瓦库角色的商店库存被打开（NMerchantInventory.Initialize）后，等界面就绪，依次自动：
/// ① 买卡（v1，r64）——角色卡 + 无色卡按「社区统计胜率 ≥ 门槛 + 支付后保留 ≥ 金币保底」；
/// ② 买遗物（v2，2026-09-18，开关 shopAssistBuyRelics）——无评级可查，只按价格 + 金币保底；
/// ③ 买药水（v2，2026-09-18，开关 shopAssistBuyPotions）——同上，且药水栏必须还有空位。
///
/// **删牌服务仍未做**（刻意）：它走
/// <c>RunManager.OneOffSynchronizer.DoLocalMerchantCardRemoval</c>，该方法读**该同步器自己的**
/// <c>_localPlayerId</c>（不是 <see cref="LocalContext"/>）并会广播 <c>MerchantCardRemovalMessage</c>；
/// 本地多控下归属与消息回环需要单独处理，随手接会删错人的牌、或让别的角色重复执行，故单列一轮做。
///
/// 与污浊药水投掷（LocalWakuuMerchantFoulThrow）同入口体系但独立触发时机：库存打开才买。
/// 为什么开库存触发而不是进房触发：合并屏/多控下只有切到该角色的商店视图，其库存才会被
/// Initialize——即"瓦库到店要消费"的可见时机，避免后台凭空花钱。
///
/// 作用域：整个采购过程把 PurchaseOwnerId 置位（AsyncLocal），个人记录器据此不把瓦库自动购买
/// 记成真人决策（与领奖/选事件的排除哲学一致）。
/// </summary>
internal static class LocalWakuuMerchantAuto
{
    /// <summary>等商店 UI 就绪的延迟（房间切换/库存绑定后给界面一帧缓冲）。</summary>
    private const int ReadyDelayMs = 500;

    /// <summary>同一（房间 × 玩家）只自动采购一次，防止反复切库存重复触发。</summary>
    private static readonly HashSet<(AbstractRoom Room, ulong Player)> _handled = new();

    private static readonly object _lock = new();

    /// <summary>
    /// 当前正在瓦库商店自动采购的归属者（沿异步链流动）。个人记录器据此跳过自动购买。
    /// ⚠ 私有 + 经 <see cref="PurchaseOwnerId"/> 属性暴露：**绝不要把 AsyncLocal 字段本身拿去比较**
    /// —— 2026-09-13 的 BUG-12 就是调用方写成 `PurchaseOwnerId == null`（字段恒非 null ⇒ 恒 false），
    /// 于是删牌记录被整块守卫吞掉、全历史 0 行。与
    /// <see cref="LocalWakuuRewardAutoClaim.AutoClaimCardOwnerId"/> 保持同一套「属性给值」的写法。
    /// </summary>
    private static readonly AsyncLocal<ulong?> _purchaseOwnerId = new();

    /// <summary>当前正在自动采购的归属者 NetId（null = 没有在采购）。个人记录器只据此值比较判断。</summary>
    internal static ulong? PurchaseOwnerId => _purchaseOwnerId.Value;

    /// <summary>由 NMerchantInventoryPatch（库存绑定到当前角色后）调用。</summary>
    public static void OnMerchantInventoryShown(Player? player)
    {
        try
        {
            if (!LocalSelfCoopContext.IsEnabled
                || !LocalWakuuAutopilotConfig.ShopAssist
                || !RunManager.Instance.IsInProgress
                || player == null
                || !LocalWakuuRelicRuntime.IsVakuuFormMode(player))
            {
                return;
            }

            if (player.RunState?.CurrentRoom is not MerchantRoom room)
            {
                return;
            }

            lock (_lock)
            {
                if (!_handled.Add((room, player.NetId)))
                {
                    return; // 本店本角色已采购过
                }
            }

            LocalMultiControlLogger.Info($"瓦库商店自动采购启动: player={player.NetId}");
            TaskHelper.RunSafely(RunAsync(room, player));
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库商店自动采购启动失败: {exception.Message}");
        }
    }

    private static async Task RunAsync(MerchantRoom room, Player player)
    {
        ulong? previousNetId = LocalContext.NetId;
        try
        {
            await Task.Delay(ReadyDelayMs);
            if (!RunManager.Instance.IsInProgress || player.Creature?.IsDead == true)
            {
                return;
            }

            MerchantInventory inventory = LocalMerchantInventoryRuntime.GetOrCreateInventory(room, player);
            LocalMerchantInventoryRuntime.BindInventoryToRoom(room, player, inventory);

            AlignContext(player.NetId);
            _purchaseOwnerId.Value = player.NetId;
            try
            {
                await TryAutoBuyCardsAsync(player, inventory);
                await TryAutoBuyRelicsAsync(player, inventory);
                await TryAutoBuyPotionsAsync(player, inventory);
            }
            finally
            {
                _purchaseOwnerId.Value = null;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库商店自动采购异常: player={player.NetId}, error={exception.Message}");
        }
        finally
        {
            RestoreContext(previousNetId);
        }
    }

    private static async Task TryAutoBuyCardsAsync(Player player, MerchantInventory inventory)
    {
        string characterId = player.Character?.Id?.Entry?.ToUpperInvariant() ?? string.Empty;
        List<MerchantCardEntry> entries = inventory.CardEntries.ToList();

        // 候选与条目**成对**收集（2026-09-18 顺手修）：原实现跳过占位卡/未上架条目时只 continue、
        // 不往候选里补位，于是 candidates 的下标与 entries 错开 —— 店里一旦出现 Null 占位卡，
        // 后续 picks 里的下标就会买到"错位的那张"（连读到的价格都是别人的）。成对收集后下标恒等。
        List<(MerchantCardEntry Entry, WakuuMerchantCardCandidate Candidate)> plan = new(entries.Count);
        foreach (MerchantCardEntry entry in entries)
        {
            CardModel? card = entry.CreationResult?.Card;
            if (card?.Id == null)
            {
                continue;
            }

            // 跳过游戏内置的 Null 占位卡（Id.Entry="NULL"，社区统计里也有数据会被选中）——
            // 它不是真正可打出的牌，买下来纯属浪费金币（2026-09-07 实测：自动采购买过 id=NULL）。
            if (card is MegaCrit.Sts2.Core.Models.Cards.Null)
            {
                continue;
            }

            double? winRate = WakuuSkadaAdapter.TryGetCardSignal(characterId, card.Id.Entry)?.WinRateHeld;
            plan.Add((entry, new WakuuMerchantCardCandidate(card.Id.Entry, SafeCost(entry), winRate)));
        }

        List<WakuuMerchantCardCandidate> candidates = plan.Select((p) => p.Candidate).ToList();
        bool buyNoData = LocalWakuuAutopilotConfig.ShopAssistBuyNoData;
        List<int> picks = WakuuMerchantPicking.SelectCardBuys(candidates, player.Gold, buyNoData: buyNoData);
        if (picks.Count == 0)
        {
            // 带上金币与最便宜候选价：不买的原因通常是「付完就跌破保底」，没有这两个数字事后无法判断
            int cheapest = candidates.Count == 0
                ? 0
                : candidates.Min((c) => c.Price);
            LocalMultiControlLogger.Info(
                $"瓦库商店自动买卡: 无符合条件候选，不买。player={player.NetId}, buyNoData={buyNoData}, "
                + $"候选={candidates.Count}, 金币={player.Gold}, 最便宜={cheapest}, "
                + $"有数据门槛胜率≥{WakuuMerchantPicking.DefaultMinBuyWinRate}, 保留≥{WakuuMerchantPicking.DefaultGoldFloor}金");
            return;
        }

        int bought = 0;
        int spent = 0;
        foreach (int index in picks)
        {
            if (!RunManager.Instance.IsInProgress)
            {
                break;
            }

            if (index < 0 || index >= plan.Count)
            {
                continue;
            }

            MerchantCardEntry entry = plan[index].Entry;
            WakuuMerchantCardCandidate candidate = plan[index].Candidate;
            try
            {
                bool success = await entry.OnTryPurchaseWrapper(inventory, ignoreCost: false);
                if (success)
                {
                    bought++;
                    spent += candidate.Price;
                    LocalMultiControlLogger.Info(
                        $"瓦库商店自动买卡成功: player={player.NetId}, card={candidate.CardId}, gold={candidate.Price}");
                }
                else
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库商店自动买卡未成交: player={player.NetId}, card={candidate.CardId}（可能已被买走/金币变动）");
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库商店自动买卡失败: player={player.NetId}, card={candidate.CardId}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"瓦库商店自动买卡完成: player={player.NetId}, 买了={bought}, 花={spent}");
    }

    /// <summary>
    /// 自动买遗物（v2，2026-09-18，开关 <c>shopAssistBuyRelics</c>，默认关）。
    ///
    /// 遗物**没有社区评级**（SkadaHelper 只有卡牌统计，`game-entities.md` 只是实体清单），
    /// 所以只按「买得起 + 付完仍保留 ≥ 金币保底」决策（见 <see cref="WakuuMerchantPicking.SelectPricedBuys"/>）。
    ///
    /// 生效条件 = 外层 <see cref="OnMerchantInventoryShown"/> 已判的「本地多角色模式启用
    /// （<see cref="LocalSelfCoopContext.IsEnabled"/>）+ 该角色是瓦库形态 + 当前在商店房」+ 本开关。
    ///
    /// ⚠ r138 订正：这里原先还有一道 `UseSingleAdventureMode` 门禁（理由写的是"遗物获得可能触发选牌，
    /// 那条链只在单机冒险模式下接管"）—— 那个属性在 <see cref="LocalSelfCoopContext"/> 里是
    /// **`=> true` 的常量**，门禁恒不成立 ⇒ 纯死代码，还让文档/日志描述了一个并不存在的条件。
    /// 遗物获得触发的选牌（如 YUI 灵草丹的变化）由 <see cref="LocalWakuuRelicEffectAutoChoice"/>
    /// 在非战斗期照常自动作答，不需要额外门禁。
    /// </summary>
    private static async Task TryAutoBuyRelicsAsync(Player player, MerchantInventory inventory)
    {
        if (!LocalWakuuAutopilotConfig.ShopAssistBuyRelics)
        {
            return;
        }

        List<MerchantRelicEntry> entries = inventory.RelicEntries.ToList();
        List<(MerchantRelicEntry Entry, WakuuMerchantPricedItem Candidate, string Rarity)> plan = new(entries.Count);
        foreach (MerchantRelicEntry entry in entries)
        {
            RelicModel? relic = entry.Model;
            if (relic?.Id?.Entry == null)
            {
                continue; // 未上架 / 已被买走（ClearAfterPurchase 会把 Model 置 null）
            }

            plan.Add((entry, new WakuuMerchantPricedItem(relic.Id.Entry, SafeCost(entry)), relic.Rarity.ToString()));
        }

        List<WakuuMerchantPricedItem> candidates = plan.Select((p) => p.Candidate).ToList();
        List<int> picks = WakuuMerchantPicking.SelectPricedBuys(candidates, player.Gold);
        if (picks.Count == 0)
        {
            int cheapest = candidates.Count == 0 ? 0 : candidates.Min((c) => c.Price);
            LocalMultiControlLogger.Info(
                $"瓦库商店自动买遗物: 无符合条件候选，不买。player={player.NetId}, "
                + $"候选={candidates.Count}, 金币={player.Gold}, 最便宜={cheapest}, "
                + $"保留≥{WakuuMerchantPicking.DefaultGoldFloor}金");
            return;
        }

        int bought = 0;
        int spent = 0;
        foreach (int index in picks)
        {
            if (!RunManager.Instance.IsInProgress)
            {
                break;
            }

            if (index < 0 || index >= plan.Count)
            {
                continue;
            }

            MerchantRelicEntry entry = plan[index].Entry;
            WakuuMerchantPricedItem candidate = plan[index].Candidate;
            try
            {
                bool success = await entry.OnTryPurchaseWrapper(inventory, ignoreCost: false);
                if (success)
                {
                    bought++;
                    spent += candidate.Price;
                    LocalMultiControlLogger.Info(
                        $"瓦库商店自动买遗物成功: player={player.NetId}, relic={candidate.ItemId}, "
                        + $"rarity={plan[index].Rarity}, gold={candidate.Price}");
                }
                else
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库商店自动买遗物未成交: player={player.NetId}, relic={candidate.ItemId}（可能已被买走/金币变动）");
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库商店自动买遗物失败: player={player.NetId}, relic={candidate.ItemId}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"瓦库商店自动买遗物完成: player={player.NetId}, 买了={bought}, 花={spent}");
    }

    /// <summary>
    /// 自动买药水（v2，2026-09-18，开关 <c>shopAssistBuyPotions</c>，默认关）。
    ///
    /// 与遗物同样**没有评级数据**，只按价格 + 金币保底；额外要求**药水栏还有空位**
    /// （<see cref="Player.HasOpenPotionSlots"/>）—— 原版 `PotionCmd.TryToProcure` 自己也会判空间，
    /// 但先判一层可以避免"白试一次 + 打一条没有意义的失败日志"；每买一瓶后再判一次
    /// （买第 2 瓶时栏位可能刚好满了）。
    /// </summary>
    private static async Task TryAutoBuyPotionsAsync(Player player, MerchantInventory inventory)
    {
        if (!LocalWakuuAutopilotConfig.ShopAssistBuyPotions)
        {
            return;
        }

        if (!player.HasOpenPotionSlots)
        {
            LocalMultiControlLogger.Info(
                $"瓦库商店自动买药水跳过：药水栏已满。player={player.NetId}, 上限={player.MaxPotionCount}");
            return;
        }

        List<MerchantPotionEntry> entries = inventory.PotionEntries.ToList();
        List<(MerchantPotionEntry Entry, WakuuMerchantPricedItem Candidate, string Rarity)> plan = new(entries.Count);
        foreach (MerchantPotionEntry entry in entries)
        {
            PotionModel? potion = entry.Model;
            if (potion?.Id?.Entry == null)
            {
                continue;
            }

            plan.Add((entry, new WakuuMerchantPricedItem(potion.Id.Entry, SafeCost(entry)), potion.Rarity.ToString()));
        }

        List<WakuuMerchantPricedItem> candidates = plan.Select((p) => p.Candidate).ToList();
        List<int> picks = WakuuMerchantPicking.SelectPricedBuys(candidates, player.Gold);
        if (picks.Count == 0)
        {
            int cheapest = candidates.Count == 0 ? 0 : candidates.Min((c) => c.Price);
            LocalMultiControlLogger.Info(
                $"瓦库商店自动买药水: 无符合条件候选，不买。player={player.NetId}, "
                + $"候选={candidates.Count}, 金币={player.Gold}, 最便宜={cheapest}, "
                + $"保留≥{WakuuMerchantPicking.DefaultGoldFloor}金");
            return;
        }

        int bought = 0;
        int spent = 0;
        foreach (int index in picks)
        {
            if (!RunManager.Instance.IsInProgress)
            {
                break;
            }

            if (!player.HasOpenPotionSlots)
            {
                LocalMultiControlLogger.Info(
                    $"瓦库商店自动买药水：药水栏已满，停止后续购买。player={player.NetId}, 已买={bought}");
                break;
            }

            if (index < 0 || index >= plan.Count)
            {
                continue;
            }

            MerchantPotionEntry entry = plan[index].Entry;
            WakuuMerchantPricedItem candidate = plan[index].Candidate;
            try
            {
                bool success = await entry.OnTryPurchaseWrapper(inventory, ignoreCost: false);
                if (success)
                {
                    bought++;
                    spent += candidate.Price;
                    LocalMultiControlLogger.Info(
                        $"瓦库商店自动买药水成功: player={player.NetId}, potion={candidate.ItemId}, "
                        + $"rarity={plan[index].Rarity}, gold={candidate.Price}");
                }
                else
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库商店自动买药水未成交: player={player.NetId}, potion={candidate.ItemId}（可能药水栏已满/金币变动）");
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库商店自动买药水失败: player={player.NetId}, potion={candidate.ItemId}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"瓦库商店自动买药水完成: player={player.NetId}, 买了={bought}, 花={spent}");
    }

    private static int SafeCost(MerchantEntry entry)
    {
        try
        {
            return entry.Cost;
        }
        catch
        {
            return int.MaxValue; // 读不到价格就不买（肯定过不了保底）
        }
    }

    /// <summary>
    /// 采购期间把「本地玩家」上下文对齐到瓦库归属者：RewardSynchronizer 的 SyncLocal*、
    /// 以及商人奖励归属都用 _localPlayerId 定位，不齐会归属到错误的玩家身上。
    /// 结束后由 RestoreContext 恢复。
    /// </summary>
    private static void AlignContext(ulong playerId)
    {
        try
        {
            LocalContext.NetId = playerId;
            LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId);
            SetSynchronizerLocalPlayerId(playerId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库商店自动采购上下文对齐失败(忽略): {exception.Message}");
        }
    }

    private static void SetSynchronizerLocalPlayerId(ulong playerId)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        TrySetLocalPlayerId(RunManager.Instance.RewardsSetSynchronizer, playerId);
        TrySetLocalPlayerId(RunManager.Instance.RewardSynchronizer, playerId);
    }

    private static void TrySetLocalPlayerId(object? target, ulong playerId)
    {
        try
        {
            if (target == null)
            {
                return;
            }

            AccessTools.Field(target.GetType(), "_localPlayerId")?.SetValue(target, playerId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"同步 {target?.GetType()?.Name ?? "?"} 的 _localPlayerId 失败(忽略): {exception.Message}");
        }
    }

    private static void RestoreContext(ulong? previousNetId)
    {
        try
        {
            if (previousNetId.HasValue)
            {
                LocalContext.NetId = previousNetId.Value;
                LocalSelfCoopContext.NetService?.SetCurrentSenderId(previousNetId.Value);
                SetSynchronizerLocalPlayerId(previousNetId.Value);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库商店自动采购上下文恢复失败(忽略): {exception.Message}");
        }
    }
}
