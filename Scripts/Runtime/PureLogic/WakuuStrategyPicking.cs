using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 选牌/选择策略的纯函数（从 LocalWakuuStrategySelector / LocalWakuuEventAutoChoice /
/// LocalWakuuSmithSelector 的逻辑原样搬移，行为零变化）。泛型化后不依赖游戏类型，可直接单测。
/// </summary>
internal static class WakuuStrategyPicking
{
    /// <summary>
    /// 按策略从候选中取前 count 个：first=原序 / last=倒序后取前 count（默认） / random=洗牌后取前 count /
    /// rare=按 rankSelector 降序（稀有度最高优先；同稀有度保持原序，OrderBy 为稳定排序）。
    /// 与运行时一致：未知模式按 last 兜底；rare 未提供 rankSelector 时同样按 last 兜底。
    /// </summary>
    public static List<T> PickByStrategy<T>(IReadOnlyList<T> source, string mode, int count, Random rng, Func<T, int>? rankSelector = null)
    {
        if (count <= 0 || source.Count == 0)
        {
            return new List<T>();
        }

        IEnumerable<T> ordered = mode switch
        {
            WakuuChoiceModes.First => source,
            WakuuChoiceModes.Random => Shuffle(source, rng),
            WakuuChoiceModes.Rare when rankSelector != null => source.OrderByDescending(rankSelector),
            _ => source.Reverse(), // last（默认）：倒序后取前 N = 原序列最后 N 张
        };

        return ordered.Take(count).ToList();
    }

    /// <summary>
    /// 按策略从 0..count-1 中选一个下标：first=0 / last=count-1 / random=随机。
    /// count&lt;=0 返回 -1（无可选项）。
    /// </summary>
    public static int PickIndexByStrategy(int count, string mode, Random rng)
    {
        if (count <= 0)
        {
            return -1;
        }

        return mode switch
        {
            WakuuChoiceModes.Last => count - 1,
            WakuuChoiceModes.Random => rng.Next(count),
            _ => 0, // first（默认）
        };
    }

    /// <summary>
    /// Fisher-Yates 洗牌（返回新列表，不改原集合）。
    /// </summary>
    public static List<T> Shuffle<T>(IReadOnlyList<T> source, Random rng)
    {
        List<T> copy = new(source);
        for (int i = copy.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (copy[i], copy[j]) = (copy[j], copy[i]);
        }

        return copy;
    }

    /// <summary>
    /// 火堆锻造选牌：优先"非打击/防御"类取最后 smithCount 张；不足的槽位用"打击/防御"类
    /// 的最后几张补齐。与 LocalWakuuSmithSelector 原逻辑一致。
    /// </summary>
    public static List<T> PickSmithCards<T>(IReadOnlyList<T> options, int smithCount, Func<T, bool> isBasicStrikeOrDefend)
    {
        List<T> list = options.ToList();
        List<T> preferred = list.Where((c) => !isBasicStrikeOrDefend(c)).ToList();
        List<T> fallback = list.Where(isBasicStrikeOrDefend).ToList();

        // 主选：非打击/防御的最后 N 张；不足的槽位用打击/防御的最后几张补齐
        List<T> selected = preferred.Skip(Math.Max(0, preferred.Count - smithCount)).ToList();
        int remaining = smithCount - selected.Count;
        if (remaining > 0)
        {
            selected.AddRange(fallback.Skip(Math.Max(0, fallback.Count - remaining)));
        }

        return selected;
    }
}
