using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库回合开始选牌自动作答。
/// 背景：酒狐等 mod 的初始遗物在战斗第一回合的 <c>AfterPlayerTurnStart</c>（此时瓦库自动出牌作用域
/// 尚未启动、全局选择器栈上无选择器）弹出 <c>CardSelectCmd.FromChooseACardScreen</c> 二选一
/// （如应力/资源）。对后台瓦库角色而言该界面无人点击，只能靠切前台交真人处理。
/// 本补丁在「后台瓦库角色 + 当前栈上无选择器」时，用策略选择器直接作答，避免真人手动接管。
/// 若栈上已有选择器（瓦库自动出牌作用域内，如攻击药水选牌），则交回原选择器处理，不在此干预。
/// 与 CardSelectForegroundSwitchPatch 的切前台逻辑正交：本补丁直接返回结果、跳过原方法，
/// 原切前台前缀仍会执行（保持既有前台行为，避免回退到"作用域外代答导致进战斗黑屏"的历史问题）。
/// </summary>
[HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
internal static class CardSelectWakuuTurnStartAutoAnswerPatch
{
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool Prefix(
        IReadOnlyList<CardModel> cards,
        Player player,
        ref Task<CardModel?> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库回合开始选牌自动作答: player={player.NetId}, options={cards.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source=FromChooseACardScreen");

        __result = ComputeAnswerAsync(cards);
        return false;
    }

    private static async Task<CardModel?> ComputeAnswerAsync(IReadOnlyList<CardModel> cards)
    {
        LocalWakuuStrategySelector selector = new();
        IEnumerable<CardModel> selected = await selector.GetSelectedCards(cards, 0, 1);
        return selected.FirstOrDefault();
    }

    private static bool ShouldAutoAnswer(Player player)
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
}
