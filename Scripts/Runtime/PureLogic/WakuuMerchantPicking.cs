using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>瓦库商店自动买卡的候选（纯数据，供决策与单测断言）。</summary>
internal readonly struct WakuuMerchantCardCandidate
{
    public WakuuMerchantCardCandidate(string cardId, int price, double? winRate)
    {
        CardId = cardId;
        Price = price;
        WinRate = winRate;
    }

    public string CardId { get; }

    public int Price { get; }

    /// <summary>社区统计胜率（无数据/查不到为 null；是否买入由 buyNoData 决定）。</summary>
    public double? WinRate { get; }
}

/// <summary>
/// 瓦库商店自动买卡决策纯函数（Phase 4，可行性分析 §9.3）：
/// - 有社区统计胜率的卡：胜率 ≥ minWinRate 才买；
/// - 无数据（null）的卡：默认跳过；buyNoData=true（shopAssistBuyNoData 开关）时按金币保底买入
///   （2026-09-06 用户拍板：mod 卡大多查不到社区统计，需放开才能让自动买卡生效）；
/// - 每张都必须满足「付完这张后仍保留 ≥ goldFloor 金币」（金币保底）；
/// - 按候选原序逐个评估，买得起的都买（不在这里做金币在多次购买间的复杂重排，够用即可）。
/// 行为全部默认关（shopAssist=false），本函数不依赖任何游戏类型，可直接单测。
/// </summary>
internal static class WakuuMerchantPicking
{
    /// <summary>
    /// 买卡的社区统计胜率门槛（低于不买）。
    /// 2026-09-06 用户实测反馈：绝大多数牌的社区胜率集中在 20%~30%，0.5 门槛过高导致几乎无牌可买，
    /// 故下调到 0.2（0.2~0.5 区间内的大量实用牌都可买，仍能挡掉明显弱势牌）。
    /// </summary>
    public const double DefaultMinBuyWinRate = 0.2;

    /// <summary>购买后必须保留的最低金币（不够不买，防买空导致事件/商店后续没钱）。</summary>
    public const int DefaultGoldFloor = 50;

    /// <summary>
    /// 占位/空卡的 id 判定：游戏内置 Null 卡的 Id.Entry 就是 "NULL"，它在社区统计里有数据
    /// （能被 0.2 门槛选中）但不是真正可打出的牌，买下来纯浪费金币（2026-09-07 实测踩到）。
    /// 这里做纯函数层兜底，运行侧另有类型判定双保险。
    /// </summary>
    public static bool IsPlaceholderCardId(string? cardId)
    {
        return string.IsNullOrWhiteSpace(cardId)
               || string.Equals(cardId, "NULL", StringComparison.OrdinalIgnoreCase);
    }

    public static List<int> SelectCardBuys(
        IReadOnlyList<WakuuMerchantCardCandidate> candidates,
        int gold,
        double minWinRate = DefaultMinBuyWinRate,
        int goldFloor = DefaultGoldFloor,
        bool buyNoData = false)
    {
        List<int> picks = new();
        if (candidates == null || candidates.Count == 0 || gold <= 0)
        {
            return picks;
        }

        int budget = gold;
        for (int i = 0; i < candidates.Count; i++)
        {
            WakuuMerchantCardCandidate candidate = candidates[i];
            if (IsPlaceholderCardId(candidate.CardId))
            {
                continue; // 空 id / 游戏内置 Null 占位卡，不买
            }

            if (!candidate.WinRate.HasValue)
            {
                if (!buyNoData)
                {
                    continue; // 无数据且未放开无数据购买 → 跳过
                }
            }
            else if (candidate.WinRate.Value < minWinRate)
            {
                continue; // 胜率不够
            }

            if (budget - candidate.Price < goldFloor)
            {
                continue; // 金币保底：买完这张就跌破保底线，不买
            }

            budget -= candidate.Price;
            picks.Add(i);
        }

        return picks;
    }
}
