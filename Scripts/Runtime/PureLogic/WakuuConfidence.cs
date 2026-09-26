using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 置信度纯函数（总规范 v2 §14「Observation Confidence」/ §62「Unknown Mechanism 安全规则」/ §73「学习必须有限制」）。
///
/// 为什么要有它：这个项目的核心是"**渐进式近似**"——不知道就保守地活（v2 §2.3/§79），
/// 而"保守"必须有个可比较的度：置信度低到某个阈值以下时，决策层才允许走安全兜底（v2 §62）。
/// 所以置信度的合成规则要**集中在一处、可单测**，不要散落在各个评分函数里。
/// </summary>
internal static class WakuuConfidence
{
    /// <summary>单次观测的置信度（v2 §14：只观察一次 ≈ 0.25）。</summary>
    public const float SingleObservation = 0.25f;

    /// <summary>多次观测后的置信度上限（v2 §14：重复 20 次、相关性 0.91 ⇒ 0.85~0.95）。</summary>
    public const float MaxObservation = 0.95f;

    /// <summary>每次额外观测带来的置信度增量（线性近似，够用且可解释）。</summary>
    public const float ObservationStep = 0.1f;

    /// <summary>安全兜底阈值（v2 §62：低于它 + 高后果未知效果 ⇒ SafeFallback / 交真人）。</summary>
    public const float SafeFallbackThreshold = 0.4f;

    /// <summary>把任意值夹到 0~1（NaN 归 0，避免污染比较）。</summary>
    public static float Clamp(float value)
    {
        if (float.IsNaN(value))
        {
            return 0f;
        }

        return Math.Clamp(value, 0f, 1f);
    }

    /// <summary>
    /// 已知元占比 → 置信度。已知信息越多越敢信；完全未知（0）时给 0（而不是 0.25——
    /// 那不是"观测出来的"，只是没信息）。
    /// </summary>
    public static float FromKnownRatio(float knownRatio)
    {
        return Clamp(knownRatio);
    }

    /// <summary>观测次数 → 置信度（v2 §14 的线性近似：0 次 = 0，1 次 = 0.25，之后每多一次 +0.1）。</summary>
    public static float FromObservations(int observationCount)
    {
        if (observationCount <= 0)
        {
            return 0f;
        }

        return Clamp(Math.Min(MaxObservation, SingleObservation + (observationCount - 1) * ObservationStep));
    }

    /// <summary>
    /// 两个独立来源的置信度合成：先按"证据独立"取折扣和，再封顶 —— 两条各 0.6 的证据 ⇒ 0.84，
    /// 而不是 1.0（避免"两条弱证据凑成绝对把握"）。
    /// </summary>
    public static float Combine(float a, float b)
    {
        float first = Clamp(a);
        float second = Clamp(b);
        return Clamp(1f - (1f - first) * (1f - second));
    }

    /// <summary>是否低到需要走安全兜底（v2 §62）。</summary>
    public static bool NeedsSafeFallback(float confidence)
    {
        return Clamp(confidence) < SafeFallbackThreshold;
    }

    /// <summary>中文档位（日志用，便于人读与 grep）。</summary>
    public static string Describe(float confidence)
    {
        float value = Clamp(confidence);
        if (value >= 0.85f)
        {
            return "高";
        }

        if (value >= SafeFallbackThreshold)
        {
            return "中";
        }

        return value > 0f ? "低" : "无";
    }
}
