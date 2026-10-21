using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 宝箱遗物格补齐（12 人扩展）：必须在 `InitializeRelics` **之前**补好 `_multiplayerHolders`，
/// 否则原版那个 `for (i &lt; _multiplayerHolders.Count)` 只会上 4 个格子 ⇒ 第 5 个遗物起看不见也点不到。
/// 4 个及以内一律不碰（原版布局回归契约），细节见 <see cref="TreasureRelicHoldersRuntime"/>。
/// </summary>
[HarmonyPatch(typeof(NTreasureRoomRelicCollection), nameof(NTreasureRoomRelicCollection.InitializeRelics))]
internal static class NTreasureRoomRelicCollectionHoldersPatch
{
    [HarmonyPrefix]
    private static void Prefix(NTreasureRoomRelicCollection __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        TreasureRelicHoldersRuntime.EnsureHoldersForCurrentRelics(__instance);
    }
}
