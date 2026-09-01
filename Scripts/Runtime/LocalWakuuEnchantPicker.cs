using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库附魔智能选牌（可行性分析 §9 智能选牌的附魔分支；规则表见
/// maintenance-docs/decision-records/原版附魔一览表.md）。
///
/// 职责：把游戏 CardModel 抽成纯数据 WakuuEnchantCardInfo → 查 WakuuEnchantRules 规则表 →
/// 用 WakuuEnchantPicking 排序取牌。任一步异常/无规则返回 null，调用方回退既有 cardPickMode。
///
/// 规则表来源说明：
/// - 牌名 / 遗物名用游戏内部 id（由中文本地化 zhs 反查），升级后缀 "+" 记为 RequireUpgraded；
/// - 遗物条件（如"必须有化学物 X"）查玩家已持有遗物；牌组条件（如"必须有化废为宝/散射炮"）查牌组。
/// </summary>
internal static class LocalWakuuEnchantPicker
{
    /// <summary>
    /// 按附魔规则挑牌。返回 null = 该附魔无规则或异常，交回既有策略。
    /// </summary>
    public static List<CardModel>? TryPick(
        Player owner,
        IReadOnlyList<CardModel> candidates,
        EnchantmentModel? enchantment,
        int minSelect,
        int maxSelect)
    {
        if (candidates == null || candidates.Count == 0 || enchantment == null)
        {
            return null;
        }

        try
        {
            List<string> ownedRelics = owner.Relics
                .Select((relic) => relic.Id.Entry)
                .ToList();
            List<string> deckCardIds = PileType.Deck.GetPile(owner).Cards
                .Select((card) => card.Id.Entry)
                .ToList();

            IReadOnlyList<WakuuEnchantRuleEntry>? rule =
                WakuuEnchantRules.Resolve(enchantment.GetType().Name, ownedRelics);
            if (rule == null)
            {
                return null; // 该附魔用户填"维持现状"
            }

            List<WakuuEnchantCardInfo> infos = candidates.Select(ToInfo).ToList();
            List<int> ranked = WakuuEnchantPicking.RankIndices(infos, rule, ownedRelics, deckCardIds);
            int take = Math.Max(maxSelect, Math.Max(minSelect, 1));
            List<CardModel> picked = ranked
                .Take(take)
                .Select((index) => candidates[index])
                .ToList();

            LocalMultiControlLogger.Info(
                $"瓦库附魔智能选牌: player={owner.NetId}, enchant={enchantment.GetType().Name}, "
                + $"options={candidates.Count}, select={picked.Count}, "
                + $"picked={string.Join(",", picked.Select((card) => card.Id.Entry))}");
            return picked;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库附魔智能选牌异常，回退既有策略: {exception.Message}");
            return null;
        }
    }

    private static WakuuEnchantCardInfo ToInfo(CardModel card)
    {
        return new WakuuEnchantCardInfo(
            id: card.Id.Entry,
            cardType: (int)card.Type,
            cost: card.EnergyCost.Canonical,
            costsX: card.EnergyCost.CostsX,
            damage: ReadVar(card, "Damage"),
            hitCount: Math.Max(ReadVar(card, "Repeat"), 1),
            block: ReadVar(card, "Block"),
            drawCount: ReadVar(card, "Cards"),
            rarity: (int)card.Rarity,
            exhaust: card.Keywords.Contains(CardKeyword.Exhaust),
            retain: card.Keywords.Contains(CardKeyword.Retain),
            upgraded: card.IsUpgraded);
    }

    /// <summary>
    /// 安全读取卡牌变量值（基础值取整）。
    /// 注意：DynamicVarSet 的具名属性（Damage/Block/Cards/Repeat）对缺失变量会抛异常，
    /// 必须走 TryGetValue。
    /// </summary>
    private static int ReadVar(CardModel card, string key)
    {
        DynamicVarSet? vars = card.DynamicVars;
        if (vars == null || !vars.TryGetValue(key, out DynamicVar? value) || value == null)
        {
            return 0;
        }

        return (int)Math.Round(value.BaseValue);
    }
}
