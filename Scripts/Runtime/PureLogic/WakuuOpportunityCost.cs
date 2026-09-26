using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 机会成本（总规范 v2 §42「Marginal Value」/ §43「Opportunity Cost」）。
///
/// 口径：加入一张牌意味着"抽到其他牌的难度上升、牌组变厚、一致性下降"，所以
/// `NetValue = CardValue - OpportunityCost`，且**牌组越健康、越大 ⇒ 机会成本越高 ⇒ 越容易 Skip**
/// （v2 §43 的原话）。
///
/// 当量单位：与 <see cref="WakuuCardFeature.Quality"/> 同一单位 —— "1.0 = 一张健康牌"。
/// <see cref="BaseDilutionPerCard"/> 的取值有一处可复算的推导（写在这里避免日后被当成魔法数字）：
/// 30 张牌、每张平均 1.0 当量（全是健康牌）⇒ 总体牌力 ≈ 30 当量；多带一张牌把每张牌被抽到的概率
/// 摊薄 1/30 ≈ 3.3% ⇒ 直接损失 ≈ 30 × 3.3% ≈ 1.0 当量；但一回合只打 3~4 张、并非每张机会等价，
/// 打个对折 ⇒ ≈ 0.5 当量。
/// 这是**校准锚**，供 M2 抓牌评分调参；它不构成任何决策门槛（v2 §23：`MarginalValue ≠ RawValue`）。
/// </summary>
internal static class WakuuOpportunityCost
{
    /// <summary>牌组规模参考值（原版一局常见规模，仅用于归一化）。</summary>
    public const float ReferenceDeckSize = 30f;

    /// <summary>参考规模下"多带一张牌"的基准稀释成本（推导见类注释）。</summary>
    public const float BaseDilutionPerCard = 0.5f;

    /// <summary>
    /// 牌组当前状态下"再多一张牌"的机会成本。
    /// <paramref name="deckSizeNow"/> = 现有张数；<paramref name="uniformity"/> = 牌组均卡度（0~1，见
    /// <see cref="WakuuDeckAssessment.Uniformity"/>）。
    /// </summary>
    public static float OfAddingOne(int deckSizeNow, float uniformity)
    {
        float sizeFactor = Math.Max(0, deckSizeNow) / ReferenceDeckSize;
        float healthFactor = 1f + WakuuConfidence.Clamp(uniformity);
        return BaseDilutionPerCard * sizeFactor * healthFactor;
    }

    /// <summary>
    /// 边际净价值（v2 §42）：`Value(deck+card) - Value(deck)` 的简化形式 =
    /// 单卡价值 − 多带一张的机会成本。**只做算术，不做取舍判断**（域外可比大小时再比）。
    /// </summary>
    public static float NetValue(float cardValue, int deckSizeNow, float uniformity)
    {
        return WakuuCardBaseline.Sanitize(cardValue) - OfAddingOne(deckSizeNow, uniformity);
    }
}
