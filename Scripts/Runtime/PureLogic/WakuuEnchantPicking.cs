using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 附魔选牌排序方式（用户填表的数值维度）。
/// </summary>
internal enum WakuuEnchantSort
{
    /// <summary>保持原序（"其它牌"、"从左到右"）。</summary>
    Index,

    /// <summary>费用最高。</summary>
    CostDesc,

    /// <summary>费用最低。</summary>
    CostAsc,

    /// <summary>伤害最高（按 伤害×段数 估算，X 费牌按费用=3 折算）。</summary>
    DamageDesc,

    /// <summary>格挡最高。</summary>
    BlockDesc,

    /// <summary>稀有度最高。</summary>
    RarityDesc,
}

/// <summary>
/// 卡牌特征快照（纯数据，由调用方从 CardModel 抽取，不依赖任何游戏类型）。
/// </summary>
internal readonly struct WakuuEnchantCardInfo
{
    public WakuuEnchantCardInfo(
        string? id,
        int cardType,
        int cost,
        bool costsX,
        int damage,
        int hitCount,
        int block,
        int drawCount,
        int rarity,
        bool exhaust,
        bool retain,
        bool upgraded)
    {
        Id = id ?? string.Empty;
        CardType = cardType;
        Cost = cost;
        CostsX = costsX;
        Damage = damage;
        HitCount = hitCount;
        Block = block;
        DrawCount = drawCount;
        Rarity = rarity;
        Exhaust = exhaust;
        Retain = retain;
        Upgraded = upgraded;
    }

    /// <summary>裸卡 id（CardModel.Id.Entry，大写）。</summary>
    public string Id { get; }

    /// <summary>(int)CardType：1=Attack 2=Skill 3=Power。</summary>
    public int CardType { get; }

    /// <summary>费用（X 费牌该值无意义，用 CostsX 判断）。</summary>
    public int Cost { get; }

    /// <summary>是否 X 费牌。</summary>
    public bool CostsX { get; }

    /// <summary>基础伤害（无伤害变量为 0）。</summary>
    public int Damage { get; }

    /// <summary>多段次数（Repeat 变量，无则为 1）。</summary>
    public int HitCount { get; }

    /// <summary>基础格挡（无格挡变量为 0）。</summary>
    public int Block { get; }

    /// <summary>抽牌数量（Cards 变量，非抽牌卡为 0）。</summary>
    public int DrawCount { get; }

    /// <summary>(int)CardRarity。</summary>
    public int Rarity { get; }

    /// <summary>带消耗关键词。</summary>
    public bool Exhaust { get; }

    /// <summary>带保留关键词。</summary>
    public bool Retain { get; }

    /// <summary>已升级。</summary>
    public bool Upgraded { get; }
}

/// <summary>
/// 筛选谓词：各字段取默认值即"不限"，多个字段为与关系。
/// </summary>
internal readonly struct WakuuEnchantPredicate
{
    /// <summary>卡牌类型位掩码：0=任意，1=Attack，2=Skill，4=Power（可组合）。</summary>
    public int CardTypeMask { get; init; }

    /// <summary>消耗要求：null=不限，true=必须消耗，false=必须不消耗。</summary>
    public bool? Exhaust { get; init; }

    /// <summary>必须带保留。</summary>
    public bool RequireRetain { get; init; }

    /// <summary>多段次数下限（0=不限）。</summary>
    public int MinHitCount { get; init; }

    /// <summary>多段次数精确匹配（0=不限）。</summary>
    public int ExactHitCount { get; init; }

    /// <summary>抽牌数下限（0=不限）。</summary>
    public int MinDrawCount { get; init; }

    /// <summary>格挡值必须大于该值（0=不限）。</summary>
    public int MinBlock { get; init; }

    /// <summary>费用下限（-1=不限）。</summary>
    public int MinCost { get; init; }

    /// <summary>费用精确匹配（null=不限；0=必须 0 费）。</summary>
    public int? ExactCost { get; init; }

    /// <summary>必须为 X 费牌（用户填表：华彩等优先 X 费）。</summary>
    public bool RequireCostsX { get; init; }

    /// <summary>必须已升级。</summary>
    public bool RequireUpgraded { get; init; }

    public bool Matches(in WakuuEnchantCardInfo card)
    {
        if (CardTypeMask != 0 && (CardTypeMask & (1 << (card.CardType - 1))) == 0)
        {
            return false;
        }

        if (Exhaust.HasValue && card.Exhaust != Exhaust.Value)
        {
            return false;
        }

        if (RequireRetain && !card.Retain)
        {
            return false;
        }

        if (MinHitCount > 0 && card.HitCount < MinHitCount)
        {
            return false;
        }

        if (ExactHitCount > 0 && card.HitCount != ExactHitCount)
        {
            return false;
        }

        if (MinDrawCount > 0 && card.DrawCount < MinDrawCount)
        {
            return false;
        }

        if (MinBlock > 0 && card.Block <= MinBlock)
        {
            return false;
        }

        if (MinCost >= 0 && card.Cost < MinCost)
        {
            return false;
        }

        if (ExactCost.HasValue && card.Cost != ExactCost.Value)
        {
            return false;
        }

        if (RequireCostsX && !card.CostsX)
        {
            return false;
        }

        if (RequireUpgraded && !card.Upgraded)
        {
            return false;
        }

        return true;
    }
}

/// <summary>
/// 规则条目：附魔优先级表的一行。
/// CardId 非空时按精确牌名匹配（可叠加升级要求），否则按 Predicate 匹配；
/// 附加条件（遗物 / 牌组）全部满足才命中。
/// </summary>
internal sealed class WakuuEnchantRuleEntry
{
    /// <summary>精确牌名（CardModel.Id.Entry）。非空时优先按牌名匹配。</summary>
    public string? CardId { get; init; }

    /// <summary>类别谓词（CardId 为空时生效）。</summary>
    public WakuuEnchantPredicate Predicate { get; init; }

    /// <summary>玩家需要持有其中任一遗物（遗物 id）。</summary>
    public string[]? RequiredRelicAny { get; init; }

    /// <summary>玩家牌组需要含其中任一张牌（卡 id）。</summary>
    public string[]? RequiredDeckCardAny { get; init; }

    /// <summary>命中该条目后的排序方式，默认保持原序。</summary>
    public WakuuEnchantSort Sort { get; init; } = WakuuEnchantSort.Index;
}

/// <summary>
/// 附魔选牌规则纯函数（用户填表 → 数据驱动优先级表）。
///
/// 执行方式：按条目顺序，取第一个能筛出非空候选的条目，按其排序方式输出下标列表
/// （调用方取前 N 张）。全部条目为空或规则为 null → 返回原序（维持既有策略）。
/// </summary>
internal static class WakuuEnchantPicking
{
    /// <summary>X 费牌在伤害 / 费用估算时按该费用折算（用户填表口径：X 费按费用为 3 计算）。</summary>
    public const int XCostAssumedValue = 3;

    // CardType 取值（与游戏 MegaCrit.Sts2.Core.Entities.Cards.CardType 一致）
    public const int CardTypeAttack = 1;
    public const int CardTypeSkill = 2;
    public const int CardTypePower = 3;

    /// <summary>类型位掩码便捷值。</summary>
    public const int MaskAttack = 1 << (CardTypeAttack - 1);
    public const int MaskSkill = 1 << (CardTypeSkill - 1);
    public const int MaskPower = 1 << (CardTypePower - 1);

    /// <summary>
    /// 按规则对候选排序，返回下标列表（稳定：同排序键保持原序）。
    /// </summary>
    public static List<int> RankIndices(
        IReadOnlyList<WakuuEnchantCardInfo> cards,
        IReadOnlyList<WakuuEnchantRuleEntry>? rule,
        IReadOnlyList<string>? ownedRelics,
        IReadOnlyList<string>? deckCardIds)
    {
        int count = cards?.Count ?? 0;
        List<int> indices = new(count);
        for (int i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        if (count == 0 || rule == null || rule.Count == 0)
        {
            return indices; // 无规则 = 维持既有 cardPickMode 策略
        }

        foreach (WakuuEnchantRuleEntry entry in rule)
        {
            if (entry == null || !ConditionsMet(entry, ownedRelics, deckCardIds))
            {
                continue;
            }

            List<int> matched = indices.Where((i) => MatchesEntry(entry, cards![i])).ToList();
            if (matched.Count == 0)
            {
                continue;
            }

            return SortIndices(matched, cards!, entry.Sort);
        }

        return indices;
    }

    private static bool MatchesEntry(WakuuEnchantRuleEntry entry, in WakuuEnchantCardInfo card)
    {
        if (entry.CardId != null)
        {
            if (!string.Equals(card.Id, entry.CardId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            return entry.Predicate.Matches(card); // 通常只叠加"必须已升级"
        }

        return entry.Predicate.Matches(card);
    }

    private static bool ConditionsMet(
        WakuuEnchantRuleEntry entry,
        IReadOnlyList<string>? ownedRelics,
        IReadOnlyList<string>? deckCardIds)
    {
        if (entry.RequiredRelicAny != null && entry.RequiredRelicAny.Length > 0
            && !ContainsAny(ownedRelics, entry.RequiredRelicAny))
        {
            return false;
        }

        if (entry.RequiredDeckCardAny != null && entry.RequiredDeckCardAny.Length > 0
            && !ContainsAny(deckCardIds, entry.RequiredDeckCardAny))
        {
            return false;
        }

        return true;
    }

    private static bool ContainsAny(IReadOnlyList<string>? owned, string[] targets)
    {
        if (owned == null || owned.Count == 0)
        {
            return false;
        }

        foreach (string target in targets)
        {
            foreach (string item in owned)
            {
                if (string.Equals(item, target, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static List<int> SortIndices(List<int> indices, IReadOnlyList<WakuuEnchantCardInfo> cards, WakuuEnchantSort sort)
    {
        switch (sort)
        {
            case WakuuEnchantSort.CostDesc:
                return indices.OrderByDescending((i) => EffectiveCost(cards[i])).ToList();
            case WakuuEnchantSort.CostAsc:
                return indices.OrderBy((i) => EffectiveCost(cards[i])).ToList();
            case WakuuEnchantSort.DamageDesc:
                return indices.OrderByDescending((i) => EstimateDamage(cards[i])).ToList();
            case WakuuEnchantSort.BlockDesc:
                return indices.OrderByDescending((i) => cards[i].Block).ToList();
            case WakuuEnchantSort.RarityDesc:
                return indices.OrderByDescending((i) => cards[i].Rarity).ToList();
            default:
                return indices; // 稳定：原序
        }
    }

    /// <summary>X 费牌按费用 3 参与费用比较。</summary>
    private static int EffectiveCost(in WakuuEnchantCardInfo card)
    {
        return card.CostsX ? XCostAssumedValue : card.Cost;
    }

    /// <summary>
    /// 伤害估算：基础伤害 × 段数；X 费牌段数按 3 折算（用户填表口径）。
    /// 用于"伤害最高的牌"这类排序，不追求精确。
    /// </summary>
    public static int EstimateDamage(in WakuuEnchantCardInfo card)
    {
        int hits = card.CostsX ? XCostAssumedValue : Math.Max(card.HitCount, 1);
        return card.Damage * hits;
    }
}
