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
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库商店自动化 v1（Phase 4，可行性分析 §9.3，默认关 shopAssist）：
/// 瓦库角色的商店库存被打开（NMerchantInventory.Initialize）后，等界面就绪，
/// 对角色卡 + 无色卡按「社区统计胜率 ≥ 门槛 + 支付后保留 ≥ 金币保底」自动买卡。
/// 遗物/药水与删牌服务自动化留待后续增量（遗物/药水无社区统计，删牌服务需选牌作用域驱动）。
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
        List<WakuuMerchantCardCandidate> candidates = new(entries.Count);
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
            candidates.Add(new WakuuMerchantCardCandidate(card.Id.Entry, SafeCost(entry), winRate));
        }

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

            if (index < 0 || index >= entries.Count)
            {
                continue;
            }

            MerchantCardEntry entry = entries[index];
            int price = candidates[index].Price;
            try
            {
                bool success = await entry.OnTryPurchaseWrapper(inventory, ignoreCost: false);
                if (success)
                {
                    bought++;
                    spent += price;
                    LocalMultiControlLogger.Info(
                        $"瓦库商店自动买卡成功: player={player.NetId}, card={candidates[index].CardId}, gold={price}");
                }
                else
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库商店自动买卡未成交: player={player.NetId}, card={candidates[index].CardId}（可能已被买走/金币变动）");
                }
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库商店自动买卡失败: player={player.NetId}, card={candidates[index].CardId}, error={exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"瓦库商店自动买卡完成: player={player.NetId}, 买了={bought}, 花={spent}");
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
