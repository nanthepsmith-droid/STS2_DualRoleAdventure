using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗内任何入口进入“需要真实选择”的瞬时时刻，由本类把前台同步切换到实际选择角色。
/// 相比原有延迟切前台：原实现 CallDeferred 会错过游戏流程在同步代码里对选择的等待（选择入口的
/// 前缀里 deferred 回调还没执行，流程已经挂起等待选择），本类在入口同步切换，保证选择者和前台一致。
/// </summary>
[HarmonyPatch]
internal static class CardSelectForegroundSwitchPatch
{
    /// <summary>
    /// 当前异步链中正在进行的选牌所属角色（从 FromHand/FromSimpleGrid 等入口解析的 chooser）。
    /// 沿异步链向下流动：交错的两条选牌链（如双方同时触发炉心融解选牌）各自持有自己的值，
    /// NPlayerHand.SelectCards 的串行化包装任务据此在展示前把前台切到正确角色。
    /// </summary>
    internal static readonly System.Threading.AsyncLocal<ulong?> CurrentChoicePlayerId = new System.Threading.AsyncLocal<ulong?>();

    private static void EnsureForegroundForCombatChoice(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        CurrentChoicePlayerId.Value = player.NetId;

        // 瓦库形态后台托管：存在全局选择器时该次选择会被自动作答、不会弹 UI，免切换；
        // 无选择器（作用域外的真实交互，如酒狐初始遗物战斗开局二选一）则保留切换，
        // 由真人手动处理——此行为经实机验证不可改为自动作答（会导致战斗开局流程异常）。
        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: true))
        {
            LocalMultiControlLogger.Info(
                $"瓦库形态后台模式，选牌将自动作答，跳过切换: player={player.NetId}, source={source}");
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

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForDiscard))]
    [HarmonyPrefix]
    private static void FromHandForDiscardPrefix(Player player)
    {
        EnsureForegroundForCombatChoice(player, "FromHandForDiscard");
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

/// <summary>
/// 全局选择器抢答守卫：CardSelectCmd.Selector 返回栈顶选择器时，若当前异步链上的选牌
/// 归属者不是瓦库形态角色（即真人正在选牌），则临时返回 null 让其走正常选牌 UI。
/// 场景：瓦库自动出牌循环进行中（托管选择器在栈上），真人同时打出需要选牌的卡
/// （如酒狐合成），若不做此守卫，真人的选牌会被选择器瞬间抢答为第一张。
/// CurrentChoicePlayerId 由上方各 From* 前缀在方法体读取 Selector 之前写入，时序可靠。
/// 注意：作用域外（栈上无选择器）的选择一律不在此兜底作答——酒狐初始遗物开局二选一
/// 依赖"自动切前台由真人处理"的原有链路，实测改为即时作答会导致进战斗黑屏。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.Selector), MethodType.Getter)]
internal static class CardSelectCmdSelectorGuardPatch
{
    [HarmonyPostfix]
    private static void Postfix(ref ICardSelector? __result)
    {
        if (__result == null)
        {
            return;
        }

        // 只认托管选择器（游戏原生 VakuuCardSelector / 本 mod 策略选择器）；其余（如测试用）不动
        if (__result is not VakuuCardSelector and not LocalWakuuStrategySelector)
        {
            return;
        }

        ulong? chooserPlayerId = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
        if (!chooserPlayerId.HasValue)
        {
            return;
        }

        if (LocalWakuuRelicRuntime.IsVakuuFormModeById(chooserPlayerId.Value))
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"检测到真人选牌请求，本次跳过瓦库选择器改走正常UI: chooser={chooserPlayerId.Value}");
        __result = null;
    }
}
