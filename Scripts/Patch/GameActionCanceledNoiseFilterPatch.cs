#nullable enable
using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Debug;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Logging;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 关掉原版「出牌动作在 <c>Canceled</c> 状态下跑完」那条 ERROR 噪声（r404，2026-11-25 实机判读）。
///
/// <para><b>现象</b>：一局 12 席极速跑下来 24 条
/// <c>[ERROR] GameAction PlayCardAction card: CARD.X (…) … finished execution, but was in state Canceled!</c>，
/// 而此前所有会话都是 0 条（跨会话对比确认是本轮才出现）。</para>
///
/// <para><b>真因（不是我们算错目标）</b>：瓦库走动作队列出牌时，入队前
/// <see cref="WakuuPlayQueuePolicy"/> 已按 <c>CanPlayTargeting</c> 校验过目标；但动作要**排队**，
/// 排队期间那只敌人被别的席位打死 / 移出战斗 ⇒ 执行时原版
/// <c>PlayCardAction.ExecuteAction</c> 里 <c>await CombatState.GetCreatureAsync(TargetId, 10.0)</c> 拿到 null ⇒
/// <c>!IsValidTarget(null)</c> ⇒ 原版自己 <c>Cancel()</c>；<c>GameAction.Execute</c> 的 finally 看到
/// <c>state != Executing</c> 就打这条 ERROR 并上报 Sentry。</para>
///
/// <para><b>为什么可以只过滤日志</b>：这条路径的**行为**已经是正确的"本次不出牌"——
/// 牌留在手牌、<c>CompletionTask</c> 以取消结束（我们的调用点 catch <see cref="OperationCanceledException"/> 后
/// 交看门狗下一轮重选目标）。原版打的是"注意：动作在暂停态被取消"的通用告警，
/// 而我们是**真的取消了**（目标没了），不是"任务在暂停态偷偷跑完"那个 bug。
/// 它唯一的作用是抬高全局 ERROR 计数、污染「异常族期望 0」的判读（同 <see cref="NHandImageCollectionDuplicateAddPatch"/> 的口径）。</para>
///
/// <para><b>过滤范围**只**限</b>：本地多控会话 + <c>PlayCardAction</c> + 该精确措辞。
/// 其它动作类型（药水 / 回合结束 …）的同类告警**原样保留**（那可能是我们没适配的地方）。
/// 每次过滤都用自己的 INFO 留一条证据（带牌名），所以"被压掉的到底是什么"仍然可查。</para>
/// </summary>
[HarmonyPatch]
internal static class GameActionCanceledNoiseFilterPatch
{
    private static readonly HashSet<string> ReportedCards = new(StringComparer.Ordinal);

    private static int _filteredCount;

    /// <summary>原版这句是 `Log.Error`：直接前缀拦掉（含 ERROR 计数与 Sentry 上报都在下面一起处理）。</summary>
    [HarmonyPatch(typeof(Log), nameof(Log.Error), new[] { typeof(string), typeof(int) })]
    [HarmonyPrefix]
    private static bool ErrorPrefix(string text)
    {
        if (!IsCardPlayCanceledText(text))
        {
            return true;
        }

        ReportOnce(text, "Log.Error");
        return false;
    }

    /// <summary>同一处还会 `SentryService.CaptureException(ex)`，异常消息与上面那句完全相同。</summary>
    [HarmonyPatch(typeof(SentryService), nameof(SentryService.CaptureException), new[] { typeof(Exception) })]
    [HarmonyPrefix]
    private static bool CaptureExceptionPrefix(Exception ex)
    {
        if (ex is not InvalidOperationException)
        {
            return true;
        }

        if (!IsCardPlayCanceledText(ex.Message))
        {
            return true;
        }

        ReportOnce(ex.Message, "Sentry");
        return false;
    }

    /// <summary>只认「出牌动作 + 那条精确措辞」（纯逻辑见 policy），且只在本地多控会话里。</summary>
    private static bool IsCardPlayCanceledText(string? text)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        return GameActionCanceledNoisePolicy.ShouldFilter(text);
    }

    /// <summary>整句原样打一条 INFO（同一张牌只打一次，另有总数），保留可追溯性。</summary>
    private static void ReportOnce(string text, string source)
    {
        _filteredCount++;

        string card = GameActionCanceledNoisePolicy.ExtractCardEntry(text);
        bool first = ReportedCards.Add(card);
        if (!first)
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"[出牌取消噪声已过滤] 原版会在排队期间目标阵亡时打 ERROR 并上报 Sentry，"
            + $"这里降级为 INFO（行为不变：该牌留手牌、下一轮重选目标）: source={source}, card={card}, "
            + $"累计={_filteredCount}");
    }

    /// <summary>会话 / 退局复位（静态计数，跨局不能留）。</summary>
    internal static void Reset(string reason)
    {
        if (_filteredCount == 0 && ReportedCards.Count == 0)
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"出牌取消噪声过滤计数已复位: filtered={_filteredCount}, cards={ReportedCards.Count}, reason={reason}");
        _filteredCount = 0;
        ReportedCards.Clear();
    }
}
