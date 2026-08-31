using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
/// 瓦库事件自动选择期间的选牌自动作答（修复「事件里需要选择卡牌附魔时不会自动选择」）。
///
/// 背景：LocalWakuuEventAutoChoice 直接调 EventOption.Chosen()，期间若事件选项触发卡牌选牌
/// （如 FieldOfManSizedHoles / GraveOfTheForgotten / SelfHelpBook 的附魔，或 mod 事件从手牌选牌附魔），
/// 会弹出 NDeckEnchantSelectScreen / NPlayerHand 选牌界面，自动选择随即因"出现弹层"而停住等真人。
///
/// 修复：事件选项执行期间 LocalWakuuEventAutoChoice 会把 InEventAutoChoiceScope 置为 true，
/// 本补丁在该作用域内拦截 CardSelectCmd 的选牌入口，用 LocalWakuuStrategySelector 按
/// cardPickMode 配置（最前/最后/随机/稀有度最高）直接作答，返回结果、不弹界面。
/// 真人玩家自己的事件选牌不在作用域内，不受影响（无全局选择器栈，天然隔离）。
/// </summary>
[HarmonyPatch]
internal static class WakuuEventEnchantAutoAnswerPatch
{
    /// <summary>
    /// 附魔选牌兜底入口（FromDeckForEnchantment 各 Player 重载最终都走这里）：
    /// 拦截后按策略作答，跳过 NDeckEnchantSelectScreen。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForEnchantment), new[]
    {
        typeof(IReadOnlyList<CardModel>),
        typeof(EnchantmentModel),
        typeof(int),
        typeof(CardSelectorPrefs),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromDeckForEnchantmentPrefix(
        IReadOnlyList<CardModel> cards,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value)
        {
            return true;
        }

        if (cards == null || cards.Count == 0)
        {
            return true;
        }

        Player? owner = cards[0].Owner;
        if (owner == null || !LocalWakuuRelicRuntime.IsVakuuFormMode(owner))
        {
            return true;
        }

        // 原方法在候选数 <= MinSelect 时自动全选，无需拦截
        if (cards.Count <= prefs.MinSelect)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库事件附魔选牌自动作答: player={owner.NetId}, eventScope=true, options={cards.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source=FromDeckForEnchantment");

        __result = ComputeAnswerAsync(cards, prefs);
        return false;
    }

    /// <summary>
    /// 手牌选牌附魔兜底（mod 事件可能从手牌选牌附魔）：
    /// 仅在事件自动选择作用域内拦截，战斗内选牌不受影响。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand), new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
        typeof(AbstractModel),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromHandPrefix(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value)
        {
            return true;
        }

        if (player == null || !LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        List<CardModel> candidates;
        try
        {
            candidates = PileType.Hand.GetPile(player).Cards
                .Where(filter ?? (_ => true))
                .ToList();
        }
        catch (Exception exception)
        {
            // 事件内手牌堆可能不可用（如非战斗），交回原方法处理
            LocalMultiControlLogger.Warn($"瓦库事件手牌选牌自动作答取手牌失败，交回原流程: {exception.Message}");
            return true;
        }

        if (candidates.Count <= prefs.MinSelect)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库事件手牌选牌自动作答: player={player.NetId}, eventScope=true, options={candidates.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source=FromHand");

        __result = ComputeAnswerAsync(candidates, prefs);
        return false;
    }

    private static async Task<IEnumerable<CardModel>> ComputeAnswerAsync(IReadOnlyList<CardModel> candidates, CardSelectorPrefs prefs)
    {
        LocalWakuuStrategySelector selector = new();
        return await selector.GetSelectedCards(candidates, prefs.MinSelect, prefs.MaxSelect);
    }
}
