using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 牌组层评估结果（总规范 v2 §24/§25「均卡」/ §42/§45；实施草案 §4.3）。
///
/// 均卡的正确定义（v2 §24 特意澄清）：**不是"25~35 张"，也不是"抓很多普通牌"**，而是
/// "牌组中大多数牌具有较高独立有效性、较高可靠性和较低条件依赖"。
/// 所以核心指标就是 <see cref="Uniformity"/>，它是"判断牌组健康程度 / 调整 Skip 阈值 / 评价新增牌"
/// 的共同输入（v2 §25）。
///
/// 本结构由纯函数 <see cref="Assess"/> 产出，**不做取舍判断**（Skip 决策属 M2）。
/// </summary>
internal readonly struct WakuuDeckAssessment
{
    public WakuuDeckAssessment(
        int totalCards,
        WakuuPortProfile ports,
        float uniformity,
        float opportunityCost,
        bool needsThinning,
        float costCurveStress,
        int unknownEffectCards,
        float averageConfidence)
    {
        TotalCards = totalCards < 0 ? 0 : totalCards;
        Ports = ports;
        Uniformity = WakuuConfidence.Clamp(uniformity);
        OpportunityCost = WakuuCardBaseline.Sanitize(opportunityCost);
        NeedsThinning = needsThinning;
        CostCurveStress = WakuuCardBaseline.Sanitize(costCurveStress);
        UnknownEffectCards = unknownEffectCards < 0 ? 0 : unknownEffectCards;
        AverageConfidence = WakuuConfidence.Clamp(averageConfidence);
    }

    /// <summary>牌组张数。</summary>
    public int TotalCards { get; }

    /// <summary>牌组端口供给合计（v2 §27：能力视角，不做流派识别）。</summary>
    public WakuuPortProfile Ports { get; }

    /// <summary>均卡度（v2 §25：`avg(Quality) × avg(Reliability) × (1 − avg(Conditionality))`）。</summary>
    public float Uniformity { get; }

    /// <summary>当前规模下"再多带一张牌"的机会成本（v2 §43）。</summary>
    public float OpportunityCost { get; }

    /// <summary>是否值得瘦身：牌组偏大且已经比较结实（v2 §43 的方向）。</summary>
    public bool NeedsThinning { get; }

    /// <summary>
    /// 费用压力（v2 §45）：`期望手牌费用 − 期望可用能量` 的**牌组级代理**。
    /// 真正的手牌级计算要等 M3 的 `WakuuTurnContext`（那里才有手牌与能量），这里用牌组平均费用代替，
    /// 明确不做"高费占比 &gt; 30% 就扣分"那种硬阈值（v2 §45/§90 第 9 条禁止）。
    /// </summary>
    public float CostCurveStress { get; }

    /// <summary>含未知效果的牌数（v2 §67 KPI 的 `UnknownCardRate` 分子）。</summary>
    public int UnknownEffectCards { get; }

    /// <summary>牌组平均知识置信度（信息够不够支撑评分）。</summary>
    public float AverageConfidence { get; }

    /// <summary>未知卡占比（0~1）。</summary>
    public float UnknownCardRate => TotalCards <= 0 ? 0f : (float)UnknownEffectCards / TotalCards;

    /// <summary>
    /// 从一组卡特征评估牌组（纯函数；空列表返回全零评估，不抛异常）。
    /// </summary>
    public static WakuuDeckAssessment Assess(IReadOnlyList<WakuuCardFeature> cards)
    {
        if (cards == null || cards.Count == 0)
        {
            return new WakuuDeckAssessment(0, WakuuPortProfile.Zero, 0f, 0f, false, 0f, 0, 0f);
        }

        WakuuPortProfile ports = WakuuPortProfile.Zero;
        float qualitySum = 0f;
        float reliabilitySum = 0f;
        float conditionalitySum = 0f;
        float confidenceSum = 0f;
        float costSum = 0f;
        int costingCards = 0;
        int unknownCards = 0;

        for (int i = 0; i < cards.Count; i++)
        {
            WakuuCardFeature card = cards[i];
            ports = ports.Add(card.Ports);
            qualitySum += card.Quality;
            reliabilitySum += card.Reliability;
            conditionalitySum += card.Conditionality;
            confidenceSum += card.Confidence;

            if (card.HasUnknownEffect)
            {
                unknownCards++;
            }

            // 费用压力只看"有费用概念"的牌：0 费与 X 费不计入分母（X 费的消耗由当前能量决定）。
            if (!card.IsXCost && card.Cost > 0)
            {
                costSum += card.Cost;
                costingCards++;
            }
        }

        float count = cards.Count;
        float quality = qualitySum / count;
        float reliability = reliabilitySum / count;
        float conditionality = conditionalitySum / count;
        float uniformity = WakuuCardBaseline.Sanitize(quality * reliability * (1f - conditionality));

        float averageCost = costingCards > 0 ? costSum / costingCards : 0f;
        float costStress = Math.Max(0f, averageCost - WakuuCardBaseline.BaselineEnergyPerTurn);

        float opportunity = WakuuOpportunityCost.OfAddingOne(cards.Count, uniformity);

        // 瘦身信号：牌组偏大 + 已经比较结实（再加牌主要是稀释）⇒ 该瘦身。
        bool needsThinning = cards.Count > WakuuOpportunityCost.ReferenceDeckSize && uniformity >= 0.5f;

        return new WakuuDeckAssessment(
            totalCards: cards.Count,
            ports: ports,
            uniformity: uniformity,
            opportunityCost: opportunity,
            needsThinning: needsThinning,
            costCurveStress: costStress,
            unknownEffectCards: unknownCards,
            averageConfidence: confidenceSum / count);
    }

    /// <summary>日志用短描述（中文）。</summary>
    public string DescribeShort()
    {
        return $"牌组={TotalCards} 张, 均卡度={Uniformity:0.00}, 机会成本={OpportunityCost:0.00}, "
            + $"费用压力={CostCurveStress:0.00}, 未知卡={UnknownEffectCards}({UnknownCardRate:P0}), "
            + $"平均置信={WakuuConfidence.Describe(AverageConfidence)}, 瘦身={NeedsThinning}";
    }
}
