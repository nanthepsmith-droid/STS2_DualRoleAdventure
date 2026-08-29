using System;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复 Bug 3：事件中卡牌奖励「归属 player」与「实际领取 player」不一致导致的软锁死。
///
/// 背景（原版链路）：RewardsCmd.OfferCustom(player, rewards) → RewardsSet(player).Offer()
/// 会为「奖励归属的 player」在 RewardsSetSynchronizer 里登记一个完成源（BeginRewardsSet），
/// 该完成源只在该 player 的奖励全部领取/跳过时触发。真人点击 NRewardButton 领取时走
/// RewardSynchronizer.SelectLocalReward，而 SelectLocalReward 内部用 synchronizer._localPlayerId
/// 定位「本地玩家」，领取校验要求 reward.Player == LocalPlayer。
///
/// 本 mod 多控下，RewardsSetSynchronizer._localPlayerId 被同步成「当前控制角色」
/// （SyncRunSynchronizerLocalPlayerId）。若奖励弹出后控制权被切换（用户手动切人 / 事件自动切换），
/// _localPlayerId 指向新控制角色，与原「奖励归属 player」错位：领取动作要么抛
/// "reward.Player != LocalPlayer" 异常，要么打进错误角色的奖励栈；奖励永远无法领取，
/// 归属 player 的完成源永不触发 → 事件 await OfferCustom 永久挂起 → 软锁死。
///
/// 对策：把「领取动作」绑定到「奖励的归属 player」而非「当前控制 player」。在
/// SelectLocalReward 期间，若 synchronizer._localPlayerId / LocalContext.NetId / 回环 sender
/// 与 reward.Player 不一致，临时统一改绑到 reward.Player，领取完成后再恢复原值。
/// 这样无论控制权当前在谁手上，真人点击领取时都能正确命中归属 player 的奖励栈与完成源，
/// 事件流程得以继续。后台瓦库奖励（RewardsCmdOfferCustomPatch）已自行结算，不受影响。
/// </summary>
[HarmonyPatch(typeof(RewardsSetSynchronizer), nameof(RewardsSetSynchronizer.SelectLocalReward))]
internal static class RewardsSetSynchronizerSelectLocalRewardPatch
{
    private struct RebindState
    {
        internal RewardsSetSynchronizer? Synchronizer;
        internal bool IsPatched;
        internal ulong PreviousSyncLocalId;
        internal ulong? PreviousContextNetId;
        internal ulong PreviousSenderId;
    }

    [HarmonyPrefix]
    private static void Prefix(RewardsSetSynchronizer __instance, Reward reward, ref RebindState __state)
    {
        __state = default;
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (reward?.Player == null)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService loopback)
        {
            return;
        }

        ulong ownerId = reward.Player.NetId;
        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(ownerId))
        {
            return;
        }

        FieldInfo? localIdField = AccessTools.Field(typeof(RewardsSetSynchronizer), "_localPlayerId");
        if (localIdField == null)
        {
            return;
        }

        ulong syncLocalId = (ulong)(localIdField.GetValue(__instance) ?? 0UL);
        if (syncLocalId == ownerId)
        {
            // 同步器归属已正确指向奖励归属角色，无需改绑。
            return;
        }

        __state.Synchronizer = __instance;
        __state.IsPatched = true;
        __state.PreviousSyncLocalId = syncLocalId;
        __state.PreviousContextNetId = LocalContext.NetId;
        __state.PreviousSenderId = loopback.NetId;

        localIdField.SetValue(__instance, ownerId);
        LocalContext.NetId = ownerId;
        loopback.SetCurrentSenderId(ownerId);

        LocalMultiControlLogger.Info(
            $"奖励领取按归属角色绑定: owner={ownerId}, syncLocal={syncLocalId} -> {ownerId}, reward={reward.GetType().Name}");
    }

    [HarmonyPostfix]
    private static void Postfix(ref RebindState __state)
    {
        Restore(ref __state, "postfix");
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, ref RebindState __state)
    {
        Restore(ref __state, "finalizer");
        return __exception;
    }

    private static void Restore(ref RebindState state, string source)
    {
        if (!state.IsPatched)
        {
            return;
        }

        try
        {
            if (RunManager.Instance.NetService is LocalLoopbackHostGameService loopback
                && loopback.NetId != state.PreviousSenderId)
            {
                loopback.SetCurrentSenderId(state.PreviousSenderId);
            }

            LocalContext.NetId = state.PreviousContextNetId;

            if (state.Synchronizer != null)
            {
                AccessTools.Field(typeof(RewardsSetSynchronizer), "_localPlayerId")
                    ?.SetValue(state.Synchronizer, state.PreviousSyncLocalId);
            }

            LocalMultiControlLogger.Info($"奖励领取归属已恢复: source={source}, syncLocal={state.PreviousSyncLocalId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"奖励领取归属恢复失败(忽略): {exception.Message}");
        }
        finally
        {
            state.IsPatched = false;
        }
    }
}
