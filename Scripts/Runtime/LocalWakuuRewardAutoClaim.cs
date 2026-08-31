using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库角色战后奖励自动领取（可行性分析·需求三 a + 待确认 #1 拍板）：
/// 卡牌奖励领最左（复用游戏自带 VakuuCardSelector.GetSelectedCardReward 的 FirstOrDefault 语义），
/// 金币/遗物直接结算；药水按 2026-08-25 追加规则领取；非瓦库角色的奖励一律不动。
///
/// 药水领取规则：
/// - 药水栏有空位 → 直接领；
/// - 满栏且栏内有鲜血药水 → 先喝掉腾位再领；
/// - 满栏无鲜血 → 奖励稀有度高于栏内最低稀有度时丢弃栏内最低者再领，否则放弃。
///
/// 实现要点：
/// - 结算走 reward.SelectUnsynchronized()（绕过 RewardsSetSynchronizer；
///   本 mod 单进程回环全员本地，无远端分歧），其内部会从父 RewardsSet 移除该奖励。
/// - CardReward.OnSelect 在 LocalContext.IsMe(Player) 且无弹屏时走游戏原生
///   "Selector.GetSelectedCardReward" 自动作答分支（TestMode 的原生工作方式），
///   因此结算期间需要：①临时把上下文对齐到瓦库玩家；②压入选择器作用域；
///   ③用 NCardRewardSelectionScreenAutoClaimPatch 抑制弹屏（仅在本类标记置位时生效）。
/// - 已结算的奖励从展示列表移除，真人看到的奖励界面干净无干扰。
/// - RewardsCmdOfferCustomPatch 复用本类的单件结算入口处理事件/遗物自定义奖励。
/// </summary>
internal static class LocalWakuuRewardAutoClaim
{
    /// <summary>喝鲜血腾位时等待动作队列处理的超时。</summary>
    private const int BloodDrinkTimeoutMs = 5000;

    private static bool _suppressCardRewardScreen;

    /// <summary>NCardRewardSelectionScreenAutoClaimPatch 读取：true 时 ShowScreen 直接返回 null 不弹屏。</summary>
    internal static bool SuppressCardRewardScreen => _suppressCardRewardScreen;

    /// <summary>
    /// 结算合并奖励列表中归属瓦库角色的可自动领取项，返回剩余需要展示给真人的奖励。
    /// 必须在 CombatRewardMergeContext.Enter() 生效期间调用（抑制镜像复制），
    /// 且在构建展示 RewardsSet 之前调用。
    /// </summary>
    public static async Task<List<Reward>> SettleAsync(List<Reward> mergedRewards)
    {
        List<Reward> remaining = new();
        foreach (Reward reward in mergedRewards)
        {
            Player? owner = reward.Player;
            if (owner == null || !ShouldAutoClaim(reward, owner))
            {
                remaining.Add(reward);
                continue;
            }

            bool settled = await TrySettleAsync(reward, owner);
            if (!settled)
            {
                remaining.Add(reward);
            }
        }

        return remaining;
    }

    private static bool ShouldAutoClaim(Reward reward, Player owner)
    {
        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(owner))
        {
            return false; // 非瓦库角色的奖励保持人工领取
        }

        switch (reward)
        {
            case CardReward:
                return LocalWakuuAutopilotConfig.AutoClaimCards;
            case GoldReward:
            case RelicReward:
                return LocalWakuuAutopilotConfig.AutoClaimGoldRelics;
            case PotionReward:
                return LocalWakuuAutopilotConfig.AutoClaimPotions;
            default:
                // 删牌/特殊奖励等保持人工
                return false;
        }
    }

    /// <summary>供 RewardsCmdOfferCustomPatch 判定单个奖励是否可自动领取。</summary>
    internal static bool IsAutoClaimable(Reward reward, Player owner)
    {
        try
        {
            return ShouldAutoClaim(reward, owner);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>供 RewardsCmdOfferCustomPatch 结算单个奖励（内部含上下文对齐与异常兜底）。</summary>
    internal static Task<bool> TrySettleSingleRewardAsync(Reward reward, Player owner)
    {
        return TrySettleAsync(reward, owner);
    }

    private static async Task<bool> TrySettleAsync(Reward reward, Player owner)
    {
        ulong? previousNetId = LocalContext.NetId;
        try
        {
            // 与看门狗相同的上下文对齐模式：OnSelect 内部依赖 LocalContext.IsMe(Player)
            AlignLocalContext(owner.NetId);

            switch (reward)
            {
                case CardReward:
                    // 用本 mod 的托管选择器（带奖励归属者，供社区统计按瓦库角色查表）；
                    // 关闭 skadaAssist 时其取牌结果与游戏原生 VakuuCardSelector 完全一致（最左）。
                    using (CardSelectCmd.PushSelector(new LocalWakuuStrategySelector(owner)))
                    {
                        _suppressCardRewardScreen = true;
                        try
                        {
                            await reward.SelectUnsynchronized();
                            LocalMultiControlLogger.Info(
                                $"瓦库卡牌奖励已自动领取: player={owner.NetId}, reward={reward.GetType().Name}");
                            return true;
                        }
                        finally
                        {
                            _suppressCardRewardScreen = false;
                        }
                    }

                case GoldReward gold:
                    await reward.SelectUnsynchronized();
                    LocalMultiControlLogger.Info($"瓦库金币奖励已自动领取: player={owner.NetId}, amount={gold.Amount}");
                    return true;

                case RelicReward relic:
                    await reward.SelectUnsynchronized();
                    LocalMultiControlLogger.Info(
                        $"瓦库遗物奖励已自动领取: player={owner.NetId}, relic={relic.Relic?.Id.Entry ?? "?"}");
                    return true;

                case PotionReward potionReward:
                    return await TrySettlePotionRewardAsync(potionReward, owner);

                default:
                    return false;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库奖励自动领取失败，保留为人工领取: reward={reward.GetType().Name}, error={exception.Message}");
            return false;
        }
        finally
        {
            AlignLocalContext(previousNetId);
        }
    }

    private static void AlignLocalContext(ulong? playerId)
    {
        if (playerId == null || LocalContext.NetId == playerId)
        {
            return;
        }

        LocalContext.NetId = playerId.Value;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId.Value);
    }

    /// <summary>
    /// 药水奖励领取（2026-08-25 拍板规则）：
    /// 有空位直接领；满栏先喝鲜血药水腾位；仍满则比较稀有度——
    /// 奖励高于栏内最低才丢最低换领，等价或更低放弃（保留人工/跳过）。
    /// 稀有度直接按 PotionRarity 枚举序比较（Common&lt;Uncommon&lt;Rare&lt;Event&lt;Token）。
    /// </summary>
    private static async Task<bool> TrySettlePotionRewardAsync(PotionReward reward, Player owner)
    {
        PotionModel? rewardPotion = reward.Potion;
        if (rewardPotion == null)
        {
            return false;
        }

        if (!owner.HasOpenPotionSlots)
        {
            List<PotionModel> slots = owner.Potions.ToList();

            // 特例：栏内有鲜血药水 → 先喝掉（回血 20% + 腾位），喝不掉再走稀有度比较
            BloodPotion? blood = slots.OfType<BloodPotion>().FirstOrDefault();
            if (blood != null)
            {
                LocalMultiControlLogger.Info(
                    $"瓦库药水栏已满，先喝鲜血药水腾位: player={owner.NetId}, 奖励={rewardPotion.Id.Entry}");
                // AnyPlayer 目标经 EnqueueManualUse 自动落到自己；NonCombat 动作战后随时可结算
                blood.EnqueueManualUse(null);
                await WaitForPotionRemovedAsync(owner, blood, BloodDrinkTimeoutMs);
            }

            if (!owner.HasOpenPotionSlots)
            {
                PotionModel? lowest = owner.Potions
                    .OrderBy((p) => (int)p.Rarity)
                    .FirstOrDefault();
                if (lowest == null)
                {
                    LocalMultiControlLogger.Warn($"瓦库药水栏状态异常（无药水但无空位），放弃领取: player={owner.NetId}");
                    return false;
                }

                if ((int)rewardPotion.Rarity <= (int)lowest.Rarity)
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库药水奖励稀有度不高于栏内最低（{rewardPotion.Rarity} <= {lowest.Rarity}），放弃领取: "
                        + $"player={owner.NetId}, 奖励={rewardPotion.Id.Entry}, 栏内最低={lowest.Id.Entry}");
                    return false;
                }

                LocalMultiControlLogger.Info(
                    $"瓦库药水奖励更优，丢弃栏内最低再领取: player={owner.NetId}, 丢弃={lowest.Id.Entry}({lowest.Rarity}), "
                    + $"领取={rewardPotion.Id.Entry}({rewardPotion.Rarity})");
                await PotionCmd.Discard(lowest);
            }
        }

        bool claimed = await reward.SelectUnsynchronized();
        LocalMultiControlLogger.Info(
            $"瓦库药水奖励自动领取: player={owner.NetId}, potion={rewardPotion.Id.Entry}({rewardPotion.Rarity}), success={claimed}");
        return claimed;
    }

    /// <summary>轮询等待某瓶药水离开药水栏（动作队列异步结算），超时返回 false。</summary>
    private static async Task WaitForPotionRemovedAsync(Player owner, PotionModel potion, int timeoutMs)
    {
        int waitedMs = 0;
        while (owner.Potions.Contains(potion) && waitedMs < timeoutMs)
        {
            await Task.Delay(150);
            waitedMs += 150;
        }

        if (owner.Potions.Contains(potion))
        {
            LocalMultiControlLogger.Warn(
                $"等待药水离开药水栏超时: player={owner.NetId}, potion={potion.Id.Entry}, waitedMs={waitedMs}");
        }
    }
}
