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
/// 瓦库事件自动选择期间的选牌自动作答（修复「事件里需要选择卡牌时不会自动选择」）。
///
/// 背景：LocalWakuuEventAutoChoice 直接调 EventOption.Chosen()，期间若事件选项触发卡牌选牌
/// （附魔 FieldOfManSizedHoles/GraveOfTheForgotten/SelfHelpBook、升级 SapphireSeed/AromaOfChaos、
/// 变化 WhisperingHollow/Trial/Symbiote/MorphicGrove、删除 FieldOfManSizedHoles/DoorsOfLightAndDark、
/// 或 mod 事件从手牌选牌），会弹出 NDeckEnchantSelectScreen / NDeckUpgradeSelectScreen /
/// NDeckTransformSelectScreen / NDeckCardSelectScreen 等选牌界面，自动选择随即因"出现弹层"而停住等真人。
///
/// 修复：事件选项执行期间 LocalWakuuEventAutoChoice 会把 InEventAutoChoiceScope 置为 true，
/// 本补丁在该作用域内拦截 CardSelectCmd 的选牌入口，用 LocalWakuuStrategySelector 按
/// cardPickMode 配置（最前/最后/随机/稀有度最高）直接作答，返回结果、不弹界面。
/// 真人玩家自己的事件选牌不在作用域内，不受影响（无全局选择器栈，天然隔离）。
///
/// ⚠ 注意：各 From* 前缀必须用「bool 返回 + ref __result」的跳过式拦截（这里不需要其它补丁的
/// __state，与 CardTransformNetIdPinPatch 不同——那个方法被 RitsuLib 等挂了 __state 前后缀）。
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
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || cards == null || cards.Count == 0)
        {
            return true;
        }

        Player? owner = cards[0].Owner;
        if (owner == null || !LocalWakuuRelicRuntime.IsVakuuFormMode(owner))
        {
            return true;
        }

        return TryAutoAnswer(owner, cards, prefs, "FromDeckForEnchantment", ref __result);
    }

    /// <summary>
    /// 手牌选牌兜底（mod 事件可能从手牌选牌附魔）：
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
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || player == null)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
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

        return TryAutoAnswer(player, candidates, prefs, "FromHand", ref __result);
    }

    /// <summary>牌库升级选牌（火堆 smith 同入口，事件里 SapphireSeed/AromaOfChaos 等触发）。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromDeckForUpgradePrefix(
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || player == null)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        List<CardModel> candidates = PileType.Deck.GetPile(player).Cards
            .Where((CardModel c) => c.IsUpgradable)
            .ToList();

        return TryAutoAnswer(player, candidates, prefs, "FromDeckForUpgrade", ref __result);
    }

    /// <summary>牌库变化选牌（WhisperingHollow/Trial/Symbiote/MorphicGrove 等事件触发）。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, CardTransformation>),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromDeckForTransformationPrefix(
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || player == null)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        // 与原方法同一过滤：排除 Quest、不可变化牌
        List<CardModel> candidates = PileType.Deck.GetPile(player).Cards
            .Where((CardModel c) => c.Type != CardType.Quest && c.IsTransformable)
            .ToList();

        return TryAutoAnswer(player, candidates, prefs, "FromDeckForTransformation", ref __result);
    }

    /// <summary>
    /// 牌库通用选牌（删除 FromDeckForRemoval 内部走这里；WoodCarvings 等按稀有度过滤的也走这里）。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
        typeof(Func<CardModel, int>),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromDeckGenericPrefix(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || player == null)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        List<CardModel> candidates = PileType.Deck.GetPile(player).Cards
            .Where(filter ?? (_ => true))
            .ToList();

        return TryAutoAnswer(player, candidates, prefs, "FromDeckGeneric", ref __result);
    }

    /// <summary>
    /// 牌库删除选牌（FromDeckForRemoval 专用入口，FieldOfManSizedHoles / DoorsOfLightAndDark /
    /// LuminousChoir / Wellspring / 商店删牌 / 各删牌遗物等全部走这里）。
    /// 拦截后按 smartPick 的"删除优先级表"作答（优先删 诅咒→状态→任务→打击→防御）。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForRemoval), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
    })]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromDeckForRemovalPrefix(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!LocalWakuuEventAutoChoice.InEventAutoChoiceScope.Value || player == null)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        // 与原方法同一过滤：只选可移除牌 + 调用方过滤（如 Amalgamator 的 Strike/Defend 限定）
        List<CardModel> candidates = PileType.Deck.GetPile(player).Cards
            .Where((CardModel c) => c.IsRemovable && (filter == null || filter(c)))
            .ToList();

        return TryAutoAnswer(player, candidates, prefs, "FromDeckForRemoval", ref __result);
    }

    /// <summary>
    /// 统一作答入口：候选非空且「需要真实选择」时按策略作答
    /// （smartPick 开启且场景明确时套优先级表，否则 cardPickMode）。
    /// 需要真实选择 = 候选数 &gt; MinSelect，或 RequireManualConfirmation（本地双控下升级/变化/通用被
    /// CardSelectManualConfirmationPatch 强制置 true）。
    /// 返回 false = 已拦截作答；返回 true = 交回原流程。
    /// </summary>
    private static bool TryAutoAnswer(
        Player player,
        IReadOnlyList<CardModel> candidates,
        CardSelectorPrefs prefs,
        string source,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return true;
        }

        if (candidates.Count <= prefs.MinSelect && !prefs.RequireManualConfirmation)
        {
            return true; // 原方法会自动全选，无需拦截
        }

        LocalMultiControlLogger.Info(
            $"瓦库事件选牌自动作答: player={player.NetId}, eventScope=true, options={candidates.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source={source}");

        __result = ComputeAnswerAsync(candidates, prefs, ScenarioForSource(source));
        return false;
    }

    /// <summary>
    /// 由 CardSelectCmd 入口方法名判定选牌场景（可行性分析 §9.1）：
    /// 专用方法（删除/变化）直接区分；未知场景（附魔/升级/手牌/通用）维持既有策略。
    /// 注意 FromDeckGeneric 被 WoodCarvings（变化选保留牌）等共用，不归入 Remove。
    /// </summary>
    private static WakuuPickScenario ScenarioForSource(string source)
    {
        return source switch
        {
            "FromDeckForRemoval" => WakuuPickScenario.Remove,
            "FromDeckForTransformation" => WakuuPickScenario.Transform,
            _ => WakuuPickScenario.Unknown,
        };
    }

    private static async Task<IEnumerable<CardModel>> ComputeAnswerAsync(
        IReadOnlyList<CardModel> candidates, CardSelectorPrefs prefs, WakuuPickScenario scenario)
    {
        LocalWakuuStrategySelector selector = new(scenario);
        return await selector.GetSelectedCards(candidates, prefs.MinSelect, prefs.MaxSelect);
    }
}
