using System;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「变化成指定一张牌 ⇒ 按消耗牌处理」的登记处（运行层，游戏类型入口）。
///
/// <para>规则与判据见纯逻辑 <see cref="WakuuTransformReplacementSet"/>；本类只负责
/// 「拿 <c>CardModel</c> 实例去查 / 记」+ 打锚点，线程安全由内部锁保证
/// （写入点在 Harmony 补丁里，读点在瓦库评分/知识层抽取里，可能落在不同线程的不同阶段）。</para>
///
/// <para>登记点是 <c>WakuuTransformReplacementRecordPatch</c>：<c>CardCmd.Transform</c> 前缀里
/// 对 <c>CardTransformation.Replacement != null</c>（= 指定目标变化）的那张**结果牌实例**登记。
/// 原版数据阶段把它原样放回牌堆，所以手牌里那张就是同一个对象（蛋类遗物 clone 升级的那条不覆盖，
/// 见补丁类注释）。</para>
/// </summary>
internal static class WakuuTransformReplacementRegistry
{
    private static readonly WakuuTransformReplacementSet FixedResults = new();
    private static readonly object Gate = new();

    /// <summary>本局在册条数（诊断 / 日志用）。</summary>
    public static int Count
    {
        get
        {
            lock (Gate)
            {
                return FixedResults.Count;
            }
        }
    }

    /// <summary>
    /// 出牌评分 / 知识层的唯一判据：这张牌是不是「变化成指定一张牌」的结果。
    /// </summary>
    public static bool IsFixedReplacementResult(CardModel? card)
    {
        if (card == null)
        {
            return false;
        }

        lock (Gate)
        {
            return FixedResults.Contains(card);
        }
    }

    /// <summary>登记一张结果牌实例（幂等）。返回 <c>true</c> 表示首次登记。</summary>
    public static bool TryRegister(CardModel? card)
    {
        if (card == null)
        {
            return false;
        }

        bool added;
        int total;
        lock (Gate)
        {
            added = FixedResults.Add(card);
            total = FixedResults.Count;
        }

        if (added)
        {
            // 实机锚点：这张牌从此按消耗牌评分（-5），也解释了"为什么托管把它放到最后才打"。
            LocalMultiControlLogger.Info(
                $"[变化牌] 变化成指定目标 ⇒ 按消耗牌处理（出牌评分 -5）: card={Describe(card)}, 本局累计={total}");
        }

        return added;
    }

    /// <summary>退局 / 换局复位。返回清掉的条数。</summary>
    public static int Clear(string reason)
    {
        int cleared;
        lock (Gate)
        {
            cleared = FixedResults.Clear();
        }

        if (cleared > 0)
        {
            LocalMultiControlLogger.Info($"[变化牌] 登记表已复位: 清掉 {cleared} 条, reason={reason}");
        }

        return cleared;
    }

    private static string Describe(CardModel card)
    {
        try
        {
            string id = card.Id.Entry;
            string owner = card.Owner == null ? "?" : card.Owner.NetId.ToString();
            return $"{id}{(card.IsUpgraded ? "+" : string.Empty)}(owner={owner})";
        }
        catch (Exception)
        {
            return "(未知)";
        }
    }
}
