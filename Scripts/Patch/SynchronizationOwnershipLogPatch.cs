using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch]
internal static class SynchronizationOwnershipLogPatch
{
    [HarmonyPostfix]
    [HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedCard))]
    private static void PostfixRewardCard(RewardSynchronizer __instance)
    {
        LogOwnership(__instance, "奖励-拿牌");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedRelic))]
    private static void PostfixRewardRelic(RewardSynchronizer __instance)
    {
        LogOwnership(__instance, "奖励-拿遗物");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedPotion))]
    private static void PostfixRewardPotion(RewardSynchronizer __instance)
    {
        LogOwnership(__instance, "奖励-拿药水");
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(RewardSynchronizer), nameof(RewardSynchronizer.SyncLocalObtainedGold))]
    private static void PostfixRewardGold(RewardSynchronizer __instance)
    {
        LogOwnership(__instance, "奖励-拿金币");
    }

    // 0.113：商店删牌不再经 `OneOffSynchronizer.DoLocalMerchantCardRemoval`（该方法已删），
    // 改成 `MerchantCardRemovalEntry.DoCardRemoval(player, isCancelable)` 本地直接执行（不再广播）。
    // 因此这里没有 synchronizer 可读，归属玩家直接取形参。
    [HarmonyPostfix]
    [HarmonyPatch(typeof(MerchantCardRemovalEntry), nameof(MerchantCardRemovalEntry.DoCardRemoval))]
    private static void PostfixMerchantRemoval(Player player)
    {
        ulong? contextId = LocalSeatSource.ContextSeatId();
        LocalMultiControlLogger.Info($"商店-删牌归属玩家: context={contextId?.ToString() ?? "null"}, player={player?.NetId.ToString() ?? "null"}");
    }

    private static void LogOwnership(object synchronizer, string operation)
    {
        // R3 B4：诊断读也走唯一取数入口（与 `LocalContext.NetId` 同源同值）。
        ulong? contextId = LocalSeatSource.ContextSeatId();
        ulong? localId = SynchronizerLocalPlayerId.TryRead(synchronizer);
        LocalMultiControl.Scripts.Runtime.LocalMultiControlLogger.Info($"{operation}归属玩家: context={contextId?.ToString() ?? "null"}, syncLocal={localId?.ToString() ?? "null"}");
    }
}
