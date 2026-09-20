using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Rewards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RewardsSet), nameof(RewardsSet.Offer))]
internal static class RewardsSetPatch
{
    [HarmonyPrefix]
    private static bool Prefix(RewardsSet __instance, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (LocalSelfCoopContext.UseSingleAdventureMode
            && __instance.Room is CombatRoom combatRoom
            && __instance.Player.RunState.Players.Count > 1)
        {
            if (!CombatRewardMergeContext.TryMarkRoomMerged(combatRoom))
            {
                LocalMultiControlLogger.Info($"检测到重复战后奖励 Offer 调用，已忽略: player={__instance.Player.NetId}");
                __result = Task.CompletedTask;
                return false;
            }

            __result = OfferMergedCombatRewards(combatRoom);
            return false;
        }

        __result = OfferLocalSelfCoop(__instance);
        return false;
    }

    private static async Task OfferMergedCombatRewards(CombatRoom combatRoom)
    {
        List<Player> allPlayers = combatRoom.CombatState.RunState.Players.ToList();
        if (allPlayers.Count == 0)
        {
            return;
        }

        CombatRewardMergeContext.Enter();
        try
        {
            List<Reward> mergedRewards = new();
            bool shouldGiveRewards = combatRoom.Encounter == null || combatRoom.Encounter.ShouldGiveRewards;
            foreach (Player player in allPlayers)
            {
                if (player.Creature?.IsDead == true)
                {
                    continue;
                }

                RewardsSet perPlayerSet = shouldGiveRewards
                    ? new RewardsSet(player).WithRewardsFromRoom(combatRoom)
                    : new RewardsSet(player).EmptyForRoom(combatRoom);
                await perPlayerSet.GenerateWithoutOffering();

                foreach (Reward reward in perPlayerSet.Rewards)
                {
                    RewardPlayerLabelRegistry.Register(reward, player.NetId);
                }

                mergedRewards.AddRange(perPlayerSet.Rewards);
                LocalMultiControlLogger.Info($"角色独立奖励已生成(Offer): player={player.NetId}, rewardCount={perPlayerSet.Rewards.Count}");
            }

            Player displayPlayer = allPlayers.FirstOrDefault((p) => p.Creature?.IsDead != true) ?? allPlayers[0];
            LocalMultiControlRuntime.SwitchControlledPlayerTo(displayPlayer.NetId, "merged-rewards-offer-from-rewardsset");
            RewardsSet displaySet = new RewardsSet(displayPlayer).WithCustomRewards(mergedRewards);

            if (TestMode.IsOn)
            {
                foreach (Reward reward in mergedRewards)
                {
                    await reward.SelectUnsynchronized();
                }

                return;
            }

            LocalMultiControlRuntime.EnsureOverlayNotCoveredForRewards("merged-rewards-offer-from-rewardsset");
            CombatRewardMergeContext.BeginDisplaySet(displaySet);
            NRewardsScreen rewardScreen = NRewardsScreen.ShowScreen(displaySet, isTerminal: true, displayPlayer.RunState);
            await rewardScreen.ToSignal(rewardScreen, NRewardsScreen.SignalName.Completed);
            CombatRewardMergeContext.CompleteDisplaySet(displaySet, "merged-rewards-offer-from-rewardsset");
        }
        finally
        {
            CombatRewardMergeContext.Exit();
        }
    }

    private static async Task OfferLocalSelfCoop(RewardsSet rewardsSet)
    {
        if (rewardsSet.Player.Creature.IsDead)
        {
            return;
        }

        await rewardsSet.GenerateWithoutOffering();
        bool isTerminal = rewardsSet.Room is CombatRoom;
        bool allowEmptyRewards = (bool)(AccessTools.Field(typeof(RewardsSet), "_allowEmptyRewards")?.GetValue(rewardsSet) ?? false);
        if (rewardsSet.Rewards.Count <= 0 && !isTerminal && !allowEmptyRewards)
        {
            return;
        }

        if (!rewardsSet.Rewards.All((reward) => reward.IsPopulated) && rewardsSet.Rewards.Any((reward) => reward.IsPopulated))
        {
            Log.Warn("Some rewards are populated and others are not when calling RewardsCmd.Offer! This might lead to hooks getting called twice");
        }

        // r143：瓦库角色的**非战斗自定义奖励**也要自动结算。
        //
        // 这是一条此前完全没被覆盖的路径：遗物效果 / 第三方 mod 自己
        // `new RewardsSet(...).WithCustomRewards(...).Offer()` 走的就是这里 ——
        // 它**不经过** `RewardsCmd.OfferCustom`（所以 RewardsCmdOfferCustomPatch 拦不到），
        // Room 也不是 CombatRoom（所以战后合并奖励那条链也不管）。
        // 原实现一律"把控制切到奖励归属者 + 弹原生奖励界面"，于是**瓦库的奖励也要真人手动点**。
        // 实机案例（2026-09-20）：YUI「赐福」类遗物（`BLESSED_*`）在拾取时给一张卡的卡牌奖励，
        // 界面弹出等人点（本局 3 次，其中一次 2 张卡），点完还进了个人统计；
        // 「星系仪」这类"拾取时给卡牌奖励"的遗物同理。
        //
        // 规则与开关**完全复用**战后奖励那条链（LocalWakuuRewardAutoClaim.SettleAsync →
        // 卡牌最左 / 金币 / 遗物 / 药水换栏规则 + 各自的自动领取开关），因此：
        // 开关关着、或奖励类型不被自动接管（如删牌奖励、药水换栏判定不值得领）时**一个都不会动**，
        // 剩余奖励照旧弹屏给真人 —— 行为与旧版逐字一致。
        // 注意：这里**不进** `CombatRewardMergeContext` —— 本路径没有"每个角色已独立生成奖励"的前提，
        // 与 `RewardsCmdOfferCustomPatch` 同一套写法（那条链同样直接调 Settle 入口、不包 Enter/Exit）。
        if (LocalWakuuRelicRuntime.IsVakuuFormMode(rewardsSet.Player))
        {
            int beforeCount = rewardsSet.Rewards.Count;
            List<Reward> remaining = await LocalWakuuRewardAutoClaim.SettleAsync(rewardsSet.Rewards.ToList());
            if (remaining.Count != beforeCount)
            {
                // 已结算的奖励从本集移除：展示界面上不该再出现"已经领过"的按钮。
                rewardsSet.Rewards.RemoveAll((reward) => !remaining.Contains(reward));
                LocalMultiControlLogger.Info(
                    $"瓦库非战斗奖励已自动结算: player={rewardsSet.Player.NetId}, "
                    + $"自动领取={beforeCount - remaining.Count}, 剩余={remaining.Count}");
            }

            if (rewardsSet.Rewards.Count == 0)
            {
                LocalMultiControlLogger.Info(
                    $"瓦库奖励已全部自动领取，不再弹奖励界面: player={rewardsSet.Player.NetId}, 原奖励数={beforeCount}");
                // 后端收口：登记 + 立刻标记完成（奖励已全部 SuccessfullySelected），
                // 与"真人领完最后一张"走的是同一条 OnRewardClaimed → CompleteDisplaySet 语义，
                // 否则同步器里会留下一个永不完成的奖励集。
                CombatRewardMergeContext.BeginDisplaySet(rewardsSet);
                CombatRewardMergeContext.CompleteDisplaySet(rewardsSet, "wakuu-local-rewards-all-claimed");
                return;
            }
        }

        LocalMultiControlRuntime.SwitchControlledPlayerTo(rewardsSet.Player.NetId, "rewards-offer");
        LocalMultiControlLogger.Info($"打开奖励界面: player={rewardsSet.Player.NetId}, count={rewardsSet.Rewards.Count}");
        Task rewardsSetTask = RunManager.Instance.RewardsSetSynchronizer.BeginRewardsSet(rewardsSet);

        if (TestMode.IsOn)
        {
            foreach (Reward reward in rewardsSet.Rewards)
            {
                await RunManager.Instance.RewardsSetSynchronizer.SelectLocalReward(reward);
            }

            await rewardsSetTask;
            return;
        }

        LocalMultiControlRuntime.EnsureOverlayNotCoveredForRewards("rewards-offer-local");
        NRewardsScreen.ShowScreen(rewardsSet, isTerminal, rewardsSet.Player.RunState);
        // 读档重放窗口内不能等待玩家操作（会阻塞 LoadRun→FadeIn 导致永久黑屏），
        // 奖励界面交由其自身流程管理，立即返回。
        if (LocalMultiControlRuntime.IsLoadReplayTransitionCovering())
        {
            LocalMultiControlLogger.Info("读档重放路径检测到非战斗奖励，跳过同步等待。");
            return;
        }

        await rewardsSetTask;
    }
}
