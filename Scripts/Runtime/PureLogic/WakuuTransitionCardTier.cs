using System;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 某张牌的**分幕抓取画像**（一层 vs 二/三层），用于识别「过渡牌」。
///
/// <para>口径来源（用户 2026-11-18，TODO §讨论-2）：
/// 「一层抓取率高但是二、三层抓取率低的牌（根据差值排。这些牌是过渡牌，因此后面删除也很正常）」。
/// 判据的含义是**同一张牌在不同幕的抓取率落差** —— 一层抢着要、后两层没人要，
/// 说明它是撑过前期的过渡牌，进中后期就是该删的。</para>
///
/// <para>纯数据 / 纯函数：只吃"offer / pick 计数"，不碰游戏类型也不读磁盘，便于单测。
/// 样本量门槛刻意设得低（个人记录器的数据本来就稀疏），宁可少识别也不要凭空造信号。</para>
/// </summary>
internal readonly struct WakuuActPickProfile
{
    /// <summary>一层至少要出现这么多次，抓取率才有意义。</summary>
    public const long MinAct1Offered = 3;

    /// <summary>二 + 三层合计至少要出现这么多次（少于此视为"后两层没数据"，不判过渡牌）。</summary>
    public const long MinLaterOffered = 2;

    /// <summary>一层抓取率下限（≥ 50% 才算"一层抢着要"）。</summary>
    public const double MinAct1PickRate = 0.50;

    /// <summary>二 + 三层合计抓取率上限（≤ 35% 才算"后两层没人要"）。</summary>
    public const double MaxLaterPickRate = 0.35;

    /// <summary>一层与后两层的抓取率落差下限（≥ 20 个百分点才算真落差，避免 0.52 vs 0.34 这种擦边）。</summary>
    public const double MinPickRateDrop = 0.20;

    public WakuuActPickProfile(long act1Offered, long act1Picked, long laterOffered, long laterPicked)
    {
        Act1Offered = Math.Max(0, act1Offered);
        Act1Picked = Math.Max(0, act1Picked);
        LaterOffered = Math.Max(0, laterOffered);
        LaterPicked = Math.Max(0, laterPicked);
    }

    public long Act1Offered { get; }

    public long Act1Picked { get; }

    public long LaterOffered { get; }

    public long LaterPicked { get; }

    public double Act1PickRate => Act1Offered > 0 ? (double)Act1Picked / Act1Offered : 0.0;

    public double LaterPickRate => LaterOffered > 0 ? (double)LaterPicked / LaterOffered : 0.0;

    /// <summary>「一层有人要、后两层没人要」且样本足够 ⇒ 过渡牌。</summary>
    public bool IsTransitionCard
        => Act1Offered >= MinAct1Offered
           && LaterOffered >= MinLaterOffered
           && Act1PickRate >= MinAct1PickRate
           && LaterPickRate <= MaxLaterPickRate
           && Act1PickRate - LaterPickRate >= MinPickRateDrop;

    /// <summary>
    /// 删除优先度的次级分值（0 = 不是过渡牌）：**一层与后两层的抓取率落差**，越大越先删。
    /// 只在同一优先级档内的「其余」之间比较（诅咒 / 状态 / 打击 / 防御的硬规则不受影响）。
    /// </summary>
    public double TransitionScore => IsTransitionCard ? Act1PickRate - LaterPickRate : 0.0;
}

/// <summary>
/// 删牌优先级的「过渡牌」档：按个人记录器（分幕 offer/pick）给候选卡算过渡分值。
///
/// <para>为什么只用个人数据：社区统计（SkadaHelper）没有"按幕"切片，只有跨局总抓取率，
/// 算不出"一层高、后两层低"的落差。</para>
///
/// <para>查询口径与其它个人信号完全一致（只计已结束且非 abandon 的局）；
/// character 传 null = 不按角色过滤。</para>
/// </summary>
internal static class WakuuTransitionCardTier
{
    /// <summary>
    /// 某张卡的过渡分值（0 = 不是过渡牌）。任何异常都返回 0（不影响主排序）。
    /// </summary>
    public static double ScoreOf(
        PersonalStore store,
        string? cardId,
        bool? isMulti,
        string? character)
    {
        if (store == null || string.IsNullOrEmpty(cardId))
        {
            return 0.0;
        }

        try
        {
            PersonalCardSlice act1 = WakuuPersonalQuery.CountCardOffers(
                store, cardId, isMulti: isMulti, character: character, act: 1);
            PersonalCardSlice act2 = WakuuPersonalQuery.CountCardOffers(
                store, cardId, isMulti: isMulti, character: character, act: 2);
            PersonalCardSlice act3 = WakuuPersonalQuery.CountCardOffers(
                store, cardId, isMulti: isMulti, character: character, act: 3);

            WakuuActPickProfile profile = new(
                act1Offered: act1.Offered,
                act1Picked: act1.Picked,
                laterOffered: act2.Offered + act3.Offered,
                laterPicked: act2.Picked + act3.Picked);

            return profile.TransitionScore;
        }
        catch (Exception)
        {
            return 0.0;
        }
    }
}
