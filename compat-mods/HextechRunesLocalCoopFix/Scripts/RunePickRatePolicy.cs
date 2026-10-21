using System;
using System.Collections.Generic;
using System.Text.Json;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>单个符文的抓率账本：出现过几次（offered）、被选中几次（picked）。</summary>
internal sealed class RunePickStat
{
    public int Offered { get; set; }

    public int Picked { get; set; }
}

/// <summary>统计文件（`%APPDATA%\SlayTheSpire2\hextech_rune_stats.json`）的 DTO。</summary>
internal sealed class RunePickStatsData
{
    public int Version { get; set; } = 1;

    public Dictionary<string, RunePickStat> Runes { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>
/// 「符文按抓率挑」的**纯逻辑**（无 Godot / 游戏类型依赖 ⇒ 可以单测）。
///
/// <para><b>抓率</b> = <c>picked / offered</c>（这个符文在候选里出现几次、其中被选了几次），
/// 用 Laplace 平滑 <c>(picked + 1) / (offered + 2)</c>：没见过的符文 = 0.5，
/// **并列时取最左** ⇒ 没有数据时行为与原来"取最左"完全一致（不会因为上了统计就突然乱选）。</para>
///
/// <para><b>谁来记</b>：真人和瓦库的每一屏都记（真人自己的偏好才是"抓率"的信号源）；
/// 瓦库只**读**不写，按抓率从当前 3 个候选里挑最高的那个。
/// ⚠ 自选池屏（锻造选择：一屏给一大堆符文）**不记**——那不是"3 选 1"，记进去会把 offered 冲爆。</para>
/// </summary>
internal static class RunePickRatePolicy
{
    /// <summary>Laplace 先验（分子）。</summary>
    internal const double PriorPicked = 1.0;

    /// <summary>Laplace 先验（分母）：没见过 = (0+1)/(0+2) = 0.5。</summary>
    internal const double PriorOffered = 2.0;

    /// <summary>抓率分数；<paramref name="stat"/> 为 null 视为"没见过"。</summary>
    internal static double Score(RunePickStat? stat)
    {
        int offered = Math.Max(0, stat?.Offered ?? 0);
        int picked = Math.Clamp(stat?.Picked ?? 0, 0, offered);
        return (picked + PriorPicked) / (offered + PriorOffered);
    }

    /// <summary>
    /// 从候选里挑下标：抓率最高者胜；**分数相同取最左**（= 旧行为）。
    /// <paramref name="entries"/> 为空返回 -1。
    /// </summary>
    internal static int ChooseIndex(IReadOnlyList<string> entries, Func<string, RunePickStat?>? lookup)
    {
        if (entries == null || entries.Count == 0)
        {
            return -1;
        }

        int best = 0;
        double bestScore = double.NegativeInfinity;
        for (int i = 0; i < entries.Count; i++)
        {
            RunePickStat? stat = null;
            if (lookup != null)
            {
                try
                {
                    stat = lookup(entries[i]);
                }
                catch
                {
                    stat = null; // 读统计失败 ⇒ 当"没见过"处理，绝不打断作答
                }
            }

            double score = Score(stat);
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                best = i;
            }
        }

        return best;
    }

    /// <summary>记一次"这个符文出现在候选里"。返回新账本（不可变风格，便于单测）。</summary>
    internal static RunePickStat RecordOffered(RunePickStat? stat)
    {
        int offered = Math.Max(0, stat?.Offered ?? 0) + 1;
        int picked = Math.Clamp(stat?.Picked ?? 0, 0, offered);
        return new RunePickStat { Offered = offered, Picked = picked };
    }

    /// <summary>记一次"这个符文被选中"（不增加 offered；picked 不会超过 offered）。</summary>
    internal static RunePickStat RecordPicked(RunePickStat? stat)
    {
        int offered = Math.Max(0, stat?.Offered ?? 0);
        int picked = Math.Clamp(stat?.Picked ?? 0, 0, offered);
        return new RunePickStat
        {
            Offered = offered,
            Picked = Math.Min(offered, picked + 1),
        };
    }

    /// <summary>序列化（缩进便于人工看/手工删条目）。</summary>
    internal static string Serialize(RunePickStatsData data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    /// <summary>
    /// 解析统计文件；**坏数据一律当空账本**（绝不抛，统计坏了只该退回"取最左"）。
    /// </summary>
    internal static RunePickStatsData Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new RunePickStatsData();
        }

        try
        {
            RunePickStatsData? parsed = JsonSerializer.Deserialize<RunePickStatsData>(json);
            if (parsed == null)
            {
                return new RunePickStatsData();
            }

            parsed.Runes ??= new Dictionary<string, RunePickStat>(StringComparer.Ordinal);
            parsed.Version = parsed.Version <= 0 ? 1 : parsed.Version;
            return parsed;
        }
        catch
        {
            return new RunePickStatsData();
        }
    }
}
