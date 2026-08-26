using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 事件/遗物自定义奖励的瓦库自动领取（2026-08-25 追加需求：事件中的卡牌奖励、药水也要能自动领）。
///
/// 背景（原版链路）：事件与遗物（未来药水/药水快递/坩埚/召唤铃等）经 RewardsCmd.OfferCustom
/// → RewardsSet.Offer 弹出奖励界面并等待玩家点击。本 mod 多控下，后台瓦库的 IsMe(Player)=false
/// 时原版不弹屏，但 BeginRewardsSet 的完成任务永远没人满足——事件选项 await OfferCustom 会
/// 永久挂起（SetEventFinished 排在其后），事件无法完成，Proceed 被拦截 = 潜在软锁。
///
/// 对策：瓦库形态角色的自定义奖励改由本补丁直接结算——
/// - 照常执行 GenerateWithoutOffering（遗物修改钩子只触发一次）；
/// - 逐个按 LocalWakuuRewardAutoClaim 的开关与规则结算（卡牌最左/金币/遗物/药水换栏规则）；
/// - 未配置自动领取的奖励调用 OnSkipped 记录跳过历史并放行（后台瓦库无人能点，
///   与其挂起不如跳过；日志明示）。
/// 前台正是瓦库（IsMe=true）时不干预：保留原生弹屏由真人手动点。
/// </summary>
[HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferCustom))]
internal static class RewardsCmdOfferCustomPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, List<Reward> rewards, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || RunManager.Instance.NetService is not LocalLoopbackHostGameService
            || player == null
            || rewards == null
            || rewards.Count == 0)
        {
            return true;
        }

        if (RunManager.Instance.DebugOnlyGetState()?.Players.Count <= 1)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player) || LocalContext.IsMe(player))
        {
            return true;
        }

        __result = SettleCustomRewardsForWakuuAsync(player, rewards);
        return false;
    }

    private static async Task SettleCustomRewardsForWakuuAsync(Player player, List<Reward> rewards)
    {
        try
        {
            // 复刻原版 Offer 的生成阶段：Populate + Hook.ModifyRewards 只触发一次（原实现被跳过）
            RewardsSet set = new RewardsSet(player).WithCustomRewards(rewards);
            await set.GenerateWithoutOffering();

            int claimedCount = 0;
            int skippedCount = 0;
            foreach (Reward reward in set.Rewards.ToList())
            {
                if (LocalWakuuRewardAutoClaim.IsAutoClaimable(reward, player))
                {
                    bool settled = await LocalWakuuRewardAutoClaim.TrySettleSingleRewardAsync(reward, player);
                    if (settled)
                    {
                        claimedCount++;
                        continue;
                    }
                }

                // 不可自动领取（未开对应开关/药水稀有度不够/未知类型）：记录跳过，避免挂起
                try
                {
                    reward.OnSkipped();
                }
                catch (Exception skipException)
                {
                    LocalMultiControlLogger.Warn(
                        $"瓦库自定义奖励跳过登记异常（忽略）: {reward.GetType().Name}, error={skipException.Message}");
                }

                skippedCount++;
                LocalMultiControlLogger.Info(
                    $"瓦库自定义奖励已跳过（不满足自动领取条件）: player={player.NetId}, reward={reward.GetType().Name}");
            }

            LocalMultiControlLogger.Info(
                $"瓦库事件/遗物自定义奖励结算完成: player={player.NetId}, 领取={claimedCount}, 跳过={skippedCount}");
        }
        catch (Exception exception)
        {
            // 结算异常也不能挂起事件链：吞掉并返回完成
            LocalMultiControlLogger.Warn(
                $"瓦库自定义奖励结算异常（放弃本批奖励以放行事件流程）: player={player.NetId}, error={exception.Message}");
        }
    }
}
