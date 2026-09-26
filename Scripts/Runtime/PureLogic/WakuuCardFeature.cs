using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>卡牌关键词（与游戏 `CardKeyword` 同口径的纯逻辑镜像，含"不可打出"这类硬过滤输入）。</summary>
[System.Flags]
internal enum WakuuCardKeywords
{
    None = 0,

    /// <summary>消耗：用后永久移除。</summary>
    Exhaust = 1,

    /// <summary>虚无：不打就消失。</summary>
    Ethereal = 2,

    /// <summary>固有：开局必在手牌。</summary>
    Innate = 4,

    /// <summary>不可打出（诅咒等硬过滤输入）。</summary>
    Unplayable = 8,

    /// <summary>保留：可以留到更好的时机。</summary>
    Retain = 16,

    /// <summary>诡计：被弃掉时触发。</summary>
    Sly = 32,

    /// <summary>永恒：不可移除。</summary>
    Eternal = 64,
}

/// <summary>
/// 一张卡的知识层特征（总规范 v2 §18「CardFeature 最终模型」/ 实施草案 §4.1）。
///
/// 三条不变量（每条都能在 v2 找到出处，别破坏）：
/// 1. **`DirectDamage` 与"效果推导出来的伤害"分开**（v2 §19）：毒牌的直接伤害是 0，
///    但 <see cref="UnknownPotential"/> / DoT 端口不为 0 ⇒ 不会被判成"没伤害的垃圾牌"。
/// 2. **未知效果绝不判 0**（v2 §5/§19/§78）：读不懂的动作进 <see cref="UnknownPotential"/>（正值）。
/// 3. **信息不足就用 <see cref="Confidence"/> 表达**（v2 §40/§62），不要靠"假装知道"。
///
/// 本结构**不参与任何决策**（M1 只做知识层）；它是 M2 抓牌评分与 M3 打牌评分的共同输入。
/// </summary>
internal readonly struct WakuuCardFeature
{
    public WakuuCardFeature(
        string id,
        WakuuScoreCardKind kind,
        int cost,
        bool isXCost,
        WakuuCardKeywords keywords,
        IReadOnlyList<WakuuEffectFeature> effects,
        int directDamage,
        int directBlock,
        int draw,
        int energy,
        WakuuPortProfile ports,
        float knownRatio,
        float unknownPotential,
        float confidence,
        WakuuEffectSource source,
        float quality,
        float reliability,
        float conditionality,
        float risk,
        int unknownEffectCount)
    {
        Id = id ?? string.Empty;
        Kind = kind;
        Cost = cost < 0 ? 0 : cost;
        IsXCost = isXCost;
        Keywords = keywords;
        Effects = effects ?? System.Array.Empty<WakuuEffectFeature>();
        DirectDamage = directDamage < 0 ? 0 : directDamage;
        DirectBlock = directBlock < 0 ? 0 : directBlock;
        Draw = draw < 0 ? 0 : draw;
        Energy = energy < 0 ? 0 : energy;
        Ports = ports;
        KnownRatio = WakuuConfidence.Clamp(knownRatio);
        UnknownPotential = WakuuCardBaseline.Sanitize(unknownPotential);
        Confidence = WakuuConfidence.Clamp(confidence);
        Source = source;
        Quality = WakuuCardBaseline.Sanitize(quality);
        Reliability = WakuuConfidence.Clamp(reliability);
        Conditionality = WakuuConfidence.Clamp(conditionality);
        Risk = WakuuConfidence.Clamp(risk);
        UnknownEffectCount = unknownEffectCount < 0 ? 0 : unknownEffectCount;
    }

    /// <summary>卡 id（游戏 `CardModel.Id.Entry`，用于日志与静态表查表）。</summary>
    public string Id { get; }

    public WakuuScoreCardKind Kind { get; }

    /// <summary>打出费用（X 费牌取"当前能量"，与游戏 `GetAmountToSpend()` 同口径）。</summary>
    public int Cost { get; }

    public bool IsXCost { get; }

    public WakuuCardKeywords Keywords { get; }

    /// <summary>抽出来的效果事实（v2 §6/§18 的 `Effects`）。</summary>
    public IReadOnlyList<WakuuEffectFeature> Effects { get; }

    /// <summary>直接伤害（读得出的部分；不含 DoT / 计算型伤害）。</summary>
    public int DirectDamage { get; }

    /// <summary>直接格挡。</summary>
    public int DirectBlock { get; }

    /// <summary>抽牌数。</summary>
    public int Draw { get; }

    /// <summary>获得能量。</summary>
    public int Energy { get; }

    /// <summary>能力端口向量（v2 §27）。</summary>
    public WakuuPortProfile Ports { get; }

    /// <summary>已知元占比（0~1）：已知信息占总信息量的比例。</summary>
    public float KnownRatio { get; }

    /// <summary>未知潜能（v2 §19/§78）：读不懂的效果折成的正价值，**永不为"没效果"背书**。</summary>
    public float UnknownPotential { get; }

    /// <summary>整张卡的知识置信度（0~1）。</summary>
    public float Confidence { get; }

    /// <summary>知识来源（v2 §74）。</summary>
    public WakuuEffectSource Source { get; }

    /// <summary>独立有效性（v2 §24：不依赖条件也成立的价值；由端口与基线折算）。</summary>
    public float Quality { get; }

    /// <summary>可靠性（v2 §24）：无前置即可生效 ⇒ 高。</summary>
    public float Reliability { get; }

    /// <summary>条件依赖度（v2 §24）：需要前置状态/特定局面才生效 ⇒ 高。</summary>
    public float Conditionality { get; }

    /// <summary>风险（自伤、消耗关键资源、不可逆）。</summary>
    public float Risk { get; }

    /// <summary>读不懂的动作个数（0 = 全部读懂）。</summary>
    public int UnknownEffectCount { get; }

    /// <summary>是否存在读不懂的效果（v2 §5：不能当成"没效果"）。</summary>
    public bool HasUnknownEffect => UnknownEffectCount > 0;

    /// <summary>硬过滤：不可打出的牌（诅咒/状态牌）——任何评分都不该给它正分。</summary>
    public bool IsUnplayable => (Keywords & WakuuCardKeywords.Unplayable) != 0;

    /// <summary>日志用短描述（中文，`[瓦库评价]` 锚点里直接用）。</summary>
    public string DescribeShort()
    {
        string name = string.IsNullOrEmpty(Id) ? "(未命名)" : Id;
        string unknown = HasUnknownEffect ? $", 未知×{UnknownEffectCount}(+{UnknownPotential:0.00})" : string.Empty;
        return $"{name}[费={Cost}{(IsXCost ? "X" : string.Empty)}, {Kind}, 伤={DirectDamage}, 防={DirectBlock}, "
            + $"抽={Draw}, 能={Energy}, 质量={Quality:0.00}, 置信={WakuuConfidence.Describe(Confidence)}{unknown}, 来源={Source}]";
    }
}
