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

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        // 原逻辑：前台正是瓦库（IsMe=true）时不干预，保留原生弹屏由真人手动点。
        // 但「瓦库事件自动选择」推进事件时，控制权恰好常被切到瓦库身上
        // （日志实证：控制上下文已更新 ... -> 瓦库, source=player-state-button 之后进入事件自动选择），
        // 此时无人会去点奖励界面，而事件选项的 `await RewardsCmd.OfferCustom(...)` 会永久挂起、
        // SetEventFinished 排在其后 → 事件完不成、Proceed 被拦截（r54）。
        // 因此：事件自动选择作用域内（且归属者就是本次的瓦库玩家）一律接管自动结算；
        // 真人自己玩的事件不在该作用域内，行为完全不变。
        bool eventAutoScope = LocalWakuuEventAutoChoice.IsAutoChoosingFor(player);

        // 开关门禁（r54）：整批里只要有一项不满足自动领取条件（对应开关关闭 / 未知奖励类型），
        // 能交真人的就交真人——正常弹奖励界面由真人点，**绝不静默跳过**。
        // 静默跳过会让"开关关了"和"瓦库漏领/结算失败"在体感上完全一样，事后排查分不清是配置还是 bug。
        // 交真人只在原版真会弹屏时才成立：前台正是瓦库（IsMe=true）→ 弹屏给真人点；
        // 后台瓦库（IsMe=false）原版本就不弹屏，交回去只会永久挂起，只能跳过（保留原行为，打 WARN）。
        Reward? notClaimable = rewards.FirstOrDefault((r) => !LocalWakuuRewardAutoClaim.IsAutoClaimable(r, player));
        if (notClaimable != null)
        {
            if (LocalContext.IsMe(player))
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自定义奖励不满足自动领取条件，弹屏交真人处理: player={player.NetId}, "
                    + $"reward={notClaimable.GetType().Name}, rewards={rewards.Count}");
                return true;
            }

            LocalMultiControlLogger.Warn(
                $"瓦库自定义奖励不满足自动领取条件，但后台瓦库原版不弹屏（交回会挂起），只能跳过: "
                + $"player={player.NetId}, reward={notClaimable.GetType().Name}");
        }

        if (eventAutoScope)
        {
            LocalMultiControlLogger.Info(
                $"瓦库事件奖励改由自动结算（事件自动选择作用域内，控制权在瓦库身上）: "
                + $"player={player.NetId}, rewards={rewards.Count}");
        }
        else if (LocalContext.IsMe(player))
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

                // 走到这里只有两种可能，都不是"开关关了"（那已在 Prefix 门禁里交真人了）：
                // ①药水换栏规则判定不值得领（设计如此，INFO 已记）；
                // ②结算真的失败了（异常路径）。两者都只能跳过以免事件挂起，
                //   但统一打 WARN——静默跳过后瓦库啥也没拿到，必须有痕迹可查。
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
                LocalMultiControlLogger.Warn(
                    $"瓦库自定义奖励已跳过（结算未成功，非开关关闭）: player={player.NetId}, "
                    + $"reward={reward.GetType().Name}");
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
