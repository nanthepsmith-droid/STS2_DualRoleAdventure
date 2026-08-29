using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

// 多目标容器：类级裸 [HarmonyPatch] 让 PatchAll 处理本类，
// 具体目标由各方法级 [HarmonyPatch(...)] 指定。
// 此前缺失类级标记导致整个类被 PatchAll 静默跳过（本 mod 坑 1），补丁从未生效。
[HarmonyPatch]
internal static class CardSelectManualConfirmationPatch
{
    private static void ForceManualIfNeeded(ref CardSelectorPrefs prefs, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        if (prefs.RequireManualConfirmation)
        {
            return;
        }

        CardSelectorPrefs patchedPrefs = new CardSelectorPrefs(prefs.Prompt, prefs.MinSelect, prefs.MaxSelect)
        {
            RequireManualConfirmation = true,
            Cancelable = prefs.Cancelable,
            UnpoweredPreviews = prefs.UnpoweredPreviews,
            PretendCardsCanBePlayed = prefs.PretendCardsCanBePlayed
        };
        patchedPrefs.ShouldGlowGold = prefs.ShouldGlowGold;
        prefs = patchedPrefs;

        LocalMultiControlLogger.Info(
            $"本地多控下强制牌组选牌弹出背包: source={source}, min={prefs.MinSelect}, max={prefs.MaxSelect}");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade), new[] { typeof(Player), typeof(CardSelectorPrefs) })]
    [HarmonyPrefix]
    private static void FromDeckForUpgradePrefix(ref CardSelectorPrefs prefs)
    {
        ForceManualIfNeeded(ref prefs, "FromDeckForUpgrade");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation), new[] { typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, CardTransformation>) })]
    [HarmonyPrefix]
    private static void FromDeckForTransformationPrefix(ref CardSelectorPrefs prefs)
    {
        ForceManualIfNeeded(ref prefs, "FromDeckForTransformation");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric), new[] { typeof(Player), typeof(CardSelectorPrefs), typeof(Func<CardModel, bool>), typeof(Func<CardModel, int>) })]
    [HarmonyPrefix]
    private static void FromDeckGenericPrefix(ref CardSelectorPrefs prefs)
    {
        ForceManualIfNeeded(ref prefs, "FromDeckGeneric");
    }
}
