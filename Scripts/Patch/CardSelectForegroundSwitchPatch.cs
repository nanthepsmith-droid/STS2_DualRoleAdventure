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

        // 「会被自动作答、不弹 UI」的两种情况：
        //   ① 栈上已有全局选择器（瓦库自动出牌作用域内，如攻击药水选牌）；
        //   ② 该入口由 CardSelectWakuuTurnStartAutoAnswerPatch 对瓦库做「作用域外自动作答」
        //      （FromChooseACardScreen / FromSimpleGrid，如工具箱三选一）。
        // 两种都不需要真人，切前台没有意义（改进-2：避免无谓的视角"闪一下"）。
        // 其余情况是「防软锁兜底」——必须切给真人，此行为经实机验证不可改为自动作答
        // （会导致战斗开局流程异常），且**不受视角档位影响**（WakuuViewPolicy 里做了保证）。
        bool willAutoAnswer = CardSelectCmd.Selector != null
            || (CardSelectWakuuTurnStartAutoAnswerPatch.IsAutoAnswerEntry(source)
                && CardSelectWakuuTurnStartAutoAnswerPatch.ShouldAutoAnswer(player));

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(
                player, WakuuViewTrigger.HumanInteractionChoice, willAutoAnswer))
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
/// 全局选择器**按归属者分发**守卫（改进-2 / Phase 1）：CardSelectCmd.Selector 返回栈顶选择器时，
/// 按本次异步链的选牌归属者（<c>CurrentChoicePlayerId</c>）决定处置：
/// <list type="bullet">
/// <item>归属者是**瓦库形态**且已在 <see cref="WakuuSelectorRegistry"/> 登记 → 改用**它自己**的选择器
///   （多瓦库作用域同时存在时不再被栈顶抢答）；</item>
/// <item>归属者是**真人** → 临时返回 null，让其走正常选牌 UI
///   （场景：瓦库自动出牌循环进行中，真人同时打出需要选牌的卡，如酒狐合成）；</item>
/// <item>归属者未知 / 瓦库但未登记 → 保持栈顶（与升级前的三条老路严格一致）。</item>
/// </list>
/// 判定口径收敛在纯函数 <see cref="WakuuSelectorDispatch.Decide"/>（可单测）。
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
        ICardSelector? top = __result;
        if (top == null)
        {
            return;
        }

        // 只认托管选择器（游戏原生 VakuuCardSelector / 本 mod 策略选择器）；其余（如测试用）不动
        if (top is not VakuuCardSelector and not LocalWakuuStrategySelector)
        {
            return;
        }

        ulong? chooserPlayerId = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
        bool chooserIsWakuu = chooserPlayerId.HasValue
            && LocalWakuuRelicRuntime.IsVakuuFormModeById(chooserPlayerId.Value);

        ICardSelector? owned = null;
        bool registryHit = false;
        if (chooserIsWakuu && chooserPlayerId.HasValue)
        {
            registryHit = WakuuSelectorRegistry.TryGet(chooserPlayerId.Value, out owned) && owned != null;
        }

        switch (WakuuSelectorDispatch.Decide(chooserPlayerId.HasValue, registryHit, chooserIsWakuu))
        {
            case SelectorDispatchDecision.UseOwned:
                // 只在**真的换了一个选择器**时打日志（命中同一实例是常态，不能刷屏）
                if (!ReferenceEquals(owned, top))
                {
                    LocalMultiControlLogger.Info(
                        $"选牌选择器按归属分发: chooser={chooserPlayerId!.Value}, "
                        + $"stackTop={top.GetType().Name}, owned={owned!.GetType().Name}, registryCount={WakuuSelectorRegistry.Count}");
                }

                __result = owned;
                return;

            case SelectorDispatchDecision.ReturnNull:
                LocalMultiControlLogger.Info(
                    $"检测到真人选牌请求，本次跳过瓦库选择器改走正常UI: chooser={chooserPlayerId!.Value}");
                __result = null;
                return;

            default:
                return;
        }
    }
}

/// <summary>
/// 运行清理时同步清空归属者注册表（改进-2 / Phase 1）。
/// 原版 <c>CardSelectCmd.Reset()</c> 只在 run cleanup 调用、且只清游戏自己的选择器栈；
/// 若不同步清我们的登记表，被卡住的异步链泄漏的条目会跨局残留（指向已失效的选择器实例）。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.Reset))]
internal static class CardSelectCmdResetRegistryPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        int cleared = WakuuSelectorRegistry.Reset();
        if (cleared > 0)
        {
            LocalMultiControlLogger.Info($"运行清理：已清空瓦库选择器归属注册表（{cleared} 条）");
        }
    }
}
