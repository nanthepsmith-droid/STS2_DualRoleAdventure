using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CrystalSphere), "PaymentPlan")]
internal static class CrystalSpherePaymentPlanPatch
{
    private static readonly AsyncLocal<bool> IsMirroringDebt = new();

    /// <summary>
    /// 本地多控多角色模式下，事件完成后关闭已完成的占卜弹层。
    /// 不吃掉待切换标记：弹层移除后由 NOverlayStackPatch 的自动切换链切到下一位角色。
    /// </summary>
    internal static void CloseOverlayIfFinished(CrystalSphere eventModel)
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || eventModel.Owner?.RunState == null
            || eventModel.Owner.RunState.Players.Count <= 1)
        {
            return;
        }

        CrystalSphereMirrorRuntime.CloseFinishedMinigameOverlays(
            $"event-finished-{eventModel.Owner.NetId}", consumePendingSwitch: false);
    }


    [HarmonyPostfix]
    private static void Postfix(CrystalSphere __instance, ref Task __result)
    {
        __result = MirrorDebtToOtherPlayersAsync(__instance, __result);
    }

    private static async Task MirrorDebtToOtherPlayersAsync(CrystalSphere eventModel, Task originalTask)
    {
        await originalTask;

        // 分期付款分支同样以关闭已完成弹层收尾（与揭幕未来一致）
        CloseOverlayIfFinished(eventModel);

        // 每角色独立结算：诅咒债只进选择者的牌组，不再镜像到其余角色
        if (!CrystalSphereMirrorRuntime.CrossPlayerMirroringEnabled)
        {
            return;
        }

        if (IsMirroringDebt.Value || !CrystalSphereMirrorRuntime.IsInCrystalSphereEventContext(eventModel.Owner))
        {
            return;
        }

        if (eventModel.Owner == null)
        {
            return;
        }

        IsMirroringDebt.Value = true;
        try
        {
            MegaCrit.Sts2.Core.Entities.Players.Player owner = eventModel.Owner;
            System.Collections.Generic.List<MegaCrit.Sts2.Core.Entities.Players.Player> otherPlayers = CrystalSphereMirrorRuntime.GetOtherPlayers(owner);
            foreach (MegaCrit.Sts2.Core.Entities.Players.Player otherPlayer in otherPlayers)
            {
                await CardPileCmd.AddCurseToDeck<Debt>(otherPlayer);
            }

            LocalMultiControlLogger.Info(
                $"水晶球事件债务卡已同步加入其余角色: owner={owner.NetId}, mirrored={string.Join(",", otherPlayers.Select((player) => player.NetId))}");
        }
        finally
        {
            IsMirroringDebt.Value = false;
        }
    }
}

/// <summary>
/// 水晶球"揭幕未来/分期付款"选项执行完毕（含奖励结算、SetEventFinished）后，
/// 占卜小游戏弹层仍挂在弹层栈上，导致：
/// 1) mod 的事件自动切换链因"弹层未关闭"被搁置，永远切不到下一位角色；
/// 2) 玩家点弹层上的 PROCEED 会直接打开地图，事件对所有人直接结束。
/// 这里在事件完成后关闭已完成的占卜弹层，让既有的自动切换链接管，
/// 下一位角色即可继续各自的占卜。
/// </summary>
[HarmonyPatch(typeof(CrystalSphere), "UncoverFuture")]
internal static class CrystalSphereUncoverFuturePatch
{
    [HarmonyPostfix]
    private static void Postfix(CrystalSphere __instance, ref Task __result)
    {
        __result = CloseOverlayAfterFinishedAsync(__instance, __result);
    }

    private static async Task CloseOverlayAfterFinishedAsync(CrystalSphere eventModel, Task originalTask)
    {
        await originalTask;
        CrystalSpherePaymentPlanPatch.CloseOverlayIfFinished(eventModel);
    }
}

/// <summary>
/// 拦截占卜弹层的 PROCEED：本地多控下只要还有其他角色的水晶球未完成，
/// 就不允许直接打开地图结束事件——关闭已完成弹层并切到下一位待占卜角色；
/// 全部完成后放行原版逻辑（正常回地图）。
/// </summary>
[HarmonyPatch(typeof(NCrystalSphereScreen), "OnProceedButtonPressed")]
internal static class CrystalSphereMinigameProceedGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NCrystalSphereScreen __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !RunManager.Instance.IsInProgress)
        {
            return true;
        }

        ulong currentPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? MegaCrit.Sts2.Core.Context.LocalContext.NetId
            ?? 0UL;
        if (currentPlayerId == 0UL)
        {
            return true;
        }

        Player? nextOwner = CrystalSphereMirrorRuntime.GetNextPendingCrystalSphereOwner(currentPlayerId);
        if (nextOwner == null)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"拦截水晶球占卜 PROCEED：仍有角色未完成占卜，切换到 player={nextOwner.NetId}");
        // 自己负责切换：先作废挂起的自动切换标记，避免弹层移除后再触发一次来回跳
        CrystalSphereMirrorRuntime.CloseFinishedMinigameOverlays("crystal-sphere-proceed-guard", consumePendingSwitch: true);
        LocalMultiControlRuntime.SwitchControlledPlayerTo(nextOwner.NetId, "crystal-sphere-proceed-next");
        return false;
    }
}
