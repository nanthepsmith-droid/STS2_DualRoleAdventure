using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Rewards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战后奖励入口拦截：原版 CombatRoom.OfferRoomEndRewards 会先为每个角色 GenerateForRoomEnd
/// 预生成一套奖励（Populate 时遗物的 TryModifyCardRewardOptions/…Late 钩子被消耗，
/// 例如华美发束 SilkenTress 会在这一步置 IsUsed=true 并把附魔打到这套即将被丢弃的卡上），
/// 之后 mod 的 RewardsSet.Offer 汇总拦截又重新生成一套展示——遗物早已被消耗，
/// 导致玩家看到的卡牌奖励没有被附魔（华美发束显示"已生效"但卡没有华彩）。
///
/// 这里在本地多控模式下整体接管 OfferRoomEndRewards：每个角色的奖励只生成一次，
/// 附魔/升级/额外遗物等钩子在真正展示的奖励上生效。旧的两个汇总补丁
/// （RewardsCmdPatch / RewardsSetPatch）保留作兜底路径，由 TryMarkRoomMerged 去重。
/// </summary>
[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.OfferRoomEndRewards))]
internal static class CombatRoomOfferRoomEndRewardsPatch
{
    [HarmonyPrefix]
    private static bool Prefix(CombatRoom __instance, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || __instance.CombatState?.RunState == null
            || __instance.CombatState.RunState.Players.Count <= 1)
        {
            return true;
        }

        if (!CombatRewardMergeContext.TryMarkRoomMerged(__instance))
        {
            LocalMultiControlLogger.Info($"检测到重复战后奖励入口调用，已忽略: room={__instance.RoomType}");
            __result = Task.CompletedTask;
            return false;
        }

        __result = OfferMergedRewardsGeneratedOnceAsync(__instance);
        return false;
    }

    private static async Task OfferMergedRewardsGeneratedOnceAsync(CombatRoom combatRoom)
    {
        IRunState runState = combatRoom.CombatState.RunState;
        List<Player> allPlayers = runState.Players.ToList();
        if (allPlayers.Count == 0)
        {
            return;
        }

        // 标记进入汇总奖励流程，抑制遗物/药水/金币的镜像复制
        CombatRewardMergeContext.Enter();
        try
        {
            await OfferMergedCore(combatRoom, allPlayers);
        }
        finally
        {
            CombatRewardMergeContext.Exit();
        }
    }

    private static async Task OfferMergedCore(CombatRoom combatRoom, List<Player> allPlayers)
    {
        bool shouldGiveRewards = combatRoom.Encounter == null || combatRoom.Encounter.ShouldGiveRewards;
        List<Reward> mergedRewards = new();

        foreach (Player player in allPlayers)
        {
            if (player.Creature?.IsDead == true)
            {
                continue;
            }

            // 关键差异：这里是每个角色唯一一次奖励生成，
            // 遗物的卡牌修改钩子（华美发束附魔、蛋类升级、银坩埚等）直接作用于将要展示的卡。
            RewardsSet perPlayerSet = shouldGiveRewards
                ? new RewardsSet(player).WithRewardsFromRoom(combatRoom)
                : new RewardsSet(player).EmptyForRoom(combatRoom);

            await perPlayerSet.GenerateWithoutOffering();

            // 与原版保持一致：结算 BeforeCombatRewardOffered（持久奶糖计数等依赖它）
            await Hook.BeforeCombatRewardOffered(perPlayerSet, player.RunState, combatRoom);

            foreach (Reward reward in perPlayerSet.Rewards)
            {
                RewardPlayerLabelRegistry.Register(reward, player.NetId);
            }

            mergedRewards.AddRange(perPlayerSet.Rewards);
            LocalMultiControlLogger.Info(
                $"角色独立奖励已生成(OfferRoomEnd): player={player.NetId}, rewardCount={perPlayerSet.Rewards.Count}");
        }

        // 切换到第一个存活角色的控制上下文来展示奖励界面
        Player? displayPlayer = allPlayers.FirstOrDefault((p) => p.Creature?.IsDead != true) ?? allPlayers[0];
        LocalMultiControlRuntime.SwitchControlledPlayerTo(displayPlayer.NetId, "merged-rewards-offer-room-end");
        RewardsSet displaySet = new RewardsSet(displayPlayer).WithCustomRewards(mergedRewards);

        if (TestMode.IsOn)
        {
            foreach (Reward reward in mergedRewards)
            {
                await reward.SelectUnsynchronized();
            }

            return;
        }

        bool isTerminal = true; // CombatRoom 的奖励界面始终是 terminal
        LocalMultiControlRuntime.EnsureOverlayNotCoveredForRewards("merged-rewards-offer-room-end");
        NRewardsScreen rewardScreen = NRewardsScreen.ShowScreen(displaySet, isTerminal, displayPlayer.RunState);
        await rewardScreen.ToSignal(rewardScreen, NRewardsScreen.SignalName.Completed);
    }
}
