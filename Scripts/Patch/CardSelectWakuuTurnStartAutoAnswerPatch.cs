using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库「作用域外」选牌自动作答（回合开始类遗物效果为主）。
///
/// 背景：酒狐初始遗物、非想天则、战斗开始给牌类遗物（如【工具箱】三选一）会在**瓦库自动出牌作用域
/// 之外**（全局选择器栈为空）弹出选牌：
/// - <c>CardSelectCmd.FromChooseACardScreen</c>：二选一（酒狐「应力/资源」等）；
/// - <c>CardSelectCmd.FromSimpleGrid</c>：网格多选一（工具箱给的无色牌三选一等）。
/// 对后台托管的瓦库角色而言这类界面无人点击，原实现只能切前台交真人处理 —— 与「瓦库托管」语义相悖
/// （实机 marker r107 日志：`combat-choice-FromSimpleGrid` 切前台后需真人替瓦库选牌）。
///
/// 本补丁在「瓦库形态 + 后台托管 + 栈上无选择器」时用策略选择器直接作答，真人无需接管。
/// 若栈上已有选择器（瓦库自动出牌作用域内，如攻击药水选牌），交回原选择器处理，不在此干预。
///
/// 与 <see cref="CardSelectForegroundSwitchPatch"/> 的关系：该补丁的切前台前缀会通过
/// <see cref="IsAutoAnswerEntry"/> + <see cref="ShouldAutoAnswer"/> 识别「这次会被自动作答」，
/// 从而跳过无谓的切前台（避免视角闪一下）。作用域外**不会**被自动作答的选牌仍切前台交真人
/// （防软锁兜底，历史教训 b949dfa：一律自动作答会导致进战斗黑屏）。
/// </summary>
[HarmonyPatch]
internal static class CardSelectWakuuTurnStartAutoAnswerPatch
{
    /// <summary>二选一入口（酒狐初始遗物等）。返回单张，作用于 <c>Task&lt;CardModel?&gt;</c>。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromChooseACardScreenPrefix(
        IReadOnlyList<CardModel> cards,
        Player player,
        ref Task<CardModel?> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外选牌自动作答: player={player.NetId}, options={cards.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source=FromChooseACardScreen");

        __result = ComputeSingleAnswerAsync(cards);
        return false;
    }

    /// <summary>网格多选一入口（工具箱等战斗开始给牌类遗物）。返回多张，作用于 <c>Task&lt;IEnumerable&lt;CardModel&gt;&gt;</c>。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromSimpleGridPrefix(
        IReadOnlyList<CardModel> cardsIn,
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外选牌自动作答: player={player.NetId}, options={cardsIn.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
            + "source=FromSimpleGrid");

        __result = ComputeGridAnswerAsync(cardsIn, prefs);
        return false;
    }

    /// <summary>该选牌入口是否由本补丁对瓦库做「作用域外自动作答」（供切前台前缀判断"要不要切"）。</summary>
    internal static bool IsAutoAnswerEntry(string source)
    {
        return source is "FromChooseACardScreen" or "FromSimpleGrid";
    }

    /// <summary>
    /// 是否满足「作用域外自动作答」条件（与入口无关的公共判据）。
    /// 供切前台前缀复用（<see cref="CardSelectForegroundSwitchPatch"/>）：命中时切前台是空操作。
    /// </summary>
    internal static bool ShouldAutoAnswer(Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return false;
        }

        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(player.NetId))
        {
            return false;
        }

        // 仅后台托管模式：瓦库不应占用前台、也不需要真人接手。
        if (!LocalWakuuAutopilotConfig.BackgroundMode)
        {
            return false;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return false;
        }

        // 已有选择器（瓦库自动出牌作用域内）→ 交回原选择器处理，避免重复作答。
        if (CardSelectCmd.Selector != null)
        {
            return false;
        }

        return true;
    }

    private static async Task<CardModel?> ComputeSingleAnswerAsync(IReadOnlyList<CardModel> cards)
    {
        LocalWakuuStrategySelector selector = new();
        IEnumerable<CardModel> selected = await selector.GetSelectedCards(cards, 0, 1);
        return selected.FirstOrDefault();
    }

    private static async Task<IEnumerable<CardModel>> ComputeGridAnswerAsync(
        IReadOnlyList<CardModel> cards,
        CardSelectorPrefs prefs)
    {
        // 与 CardSelectCmd.FromSimpleGrid 走全局选择器时的口径一致：min/max 取自 prefs。
        LocalWakuuStrategySelector selector = new();
        return await selector.GetSelectedCards(cards, prefs.MinSelect, prefs.MaxSelect);
    }
}
