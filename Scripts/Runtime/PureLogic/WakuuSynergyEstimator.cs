using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 协同估计的输入（总规范 v2 §37/§38）。
/// 只容纳"可证实的简单协同"所需的量：**牌组里已经有多少该前置条件的来源**、
/// **抽牌支持有多强**（决定条件能不能及时上线）。
/// </summary>
internal readonly struct WakuuSynergyContext
{
    public WakuuSynergyContext(int sourceCount, float drawSupport, int deckSize, float confidence)
    {
        SourceCount = sourceCount < 0 ? 0 : sourceCount;
        DrawSupport = WakuuCardBaseline.Sanitize(drawSupport);
        DeckSize = deckSize < 0 ? 0 : deckSize;
        Confidence = WakuuConfidence.Clamp(confidence);
    }

    /// <summary>牌组中"能提供该前置条件"的牌数（0 ⇒ 当前是孤儿组件）。</summary>
    public int SourceCount { get; }

    /// <summary>牌组的抽牌/过滤端口合计（越高越容易把条件凑齐）。</summary>
    public float DrawSupport { get; }

    /// <summary>牌组张数（越大越难在关键回合凑齐条件）。</summary>
    public int DeckSize { get; }

    /// <summary>这条协同判断本身的置信度（看不懂的联动应给低值，v2 §40）。</summary>
    public float Confidence { get; }
}

/// <summary>协同估计结果（v2 §38：`SynergyEV = P(ConditionOnline) × Payoff`）。</summary>
internal readonly struct WakuuSynergyEstimate
{
    public WakuuSynergyEstimate(float probability, float payoff)
    {
        Probability = WakuuConfidence.Clamp(probability);
        Payoff = WakuuCardBaseline.Sanitize(payoff);
    }

    /// <summary>条件在本局上线的概率估计（0~1）。</summary>
    public float Probability { get; }

    /// <summary>条件成立时的收益（基准牌当量）。</summary>
    public float Payoff { get; }

    /// <summary>期望协同价值。</summary>
    public float Value => Probability * Payoff;

    /// <summary>
    /// 孤儿组件（v2 §39）：当前**没有**任何来源、却要靠条件才成立的牌。
    /// 语义是"提醒叠加方不要因其理论上限巨大而高估"，这里只标记，不在这里扣分。
    /// </summary>
    public bool IsOrphanComponent => Probability <= 0f && Payoff > 0f;
}

/// <summary>
/// 协同估计纯函数（总规范 v2 §37「只做可证实的简单协同」/ §38「SynergyEV」/ §40「复杂联动降置信度」）。
///
/// 明确不做的事（都是 v2 的硬约束）：
/// - 不做 `A → B → C → D` 的多跳推理（§37）；
/// - 不用固定倍率（§38 明禁"×1.5"这种写法），而是显式给出 **P(条件上线) × 收益**；
/// - 来源为 0 时**直接返回 0**，绝不用"理论上限巨大"来加分（§39/§40）。
/// </summary>
internal static class WakuuSynergyEstimator
{
    /// <summary>每多一个来源，剩余不确定性按此比例衰减（1 个来源 ⇒ 0.45 的"未上线概率"）。</summary>
    public const float SourceSaturationBase = 0.55f;

    /// <summary>抽牌支持带来的概率加成上限（抽牌再多也不能把条件概率顶满）。</summary>
    public const float MaxDrawBonus = 0.15f;

    /// <summary>概率上限：即便来源极多也留出余量（牌序、耗牌、被清除等）。</summary>
    public const float MaxProbability = 0.9f;

    /// <summary>抽牌端口每 1.0 折算的加成。</summary>
    public const float DrawBonusPerPort = 0.05f;

    /// <summary>
    /// 估计协同期望值。<paramref name="payoff"/> = 条件成立时的收益（基准牌当量）。
    /// </summary>
    public static WakuuSynergyEstimate Estimate(in WakuuSynergyContext context, float payoff)
    {
        float sanitizedPayoff = WakuuCardBaseline.Sanitize(payoff);
        if (context.SourceCount <= 0)
        {
            // 无来源 ⇒ 当前价值为 0（孤儿组件），不做任何"未来会很强"的乐观估计。
            return new WakuuSynergyEstimate(0f, sanitizedPayoff);
        }

        float notOnline = (float)Math.Pow(SourceSaturationBase, context.SourceCount);
        float probability = 1f - notOnline;

        // 牌组越厚，越难在关键回合凑齐条件：按参考规模做归一化，最多打七折（只减不增）。
        float deckFactor = Math.Clamp(
            WakuuOpportunityCost.ReferenceDeckSize / Math.Max(1f, context.DeckSize),
            0.7f,
            1f);

        probability *= deckFactor;
        float drawBonus = Math.Min(MaxDrawBonus, context.DrawSupport * DrawBonusPerPort);
        probability = Math.Min(MaxProbability, probability + drawBonus);

        // 置信度参与：看不懂的联动不许给出满概率（v2 §40）。
        probability *= context.Confidence;

        return new WakuuSynergyEstimate(probability, sanitizedPayoff);
    }
}
