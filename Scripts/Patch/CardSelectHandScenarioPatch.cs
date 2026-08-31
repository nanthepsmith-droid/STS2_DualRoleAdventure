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
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗内手牌选牌场景自动作答（可行性分析 §9.1 ★★：FromHand 按 source 类型名 / prefs 标题 loc key 分类）。
///
/// 背景：瓦库自动出牌作用域内压入的全局选择器（LocalWakuuStrategySelector 无场景）会把所有
/// From* 选牌按 cardPickMode 策略作答。但对「从手牌选一张复制它」（DualWield 双重挥砍等复制类）、
/// 「从手牌选一张消耗它」（Brand / 保暖手套 / 暴政之力等）、「从手牌选一张变化它」（熵 / 离去等），
/// §9.2 有专门的优先级表（Copy 首选非坏牌 / Remove 优先坏牌 / Transform 优先打击防御）——
/// 比无脑 first/last 更符合"不犯低级错误"的瓦库目标。
///
/// 方案：本补丁在 smartPick 开启且处于瓦库出牌作用域时，用 source 类型名 + prefs 标题判定场景，
/// 命中即用带场景的 LocalWakuuStrategySelector 直接作答；场景 Unknown（弃牌/附魔等无明确优先级
/// 语义）交回原流程，维持既有 cardPickMode 策略。
///
/// ⚠ 硬约束（r6 回滚教训）：必须在「瓦库出牌作用域内」才代答（栈上全局选择器是本 mod 的策略
/// 选择器）。作用域外选牌一律不代答，避免进战斗黑屏。
/// 事件自动选择作用域内的 FromHand 由 WakuuEventEnchantAutoAnswerPatch 负责，此处不重复拦截。
/// </summary>
[HarmonyPatch]
internal static class CardSelectHandScenarioPatch
{
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
        AbstractModel source,
        ref Task<IEnumerable<CardModel>> __result)
    {
        // 事件自动选择作用域内由 WakuuEventEnchantAutoAnswerPatch 作答（含场景判定），此处不重复拦截。
        if (LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value)
        {
            return true;
        }

        // 仅在智能选牌优先级开启 + 瓦库形态角色上生效；非瓦库角色保持原行为。
        if (!LocalWakuuAutopilotConfig.SmartPick
            || player == null
            || !LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        // 必须在瓦库出牌作用域内（栈上全局选择器为本 mod 的策略选择器）——作用域外选牌一律不代答
        // （r6 回滚教训：作用域外自动作答曾导致进战斗黑屏，选牌仍交真人/安全网处理）。
        if (CardSelectCmd.Selector is not LocalWakuuStrategySelector)
        {
            return true;
        }

        WakuuPickScenario scenario = WakuuPriorityPicking.ClassifyHandScenario(
            source?.GetType().Name,
            BuildPrefsLocKey(prefs.Prompt));
        if (scenario == WakuuPickScenario.Unknown)
        {
            // 未知来源（弃牌/附魔/升级等无明确优先级语义）维持既有 cardPickMode 策略
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
            // 手牌堆不可用（异常时序）时交回原流程处理
            LocalMultiControlLogger.Warn($"瓦库手牌智能选牌取手牌失败，交回原流程: {exception.Message}");
            return true;
        }

        if (candidates.Count == 0)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库手牌智能选牌: player={player.NetId}, scenario={scenario}, source={source?.GetType().Name}, "
            + $"options={candidates.Count}, select={prefs.MaxSelect}");
        __result = new LocalWakuuStrategySelector(scenario)
            .GetSelectedCards(candidates, prefs.MinSelect, prefs.MaxSelect);
        return false;
    }

    /// <summary>prefs 标题 loc key（如 card_selection/TO_EXHAUST）。供 ClassifyHandScenario 识别预设标题。</summary>
    internal static string BuildPrefsLocKey(LocString? prompt)
    {
        if (prompt == null || string.IsNullOrEmpty(prompt.LocEntryKey))
        {
            return string.Empty;
        }

        return prompt.LocTable + "/" + prompt.LocEntryKey;
    }
}
