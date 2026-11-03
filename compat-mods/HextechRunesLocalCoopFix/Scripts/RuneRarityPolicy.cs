using System;
using System.Collections.Generic;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 「符文稀有度优先」的**纯逻辑**（v1.8.0；无 Godot / 游戏类型依赖 ⇒ 可以单测）。
///
/// <para><b>为什么又捡回稀有度</b>（用户 2026-10-31 新口径，订正 2026-10-11 的判断）：
/// 此前认为「海克斯每次给的 3 个选项稀有度一致」⇒ 删掉了按稀有度挑；实测**是错的**
/// （roll 之后可能出稀有度更高的符文）⇒ 现在的选择顺序 =
/// <b>稀有度更高者优先 → 同档按抓率 → 再并列取最左</b>。</para>
///
/// <para><b>档位</b>与海克斯 <c>HextechRarityTier</c> 同序：银(0) &lt; 金(1) &lt; 棱彩(2)。
/// 读不到稀有度 = <see cref="UnknownRank"/>（-1），<b>永远排在已知档位之后</b>；
/// 若全部读不到（反射面变了）⇒ 全部并列 ⇒ 退化成纯抓率，行为与 v1.7.x 完全一致。</para>
///
/// <para><b>reroll 也要算上「升档机会」</b>（用户 2026-10-31：「判据可能也要把 roll 有机会出更高稀有度算进去」）：
/// 见 <see cref="ChooseUpgradeSlot"/>。</para>
/// </summary>
internal static class RuneRarityPolicy
{
    /// <summary>稀有度读不到（未知）—— 排在所有已知档位之后。</summary>
    internal const int UnknownRank = -1;

    internal const int SilverRank = 0;
    internal const int GoldRank = 1;
    internal const int PrismaticRank = 2;

    /// <summary>日志用的中文档位名。</summary>
    internal static string RankName(int rank)
    {
        return rank switch
        {
            0 => "银",
            1 => "金",
            2 => "棱彩",
            _ => "?",
        };
    }

    /// <summary>日志用：`金, 金, 棱彩`（长度与档位数组一致）。</summary>
    internal static string DescribeRanks(IReadOnlyList<int>? rarityRanks)
    {
        if (rarityRanks == null || rarityRanks.Count == 0)
        {
            return string.Empty;
        }

        var parts = new List<string>(rarityRanks.Count);
        for (int i = 0; i < rarityRanks.Count; i++)
        {
            parts.Add(RankName(rarityRanks[i]));
        }

        return string.Join(", ", parts);
    }

    /// <summary>
    /// 从候选里挑下标：**稀有度最高者胜**；同档比抓率（Laplace 平滑，见 <see cref="RunePickRatePolicy.Score"/>）；
    /// 分数也并列 ⇒ **取最左**。空候选返回 -1。
    ///
    /// <para><paramref name="rarityRanks"/> 与 <paramref name="entries"/> 长度不一致（或为 null）时
    /// **忽略稀有度**，退回 <see cref="RunePickRatePolicy.ChooseIndex"/> —— 绝不能因为读不到稀有度就不选。</para>
    /// </summary>
    internal static int ChooseIndex(
        IReadOnlyList<string>? entries,
        IReadOnlyList<int>? rarityRanks,
        Func<string, RunePickStat?>? lookup)
    {
        if (entries == null || entries.Count == 0)
        {
            return -1;
        }

        if (rarityRanks == null || rarityRanks.Count != entries.Count)
        {
            return RunePickRatePolicy.ChooseIndex(entries, lookup);
        }

        int best = 0;
        int bestRank = int.MinValue;
        double bestScore = double.NegativeInfinity;

        for (int i = 0; i < entries.Count; i++)
        {
            int rank = rarityRanks[i];
            double score = RunePickRatePolicy.Score(SafeLookup(lookup, entries[i]));

            bool better = rank > bestRank || (rank == bestRank && score > bestScore + 1e-9);
            if (better)
            {
                best = i;
                bestRank = rank;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// 该 reroll 哪一槽来**升档**（返回下标；<c>-1</c> = 没有升档收益，交给旧判据）。
    ///
    /// <para>判据：存在某槽 <c>可 reroll 且 reroll 后可达档位 &gt; 当前候选里的最高档位</c> ⇒ 值得 roll
    /// （即使三张都不"烂"）。多个候选时：<b>可达档位最高</b>优先 → 当前档位最低者 → 并列取最左。</para>
    ///
    /// <para>「可达档位」由运行时算（<see cref="RuneRarityReader.TryReadRerollTargets"/>）：
    /// 金色 reroll = 升一档；保底首抽槽 = 本幕档位；其余槽 = 保持自己那一档（普通 reroll 不升档）。
    /// 读不到 ⇒ 填成当前档位 ⇒ 这里自然返回 -1（退化成 v1.7.x 的"全烂才 roll"）。</para>
    /// </summary>
    internal static int ChooseUpgradeSlot(
        IReadOnlyList<int>? currentRanks,
        IReadOnlyList<int>? rerollTargetRanks,
        IReadOnlyList<bool>? canReroll)
    {
        if (currentRanks == null || rerollTargetRanks == null || canReroll == null)
        {
            return -1;
        }

        int count = currentRanks.Count;
        if (count == 0 || rerollTargetRanks.Count != count || canReroll.Count != count)
        {
            return -1;
        }

        int maxCurrent = int.MinValue;
        for (int i = 0; i < count; i++)
        {
            maxCurrent = Math.Max(maxCurrent, currentRanks[i]);
        }

        int best = -1;
        int bestTarget = int.MinValue;
        int bestCurrent = int.MaxValue;

        for (int i = 0; i < count; i++)
        {
            if (!canReroll[i])
            {
                continue;
            }

            int target = rerollTargetRanks[i];
            if (target < 0 || target <= maxCurrent)
            {
                continue; // 没有升档收益（未知 / 达不到本屏最高档）
            }

            int current = currentRanks[i];
            bool better = target > bestTarget || (target == bestTarget && current < bestCurrent);
            if (better)
            {
                best = i;
                bestTarget = target;
                bestCurrent = current;
            }
        }

        return best;
    }

    private static RunePickStat? SafeLookup(Func<string, RunePickStat?>? lookup, string entry)
    {
        if (lookup == null)
        {
            return null;
        }

        try
        {
            return lookup(entry);
        }
        catch
        {
            return null; // 读统计失败 ⇒ 当"没见过"（0.5），绝不打断作答
        }
    }
}
