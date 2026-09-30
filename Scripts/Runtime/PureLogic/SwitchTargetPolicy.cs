using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战斗内「切换操控角色」的**目标选择数学**（R4 第二刀：从 `LocalMultiControlRuntime` 提纯）。
///
/// 拆出来的理由：同一段"下标解析 + 环形推进"原先在 Runtime 里有 **3 份**逐字副本
/// （① 单步前后切 ② 环形找"下一个可操作的非瓦库角色" ③ 环形找"下一个手牌可出的角色"），
/// 且全靠在实机里看切人对不对 —— 提成纯逻辑后，"找不到当前席位时退化为从头开始""环形顺序""少于 2 席不出目标"
/// 这些边界都能被单测钉住。
///
/// 口径与原实现**逐字等价**：入参是**已经过滤过的本地席位列表**（调用方先做
/// `LocalSeatSource.IsLocalSeat` 过滤 + `Distinct`，本类不碰身份判定）；"存活 / 未结束回合 / 手牌可出 / 是不是瓦库"
/// 这些需要游戏侧的判据仍留在调用方的循环里（本类只给候选顺序）。
/// </summary>
internal static class SwitchTargetPolicy
{
    /// <summary>
    /// 解析当前席位在列表里的下标：找不到（含空列表）按 **0** —— 与原实现的 `IndexOf` + `&lt; 0 ⇒ 0` 同义。
    /// </summary>
    internal static int ResolveIndex(IReadOnlyList<ulong> orderedSeatIds, ulong currentSeatId)
    {
        if (orderedSeatIds == null || orderedSeatIds.Count == 0)
        {
            return 0;
        }

        for (int i = 0; i < orderedSeatIds.Count; i++)
        {
            if (orderedSeatIds[i] == currentSeatId)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>
    /// 单步目标（"切下一个 / 上一个"）：等价于原实现
    /// `(index + (next ? 1 : count - 1)) % count`。少于 2 席 ⇒ null（原实现的 `Count &lt; 2 ⇒ 直接失败`）。
    /// </summary>
    internal static ulong? SingleStep(IReadOnlyList<ulong> orderedSeatIds, ulong currentSeatId, bool next)
    {
        if (orderedSeatIds == null || orderedSeatIds.Count < 2)
        {
            return null;
        }

        int currentIndex = ResolveIndex(orderedSeatIds, currentSeatId);
        int step = next ? 1 : orderedSeatIds.Count - 1;
        return orderedSeatIds[(currentIndex + step) % orderedSeatIds.Count];
    }

    /// <summary>
    /// 环形候选顺序：从当前席位**之后**开始绕一圈（不含当前席位自身）。
    /// 调用方按此顺序逐个套自己的判据（存活 / 未结束回合 / 可出牌 / 非瓦库），命中即切。
    /// 少于 2 席 ⇒ 空（没有可切的目标）。
    /// </summary>
    internal static IReadOnlyList<ulong> CandidateOrder(IReadOnlyList<ulong> orderedSeatIds, ulong currentSeatId)
    {
        if (orderedSeatIds == null || orderedSeatIds.Count < 2)
        {
            return Array.Empty<ulong>();
        }

        int currentIndex = ResolveIndex(orderedSeatIds, currentSeatId);
        List<ulong> candidates = new List<ulong>(orderedSeatIds.Count - 1);
        for (int offset = 1; offset < orderedSeatIds.Count; offset++)
        {
            candidates.Add(orderedSeatIds[(currentIndex + offset) % orderedSeatIds.Count]);
        }

        return candidates;
    }
}
