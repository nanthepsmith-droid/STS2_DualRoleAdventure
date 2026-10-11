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

    /// <summary>
    /// **同档随机**（用户 2026-10-11 口径：两个条件"优先级相当"时不要总挑同一个）。
    /// 纯逻辑里用调用方传入的 <c>shuffleSeed</c> 做**确定性**打乱（同种子同结果 ⇒ 可单测），
    /// 运行时传真随机种子 ⇒ 表现为"每次随机挑一个"。
    /// </summary>
    Random,
}

/// <summary>
/// 卡牌 id 关键词（原版只按"打击/防御"这类基础牌附魔，但 mod 卡池命名不统一 ⇒ 用 id 关键词兜）。
/// </summary>
internal enum WakuuEnchantKeyword
{
    /// <summary>不限。</summary>
    None = 0,

    /// <summary>打击类（id 含 STRIKE）。</summary>
    Strike = 1,

    /// <summary>防御类（id 含 DEFEND）。</summary>
    Defend = 2,

    /// <summary>打击或防御（两者之一）。</summary>
    StrikeOrDefend = 3,
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

    /// <summary>
    /// 卡牌 id 关键词（打击/防御）：原版"漩涡 / 特兹卡塔拉的余烬"只允许选基础打击/防御牌，
    /// 但 **mod 卡池**里可能挂着同名附魔却能选别的牌 ⇒ 用户 2026-10-11 口径改成
    /// "先按费用挑，再用打击/防御关键词兜底"。
    /// </summary>
    public WakuuEnchantKeyword Keyword { get; init; }

    public bool Matches(in WakuuEnchantCardInfo card)
    {
        if (!KeywordMatches(Keyword, card.Id))
        {
            return false;
        }

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

    /// <summary>id 关键词匹配（复用 <see cref="WakuuCardId"/>，与主 mod 其它地方的"打击/防御"口径一致）。</summary>
    private static bool KeywordMatches(WakuuEnchantKeyword keyword, string id)
    {
        return keyword switch
        {
            WakuuEnchantKeyword.Strike => WakuuCardId.IsStrikeId(id),
            WakuuEnchantKeyword.Defend => WakuuCardId.IsDefendId(id),
            WakuuEnchantKeyword.StrikeOrDefend => WakuuCardId.IsBasicStrikeOrDefendId(id),
            _ => true,
        };
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

    /// <summary>
    /// 「同档」分组：**默认 <c>-1</c> = 该条目独占一档**（与旧行为逐字一致）。
    ///
    /// <para>≥0 时，相同值的条目算**同一个优先级档**（用户 2026-10-11 口径：漩涡的"能力牌"与
    /// "打击/防御"**优先级相当** ⇒ 应从中随机挑，而不是固定先看能力牌）。
    /// 档内候选取各条目命中的**并集**（按候选原序去重），再按该档**第一个条目**的
    /// <see cref="Sort"/> 排序；档与档之间仍按"条目首次出现顺序"。</para>
    /// </summary>
    public int RankGroup { get; init; } = -1;
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
    ///
    /// <para>逐**档**处理：默认每条目一档（旧行为）；<see cref="WakuuEnchantRuleEntry.RankGroup"/> ≥0
    /// 的条目合并成同一档（档内并集）。第一个筛出非空候选的档胜出，按其 <c>Sort</c> 排序后返回。</para>
    ///
    /// <para><paramref name="shuffleSeed"/>：<see cref="WakuuEnchantSort.Random"/> 档的确定性打乱种子
    /// （纯逻辑不给真随机 ⇒ 同种子同结果、可单测；运行时传 <c>Random.Shared.Next()</c> 就是真随机）。</para>
    /// </summary>
    public static List<int> RankIndices(
        IReadOnlyList<WakuuEnchantCardInfo> cards,
        IReadOnlyList<WakuuEnchantRuleEntry>? rule,
        IReadOnlyList<string>? ownedRelics,
        IReadOnlyList<string>? deckCardIds,
        int? shuffleSeed = null)
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

        foreach (List<WakuuEnchantRuleEntry> rank in BuildRanks(rule, ownedRelics, deckCardIds))
        {
            List<int> matched = new();
            foreach (int i in indices)
            {
                foreach (WakuuEnchantRuleEntry entry in rank)
                {
                    if (MatchesEntry(entry, cards![i]))
                    {
                        matched.Add(i);
                        break;
                    }
                }
            }

            if (matched.Count == 0)
            {
                continue;
            }

            // 档内排序方式以该档**第一个条目**为准（同档多条目约定写一致的 Sort）。
            return SortIndices(matched, cards!, rank[0].Sort, shuffleSeed);
        }

        return indices;
    }

    /// <summary>
    /// 把规则条目按档分组：<c>RankGroup &lt; 0</c> 的条目独占一档；<c>≥ 0</c> 的同值条目合并，
    /// 档序 = 该 group 首次出现的条目顺序（条件不满足的条目在这里就被过滤掉）。
    /// </summary>
    private static List<List<WakuuEnchantRuleEntry>> BuildRanks(
        IReadOnlyList<WakuuEnchantRuleEntry> rule,
        IReadOnlyList<string>? ownedRelics,
        IReadOnlyList<string>? deckCardIds)
    {
        List<List<WakuuEnchantRuleEntry>> ranks = new();
        Dictionary<int, List<WakuuEnchantRuleEntry>> byGroup = new();

        foreach (WakuuEnchantRuleEntry entry in rule)
        {
            if (entry == null || !ConditionsMet(entry, ownedRelics, deckCardIds))
            {
                continue;
            }

            if (entry.RankGroup < 0)
            {
                ranks.Add(new List<WakuuEnchantRuleEntry> { entry });
                continue;
            }

            if (!byGroup.TryGetValue(entry.RankGroup, out List<WakuuEnchantRuleEntry>? group))
            {
                group = new List<WakuuEnchantRuleEntry>();
                byGroup[entry.RankGroup] = group;
                ranks.Add(group);
            }

            group.Add(entry);
        }

        return ranks;
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

    private static List<int> SortIndices(
        List<int> indices,
        IReadOnlyList<WakuuEnchantCardInfo> cards,
        WakuuEnchantSort sort,
        int? shuffleSeed)
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
            case WakuuEnchantSort.Random:
                return Shuffle(indices, shuffleSeed ?? 0);
            default:
                return indices; // 稳定：原序
        }
    }

    /// <summary>
    /// 确定性打乱（Fisher–Yates，自写 LCG ⇒ **可单测**：同种子同结果）。
    /// 目的只是"同档随机挑一张"，不需要密码学强度。
    /// </summary>
    private static List<int> Shuffle(List<int> indices, int seed)
    {
        List<int> result = new(indices);
        uint state = unchecked((uint)seed);
        for (int i = result.Count - 1; i > 0; i--)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            int j = (int)(state % (uint)(i + 1));
            (result[i], result[j]) = (result[j], result[i]);
        }

        return result;
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
