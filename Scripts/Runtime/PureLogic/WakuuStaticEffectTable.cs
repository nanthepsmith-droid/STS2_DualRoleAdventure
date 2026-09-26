using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 静态语义表的一条：**只声明"这个状态/效果在行为上属于哪一类"**，不写任何决策逻辑
/// （总规范 v2 §9 / 实施草案 §4.2）。
/// </summary>
internal readonly struct WakuuStaticEffect
{
    public WakuuStaticEffect(
        string id,
        WakuuGenericBehavior behavior,
        float portDamage,
        float portDefense,
        float portScaling,
        float portControl,
        float portResource,
        string requires,
        float confidence,
        string note)
    {
        Id = id;
        Behavior = behavior;
        PortOverride = new WakuuPortProfile(
            damage: portDamage,
            defense: portDefense,
            draw: 0f,
            energy: 0f,
            scaling: portScaling,
            control: portControl,
            aoe: 0f,
            deckControl: 0f,
            resourceGeneration: portResource);
        Requires = requires ?? string.Empty;
        Confidence = WakuuConfidence.Clamp(confidence);
        Note = note ?? string.Empty;
    }

    /// <summary>状态/效果标识（与游戏 `PowerVar` 的名字同口径，如 `PoisonPower`）。</summary>
    public string Id { get; }

    public WakuuGenericBehavior Behavior { get; }

    /// <summary>该效果给"提供方"带来的端口覆盖（0 = 不覆盖，交给推断层）。</summary>
    public WakuuPortProfile PortOverride { get; }

    /// <summary>前置条件说明（空 = 无前置）。非空 ⇒ 这张卡**当前可能什么也不做**（v2 §39 孤儿组件）。</summary>
    public string Requires { get; }

    public float Confidence { get; }

    /// <summary>口径备注（写清"为什么这么归"，便于日后复核）。</summary>
    public string Note { get; }

    /// <summary>是否为孤儿组件候选（v2 §39：很弱、必须依靠未来条件才成立）。</summary>
    public bool HasPrerequisite => Requires.Length > 0;
}

/// <summary>
/// 最小语义集（实施草案 §4.2，用户拍板 Q3）——**上限 50 条**，只覆盖"数值读不出但语义关键"的
/// 原版状态/持续伤害/增减益类；其余一律走推断 + 未来观测/真人先验。
///
/// 三条维护纪律：
/// 1. **只收"看名字即可确定粗语义"的原版效果**：拿不准的一律不入表（宁可由观测层以后补，
///    也不要写错一条把评分带偏）——这是 v2 §40「降低置信度而不是假定联动」的同一条纪律。
/// 2. **只声明行为与端口**，不写"什么时候该打"（决策属 M2/M3）。
/// 3. **不做 mod 专属适配**（v2 §60 禁止 per-MOD AI）：mod 自定义状态落到 `Unknown`，
///    由 <see cref="WakuuCardBaseline.UnknownPotential"/> 保守承接，绝不判 0。
/// </summary>
internal static class WakuuStaticEffectTable
{
    /// <summary>条目上限（Q3 拍板：最小语义集 ≤50 条）。单测会钉死它。</summary>
    public const int MaxEntries = 50;

    /// <summary>
    /// 表本体。键 = 游戏 `PowerVar` 的名字（= `DynamicVarSet` 里的键），大小写不敏感。
    /// 端口数值是"这张卡提供该效果时给多少端口"的粗权重（0~1 量级），用于后续评分折算。
    /// </summary>
    private static readonly Dictionary<string, WakuuStaticEffect> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        // ---- 持续伤害（v2 §5/§70 的毒牌范式）----
        ["PoisonPower"] = new("PoisonPower", WakuuGenericBehavior.DamageOverTime,
            portDamage: 0.6f, portDefense: 0f, portScaling: 0.1f, portControl: 0.1f, portResource: 0f,
            requires: "敌人", confidence: 0.95f, note: "每层回合结束结算 1 点伤害，属典型 DoT"),
        ["ConstrictPower"] = new("ConstrictPower", WakuuGenericBehavior.DamageOverTime,
            portDamage: 0.6f, portDefense: 0f, portScaling: 0.1f, portControl: 0.2f, portResource: 0f,
            requires: "敌人", confidence: 0.9f, note: "缠绕：回合结束掉血且随回合衰减，归 DoT"),
        ["DisintegrationPower"] = new("DisintegrationPower", WakuuGenericBehavior.DamageOverTime,
            portDamage: 0.5f, portDefense: 0f, portScaling: 0f, portControl: 0.2f, portResource: 0f,
            requires: string.Empty, confidence: 0.8f, note: "解体：持续掉血类状态，粗归 DoT"),
        ["DoomPower"] = new("DoomPower", WakuuGenericBehavior.DelayedEffect,
            portDamage: 0.5f, portDefense: 0f, portScaling: 0.2f, portControl: 0.3f, portResource: 0f,
            requires: string.Empty, confidence: 0.8f, note: "末日：延迟结算的死亡威胁，归延迟效果"),

        // ---- 负面减益（对敌人施放 ⇒ 控场端口）----
        ["VulnerablePower"] = new("VulnerablePower", WakuuGenericBehavior.Debuff,
            portDamage: 0.3f, portDefense: 0f, portScaling: 0.1f, portControl: 0.4f, portResource: 0f,
            requires: "敌人", confidence: 0.95f, note: "易伤：使目标受到伤害提高 ⇒ 等价于我方输出放大"),
        ["WeakPower"] = new("WeakPower", WakuuGenericBehavior.Debuff,
            portDamage: 0f, portDefense: 0.3f, portScaling: 0f, portControl: 0.5f, portResource: 0f,
            requires: "敌人", confidence: 0.95f, note: "虚弱：降低目标造成的伤害 ⇒ 等价于我方减伤（v2 §33 不硬换算成格挡）"),
        ["FrailPower"] = new("FrailPower", WakuuGenericBehavior.Debuff,
            portDamage: 0f, portDefense: 0.2f, portScaling: 0f, portControl: 0.4f, portResource: 0f,
            requires: string.Empty, confidence: 0.9f, note: "脆弱：降低目标获得的格挡"),
        ["DebilitatePower"] = new("DebilitatePower", WakuuGenericBehavior.Debuff,
            portDamage: 0.3f, portDefense: 0f, portScaling: 0f, portControl: 0.4f, portResource: 0f,
            requires: "敌人", confidence: 0.75f, note: "削弱类减益，归 Debuff（具体倍率不影响粗归类）"),
        ["DarkShacklesPower"] = new("DarkShacklesPower", WakuuGenericBehavior.Debuff,
            portDamage: 0.2f, portDefense: 0f, portScaling: 0f, portControl: 0.4f, portResource: 0f,
            requires: "敌人", confidence: 0.75f, note: "枷锁类减益"),

        // ---- 正面增益 / 成长（给我方 ⇒ 成长端口）----
        ["StrengthPower"] = new("StrengthPower", WakuuGenericBehavior.Scaling,
            portDamage: 0.4f, portDefense: 0f, portScaling: 1.0f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.95f, note: "力量：每层线性放大攻击伤害，长线成长核心"),
        ["DexterityPower"] = new("DexterityPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.4f, portScaling: 0.8f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.95f, note: "敏捷：每层放大格挡"),
        ["FocusPower"] = new("FocusPower", WakuuGenericBehavior.Scaling,
            portDamage: 0.4f, portDefense: 0f, portScaling: 0.9f, portControl: 0f, portResource: 0.2f,
            requires: "充能球", confidence: 0.85f, note: "集中：放大充能球效果 ⇒ 有前置（孤儿组件候选）"),
        ["ArtifactPower"] = new("ArtifactPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.3f, portScaling: 0.3f, portControl: 0.3f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "神器：抵消负面状态次数"),
        ["BufferPower"] = new("BufferPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.4f, portScaling: 0.4f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "缓冲：免伤次数（v2 §32 的致死保险）"),
        ["RegenPower"] = new("RegenPower", WakuuGenericBehavior.Healing,
            portDamage: 0f, portDefense: 0.3f, portScaling: 0.5f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.9f, note: "再生：回合结束回血"),
        ["BarricadePower"] = new("BarricadePower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.5f, portScaling: 0.6f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "壁垒：格挡不再清除 ⇒ 防御型成长"),
        ["BlurPower"] = new("BlurPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.4f, portScaling: 0.4f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "残影：下回合保留格挡"),
        ["ThornsPower"] = new("ThornsPower", WakuuGenericBehavior.Buff,
            portDamage: 0.2f, portDefense: 0.3f, portScaling: 0.3f, portControl: 0.2f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "荆棘：反伤（受击时对攻击者造成伤害）"),
        ["FlameBarrierPower"] = new("FlameBarrierPower", WakuuGenericBehavior.Buff,
            portDamage: 0.3f, portDefense: 0.2f, portScaling: 0.2f, portControl: 0.2f, portResource: 0f,
            requires: string.Empty, confidence: 0.8f, note: "火焰屏障：格挡 + 反伤"),
        ["DoubleDamagePower"] = new("DoubleDamagePower", WakuuGenericBehavior.DamageMultiplier,
            portDamage: 0.4f, portDefense: 0f, portScaling: 0.7f, portControl: 0f, portResource: 0f,
            requires: "攻击牌", confidence: 0.8f, note: "双倍伤害：下一张攻击翻倍 ⇒ 有前置（孤儿组件候选）"),
        ["EchoFormPower"] = new("EchoFormPower", WakuuGenericBehavior.Buff,
            portDamage: 0.3f, portDefense: 0.2f, portScaling: 0.8f, portControl: 0f, portResource: 0.3f,
            requires: string.Empty, confidence: 0.8f, note: "回响形态：每回合首张牌额外结算一次"),
        ["BurstPower"] = new("BurstPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.2f, portScaling: 0.6f, portControl: 0f, portResource: 0.3f,
            requires: "技能牌", confidence: 0.8f, note: "爆发：下一张技能翻倍 ⇒ 有前置（孤儿组件候选）"),
        ["CorruptionPower"] = new("CorruptionPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.2f, portScaling: 0.6f, portControl: 0f, portResource: 0.5f,
            requires: "技能牌", confidence: 0.8f, note: "腐化：技能牌费用归零"),
        ["FeelNoPainPower"] = new("FeelNoPainPower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.4f, portScaling: 0.5f, portControl: 0f, portResource: 0.2f,
            requires: "消耗卡", confidence: 0.8f, note: "无痛：每次消耗获得格挡 ⇒ 有前置（孤儿组件候选）"),
        ["DarkEmbracePower"] = new("DarkEmbracePower", WakuuGenericBehavior.CardDraw,
            portDamage: 0f, portDefense: 0f, portScaling: 0.3f, portControl: 0f, portResource: 0.4f,
            requires: "消耗卡", confidence: 0.8f, note: "黑暗拥抱：消耗时抽牌 ⇒ 有前置（孤儿组件候选）"),
        ["EnvenomPower"] = new("EnvenomPower", WakuuGenericBehavior.DamageOverTime,
            portDamage: 0.4f, portDefense: 0f, portScaling: 0.4f, portControl: 0.2f, portResource: 0f,
            requires: "攻击牌", confidence: 0.8f, note: "涂毒：攻击附加毒 ⇒ 有前置（孤儿组件候选）"),
        ["DemonFormPower"] = new("DemonFormPower", WakuuGenericBehavior.Scaling,
            portDamage: 0.3f, portDefense: 0f, portScaling: 1.0f, portControl: 0f, portResource: 0f,
            requires: string.Empty, confidence: 0.85f, note: "恶魔形态：每回合获得力量，典型长线成长"),

        // ---- 资源 / 抽牌（延迟型）----
        ["DrawCardsNextTurnPower"] = new("DrawCardsNextTurnPower", WakuuGenericBehavior.CardDraw,
            portDamage: 0f, portDefense: 0f, portScaling: 0.1f, portControl: 0f, portResource: 0.4f,
            requires: string.Empty, confidence: 0.85f, note: "下回合抽牌 ⇒ 延迟抽牌"),
        ["EnergyNextTurnPower"] = new("EnergyNextTurnPower", WakuuGenericBehavior.ResourceGeneration,
            portDamage: 0f, portDefense: 0f, portScaling: 0.1f, portControl: 0f, portResource: 0.6f,
            requires: string.Empty, confidence: 0.85f, note: "下回合能量 ⇒ 延迟资源"),
        ["AccuracyPower"] = new("AccuracyPower", WakuuGenericBehavior.Scaling,
            portDamage: 0.4f, portDefense: 0f, portScaling: 0.8f, portControl: 0f, portResource: 0f,
            requires: "小刀类", confidence: 0.75f, note: "精准：放大特定攻击 ⇒ 有前置（孤儿组件候选）"),
        ["AfterimagePower"] = new("AfterimagePower", WakuuGenericBehavior.Buff,
            portDamage: 0f, portDefense: 0.5f, portScaling: 0.5f, portControl: 0f, portResource: 0f,
            requires: "打牌", confidence: 0.75f, note: "残像：每打一张牌获得格挡"),
    };

    /// <summary>表内条目数（单测钉死 ≤ <see cref="MaxEntries"/>）。</summary>
    public static int Count => Table.Count;

    /// <summary>全部条目（只读，供报表/对账用）。</summary>
    public static IReadOnlyDictionary<string, WakuuStaticEffect> Entries => Table;

    /// <summary>按 id 查表（大小写不敏感）；找不到返回 false ⇒ 调用方必须走保守路径（不能判 0）。</summary>
    public static bool TryGet(string? id, out WakuuStaticEffect effect)
    {
        effect = default;
        if (string.IsNullOrWhiteSpace(id))
        {
            return false;
        }

        return Table.TryGetValue(id, out effect);
    }
}
