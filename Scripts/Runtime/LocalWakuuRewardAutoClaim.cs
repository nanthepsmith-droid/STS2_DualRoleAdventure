using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库角色战后奖励自动领取（可行性分析·需求三 a + 待确认 #1 拍板）：
/// 卡牌奖励领最左（复用游戏自带 VakuuCardSelector.GetSelectedCardReward 的 FirstOrDefault 语义），
/// 金币/遗物直接结算；药水不自动领；非瓦库角色的奖励一律不动。
///
/// 实现要点：
/// - 结算走 reward.SelectUnsynchronized()（绕过 RewardsSetSynchronizer；
///   本 mod 单进程回环全员本地，无远端分歧），其内部会从父 RewardsSet 移除该奖励。
/// - CardReward.OnSelect 在 LocalContext.IsMe(Player) 且无弹屏时走游戏原生
///   "Selector.GetSelectedCardReward" 自动作答分支（TestMode 的原生工作方式），
///   因此结算期间需要：①临时把上下文对齐到瓦库玩家；②压入选择器作用域；
///   ③用 NCardRewardSelectionScreenAutoClaimPatch 抑制弹屏（仅在本类标记置位时生效）。
/// - 已结算的奖励从展示列表移除，真人看到的奖励界面干净无干扰。
/// </summary>
internal static class LocalWakuuRewardAutoClaim
{
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
            default:
                // 药水/删牌/特殊奖励等保持人工（拍板 #1）
                return false;
        }
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
                    using (CardSelectCmd.PushSelector(new VakuuCardSelector()))
                    {
                        _suppressCardRewardScreen = true;
                        try
                        {
                            await reward.SelectUnsynchronized();
                            LocalMultiControlLogger.Info(
                                $"瓦库卡牌奖励已自动领最左: player={owner.NetId}, reward={reward.GetType().Name}");
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
}
