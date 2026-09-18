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
/// 商店里「没有社区评级、只按价格 + 金币保底」的商品候选（遗物 / 药水，Phase 4 增量）。
///
/// 遗物与药水**没有社区统计胜率可查**（社区数据只有卡牌的 PickRate/WinRate），所以不能沿用
/// 买卡那套「胜率门槛」，只能退化为「买得起 + 付完仍保留保底金」。两者决策规则完全一致，
/// 因此共用一个候选形状，差别只在运行层怎么读 id 与价格。
/// </summary>
internal readonly struct WakuuMerchantPricedItem
{
    public WakuuMerchantPricedItem(string itemId, int price, WakuuShopSignal? signal = null)
    {
        ItemId = itemId;
        Price = price;
        Signal = signal;
    }

    /// <summary>商品标识（遗物 / 药水的 Id.Entry），仅用于日志与空值判定。</summary>
    public string ItemId { get; }

    public int Price { get; }

    /// <summary>
    /// 个人统计信号（可选，2026-09-18）：仅当「个人统计决策辅助」开关开启且样本足够时由运行层查好填入；
    /// null = 无个人数据（回退纯价格规则）。判据见 <see cref="WakuuMerchantPicking.IsPersonalStatsVeto"/>。
    /// </summary>
    public WakuuShopSignal? Signal { get; }
}

/// <summary>
/// 瓦库商店自动购买决策纯函数（Phase 4，可行性分析 §9.3）：买卡 / 遗物 / 药水三类。
///
/// 卡牌（<see cref="SelectCardBuys"/>）：
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

    /// <summary>
    /// 遗物 / 药水购买决策（Phase 4 增量，2026-09-18）：**没有评级数据可用**，只做两件事 ——
    /// ① 丢掉读不到 id / 价格异常（≤0，多为 SafeCost 兜底值）的条目；
    /// ② 每件都必须满足「付完这件后仍保留 ≥ goldFloor 金币」。
    ///
    /// 刻意**不做稀有度排序或选择性购买**：遗物/药水当前没有任何可信的"值不值得买"数据源
    /// （社区统计只有卡牌；`maintenance-docs/game-entities.md` 是实体清单，不含评级），
    /// 凭空加权重就是拍脑袋。留给用户的控制手段是各自独立的开关 + 金币保底，
    /// 实机观察后再决定要不要收紧（先量后猜）。
    ///
    /// 与原序保持一致：目录顺序即游戏给的随机顺序，不做重排。
    /// </summary>
    public static List<int> SelectPricedBuys(
        IReadOnlyList<WakuuMerchantPricedItem> items,
        int gold,
        int goldFloor = DefaultGoldFloor,
        long personalMinSample = 0,
        double personalMinGain = 0.0)
    {
        List<int> picks = new();
        if (items == null || items.Count == 0 || gold <= 0)
        {
            return picks;
        }

        int budget = gold;
        for (int i = 0; i < items.Count; i++)
        {
            WakuuMerchantPricedItem item = items[i];
            if (string.IsNullOrWhiteSpace(item.ItemId) || item.Price <= 0)
            {
                continue; // 未上架 / 读不到价格（SafeCost 兜底 int.MaxValue 也会被下面拦掉）
            }

            if (IsPersonalStatsVeto(item.Signal, personalMinSample, personalMinGain))
            {
                continue; // 个人统计负面否决：买过它的局明显更容易输
            }

            if (item.Price > budget - goldFloor)
            {
                // 金币保底：买完这件就跌破保底线，不买。
                // 写成「价格 > 余额 - 保底」而不是「余额 - 价格 < 保底」：价格可能是运行层
                // SafeCost 兜底的 int.MaxValue，减法会溢出（unchecked 下结论虽相同，但别依赖它）。
                continue;
            }

            budget -= item.Price;
            picks.Add(i);
        }

        return picks;
    }

    /// <summary>
    /// **个人统计负面否决**（Phase 4 增量，2026-09-18）：是否因为"买过它反而更容易输"而不买。
    ///
    /// 与卡牌 / 事件的负面信号出局同一套哲学（r48 教训：候选里只有部分"有数据"时，
    /// 那个唯一有数据的候选会自动胜出 —— 哪怕它的信号是负的，等于用一个确切的坏信号
    /// 覆盖中性默认，比不查表更糟）。这里**比卡牌更保守：只做否决、不做主动挑选** ——
    /// 遗物 / 药水既没有选择率、样本也远少于卡牌，"用统计决定该买什么"是过度解读，
    /// "阻止买明显亏的东西"才是它可靠的用途。
    ///
    /// 条件（三条全满足才否决）：① 统计已启用（<paramref name="minSample"/> &gt; 0）；
    /// ② 有信号且"买过它的已结束局数"达到门槛；③ 因果增益低于 <paramref name="minGain"/>（默认 0）。
    /// </summary>
    public static bool IsPersonalStatsVeto(
        WakuuShopSignal? signal,
        long minSample,
        double minGain = 0.0)
    {
        if (minSample <= 0 || !signal.HasValue)
        {
            return false; // 未启用统计 / 无数据 → 不否决（回退纯价格规则）
        }

        WakuuShopSignal value = signal.Value;
        return value.BoughtRuns >= minSample && value.WinRateGain < minGain;
    }
}
