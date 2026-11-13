using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 宝箱遗物格补齐（12 人扩展 / 极速模式每人 2 件）：必须在 `InitializeRelics` **之前**把能放下
/// 当前遗物数的那套布局准备好（0.113 是 `Layout1..6` 布局制，按 `relics-1` 挑一套 ⇒ 7 件起没有可用布局）。
/// 6 件及以内一律不碰（原版布局回归契约），细节见 <see cref="TreasureRelicHoldersRuntime"/>。
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

        TreasureRelicHoldersRuntime.EnsureLayoutForCurrentRelics(__instance);
    }
}
