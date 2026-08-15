using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗内任何入口进入“需要真实选择”的瞬时时刻，由本类把前台同步切换到实际选择角色。
/// 相比原有延迟切前台：原实现 CallDeferred 会错过游戏流程在同步代码里对选择的等待（选择入口的
/// 前缀里 deferred 回调还没执行，流程已经挂起等待选择），本类在入口同步切换，保证选择者和前台一致。
/// </summary>
internal static class CardSelectForegroundSwitchPatch
{
    private static void EnsureForegroundForCombatChoice(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, $"combat-choice-{source}");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand))]
    [HarmonyPrefix]
    private static void FromHandPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromHand");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade))]
    [HarmonyPrefix]
    private static void FromHandForUpgradePrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromHandForUpgrade");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPrefix]
    private static void FromSimpleGridPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromSimpleGrid");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    [HarmonyPrefix]
    private static void FromChooseACardScreenPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromChooseACardScreen");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile), new Type[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardPile),
        typeof(Player),
        typeof(CardSelectorPrefs)
    })]
    [HarmonyPrefix]
    private static void FromCombatPilePrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromCombatPile");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile), new Type[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardPile),
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>)
    })]
    [HarmonyPrefix]
    private static void FromCombatPileWithFilterPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromCombatPile");
    }
}