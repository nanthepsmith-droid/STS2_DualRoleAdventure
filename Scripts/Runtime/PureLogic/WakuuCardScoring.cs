using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>评分用的卡牌类型（纯逻辑镜像，避免 PureLogic 依赖游戏模型）。</summary>
internal enum WakuuScoreCardKind
{
    /// <summary>无类型/状态/诅咒/任务等。</summary>
    Other,

    Attack,
    Skill,
    Power,
}

/// <summary>评分关注的关键词（只取影响出牌优先级的三个，§18.2.4）。</summary>
[Flags]
internal enum WakuuScoreKeywords
{
    None = 0,

    /// <summary>消耗：用后永久移除，谨慎打（-5）。</summary>
    Exhaust = 1,

    /// <summary>虚无：不打就消失，倾向打出（+10）。</summary>
    Ethereal = 2,

    /// <summary>保留：可以留到更好的时机（-8）。</summary>
    Retain = 4,
}

/// <summary>评分关注的指向类型（只区分会改变评分口径的几种）。</summary>
internal enum WakuuScoreTargetKind
{
    None,
    Self,
    AnyEnemy,
    AllEnemies,
    AnyPlayer,
    AnyAlly,
    AllAllies,

    /// <summary>其它/未知（含 RandomEnemy / TargetedNoCreature / Osty 等）。</summary>
    Other,
}

/// <summary>
/// 单张牌的评分输入（纯数据，不依赖游戏类型，可直接单测）。
/// 运行时由 <c>ScoredWakuuBrain</c> 从 <c>CardModel</c> 映射而来。
/// </summary>
internal readonly struct WakuuScoreInput
{
    public WakuuScoreInput(
        WakuuScoreCardKind kind,
        int cost,
        bool isXCost,
        bool gainsBlock,
        WakuuScoreKeywords keywords,
        WakuuScoreTargetKind targetKind,
        int estimatedDamage,
        int estimatedBlock)
    {
        Kind = kind;
        Cost = cost;
        IsXCost = isXCost;
        GainsBlock = gainsBlock;
        Keywords = keywords;
        TargetKind = targetKind;
        EstimatedDamage = estimatedDamage;
        EstimatedBlock = estimatedBlock;
    }

    public WakuuScoreCardKind Kind { get; }

    /// <summary>本次要花掉的能量（X 费牌 = 当前能量，与游戏 <c>GetAmountToSpend()</c> 同口径）。</summary>
    public int Cost { get; }

    /// <summary>X 费牌（费用随能量浮动，评分与排序都单独处理）。</summary>
    public bool IsXCost { get; }

    /// <summary>是否提供格挡（游戏 <c>CardModel.GainsBlock</c>）。</summary>
    public bool GainsBlock { get; }

    public WakuuScoreKeywords Keywords { get; }

    public WakuuScoreTargetKind TargetKind { get; }

    /// <summary>对单个目标的粗估伤害（含力量、含多段；不含易伤/虚弱等 Hook 修正）。</summary>
    public int EstimatedDamage { get; }

    /// <summary>粗估格挡（含敏捷；不含其它 Hook 修正）。</summary>
    public int EstimatedBlock { get; }
}

/// <summary>战场态势（评分用的标量快照，全部由 <c>ScoredWakuuBrain</c> 现算）。</summary>
internal readonly struct WakuuScoreSituation
{
    public WakuuScoreSituation(
        int playerHp,
        int playerMaxHp,
        int energy,
        int turnNumber,
        int enemyCount,
        int weakestEnemyEffectiveHp,
        bool lethalDanger)
    {
        PlayerHp = playerHp;
        PlayerMaxHp = playerMaxHp;
        Energy = energy;
        TurnNumber = turnNumber;
        EnemyCount = enemyCount;
        WeakestEnemyEffectiveHp = weakestEnemyEffectiveHp;
        LethalDanger = lethalDanger;
    }

    public int PlayerHp { get; }

    public int PlayerMaxHp { get; }

    public int Energy { get; }

    /// <summary>当前回合数（1 起）。</summary>
    public int TurnNumber { get; }

    /// <summary>可打击的敌人数量。</summary>
    public int EnemyCount { get; }

    /// <summary>最"好杀"的敌人的有效血量（= 当前血 - 格挡）；无敌人时 -1。</summary>
    public int WeakestEnemyEffectiveHp { get; }

    /// <summary>
    /// 致死线：本回合不格挡就会被打死（粗估来袭伤害 ≥ 当前血 + 格挡）。
    /// 由 <c>LocalWakuuThreatEstimate</c> 的意图求和给出；估不到伤害时恒 false（保守，不乱加防御分）。
    /// </summary>
    public bool LethalDanger { get; }
}

/// <summary>
/// 卡牌评分纯函数（可行性分析 §18.2.4「阶段 B：卡牌评分函数」，用户 2026-09-20 拍板实现）。
///
/// 设计取舍（每条都来自可行性分析，不是临场发明）：
/// 1. **§21.4.2 #7 的已知缺陷**：这份评分是**线性总分**，而参考实现明确拒绝线性总分
///    （它把"中间排序"与"终局排序"分离）。当前量级可接受，但**致死线必须前置为硬门槛**，
///    所以本文件的入口是 <see cref="PickBestIndex"/> ——「先筛救命牌、再比分数」，
///    而不是让救命牌去和攻击牌比总分。
/// 2. **§18.2.7 X 费收尾**：X 费牌的效果随能量放大，应当**最后打**；
///    只要还有非 X 费可打牌，就不选 X 费（<see cref="PickBestIndex"/> 里的两级筛选）。
/// 3. **同分保留最左**：与既有「最左兜底」语义一致（<c>HeuristicWakuuBrain</c> 取手牌最左可打牌），
///    让"评分无差别"时行为可预期、可回归。
/// 4. **分数下限 0**：负分一律截断到 0，避免"惩罚项把牌压成负数"后在并列比较里出现反直觉结果。
/// </summary>
internal static class WakuuCardScoring
{
    /// <summary>危险血线：当前血量低于上限一半时视为需要防御（§18.2.4）。</summary>
    private const int DangerHpNumerator = 1;

    private const int DangerHpDenominator = 2;

    /// <summary>格挡量加分上限（§18.2.4：格挡越高分越高，封顶 20）。</summary>
    private const int MaxBlockBonus = 20;

    /// <summary>单张牌的总分（0-100+，见类注释的取舍说明）。</summary>
    public static int Score(WakuuScoreInput card, WakuuScoreSituation situation)
    {
        int score = card.Kind switch
        {
            WakuuScoreCardKind.Power => 50,
            WakuuScoreCardKind.Attack => 30,
            WakuuScoreCardKind.Skill => 35,
            _ => 10,
        };

        if (card.IsXCost)
        {
            // X 费：能量越多越值（排序上已在 PickBestIndex 里压到最后，这里只区分"值不值得再囤"）。
            score += Math.Min(Math.Max(situation.Energy, 0), 5) * 4;
        }
        else if (card.Cost == 0)
        {
            score += 15;
        }
        else if (card.Cost <= 1)
        {
            score += 5;
        }

        score += ContextModifier(card, situation);

        if ((card.Keywords & WakuuScoreKeywords.Exhaust) != 0)
        {
            score -= 5;
        }

        if ((card.Keywords & WakuuScoreKeywords.Ethereal) != 0)
        {
            score += 10;
        }

        if ((card.Keywords & WakuuScoreKeywords.Retain) != 0)
        {
            score -= 8;
        }

        return Math.Max(score, 0);
    }

    /// <summary>
    /// 场景修正（§18.2.4，核心智能所在）。判据全部来自 <see cref="WakuuScoreSituation"/> 的标量快照。
    /// </summary>
    private static int ContextModifier(WakuuScoreInput card, WakuuScoreSituation situation)
    {
        int mod = 0;

        // 防御优先：致死线或血量危险时，格挡牌大幅加分。
        bool needsDefense = situation.LethalDanger
            || (situation.PlayerMaxHp > 0
                && situation.PlayerHp * DangerHpDenominator < situation.PlayerMaxHp * DangerHpNumerator);
        if (needsDefense && card.GainsBlock)
        {
            mod += 30;
            mod += Math.Min(Math.Max(card.EstimatedBlock, 0), MaxBlockBonus);
        }

        // 攻击优先：有可击杀窗口时。
        bool killWindow = situation.WeakestEnemyEffectiveHp >= 0;
        if (killWindow && card.Kind == WakuuScoreCardKind.Attack)
        {
            mod += 20;
            if (card.EstimatedDamage >= situation.WeakestEnemyEffectiveHp)
            {
                mod += 40; // 能击杀：消除一个威胁源，最高优先级（非致死线时）
            }
        }

        // 第一回合：能力牌额外加分（早铺永久增益）。
        if (situation.TurnNumber <= 1 && card.Kind == WakuuScoreCardKind.Power)
        {
            mod += 20;
        }

        // AoE 价值：多敌人时全体伤害牌加分（每多一个敌人 +5）。
        if (situation.EnemyCount >= 2 && card.TargetKind == WakuuScoreTargetKind.AllEnemies)
        {
            mod += 15 + (situation.EnemyCount - 2) * 5;
        }

        // 集火价值：单体攻击牌且当前有击杀窗口。
        if (killWindow && card.TargetKind == WakuuScoreTargetKind.AnyEnemy)
        {
            mod += 10;
        }

        // 能量富余时倾向打贵牌（把能量变成价值）。
        if (situation.Energy >= 4 && card.Cost >= 3)
        {
            mod += 10;
        }

        return mod;
    }

    /// <summary>
    /// 从若干可打牌里挑出**该打的那一张**（返回下标；无候选返回 -1）。规则：
    /// ① 致死线且手上有格挡牌 → 只在格挡牌里选（硬门槛，§21.4.2 #4）；
    /// ② 还有非 X 费可打牌 → 不选 X 费（X 值攒到最后，§18.2.7）；
    /// ③ 同分保留最左（与既有"最左兜底"一致）。
    /// 两级筛选都会在"筛空了"时逐级放宽，保证任何手牌都能给出候选（绝不返回 -1 而手上有牌）。
    /// </summary>
    public static int PickBestIndex(IReadOnlyList<WakuuScoreInput> cards, WakuuScoreSituation situation)
    {
        if (cards == null || cards.Count == 0)
        {
            return -1;
        }

        bool lethalGate = situation.LethalDanger && HasAny(cards, static card => card.GainsBlock);
        bool preferNonX = HasAny(cards, static card => !card.IsXCost);

        int best = SelectBest(cards, situation, requireBlock: lethalGate, excludeX: preferNonX);
        if (best < 0 && preferNonX)
        {
            // 放宽一级：允许 X 费（但仍守致死门槛）。
            best = SelectBest(cards, situation, requireBlock: lethalGate, excludeX: false);
        }

        if (best < 0 && lethalGate)
        {
            // 再放宽：连格挡牌都没有（理论上不会发生，因为 lethalGate 已确认有格挡牌）。
            best = SelectBest(cards, situation, requireBlock: false, excludeX: false);
        }

        // 最后兜底：保持"最左"语义。
        return best < 0 ? 0 : best;
    }

    private static int SelectBest(IReadOnlyList<WakuuScoreInput> cards, WakuuScoreSituation situation, bool requireBlock, bool excludeX)
    {
        int best = -1;
        int bestScore = int.MinValue;
        for (int i = 0; i < cards.Count; i++)
        {
            WakuuScoreInput card = cards[i];
            if (requireBlock && !card.GainsBlock)
            {
                continue;
            }

            if (excludeX && card.IsXCost)
            {
                continue;
            }

            int score = Score(card, situation);
            if (score > bestScore)
            {
                // 严格大于 ⇒ 同分保留更靠前的那张（最左兜底）
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    private static bool HasAny(IReadOnlyList<WakuuScoreInput> cards, Func<WakuuScoreInput, bool> predicate)
    {
        for (int i = 0; i < cards.Count; i++)
        {
            if (predicate(cards[i]))
            {
                return true;
            }
        }

        return false;
    }
}
