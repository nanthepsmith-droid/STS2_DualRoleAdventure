using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 知识层抽取器（实施草案 §5.1 / 总规范 v2 §6「Effect Extraction」）：
/// 把一张 <see cref="CardModel"/> 折算成纯逻辑的 <see cref="WakuuCardFeature"/>。
///
/// 三个信息来源（v2 §59 的四档兼容模式里"原版/已知卡"与"普通 MOD"这两档）：
/// ① **有限元（Parsed）**：`DynamicVars` 里读得出数值的部分（伤害/格挡/抽牌/能量/生命/资源），
///    以及 `PowerVar` 形式的"施加某状态 N 层"事实；
/// ② **静态语义表（Static）**：把状态名翻译成通用行为（`PoisonPower → DamageOverTime`），表在
///    <see cref="WakuuStaticEffectTable"/>，上限 50 条；
/// ③ **推断（DefaultHeuristic）**：读不出来的部分**不判 0**，进 <see cref="WakuuCardFeature.UnknownPotential"/>
///    并压低置信度（v2 §5/§19/§78）。
///
/// 硬约束（照抄仓库与规范的既有坑，别改）：
/// - **不能用属性访问判空**：`DynamicVarSet` 的 `Damage` 等属性在缺键时抛 `KeyNotFoundException`
///   （实施草案 §1.1 #7），所以全程走 `ContainsKey` / 枚举；
/// - **本类只读**：不写游戏状态、不触发动画、不做决策（M1 明确定位"不做决策接入"）；
/// - **绝不让异常冒出去**：任何异常都退化成保守特征（未知潜能 &gt; 0 + 置信度 0），
///   因为知识层出问题**永远不能**变成瓦库不出牌（v2 §82「Solver 失败时怎么办」）。
/// </summary>
internal static class WakuuEffectExtractor
{
    /// <summary>静态表端口覆盖的层数基准：5 层（Poison 10 层就是 1.5 倍权重，上限 1.5）。</summary>
    private const float LayerReference = 5f;

    private const float LayerScaleMin = 0.5f;

    private const float LayerScaleMax = 1.5f;

    /// <summary>抽取一张卡的知识特征；任何异常都返回保守特征（不抛给调用方）。</summary>
    public static WakuuCardFeature Extract(CardModel card, Player owner)
    {
        if (card == null)
        {
            return Fallback(string.Empty, "卡模型为空");
        }

        try
        {
            return ExtractCore(card, owner);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库知识层抽取异常，本次按未知卡保守处理: card={SafeId(card)}, error={exception.Message}");
            return Fallback(SafeId(card), exception.Message);
        }
    }

    /// <summary>批量抽取（顺序与入参一致）；单张失败不影响其余。</summary>
    public static List<WakuuCardFeature> ExtractAll(IReadOnlyList<CardModel> cards, Player owner)
    {
        List<WakuuCardFeature> features = new();
        if (cards == null)
        {
            return features;
        }

        for (int i = 0; i < cards.Count; i++)
        {
            features.Add(Extract(cards[i], owner));
        }

        return features;
    }

    // ------------------------------------------------------------------
    // 主体
    // ------------------------------------------------------------------

    private static WakuuCardFeature ExtractCore(CardModel card, Player owner)
    {
        string id = SafeId(card);
        int cost = card.EnergyCost.GetAmountToSpend();
        bool isXCost = card.EnergyCost.CostsX;
        WakuuScoreCardKind kind = MapKind(card.Type);
        WakuuScoreTargetKind targetKind = MapTargetKind(card.TargetType);
        WakuuCardKeywords keywords = MapKeywords(card.Keywords);

        List<WakuuEffectFeature> effects = new();
        int unknownSignals = 0;
        int knownSignals = 0;
        float confidenceSum = 0f;
        int confidenceCount = 0;

        int directDamage = 0;
        int directBlock = 0;
        int draw = 0;
        int energy = 0;
        float dotPort = 0f;
        float tableDamagePort = 0f;
        float tableDefensePort = 0f;
        float tableScalingPort = 0f;
        float tableControlPort = 0f;
        float tableResourcePort = 0f;
        float deckControlPort = 0f;
        float risk = 0f;
        float conditionality = 0f;
        bool sawCalculatedDamage = false;
        bool hasDamageVar = false;

        int strength = owner == null ? 0 : SafePowerAmount<StrengthPower>(owner);
        int dexterity = owner == null ? 0 : SafePowerAmount<DexterityPower>(owner);
        int repeat = ReadRepeat(card);

        foreach (KeyValuePair<string, DynamicVar> pair in card.DynamicVars)
        {
            string key = pair.Key;
            int amount = pair.Value.IntValue;

            switch (key)
            {
                case "Damage":
                    hasDamageVar = true;
                    directDamage += (amount + strength) * repeat;
                    AddEffect(effects, WakuuEffectKind.DirectDamage, targetKind, key, amount,
                        WakuuGenericBehavior.DirectDamage, WakuuEffectSource.Parsed, 1f);
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "Block":
                    directBlock += Math.Max(0, amount + dexterity);
                    AddEffect(effects, WakuuEffectKind.DirectBlock, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.DirectBlock, WakuuEffectSource.Parsed, 1f);
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "Cards":
                    draw += Math.Max(0, amount);
                    AddEffect(effects, WakuuEffectKind.CardDraw, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.CardDraw, WakuuEffectSource.Parsed, 1f);
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "Energy":
                    energy += Math.Max(0, amount);
                    AddEffect(effects, WakuuEffectKind.EnergyGain, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.ResourceGeneration, WakuuEffectSource.Parsed, 1f);
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "Heal":
                    AddEffect(effects, WakuuEffectKind.Heal, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.Healing, WakuuEffectSource.Parsed, 1f);
                    tableDefensePort += 0.2f;
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "Gold":
                case "Forge":
                case "Stars":
                case "Summon":
                    AddEffect(effects, WakuuEffectKind.GainResource, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.ResourceGeneration, WakuuEffectSource.Parsed, 1f);
                    tableResourcePort += 0.4f;
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "HpLoss":
                    AddEffect(effects, WakuuEffectKind.HpLoss, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.Risk, WakuuEffectSource.Parsed, 1f);
                    risk += 0.4f;
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "MaxHp":
                    AddEffect(effects, WakuuEffectKind.ModifyPower, WakuuEffectTarget.Self, key, amount,
                        WakuuGenericBehavior.Buff, WakuuEffectSource.Parsed, 1f);
                    tableScalingPort += 0.3f;
                    knownSignals++;
                    confidenceSum += 1f;
                    confidenceCount++;
                    break;

                case "CalculatedDamage":
                case "CalculatedBlock":
                case "CalculationBase":
                case "CalculationExtra":
                case "ExtraDamage":
                case "OstyDamage":
                    // 计算型数值：**不解析**（实施草案 §1.1 #7 的口径），但必须留下"这里有效果但读不出"
                    sawCalculatedDamage = true;
                    unknownSignals++;
                    break;

                default:
                    // 其余键：PowerVar 形式（施加/修改某状态），含 mod 自定义状态。
                    ParseStatusVar(
                        key, amount, targetKind, effects,
                        ref unknownSignals, ref knownSignals, ref confidenceSum, ref confidenceCount,
                        ref dotPort, ref tableDamagePort, ref tableDefensePort, ref tableScalingPort,
                        ref tableControlPort, ref tableResourcePort, ref conditionality);
                    break;
            }
        }

        // 攻击牌却没有读得出的伤害来源 ⇒ 计算型/脚本型伤害（v2 §19/§77：绝不能判 0）。
        if (kind == WakuuScoreCardKind.Attack && !hasDamageVar)
        {
            unknownSignals++;
            sawCalculatedDamage = true;
        }

        // 端口折算（端口是"当量"，1.0 = 一张健康牌）。
        float damagePort = WakuuCardBaseline.DamagePort(directDamage, cost) + dotPort + tableDamagePort;
        float defensePort = WakuuCardBaseline.DefensePort(directBlock, cost) + tableDefensePort;
        float drawPort = WakuuCardBaseline.DrawPort(draw);
        float energyPort = WakuuCardBaseline.EnergyPort(energy);
        float aoePort = targetKind == WakuuScoreTargetKind.AllEnemies && directDamage > 0 ? 0.3f : 0f;

        if ((keywords & WakuuCardKeywords.Exhaust) != 0)
        {
            // 消耗：牌堆变薄（正面）但用掉就没了（风队里是代价）—— 只记很小的牌堆操作端口。
            deckControlPort += 0.1f;
            risk += 0.15f;
        }

        if ((keywords & WakuuCardKeywords.Ethereal) != 0)
        {
            // 虚无：不打就消失 ⇒ 条件性强（必须在有限时机打出去）。
            conditionality += 0.15f;
        }

        if (isXCost)
        {
            conditionality += 0.2f;
        }

        if (cost > 2)
        {
            conditionality += 0.15f;
        }

        if (unknownSignals > 0)
        {
            conditionality += 0.2f;
            risk += 0.2f;
        }

        if (sawCalculatedDamage)
        {
            conditionality += 0.1f;
        }

        if (card.GainsBlock && directBlock <= 0)
        {
            // GainsBlock 为真却读不出 Block 变量：说明格挡量依赖运行时（常见于 mod 卡）。
            unknownSignals++;
        }

        WakuuPortProfile ports = new(
            damage: damagePort,
            defense: defensePort,
            draw: drawPort,
            energy: energyPort,
            scaling: tableScalingPort,
            control: tableControlPort,
            aoe: aoePort,
            deckControl: deckControlPort,
            resourceGeneration: tableResourcePort);

        int totalSignals = knownSignals + unknownSignals;
        float knownRatio = totalSignals <= 0 ? 0f : (float)knownSignals / totalSignals;
        float behaviorConfidence = confidenceCount > 0 ? confidenceSum / confidenceCount : 0f;
        float confidence = WakuuConfidence.Combine(WakuuConfidence.FromKnownRatio(knownRatio), behaviorConfidence);

        float unknownPotential = WakuuCardBaseline.UnknownPotential(unknownSignals);
        float clampedConditionality = WakuuConfidence.Clamp(conditionality);
        float reliability = WakuuConfidence.Clamp((1f - clampedConditionality) * (0.5f + 0.5f * confidence));
        float quality = ports.Total;

        if ((keywords & WakuuCardKeywords.Unplayable) != 0)
        {
            // 不可打出：硬过滤输入（诅咒/状态牌），M1 只如实标注，不做决策。
            quality = 0f;
            reliability = 0f;
        }

        WakuuEffectSource source = WakuuEffectSource.DefaultHeuristic;
        if (effects.Exists(static effect => effect.Source == WakuuEffectSource.Static))
        {
            source = WakuuEffectSource.Static;
        }
        else if (knownSignals > 0)
        {
            source = WakuuEffectSource.Parsed;
        }

        return new WakuuCardFeature(
            id: id,
            kind: kind,
            cost: cost,
            isXCost: isXCost,
            keywords: keywords,
            effects: effects,
            directDamage: directDamage,
            directBlock: directBlock,
            draw: draw,
            energy: energy,
            ports: ports,
            knownRatio: knownRatio,
            unknownPotential: unknownPotential,
            confidence: confidence,
            source: source,
            quality: quality,
            reliability: reliability,
            conditionality: clampedConditionality,
            risk: WakuuConfidence.Clamp(risk),
            unknownEffectCount: unknownSignals);
    }

    /// <summary>
    /// 解析 `PowerVar` 形式的键：先查静态语义表（Known Semantic），查不到就**保留事实 + 未知行为**
    /// （v2 §8 的 Unknown Semantic）——这是"未知 mod 卡不被判垃圾"的关键一步。
    /// </summary>
    private static void ParseStatusVar(
        string key,
        int amount,
        WakuuScoreTargetKind targetKind,
        List<WakuuEffectFeature> effects,
        ref int unknownSignals,
        ref int knownSignals,
        ref float confidenceSum,
        ref int confidenceCount,
        ref float dotPort,
        ref float tableDamagePort,
        ref float tableDefensePort,
        ref float tableScalingPort,
        ref float tableControlPort,
        ref float tableResourcePort,
        ref float conditionality)
    {
        float layerScale = Math.Clamp(amount / LayerReference, LayerScaleMin, LayerScaleMax);
        WakuuEffectTarget target = targetKind switch
        {
            WakuuScoreTargetKind.AnyEnemy => WakuuEffectTarget.Enemy,
            WakuuScoreTargetKind.AllEnemies => WakuuEffectTarget.AllEnemies,
            WakuuScoreTargetKind.AnyAlly => WakuuEffectTarget.Ally,
            WakuuScoreTargetKind.AllAllies => WakuuEffectTarget.AllAllies,
            _ => WakuuEffectTarget.Self,
        };

        if (WakuuStaticEffectTable.TryGet(key, out WakuuStaticEffect known))
        {
            AddEffect(effects, WakuuEffectKind.ApplyStatus, target, key, amount,
                known.Behavior, WakuuEffectSource.Static, known.Confidence);

            WakuuPortProfile port = known.PortOverride.Scale(layerScale);
            tableDamagePort += port.Damage;
            tableDefensePort += port.Defense;
            tableScalingPort += port.Scaling;
            tableControlPort += port.Control;
            tableResourcePort += port.ResourceGeneration;

            if (known.HasPrerequisite)
            {
                // 有前置条件（v2 §39 的孤儿组件候选）：当前可能什么也不做。
                conditionality += 0.35f;
            }

            knownSignals++;
            confidenceSum += known.Confidence;
            confidenceCount++;
            return;
        }

        // 不认识：**保留事实**（id + 数量），行为记 Unknown，并计入未知潜能（v2 §5/§7/§8）。
        AddEffect(effects, WakuuEffectKind.ApplyStatus, target, key, amount,
            WakuuGenericBehavior.Unknown, WakuuEffectSource.DefaultHeuristic, 0f);
        unknownSignals++;

        if (IsDamageOverTimeName(key))
        {
            // 名字里带毒/灼烧/流血等"持续伤害"语素的 mod 状态：给一个**低置信度**的 DoT 端口，
            // 并且**同时**保留未知潜能（不因为猜了一次就把不确定性抹掉）。
            dotPort += WakuuCardBaseline.DamageOverTimePort(amount) * 0.5f;
        }
    }

    /// <summary>
    /// 名字启发式（v2 §9：行为相似就归一类）。刻意只认最直白的语素，**并且只给一半权重**——
    /// 猜错也不至于把评分带偏（v2 §40 的纪律）。
    /// </summary>
    private static bool IsDamageOverTimeName(string key)
    {
        return key.Contains("Poison", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Bleed", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Burn", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Blight", StringComparison.OrdinalIgnoreCase)
            || key.Contains("Corrupt", StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------
    // 小工具
    // ------------------------------------------------------------------

    private static void AddEffect(
        List<WakuuEffectFeature> effects,
        WakuuEffectKind kind,
        WakuuEffectTarget target,
        string id,
        int amount,
        WakuuGenericBehavior behavior,
        WakuuEffectSource source,
        float confidence)
    {
        effects.Add(new WakuuEffectFeature(kind, target, id, amount, behavior, source, confidence));
    }

    private static void AddEffect(
        List<WakuuEffectFeature> effects,
        WakuuEffectKind kind,
        WakuuScoreTargetKind targetKind,
        string id,
        int amount,
        WakuuGenericBehavior behavior,
        WakuuEffectSource source,
        float confidence)
    {
        WakuuEffectTarget target = targetKind switch
        {
            WakuuScoreTargetKind.AnyEnemy => WakuuEffectTarget.Enemy,
            WakuuScoreTargetKind.AllEnemies => WakuuEffectTarget.AllEnemies,
            WakuuScoreTargetKind.AnyAlly => WakuuEffectTarget.Ally,
            WakuuScoreTargetKind.AllAllies => WakuuEffectTarget.AllAllies,
            WakuuScoreTargetKind.Self => WakuuEffectTarget.Self,
            WakuuScoreTargetKind.AnyPlayer => WakuuEffectTarget.Self,
            _ => WakuuEffectTarget.None,
        };

        AddEffect(effects, kind, target, id, amount, behavior, source, confidence);
    }

    private static int ReadRepeat(CardModel card)
    {
        if (card.DynamicVars.ContainsKey("Repeat"))
        {
            return Math.Max(1, card.DynamicVars.Repeat.IntValue);
        }

        return 1;
    }

    private static int SafePowerAmount<TPower>(Player owner)
        where TPower : PowerModel
    {
        try
        {
            return owner.Creature.GetPowerAmount<TPower>();
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static string SafeId(CardModel? card)
    {
        try
        {
            return card?.Id.Entry ?? string.Empty;
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static WakuuCardKeywords MapKeywords(IReadOnlySet<CardKeyword> keywords)
    {
        WakuuCardKeywords mapped = WakuuCardKeywords.None;
        if (keywords == null)
        {
            return mapped;
        }

        if (keywords.Contains(CardKeyword.Exhaust))
        {
            mapped |= WakuuCardKeywords.Exhaust;
        }

        if (keywords.Contains(CardKeyword.Ethereal))
        {
            mapped |= WakuuCardKeywords.Ethereal;
        }

        if (keywords.Contains(CardKeyword.Innate))
        {
            mapped |= WakuuCardKeywords.Innate;
        }

        if (keywords.Contains(CardKeyword.Unplayable))
        {
            mapped |= WakuuCardKeywords.Unplayable;
        }

        if (keywords.Contains(CardKeyword.Retain))
        {
            mapped |= WakuuCardKeywords.Retain;
        }

        if (keywords.Contains(CardKeyword.Sly))
        {
            mapped |= WakuuCardKeywords.Sly;
        }

        if (keywords.Contains(CardKeyword.Eternal))
        {
            mapped |= WakuuCardKeywords.Eternal;
        }

        return mapped;
    }

    private static WakuuScoreCardKind MapKind(CardType type)
    {
        return type switch
        {
            CardType.Attack => WakuuScoreCardKind.Attack,
            CardType.Skill => WakuuScoreCardKind.Skill,
            CardType.Power => WakuuScoreCardKind.Power,
            _ => WakuuScoreCardKind.Other,
        };
    }

    private static WakuuScoreTargetKind MapTargetKind(TargetType type)
    {
        return type switch
        {
            TargetType.Self => WakuuScoreTargetKind.Self,
            TargetType.AnyEnemy => WakuuScoreTargetKind.AnyEnemy,
            TargetType.AllEnemies => WakuuScoreTargetKind.AllEnemies,
            TargetType.AnyPlayer => WakuuScoreTargetKind.AnyPlayer,
            TargetType.AnyAlly => WakuuScoreTargetKind.AnyAlly,
            TargetType.AllAllies => WakuuScoreTargetKind.AllAllies,
            TargetType.None => WakuuScoreTargetKind.None,
            _ => WakuuScoreTargetKind.Other,
        };
    }

    /// <summary>
    /// 保守兜底特征（vs v2 §62/§79）：**有未知潜能、置信度 0、来源=兜底启发式**，
    /// 保证"抽取失败"在评分层表现为"看不懂但可能有价值"，而不是"没效果"。
    /// </summary>
    private static WakuuCardFeature Fallback(string id, string reason)
    {
        return new WakuuCardFeature(
            id: string.IsNullOrEmpty(id) ? "(抽取失败)" : id,
            kind: WakuuScoreCardKind.Other,
            cost: 0,
            isXCost: false,
            keywords: WakuuCardKeywords.None,
            effects: new List<WakuuEffectFeature>
            {
                new(WakuuEffectKind.Unknown, WakuuEffectTarget.None, reason, 0,
                    WakuuGenericBehavior.Unknown, WakuuEffectSource.DefaultHeuristic, 0f),
            },
            directDamage: 0,
            directBlock: 0,
            draw: 0,
            energy: 0,
            ports: WakuuPortProfile.Zero,
            knownRatio: 0f,
            unknownPotential: WakuuCardBaseline.UnknownPotential(1),
            confidence: 0f,
            source: WakuuEffectSource.DefaultHeuristic,
            quality: 0f,
            reliability: 0f,
            conditionality: 1f,
            risk: 0.5f,
            unknownEffectCount: 1);
    }
}
