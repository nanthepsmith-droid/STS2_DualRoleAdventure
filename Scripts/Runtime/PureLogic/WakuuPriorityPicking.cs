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

    /// <summary>
    /// 弃牌类（滤牌/腾手牌，如杂技/预谋/赌徒芯片/行商之手）：优先丢奇巧牌
    /// （Sly，丢弃后自动打出=白嫖一次出牌），其次按坏牌优先的消耗类顺序。
    /// </summary>
    Discard,
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

    /// <summary>奇巧（Sly）牌：带奇巧标签或被效果临时赋予奇巧，丢弃后本回合自动打出。</summary>
    Sly,
}

/// <summary>
/// 智能选牌优先级纯函数（可行性分析 §9.2 规则表）。不依赖任何游戏类型，可直接单测。
///
/// 规则表（§9.2）：
///   Remove（消耗/删除）：Curse &gt; Status &gt; Quest &gt; Strike &gt; 基础防御 &gt; 其余从左到右
///   Copy（复制）：首选非 {Curse/Status/Quest/Strike/基础防御} 的牌；候选全为坏牌时
///        倒序 防御 &gt; 打击 &gt; 任务 &gt; 状态 &gt; 诅咒
///   Transform（变化）：Strike &gt; Defend &gt; 其余从左到右；硬排除 Curse/Status/Quest/Sly
///   Discard（弃牌，用户追加）：Sly(奇巧) &gt; Curse &gt; Status &gt; Quest &gt; Strike &gt; 基础防御 &gt; 其余
///        —— 奇巧牌丢弃后本回合自动打出，优先丢它是白嫖一次出牌。
/// 跨场景对 Sly 的默认语义：Remove 视同 Other（不优先消耗正面牌）；Copy 视同 Other（可优先复制）；
/// Transform 硬排除（变掉会失去奇巧白嫖机制）。
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
    /// 复制类来源的类型名关键词（§9.1 ★★）：原版 DualWield 用自定义 SelectionScreenPrompt，
    /// prefs loc key 识别不到复制语义，只能靠 source 类型名。mod 复制卡按含复制/镜像语义的关键词推断。
    /// </summary>
    private static readonly string[] CopySourceTypeNameKeywords =
    {
        "DualWield",
        "Copy",
        "Duplicate",
        "Echo",
        "Clone",
        "Double",
        "Mirror",
    };

    /// <summary>
    /// 手牌选牌场景判定（可行性分析 §9.1 ★★）：FromHand 的 source 类型名 + prefs 标题 loc key → 场景。
    /// 两条线索独立兜底，任一命中即返回：
    /// - prefs loc key：TO_EXHAUST / TO_REMOVE → Remove；TO_TRANSFORM → Transform；
    ///   TO_DISCARD → Discard（杂技/预谋/赌徒芯片/行商之手等弃牌入口）。
    ///   （原版 Brand / 保暖手套 / 暴政之力 消耗用 ExhaustSelectionPrompt；熵 / 离去 用 TransformSelectionPrompt）
    /// - source 类型名：含复制语义关键词 → Copy（原版 DualWield 复制攻击/能力牌，标题是自定义的）；
    ///   含 Exhaust → Remove。
    /// 未知 → Unknown（维持既有 cardPickMode 策略，不越权）。
    /// prefsLocKey 建议传 "LocTable/LocEntryKey"（如 card_selection/TO_EXHAUST），纯 key 亦可。
    /// </summary>
    public static WakuuPickScenario ClassifyHandScenario(string? sourceTypeName, string? prefsLocKey)
    {
        // 线索 1：prefs 标题 loc key（游戏 CardSelectorPrefs 预设，确定性最高）
        if (!string.IsNullOrEmpty(prefsLocKey))
        {
            if (prefsLocKey.Contains("TO_TRANSFORM", StringComparison.Ordinal))
            {
                return WakuuPickScenario.Transform;
            }

            if (prefsLocKey.Contains("TO_EXHAUST", StringComparison.Ordinal)
                || prefsLocKey.Contains("TO_REMOVE", StringComparison.Ordinal))
            {
                return WakuuPickScenario.Remove;
            }

            if (prefsLocKey.Contains("TO_DISCARD", StringComparison.Ordinal))
            {
                return WakuuPickScenario.Discard;
            }
        }

        // 线索 2：source 类型名（Copy 无专用 loc key，靠来源模型识别；mod 卡按语义关键词推断）
        if (!string.IsNullOrEmpty(sourceTypeName))
        {
            foreach (string keyword in CopySourceTypeNameKeywords)
            {
                if (sourceTypeName.Contains(keyword, StringComparison.Ordinal))
                {
                    return WakuuPickScenario.Copy;
                }
            }

            if (sourceTypeName.Contains("Exhaust", StringComparison.Ordinal))
            {
                return WakuuPickScenario.Remove;
            }
        }

        return WakuuPickScenario.Unknown;
    }

    /// <summary>
    /// 按卡 id 与 CardType 枚举值把卡归入优先级类别。
    /// cardType 传 (int)CardModel.Type；id 传 CardModel.Id.Entry（裸 id，含 STRIKE/DEFEND 等命名）；
    /// isSly 传 CardModel.IsSlyThisTurn（带奇巧标签或被效果临时赋予奇巧，优先级最高优先被弃）。
    /// </summary>
    public static WakuuCardKind ClassifyCard(string? id, int cardType, bool isSly = false)
    {
        if (isSly)
        {
            return WakuuCardKind.Sly;
        }

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
            // 消耗/删除：坏牌优先被删（奇巧是正面牌，不优先消耗）
            WakuuPickScenario.Remove => kind switch
            {
                WakuuCardKind.Curse => 6,
                WakuuCardKind.Status => 5,
                WakuuCardKind.Quest => 4,
                WakuuCardKind.BasicStrike => 3,
                WakuuCardKind.BasicDefend => 2,
                _ => 1,
            },

            // 复制：首选非坏牌（奇巧算好牌）；全坏时按"防御→打击→任务→状态→诅咒"倒序（Defend 权重最高）
            WakuuPickScenario.Copy => kind switch
            {
                WakuuCardKind.Other => 10,
                WakuuCardKind.Sly => 10,
                WakuuCardKind.BasicDefend => 5,
                WakuuCardKind.BasicStrike => 4,
                WakuuCardKind.Quest => 3,
                WakuuCardKind.Status => 2,
                WakuuCardKind.Curse => 1,
                _ => 0,
            },

            // 变化：优先变掉平庸的打击/防御；诅咒/状态/任务/奇巧硬排除（变掉奇巧会失去白嫖机制）
            WakuuPickScenario.Transform => kind switch
            {
                WakuuCardKind.BasicStrike => 3,
                WakuuCardKind.BasicDefend => 2,
                WakuuCardKind.Other => 1,
                _ => ExcludedPriority,
            },

            // 弃牌：奇巧牌丢弃后本回合自动打出（白嫖），优先丢；其次坏牌优先
            WakuuPickScenario.Discard => kind switch
            {
                WakuuCardKind.Sly => 7,
                WakuuCardKind.Curse => 6,
                WakuuCardKind.Status => 5,
                WakuuCardKind.Quest => 4,
                WakuuCardKind.BasicStrike => 3,
                WakuuCardKind.BasicDefend => 2,
                _ => 1,
            },

            _ => 0,
        };
    }
}
