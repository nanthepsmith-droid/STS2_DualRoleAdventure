using System;
using System.Collections.Generic;
using System.Text;

namespace LocalMultiControl.Scripts.Runtime;

// ============================================================
// 自有统计角标/悬停弹窗的数据聚合层（Phase 4 增量，marker r69）。
// 只读个人偏好记录器（LocalPersonalRecorder 的 PersonalStore），与皮皮军师/SkadaHelper
// 社区统计**完全独立**：本层数据 = 真人自己打出来的记录，不读第三方任何数据。
// 设计原则：本文件不依赖 Godot / 游戏类型，纯数据 + 基础类型，可直接单测。
// ============================================================

/// <summary>一对「次数 → 比例」：卡牌抓取率 / 事件选择率共用。</summary>
internal readonly struct StatRatePair
{
    public StatRatePair(long offered, long picked)
    {
        Offered = offered;
        Picked = picked;
    }

    public long Offered { get; }

    public long Picked { get; }

    /// <summary>比例（0~1）；分母为 0 时为 0。</summary>
    public double Rate => Offered > 0 ? (double)Picked / Offered : 0.0;

    /// <summary>角标/展示用整百分比文本（如 "47%"；无样本为 "-"）。</summary>
    public string PercentText => Offered > 0 ? WakuuStatBadgeFormat.RateToPercent(Rate) : "-";
}

/// <summary>一对「局数 → 胜率」：拿了/选了 vs 没拿/没选 的胜负归因。</summary>
internal readonly struct StatWinPair
{
    public StatWinPair(long runs, long wins)
    {
        Runs = runs;
        Wins = wins;
    }

    public long Runs { get; }

    public long Wins { get; }

    public double Rate => Runs > 0 ? (double)Wins / Runs : 0.0;

    public string PercentText => Runs > 0 ? WakuuStatBadgeFormat.RateToPercent(Rate) : "-";
}

/// <summary>某一幕（act）的卡牌首抓/重复统计。</summary>
internal readonly struct CardActStat
{
    public CardActStat(int act, StatRatePair first, StatRatePair repeat)
    {
        Act = act;
        First = first;
        Repeat = repeat;
    }

    public int Act { get; }

    public StatRatePair First { get; }

    public StatRatePair Repeat { get; }

    public bool HasData => First.Offered > 0 || Repeat.Offered > 0;
}

/// <summary>卡牌统计角标/弹窗数据（1~3 幕分首/重 + 整体胜率）。</summary>
internal readonly struct CardStatBadge
{
    public CardStatBadge(
        StatRatePair total,
        StatWinPair held,
        StatWinPair skipped,
        IReadOnlyList<CardActStat> byAct)
    {
        Total = total;
        Held = held;
        Skipped = skipped;
        ByAct = byAct;
    }

    /// <summary>总抓取率（不分幕/首重），右下角角标显示它。</summary>
    public StatRatePair Total { get; }

    /// <summary>拿了该卡的局 → 胜率。</summary>
    public StatWinPair Held { get; }

    /// <summary>展示了但整局没拿的局 → 胜率。</summary>
    public StatWinPair Skipped { get; }

    public IReadOnlyList<CardActStat> ByAct { get; }

    public bool HasData => Total.Offered > 0;
}

/// <summary>某一幕（act）的事件选项选择统计。</summary>
internal readonly struct EventActStat
{
    public EventActStat(int act, StatRatePair chosen)
    {
        Act = act;
        Chosen = chosen;
    }

    public int Act { get; }

    public StatRatePair Chosen { get; }

    public bool HasData => Chosen.Offered > 0;
}

/// <summary>事件选项统计角标/弹窗数据。</summary>
internal readonly struct EventStatBadge
{
    public EventStatBadge(
        StatRatePair total,
        StatWinPair held,
        StatWinPair skipped,
        IReadOnlyList<EventActStat> byAct)
    {
        Total = total;
        Held = held;
        Skipped = skipped;
        ByAct = byAct;
    }

    /// <summary>总选择率（= 本选项被点中次数 ÷ 本页作为可选项出现的次数），右下角角标显示它。</summary>
    public StatRatePair Total { get; }

    public StatWinPair Held { get; }

    public StatWinPair Skipped { get; }

    public IReadOnlyList<EventActStat> ByAct { get; }

    public bool HasData => Total.Offered > 0;
}

/// <summary>文本格式化（纯函数，供 UI 与单测共用）。</summary>
internal static class WakuuStatBadgeFormat
{
    public static string RateToPercent(double rate)
    {
        return $"{(int)Math.Round(rate * 100.0)}%";
    }

    /// <summary>卡牌悬停弹窗正文（多行）。act 无数据行省略。</summary>
    public static string FormatCardTipBody(CardStatBadge badge, string scopeLabel)
    {
        StringBuilder sb = new();
        sb.Append("我的统计").Append(scopeLabel).Append('\n');
        sb.Append("总抓取率 ").Append(badge.Total.Picked).Append('/').Append(badge.Total.Offered)
          .Append(' ').Append(badge.Total.PercentText).Append('\n');
        foreach (CardActStat act in badge.ByAct)
        {
            if (!act.HasData)
            {
                continue;
            }

            sb.Append("第").Append(act.Act).Append("幕 ");
            sb.Append("首抓 ").Append(act.First.Picked).Append('/').Append(act.First.Offered)
              .Append(' ').Append(act.First.PercentText);
            if (act.Repeat.Offered > 0)
            {
                sb.Append("  重复 ").Append(act.Repeat.Picked).Append('/').Append(act.Repeat.Offered)
                  .Append(' ').Append(act.Repeat.PercentText);
            }

            sb.Append('\n');
        }

        AppendWinLine(sb, "拿了", badge.Held, "没拿", badge.Skipped);
        return sb.ToString().TrimEnd();
    }

    /// <summary>事件选项悬停弹窗正文（多行）。</summary>
    public static string FormatEventTipBody(EventStatBadge badge, string scopeLabel)
    {
        StringBuilder sb = new();
        sb.Append("我的统计").Append(scopeLabel).Append('\n');
        sb.Append("选择率 ").Append(badge.Total.Picked).Append('/').Append(badge.Total.Offered)
          .Append(' ').Append(badge.Total.PercentText).Append('\n');
        foreach (EventActStat act in badge.ByAct)
        {
            if (!act.HasData)
            {
                continue;
            }

            sb.Append("第").Append(act.Act).Append("幕 ").Append(act.Chosen.Picked).Append('/')
              .Append(act.Chosen.Offered).Append(' ').Append(act.Chosen.PercentText).Append('\n');
        }

        AppendWinLine(sb, "选了", badge.Held, "没选", badge.Skipped);
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 无个人记录时的悬停正文（角标显示 0%，悬停这里说明「样本 0」而不是什么都不弹）。
    /// </summary>
    public static string FormatEmptyTip(string scopeLabel, string targetLabel)
    {
        return "我的统计" + scopeLabel + "\n暂无" + targetLabel + "的个人记录（样本 0）："
               + "这张/这个还没在你打完的局里出现过。";
    }

    /// <summary>
    /// 社区（皮皮军师 / SkadaHelper）数据补足时的悬停正文。
    /// 用于「个人无记录 + 开启社区兜底」的场景：角标显示社区抓取率，这里标注数据来源，
    /// 不与个人统计同时显示两套数字。rate 均为 0~1。
    /// </summary>
    public static string FormatCommunityCardBody(
        string scopeLabel,
        double pickRate,
        double winRateHeld,
        double winRateSkipped,
        long sample)
    {
        StringBuilder sb = new();
        sb.Append("统计").Append(scopeLabel).Append("（来源：社区·皮皮军师）\n");
        sb.Append("抓取率 ").Append(RateToPercent(pickRate)).Append("  样本 ").Append(sample).Append('\n');
        sb.Append("拿了胜率 ").Append(RateToPercent(winRateHeld))
          .Append("  没拿 ").Append(RateToPercent(winRateSkipped));
        return sb.ToString();
    }

    /// <summary>0~1 → 整百分比文本（社区数据用；与 StatRatePair.PercentText 同格式）。</summary>
    public static string RateToPercentText(double rate)
    {
        return RateToPercent(rate);
    }

    /// <summary>
    /// 「个人 + 社区」融合档的悬停正文：一个融合百分比 + 个人/社区两侧明细。
    /// </summary>
    public static string FormatBlendedCardBody(
        CardStatBadge badge,
        string scopeLabel,
        double communityPickRate,
        long communitySample,
        double blendedRate)
    {
        StringBuilder sb = new();
        sb.Append("统计").Append(scopeLabel).Append("（个人 + 社区·皮皮军师 融合）\n");
        sb.Append("融合抓取率 ").Append(RateToPercent(blendedRate)).Append('\n');
        sb.Append("个人 ").Append(badge.Total.Picked).Append('/').Append(badge.Total.Offered)
          .Append(' ').Append(badge.Total.PercentText).Append('\n');
        sb.Append("社区 抓取率 ").Append(RateToPercent(communityPickRate))
          .Append("  样本 ").Append(communitySample).Append('\n');
        foreach (CardActStat act in badge.ByAct)
        {
            if (!act.HasData)
            {
                continue;
            }

            sb.Append("第").Append(act.Act).Append("幕 ");
            sb.Append("首抓 ").Append(act.First.Picked).Append('/').Append(act.First.Offered)
              .Append(' ').Append(act.First.PercentText);
            if (act.Repeat.Offered > 0)
            {
                sb.Append("  重复 ").Append(act.Repeat.Picked).Append('/').Append(act.Repeat.Offered)
                  .Append(' ').Append(act.Repeat.PercentText);
            }

            sb.Append('\n');
        }

        AppendWinLine(sb, "拿了", badge.Held, "没拿", badge.Skipped);
        return sb.ToString().TrimEnd();
    }

    private static void AppendWinLine(StringBuilder sb, string heldLabel, StatWinPair held, string skippedLabel, StatWinPair skipped)
    {
        sb.Append(heldLabel).Append("胜率 ").Append(held.Wins).Append('/').Append(held.Runs)
          .Append(' ').Append(held.PercentText);
        if (skipped.Runs > 0)
        {
            sb.Append("  ").Append(skippedLabel).Append(' ').Append(skipped.Wins).Append('/')
              .Append(skipped.Runs).Append(' ').Append(skipped.PercentText);
        }
    }
}

/// <summary>
/// 统计角标聚合查询（读 PersonalStore，全部只统计「已结束且非 abandon」的局，口径与
/// WakuuPersonalQuery 完全一致）。mode/角色不做过滤之外的分片；角色取全部（本地双人同屏，
/// 角标是给在场真人看的共同参考）。
/// </summary>
internal static class WakuuStatBadgeQuery
{
    private const int MaxAct = 3;

    /// <summary>
    /// 融合档的「伪计数」强度：社区抓取率折算成这么多次虚拟 offer 与个人实际次数一起算，
    /// 个人样本越多越主导（个人 5 次以上即与社区平手以上）。取值越小越信个人。
    /// </summary>
    public const double DefaultBlendPriorStrength = 5.0;

    /// <summary>
    /// 个人 offer/pick 与社区抓取率的融合（0~1）。社区无数据或样本为 0 时退化为纯个人抓取率。
    /// </summary>
    public static double BlendPickRate(
        long picked,
        long offered,
        double? communityPickRate,
        long communitySample,
        double priorStrength = DefaultBlendPriorStrength)
    {
        if (communityPickRate == null || communitySample <= 0)
        {
            return offered > 0 ? (double)picked / offered : 0.0;
        }

        double strength = Math.Max(0.0, priorStrength);
        return (picked + strength * communityPickRate.Value) / (offered + strength);
    }

    /// <summary>
    /// 聚合某张卡的角标数据。cardId 需为卡牌裸 id（CardModel.Id.Entry，大小写不敏感）。
    /// </summary>
    public static CardStatBadge BuildCard(PersonalStore store, string cardId, bool? isMulti = null)
    {
        if (store == null || string.IsNullOrEmpty(cardId))
        {
            return new CardStatBadge(new StatRatePair(0, 0), new StatWinPair(0, 0), new StatWinPair(0, 0), Array.Empty<CardActStat>());
        }

        PersonalCardSlice total = WakuuPersonalQuery.CountCardOffers(store, cardId, isMulti: isMulti);
        PersonalWinSlice win = WakuuPersonalQuery.CountCardWinSlice(store, cardId, isMulti: isMulti);

        List<CardActStat> byAct = new();
        for (int act = 1; act <= MaxAct; act++)
        {
            PersonalCardSlice first = WakuuPersonalQuery.CountCardOffers(
                store, cardId, isMulti: isMulti, act: act, isRepeat: false);
            PersonalCardSlice repeat = WakuuPersonalQuery.CountCardOffers(
                store, cardId, isMulti: isMulti, act: act, isRepeat: true);
            CardActStat row = new(act,
                new StatRatePair(first.Offered, first.Picked),
                new StatRatePair(repeat.Offered, repeat.Picked));
            if (row.HasData)
            {
                byAct.Add(row);
            }
        }

        return new CardStatBadge(
            new StatRatePair(total.Offered, total.Picked),
            new StatWinPair(win.HeldRuns, win.HeldWins),
            new StatWinPair(win.SkippedRuns, win.SkippedWins),
            byAct);
    }

    /// <summary>
    /// 聚合某事件选项的角标数据。eventId = EventModel.Id.Entry（大写），optionKey = EventOption.TextKey（稳定 loc 键）。
    /// </summary>
    public static EventStatBadge BuildEvent(PersonalStore store, string eventId, string optionKey, bool? isMulti = null)
    {
        if (store == null || string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(optionKey))
        {
            return new EventStatBadge(new StatRatePair(0, 0), new StatWinPair(0, 0), new StatWinPair(0, 0), Array.Empty<EventActStat>());
        }

        PersonalEventSlice total = WakuuPersonalQuery.CountEventOptionSlice(store, eventId, optionKey, isMulti: isMulti);
        PersonalWinSlice win = WakuuPersonalQuery.CountEventWinSlice(store, eventId, optionKey, isMulti: isMulti);

        List<EventActStat> byAct = new();
        for (int act = 1; act <= MaxAct; act++)
        {
            PersonalEventSlice slice = WakuuPersonalQuery.CountEventOptionSlice(
                store, eventId, optionKey, isMulti: isMulti, act: act);
            EventActStat row = new(act, new StatRatePair(slice.Offered, slice.Chosen));
            if (row.HasData)
            {
                byAct.Add(row);
            }
        }

        return new EventStatBadge(
            new StatRatePair(total.Offered, total.Chosen),
            new StatWinPair(win.HeldRuns, win.HeldWins),
            new StatWinPair(win.SkippedRuns, win.SkippedWins),
            byAct);
    }
}

/// <summary>
/// 统计角标的数据来源档位（用户 2026-09-07 拍板三档）。
/// 皮皮军师（SkadaHelper 轻量版）自己已不在卡上画统计，只提供数据接口，
/// 因此由本 mod 决定「怎么用社区数据」，且始终**只显示一个百分比**。
/// </summary>
internal static class WakuuStatBadgeSource
{
    /// <summary>仅个人记录（默认）：完全用自己打出来的统计。</summary>
    public const string PersonalOnly = "personalOnly";

    /// <summary>个人优先 + 社区兜底：该卡没有个人记录时才用社区抓取率补足。</summary>
    public const string PersonalThenCommunity = "personalThenCommunity";

    /// <summary>融合：个人与社区按「伪计数」加权合为一个抓取率（个人样本越多越主导）。</summary>
    public const string Blended = "blended";

    public const string Default = PersonalOnly;

    public static string Normalize(string? value)
    {
        return value switch
        {
            PersonalThenCommunity => PersonalThenCommunity,
            Blended => Blended,
            _ => PersonalOnly,
        };
    }
}

/// <summary>
/// 统计角标在目标矩形内的位置档位。
/// 默认 **左下**：皮皮军师（SkadaHelper）会自己把社区统计标签画在卡的右侧
/// （其 dll 内有 StatsLabelWidth / GetStatsLabelX / StatsRightInset，且 patch 了
/// NCardRewardSelectionScreen 与 NGridCardHolder），默认右下会与它重叠并被我们的
/// 顶层 overlay 盖住。用户可在设置页切换四档以避开/对齐其它 mod 的叠加。
/// </summary>
internal static class WakuuStatBadgeCorner
{
    public const string BottomRight = "bottomRight";

    public const string BottomLeft = "bottomLeft";

    public const string TopRight = "topRight";

    public const string TopLeft = "topLeft";

    public const string Default = BottomLeft;

    public static string Normalize(string? value)
    {
        return value switch
        {
            BottomRight => BottomRight,
            TopRight => TopRight,
            TopLeft => TopLeft,
            _ => BottomLeft,
        };
    }
}

/// <summary>角标摆位解算结果（锚点系数 + 像素偏移，供 UI 计算最终坐标）。</summary>
internal readonly struct StatBadgePlacement
{
    public StatBadgePlacement(float anchorX, float anchorY, float offsetX, float offsetY)
    {
        AnchorX = anchorX;
        AnchorY = anchorY;
        OffsetX = offsetX;
        OffsetY = offsetY;
    }

    public float AnchorX { get; }

    public float AnchorY { get; }

    public float OffsetX { get; }

    public float OffsetY { get; }
}

/// <summary>角标位置纯函数（可单测，无 Godot 依赖）。</summary>
internal static class WakuuStatBadgeLayout
{
    public static StatBadgePlacement Resolve(
        string corner,
        float width,
        float height,
        float gapX = 8f,
        float gapY = 5f)
    {
        switch (WakuuStatBadgeCorner.Normalize(corner))
        {
            case WakuuStatBadgeCorner.TopLeft:
                return new StatBadgePlacement(0f, 0f, gapX, gapY);
            case WakuuStatBadgeCorner.TopRight:
                return new StatBadgePlacement(1f, 0f, -(gapX + width), gapY);
            case WakuuStatBadgeCorner.BottomRight:
                return new StatBadgePlacement(1f, 1f, -(gapX + width), -(gapY + height));
            default:
                return new StatBadgePlacement(0f, 1f, gapX, -(gapY + height));
        }
    }
}
