using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CardModel), "EnqueueManualPlay", new[] { typeof(Creature) })]
internal static class CardManualPlayContextPatch
{
    private readonly struct ManualPlayPatchState
    {
        public ManualPlayPatchState(bool guardEntered)
        {
            GuardEntered = guardEntered;
        }

        public bool GuardEntered { get; }
    }

    [HarmonyPrefix]
    private static void Prefix(CardModel __instance, ref ManualPlayPatchState __state)
    {
        __state = new ManualPlayPatchState(guardEntered: false);
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        __state = new ManualPlayPatchState(guardEntered: true);
        LocalManualPlayGuard.Enter("CardModel.EnqueueManualPlay");

        Player? owner = __instance.Owner;
        if (owner == null)
        {
            return;
        }

        // 并发出牌档（方案 D 第二步 / r127）：真人一按出牌，就把瓦库"已入队、还在排队"的动作撤掉，
        // 让真人这张牌插到队首（原版按全局 action ID 取下一个执行，否则真人要白等好几张瓦库牌）。
        // 瓦库自己走的是 RequestEnqueue，不经 EnqueueManualPlay；这里再兜一层防御，别把自己的动作撤了。
        if (!LocalWakuuRelicRuntime.IsVakuuFormModeById(owner.NetId))
        {
            LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman(owner.NetId, "card-enqueue-manual-play");
        }

        LocalMultiControlRuntime.AlignContextForActionOwner(owner.NetId, "card-enqueue-manual-play");
    }

    [HarmonyFinalizer]
    private static System.Exception? Finalizer(System.Exception? __exception, ManualPlayPatchState __state)
    {
        if (__state.GuardEntered)
        {
            LocalManualPlayGuard.Exit("CardModel.EnqueueManualPlay");
        }

        return __exception;
    }
}
