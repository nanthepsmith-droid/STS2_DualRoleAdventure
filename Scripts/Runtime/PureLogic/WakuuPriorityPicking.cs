using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 选牌场景（可行性分析 §9.1）：由 CardSelectCmd 的专用入口 / 来源模型直接判定。
/// 仅在「智能选牌优先级」开关开启时生效；未知场景一律维持既有 cardPickMode 策略。
/// </summary>
internal enum WakuuPickScenario
{
    /// <summary>未识别 / 无明确优先级语义（走既有策略）。</summary>
    Unknown,

    /// <summary>消耗 / 删除类（去掉坏牌）：优先删 诅咒→状态→任务→打击→基础防御→其余。</summary>
    Remove,

    /// <summary>复制类（复制好牌）：首选非坏牌，候选全坏时倒序 防御→打击→任务→状态→诅咒。</summary>
    Copy,

    /// <summary>变化类（变掉平庸牌、保留特殊牌）：优先 打击→防御→其余，硬排除诅咒/状态/任务。</summary>
    Transform,
}

/// <summary>卡牌按优先级表分类的类别（纯数据，不依赖游戏类型）。</summary>
internal enum WakuuCardKind
{
    Other,
    BasicDefend,
    BasicStrike,
    Quest,
    Status,
    Curse,
}

/// <summary>
/// 智能选牌优先级纯函数（可行性分析 §9.2 规则表）。不依赖任何游戏类型，可直接单测。
///
/// 规则表（§9.2）：
///   Remove（消耗/删除）：Curse &gt; Status &gt; Quest &gt; Strike &gt; 基础防御 &gt; 其余从左到右
///   Copy（复制）：首选非 {Curse/Status/Quest/Strike/基础防御} 的牌；候选全为坏牌时
///        倒序 防御 &gt; 打击 &gt; 任务 &gt; 状态 &gt; 诅咒
///   Transform（变化）：Strike &gt; Defend &gt; 其余从左到右；硬排除 Curse/Status/Quest
///
/// 实现要点：按类别给权重做稳定降序排列（同权重保持原序 = "从左到右"）。
/// </summary>
internal static class WakuuPriorityPicking
{
    // 与游戏枚举 MegaCrit.Sts2.Core.Entities.Cards.CardType 的取值保持一致（纯函数里用 int 避免依赖游戏类型）
    public const int CardTypeNone = 0;
    public const int CardTypeAttack = 1;
    public const int CardTypeSkill = 2;
    public const int CardTypePower = 3;
    public const int CardTypeStatus = 4;
    public const int CardTypeCurse = 5;
    public const int CardTypeQuest = 6;

    /// <summary>Transform 场景硬排除类别的权重（远低于任何可选项）。</summary>
    private const int ExcludedPriority = -100;

    /// <summary>
    /// 按卡 id 与 CardType 枚举值把卡归入优先级类别。
    /// cardType 传 (int)CardModel.Type；id 传 CardModel.Id.Entry（裸 id，含 STRIKE/DEFEND 等命名）。
    /// </summary>
    public static WakuuCardKind ClassifyCard(string? id, int cardType)
    {
        if (cardType == CardTypeCurse)
        {
            return WakuuCardKind.Curse;
        }

        if (cardType == CardTypeStatus)
        {
            return WakuuCardKind.Status;
        }

        if (cardType == CardTypeQuest)
        {
            return WakuuCardKind.Quest;
        }

        if (cardType == CardTypeAttack && WakuuCardId.IsStrikeId(id))
        {
            return WakuuCardKind.BasicStrike;
        }

        if (cardType == CardTypeSkill && WakuuCardId.IsDefendId(id))
        {
            return WakuuCardKind.BasicDefend;
        }

        return WakuuCardKind.Other;
    }

    /// <summary>
    /// 按场景对候选做优先级降序排序，返回**下标列表**（调用方取前 N 个）。
    /// 稳定排序 → 同优先级保持原序（"其余从左到右"）。
    /// Unknown 场景直接返回原序（0..N-1）。
    /// </summary>
    public static List<int> RankIndicesByScenario(WakuuPickScenario scenario, IReadOnlyList<WakuuCardKind> kinds)
    {
        int count = kinds?.Count ?? 0;
        List<int> indices = new(count);
        for (int i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        if (scenario == WakuuPickScenario.Unknown)
        {
            return indices;
        }

        // 容忍外部传 null（防御性），count 已在上面取到 0 时直接返回空序
        IReadOnlyList<WakuuCardKind> effectiveKinds = kinds ?? Array.Empty<WakuuCardKind>();
        return indices
            .OrderByDescending((i) => PriorityOf(scenario, effectiveKinds[i]))
            .ToList();
    }

    private static int PriorityOf(WakuuPickScenario scenario, WakuuCardKind kind)
    {
        return scenario switch
        {
            // 消耗/删除：坏牌优先被删
            WakuuPickScenario.Remove => kind switch
            {
                WakuuCardKind.Curse => 6,
                WakuuCardKind.Status => 5,
                WakuuCardKind.Quest => 4,
                WakuuCardKind.BasicStrike => 3,
                WakuuCardKind.BasicDefend => 2,
                _ => 1,
            },

            // 复制：首选非坏牌；全坏时按"防御→打击→任务→状态→诅咒"倒序（Defend 权重最高）
            WakuuPickScenario.Copy => kind switch
            {
                WakuuCardKind.Other => 10,
                WakuuCardKind.BasicDefend => 5,
                WakuuCardKind.BasicStrike => 4,
                WakuuCardKind.Quest => 3,
                WakuuCardKind.Status => 2,
                WakuuCardKind.Curse => 1,
                _ => 0,
            },

            // 变化：优先变掉平庸的打击/防御；诅咒/状态/任务硬排除
            WakuuPickScenario.Transform => kind switch
            {
                WakuuCardKind.BasicStrike => 3,
                WakuuCardKind.BasicDefend => 2,
                WakuuCardKind.Other => 1,
                _ => ExcludedPriority,
            },

            _ => 0,
        };
    }
}
