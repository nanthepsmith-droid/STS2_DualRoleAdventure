using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 卡牌经济学基线（总规范 v2 §20~§23「Baseline 只能用于校准」/ §76「1 费 14 伤害只是起点」）。
///
/// 为什么要集中：v2 §22 明令"不要在代码里四处散落魔法数字"，v2 §90 第 7/8 条又禁止
/// "未知效果判 0""Weak/Draw/Energy 永远固定换算成分数"。所以本文件只提供
/// **三类东西**：① 可查证的原版锚点；② 把点数折成"基准牌当量"的换算；③ 未知效果的保守当量。
/// **不做任何决策**，也不针对具体卡做评价。
///
/// 锚点来源（可核对，不是拍脑袋）：
/// - <see cref="BasicStrikeDamage1E"/> = 6：原版 5 个角色的基础打击牌 `DamageVar(6m)` 完全一致
///   （`sts2src` 的 `StrikeIronclad/Silent/Defect/Regent/Necrobinder.cs`）。
/// - <see cref="BasicDefendBlock1E"/> = 5：原版 5 个角色的基础防御牌 `BlockVar(5m)` 完全一致。
/// - <see cref="HealthyAttack1E"/> = 14：v2 §21/§76 给的"1 费纯攻击健康线"。
///   **它只是校准锚**，不是"低于 14 就不能抓"的硬门槛（v2 §23/§76）。
/// - <see cref="HealthyBlock1E"/>：v2 未给数值，按"健康线 ≈ 基础牌的 2 倍"自定，口径写明在此，
///   以后要改只改这一行。
/// </summary>
internal static class WakuuCardBaseline
{
    /// <summary>一回合的基准能量（原版每回合 3 点），用于费用压力与端口折算的分母。</summary>
    public const float BaselineEnergyPerTurn = 3f;

    /// <summary>基础打击：1 费 6 伤害（原版 5 角色一致，见类注释的证据）。</summary>
    public const float BasicStrikeDamage1E = 6f;

    /// <summary>基础防御：1 费 5 格挡（原版 5 角色一致）。</summary>
    public const float BasicDefendBlock1E = 5f;

    /// <summary>1 费纯攻击"健康线"（v2 §21/§76）。</summary>
    public const float HealthyAttack1E = 14f;

    /// <summary>1 费纯防御"健康线"（v2 未给数值；按 ≈ 基础防御 × 2 自定，只用于折算端口）。</summary>
    public const float HealthyBlock1E = BasicDefendBlock1E * 2f;

    /// <summary>抽 1 张牌的基准价值（折算成"基准攻击牌当量"的系数，v2 §34：不能固定换算成分数）。</summary>
    public const float DrawPortPerCard = 0.35f;

    /// <summary>1 点能量的基准价值（v2 §35：不能固定换算，这里只用于端口折算的粗权重）。</summary>
    public const float EnergyPortPerPoint = 0.5f;

    /// <summary>把 1 层持续伤害折算成伤害当量的保守系数（毒每层通常结算 1 点，这里留折扣）。</summary>
    public const float DamageOverTimePerStack = 0.6f;

    /// <summary>
    /// 未知效果折算成的"基准攻击牌当量"（v2 §19/§78：`UnknownPotential &gt; 0`，绝不能判 0）。
    ///
    /// 取 0.35 的语义：读不懂的牌**至少**按"半张多一点的基准牌"算，避免"看不懂 ⇒ 当垃圾 ⇒ 永不抓"
    /// （v2 §5 毒牌反例）。它同时**不高于**一张健康牌——信息不足时不敢高估（v2 §40：降低置信度而不是
    /// 假定巨大联动）。数值可调，只此一处。
    /// </summary>
    public const float UnknownEffectEquivalent = 0.35f;

    /// <summary>参数数量异常的防护：把任意数夹到 ≥ 0（NaN ⇒ 0）。</summary>
    public static float Sanitize(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Max(0f, value);
    }

    /// <summary>
    /// 某费用下的"健康"攻击点数（线性放大；<paramref name="energy"/> ≤ 1 一律按 1 费算）。
    /// 只用于归一化，不是断言"高费牌必须达到这个值"（v2 §45 禁止硬阈值）。
    /// </summary>
    public static float ExpectedAttackAtCost(int energy)
    {
        return HealthyAttack1E * Math.Max(1, energy);
    }

    /// <summary>某费用下的"健康"格挡点数（同 <see cref="ExpectedAttackAtCost"/> 的口径）。</summary>
    public static float ExpectedBlockAtCost(int energy)
    {
        return HealthyBlock1E * Math.Max(1, energy);
    }

    /// <summary>伤害点数 → 伤害端口当量（0~1 归一化后按费用给上限，避免高费牌端口爆表）。</summary>
    public static float DamagePort(int damage, int energy)
    {
        return Sanitize(damage) / Math.Max(1f, ExpectedAttackAtCost(energy));
    }

    /// <summary>格挡点数 → 防御端口当量。</summary>
    public static float DefensePort(int block, int energy)
    {
        return Sanitize(block) / Math.Max(1f, ExpectedBlockAtCost(energy));
    }

    /// <summary>持续伤害层数 → 伤害端口当量（v2 §19：`EffectDamage = estimated DoT`）。</summary>
    public static float DamageOverTimePort(int stacks)
    {
        return Sanitize(stacks) * DamageOverTimePerStack / HealthyAttack1E;
    }

    /// <summary>抽牌数 → 抽牌端口当量。</summary>
    public static float DrawPort(int cards)
    {
        return Sanitize(cards) * DrawPortPerCard;
    }

    /// <summary>能量点数 → 能量端口当量。</summary>
    public static float EnergyPort(int points)
    {
        return Sanitize(points) * EnergyPortPerPoint;
    }

    /// <summary>
    /// 未识别信号 → 未知潜能当量（v2 §19/§78）。<paramref name="unknownSignals"/> = 读不懂的动作个数，
    /// 逐条累加但**收敛**（用平方根抑制），避免"一堆读不懂 ⇒ 分数爆表"。
    /// </summary>
    public static float UnknownPotential(int unknownSignals)
    {
        if (unknownSignals <= 0)
        {
            return 0f;
        }

        return UnknownEffectEquivalent * (float)Math.Sqrt(unknownSignals);
    }
}
