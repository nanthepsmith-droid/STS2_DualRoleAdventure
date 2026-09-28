using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Rewards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), new[] { typeof(RelicModel), typeof(Player), typeof(int) })]
internal static class RelicCmdObtainPatch
{
    private static readonly HashSet<RelicModel> NonSharedChainRelics = new(ReferenceEqualityComparer.Instance);

    internal static bool TryConsumeNonSharedChainRelic(RelicModel relic)
    {
        return NonSharedChainRelics.Remove(relic);
    }

    [HarmonyPrefix]
    private static void Prefix(Player player)
    {
        GoldMirrorSuppressionContext.EnterSuppression();

        // 遗物「获得时触发选牌」（灵草丹等"获得遗物把一张卡变化"）的瓦库自动作答作用域。
        // 必须在前缀压栈：Obtain 内部 await relic.AfterObtained() 的调用发生在返回 Task 之前，
        // 后缀会晚一步、兜不住（r94；详见 LocalWakuuRelicEffectAutoChoice 注释）。
        LocalWakuuRelicEffectAutoChoice.Enter(player);
    }

    [HarmonyPostfix]
    private static void Postfix(Player player, ref Task<RelicModel> __result)
    {
        // 前缀压入的选择器作用域交给后续异步链释放（原版 Obtain 完成后立即释放，
        // 之后的"镜像给其它玩家"属于别人的遗物获取，必须恢复真人手动）。
        IDisposable? relicEffectScope = LocalWakuuRelicEffectAutoChoice.TakePendingScope();
        __result = GoldMirrorSuppressionContext.ExitSuppressionWhenCompleteAsync(
            MirrorObtainForOtherLocalPlayersAsync(player, __result, relicEffectScope));
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        if (__exception != null)
        {
            LocalWakuuRelicEffectAutoChoice.DisposePendingScope();
            GoldMirrorSuppressionContext.ExitSuppressionOnce();
        }

        return __exception;
    }

    private static async Task<RelicModel> MirrorObtainForOtherLocalPlayersAsync(
        Player player, Task<RelicModel> originalTask, IDisposable? relicEffectScope)
    {
        RelicModel obtainedRelic;
        try
        {
            obtainedRelic = await originalTask;
        }
        finally
        {
            relicEffectScope?.Dispose();
        }

        if (!LocalRewardMirror.IsMirrorFeatureEnabled)
        {
            return obtainedRelic;
        }

        if (player.RunState?.Players == null || player.RunState.Players.Count <= 1)
        {
            return obtainedRelic;
        }

        // r147：**来源**也必须是本地席位。旧实现只过滤了"镜像给谁"，于是第三方席位（Co-op Bots 的
        // 合成 Bot）自己的遗物被复制给两个真人 —— 实机最典型的是「七咒之戒」的额外战斗掉落遗物
        // （2026-09-25：GORGET / CANDELABRA / ODDLY_SMOOTH_STONE / PEAR / TROPICAL_FISH /
        // TRAVEL_PERMIT / LETTER_OPENER，以及第一幕 BOSS 的千咒卷轴 YUWANCARD-THOUSAND_CURSE_SCROLL，
        // 各 8 件 × 2 人，且是直接 AddRelicInternal ⇒ 真人连"要不要拿"都没得选）。
        if (!LocalRewardMirror.IsMirrorableSource(player))
        {
            LocalMultiControlLogger.Info(
                $"第三方席位获得的遗物不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        // 汇总奖励流程中，每个角色已独立生成奖励，不需要镜像
        if (CombatRewardMergeContext.IsActive)
        {
            LocalMultiControlLogger.Info($"汇总奖励流程中跳过遗物镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        // 战斗结束奖励 或 水晶球事件（占卜遗物只归揭示者，靠这个判据排除）
        if (!LocalRewardMirror.IsMirrorableRewardContext(player))
        {
            return obtainedRelic;
        }

        if (PaelsWingPatch.TryConsumePendingOwner(player.NetId))
        {
            LocalMultiControlLogger.Info($"佩尔之翼献祭产出的遗物不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        bool skipChainMirror = ShouldSkipChainMirror(obtainedRelic);
        if (skipChainMirror)
        {
            NonSharedChainRelics.Add(obtainedRelic);
            LocalMultiControlLogger.Info($"链式遗物按特判处理，不做共享镜像: relic={obtainedRelic.Id.Entry}, owner={player.NetId}");
            return obtainedRelic;
        }

        // 只镜像给**本地席位**：第三方席位（Co-op Bots 的合成 Bot）是独立队友，不该跟着我们共享遗物
        // （它的遗物由它自己的奖励流程获得）。来源端同样必须是本地席位（r147，见 MirrorSeatPolicy）。
        foreach (Player otherPlayer in LocalRewardMirror.SelectTargets(player))
        {
            if (!obtainedRelic.IsStackable && otherPlayer.GetRelicById(obtainedRelic.Id) != null)
            {
                continue;
            }

            try
            {
                RelicModel mirroredRelic = RelicModel.FromSerializable(obtainedRelic.ToSerializable());
                otherPlayer.AddRelicInternal(mirroredRelic);
                await mirroredRelic.AfterObtained();
                LocalMultiControlLogger.Info($"本地多控共享遗物同步: {obtainedRelic.Id.Entry}, {player.NetId} -> {otherPlayer.NetId}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"共享遗物同步失败(获得): target={otherPlayer.NetId}, error={exception.Message}");
            }
        }

        return obtainedRelic;
    }

    private static bool ShouldSkipChainMirror(RelicModel relic)
    {
        if (relic.IsWax)
        {
            return true;
        }

        string relicId = relic.Id.Entry;
        return relicId == "LARGE_CAPSULE" || relicId == "TOY_BOX";
    }
}

[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Remove))]
internal static class RelicCmdRemovePatch
{
    [HarmonyPostfix]
    private static void Postfix(RelicModel relic, ref Task __result)
    {
        __result = MirrorRemoveForOtherLocalPlayersAsync(relic, __result);
    }

    private static async Task MirrorRemoveForOtherLocalPlayersAsync(RelicModel removedRelic, Task originalTask)
    {
        await originalTask;
        if (RelicCmdObtainPatch.TryConsumeNonSharedChainRelic(removedRelic))
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || removedRelic.Owner?.RunState == null)
        {
            return;
        }

        // 汇总奖励流程中跳过镜像
        if (CombatRewardMergeContext.IsActive)
        {
            return;
        }

        // 战斗结束奖励 或 水晶球事件（占卜遗物移除同样只作用于本人）
        if (!LocalRewardMirror.IsMirrorableRewardContext(removedRelic.Owner))
        {
            return;
        }

        IRunState runState = removedRelic.Owner.RunState;
        if (runState.Players.Count <= 1)
        {
            return;
        }

        // r147：来源席位同样过滤（第三方席位的遗物我们从没镜像过，也不该跟着它一起移除）。
        if (!LocalRewardMirror.IsMirrorableSource(removedRelic.Owner))
        {
            return;
        }

        // 同步移除同样只针对**本地席位**（第三方席位从没被我们镜像过遗物）。
        foreach (Player otherPlayer in LocalRewardMirror.SelectTargets(removedRelic.Owner))
        {
            RelicModel? mirroredRelic = otherPlayer.GetRelicById(removedRelic.Id);
            if (mirroredRelic == null)
            {
                continue;
            }

            try
            {
                otherPlayer.RemoveRelicInternal(mirroredRelic);
                await mirroredRelic.AfterRemoved();
                LocalMultiControlLogger.Info($"本地多控共享遗物同步移除: {removedRelic.Id.Entry}, owner={otherPlayer.NetId}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"共享遗物同步失败(移除): target={otherPlayer.NetId}, error={exception.Message}");
            }
        }
    }
}
