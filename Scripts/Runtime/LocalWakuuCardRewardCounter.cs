using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「本局这一位瓦库已经遇到第几次卡牌奖励」的计数（2026-11-19 抓牌档位）。
///
/// <para>用途只有一个：中卡组档的 **前三次卡牌奖励一律不跳**（开局牌太少时不许托管自己把奖励跳过，
/// 否则牌组会一直薄下去）。</para>
///
/// <para>**按席位记**（多瓦库各自独立计数）；进局 / 退局各复位一次（<see cref="Reset"/>），
/// 与其它进程级静态状态同一口径 —— 绝不跨局残留。</para>
/// </summary>
internal static class LocalWakuuCardRewardCounter
{
    private static readonly Dictionary<ulong, int> _counts = new();

    private static readonly object _lock = new();

    /// <summary>取该席位的「下一次」序号（1 起）。</summary>
    internal static int Next(ulong netId)
    {
        lock (_lock)
        {
            _counts.TryGetValue(netId, out int current);
            int next = current + 1;
            _counts[netId] = next;
            return next;
        }
    }

    /// <summary>当前序号（不递增，仅日志/判读用）。</summary>
    internal static int Peek(ulong netId)
    {
        lock (_lock)
        {
            return _counts.TryGetValue(netId, out int current) ? current : 0;
        }
    }

    internal static void Reset(string reason)
    {
        lock (_lock)
        {
            if (_counts.Count == 0)
            {
                return;
            }

            LocalMultiControlLogger.Info(
                $"卡牌奖励序号已复位: 席位={_counts.Count}, reason={reason}");
            _counts.Clear();
        }
    }
}
