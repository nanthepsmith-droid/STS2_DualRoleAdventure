using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>变化类选牌的「结果形态」（决定"该优先变掉哪张牌"）。</summary>
internal enum WakuuTransformTargetKind
{
    /// <summary>认不出来（未登记来源，如 mod 卡）：按 <see cref="Random"/> 处理，维持既有顺序。</summary>
    Unknown = 0,

    /// <summary>变成**同角色随机牌**（原版 <c>TransformToRandom</c> / 无参 <c>CardTransformation</c>）。</summary>
    Random = 1,

    /// <summary>变成**指定的一张牌**（与变掉哪张无关，如【离去】固定变成【仆从打击】）。</summary>
    Fixed = 2,
}

/// <summary>
/// 变化类选牌「变掉哪张」的形态判据（2026-11-23 用户登记）。
///
/// <para><b>为什么需要区分</b>：两种变化的经济学完全相反。
/// <list type="bullet">
/// <item><b>随机变化</b>：结果是**原牌自己池子里**的随机牌 —— 变诅咒/状态只会变成**另一个**诅咒/状态
///   （原版 <c>CurseCardPool</c>/<c>StatusCardPool</c> 里没有普通牌，且 <c>GetFilteredTransformationOptions</c>
///   对 Status=8/Curse=9 跳过稀有度过滤）⇒ 等于白花一次变化。所以这时该变**平庸的基础牌**
///   （打击 → 防御 → 过渡牌 → 其余，见 <see cref="WakuuPriorityPicking"/>）。</item>
/// <item><b>固定目标变化</b>：结果与变掉哪张无关 ⇒ 这时该"把最烂的牌换掉"（诅咒 → 状态 → 打击 → 防御 → 其余）。</item>
/// </list></para>
///
/// <para><b>为什么只能按来源登记</b>：选牌那一刻，两种变化给的都是同一个
/// <c>TransformSelectionPrompt</c>（loc key 都是 <c>TO_TRANSFORM</c>），游戏**没有**在选牌前就把
/// "结果是不是固定牌"暴露出来 —— 固定的那张牌是选完之后由调用方 <c>CreateCard&lt;T&gt;()</c> 造的
/// （见 <c>Begone.OnPlay</c>）。所以判据只能落在**是谁在变**（<c>source</c> 模型）上。</para>
///
/// <para>登记表逐条对照反编译源码核过（0.113）：<c>Begone</c>/<c>Charge</c>/<c>Seance</c> 是固定目标，
/// <c>EntropyPower</c> 是随机。未登记的来源一律 <see cref="WakuuTransformTargetKind.Unknown"/>
/// ⇒ 走旧的"打击优先"规则（宁可不变烂牌，也不要白花变化）。</para>
///
/// <para>纯逻辑、不碰游戏类型 ⇒ 可离线单测。</para>
/// </summary>
internal static class WakuuTransformTargetPolicy
{
    /// <summary>已知来源 → 结果形态（KEY = 游戏里 `source` 的**类型简名**，即我们日志里的 `source=`）。</summary>
    private static readonly Dictionary<string, WakuuTransformTargetKind> KnownSources =
        new(StringComparer.Ordinal)
        {
            // 【离去】手牌选 1 张 → 固定变成【仆从打击】（自身已升级则目标也升级）。
            ["Begone"] = WakuuTransformTargetKind.Fixed,
            // 【冲锋】抽牌堆选 2 张 → 固定变成【仆从俯冲弹】。
            ["Charge"] = WakuuTransformTargetKind.Fixed,
            // 【降神】抽牌堆选 N 张 → 固定变成【魂】。
            ["Seance"] = WakuuTransformTargetKind.Fixed,
            // 【熵】能力：回合开始手牌选 N 张 → TransformToRandom（同角色随机）。
            ["EntropyPower"] = WakuuTransformTargetKind.Random,
        };

    /// <summary>解析来源形态；未登记 / 空 ⇒ <see cref="WakuuTransformTargetKind.Unknown"/>。</summary>
    public static WakuuTransformTargetKind Resolve(string? sourceTypeName)
    {
        if (string.IsNullOrWhiteSpace(sourceTypeName))
        {
            return WakuuTransformTargetKind.Unknown;
        }

        return KnownSources.TryGetValue(sourceTypeName!.Trim(), out WakuuTransformTargetKind kind)
            ? kind
            : WakuuTransformTargetKind.Unknown;
    }

    /// <summary>本次变化的结果是不是"指定的一张牌"（未登记来源按 false = 走旧规则）。</summary>
    public static bool IsFixedTarget(string? sourceTypeName)
    {
        return Resolve(sourceTypeName) == WakuuTransformTargetKind.Fixed;
    }
}
