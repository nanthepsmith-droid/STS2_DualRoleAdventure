#nullable enable
using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「正在打出的那张牌不该再被当成手牌目标」的候选过滤（纯逻辑，r405）。
///
/// <para><b>症状</b>（2026-11-25 用户实机）：一张牌打出后，因为**别的席位的牌还在飞**，
/// 它的节点暂时停在屏幕中间；随后手牌自愈又给它补了一个手牌节点（看上去"回到了手牌"）。
/// 如果这张牌本身就是「变化手牌里的其它牌」那种效果，选牌候选里还会包含它自己 ⇒ **把自己变化掉**。</para>
///
/// <para><b>判据</b>：候选牌是"正在打出" = 数据可能还在手牌堆，但**节点已经在出牌队列 / 出牌区**
/// （<c>NCombatUi.PlayQueue.GetCardNode</c> / <c>GetCardFromPlayContainer</c> 命中）。
/// 正常时序里原版会把这类牌的**数据**先移进 Play 堆、候选里根本不会出现；这里只是把"数据还没搬走"
/// 的异常窗口补齐（效果语义与"它已经不在手牌了"一致）。</para>
/// </summary>
internal static class WakuuMidPlayCardGuardPolicy
{
    /// <summary>
    /// 从候选里剔掉"正在打出"的牌。<paramref name="isMidPlay"/> 由调用方提供（节点判定在本层之外）。
    /// </summary>
    /// <param name="removed">被剔掉的项（按原顺序），供调用方打日志留证。</param>
    internal static List<T> WithoutMidPlayCards<T>(
        IEnumerable<T>? candidates,
        Func<T, bool> isMidPlay,
        out List<T> removed)
    {
        removed = new List<T>();
        List<T> kept = new List<T>();
        if (candidates == null)
        {
            return kept;
        }

        foreach (T candidate in candidates)
        {
            if (isMidPlay(candidate))
            {
                removed.Add(candidate);
                continue;
            }

            kept.Add(candidate);
        }

        return kept;
    }
}
