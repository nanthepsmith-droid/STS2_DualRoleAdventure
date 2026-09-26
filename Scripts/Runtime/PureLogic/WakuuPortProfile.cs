using System;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 能力端口向量（总规范 v2 §27/§28）。
///
/// 端口不是"流派"，而是"能力"：`Poison → DamageOverTime / Scaling`、`Wrath → DamageMultiplier`、
/// `Calm → ResourceGeneration`、`Discard → DeckControl / ResourceGeneration`。
/// 决策层用"端口需求 × 端口供给"（v2 §29 的 NeedFit）代替流派识别（v2 §90 第 6 条明令禁止流派硬编码）。
///
/// 量纲约定：各分量都是 0~∞ 的**当量值**（用 <see cref="WakuuCardBaseline"/> 的健康线折算），
/// 不是原始点数，这样不同来源的端口可以直接相加/比较。纯结构，可单测。
/// </summary>
internal readonly struct WakuuPortProfile
{
    public WakuuPortProfile(
        float damage,
        float defense,
        float draw,
        float energy,
        float scaling,
        float control,
        float aoe,
        float deckControl,
        float resourceGeneration)
    {
        Damage = Sanitize(damage);
        Defense = Sanitize(defense);
        Draw = Sanitize(draw);
        Energy = Sanitize(energy);
        Scaling = Sanitize(scaling);
        Control = Sanitize(control);
        AoE = Sanitize(aoe);
        DeckControl = Sanitize(deckControl);
        ResourceGeneration = Sanitize(resourceGeneration);
    }

    /// <summary>全零端口。</summary>
    public static WakuuPortProfile Zero => default;

    /// <summary>单点伤害端口（AoE 由调用方另行给 <see cref="AoE"/>）。</summary>
    public float Damage { get; }

    /// <summary>防御/格挡能力。</summary>
    public float Defense { get; }

    /// <summary>抽牌与过滤。</summary>
    public float Draw { get; }

    /// <summary>能量（产出或节省）。</summary>
    public float Energy { get; }

    /// <summary>长线成长（力量/姿态/每回合增益）。</summary>
    public float Scaling { get; }

    /// <summary>控场（虚弱/脆弱/眩晕等弱化敌人的手段）。</summary>
    public float Control { get; }

    /// <summary>群体伤害倾向（敌人多时价值上升）。</summary>
    public float AoE { get; }

    /// <summary>牌堆操作（弃牌、消耗、检索、排序）。</summary>
    public float DeckControl { get; }

    /// <summary>非能量资源产出（锻造、辉星、召唤、充能等）。</summary>
    public float ResourceGeneration { get; }

    /// <summary>端口总量（粗粒度"这张牌有多少内容"，用于日志与排序稳定性）。</summary>
    public float Total =>
        Damage + Defense + Draw + Energy + Scaling + Control + AoE + DeckControl + ResourceGeneration;

    /// <summary>相加（牌组聚合 / 多张牌叠加）。</summary>
    public WakuuPortProfile Add(in WakuuPortProfile other)
    {
        return new WakuuPortProfile(
            Damage + other.Damage,
            Defense + other.Defense,
            Draw + other.Draw,
            Energy + other.Energy,
            Scaling + other.Scaling,
            Control + other.Control,
            AoE + other.AoE,
            DeckControl + other.DeckControl,
            ResourceGeneration + other.ResourceGeneration);
    }

    /// <summary>按系数缩放（保留小数，不要提前取整，否则多张牌累加会掉精度）。</summary>
    public WakuuPortProfile Scale(float factor)
    {
        float k = float.IsNaN(factor) ? 0f : factor;
        return new WakuuPortProfile(
            Damage * k,
            Defense * k,
            Draw * k,
            Energy * k,
            Scaling * k,
            Control * k,
            AoE * k,
            DeckControl * k,
            ResourceGeneration * k);
    }

    /// <summary>
    /// 端口供给与需求的点积（v2 §29 `NeedFit = Σ PortValue_i × Need_i`）；
    /// 两个向量维度相同，缺项一律视为 0。
    /// </summary>
    public float FitWith(in WakuuPortProfile needs)
    {
        return (Damage * needs.Damage)
            + (Defense * needs.Defense)
            + (Draw * needs.Draw)
            + (Energy * needs.Energy)
            + (Scaling * needs.Scaling)
            + (Control * needs.Control)
            + (AoE * needs.AoE)
            + (DeckControl * needs.DeckControl)
            + (ResourceGeneration * needs.ResourceGeneration);
    }

    /// <summary>日志用（中文，便于 grep 与肉眼对账）。</summary>
    public override string ToString()
    {
        StringBuilder builder = new();
        builder.Append("攻=").Append(Damage.ToString("0.0"));
        builder.Append(" 防=").Append(Defense.ToString("0.0"));
        builder.Append(" 抽=").Append(Draw.ToString("0.0"));
        builder.Append(" 能=").Append(Energy.ToString("0.0"));
        builder.Append(" 成长=").Append(Scaling.ToString("0.0"));
        builder.Append(" 控=").Append(Control.ToString("0.0"));
        builder.Append(" 群=").Append(AoE.ToString("0.0"));
        builder.Append(" 堆=").Append(DeckControl.ToString("0.0"));
        builder.Append(" 资源=").Append(ResourceGeneration.ToString("0.0"));
        return builder.ToString();
    }

    private static float Sanitize(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return 0f;
        }

        return Math.Max(0f, value);
    }
}
