using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 给「临时席位（战神）」在 `ActionQueueSet` 里**补一条本地动作队列**（r266；根因见 references **AE-24**）。
///
/// <para><b>问题</b>：原版 <c>ActionQueueSet</c> 的队列在**开局**按 <c>runState.Players</c> 建
/// （<c>new ActionQueueSet(State.Players)</c>，见 `RunManager`），之后按 <c>ownerId</c> 查：
/// <c>_actionQueues.FirstOrDefault(q =&gt; q.ownerId == playerId)</c>，查不到就抛
/// <c>Tried to get local action queue for nonexistent player with ID …</c>。
/// 战神是**战斗内**才出现的（刻意不进 `runState.Players`，AE-1/AE-3）⇒ 它一旦要入队任何 action
/// （实机是「不死之烟」触发的 `GenericHookGameAction`，日志 `Enqueueing HOOK action with id 0`
/// / `owner 4098`）就会抛，冒穿托管出牌链（2026-10-11 r265 局实测：被 r262 的"出牌异常 ⇒ 本局不让托管打
/// 这张牌"黑名单兜住 = 该牌本局托管不打、真人可手打；**不卡死**，但效果被降级）。</para>
///
/// <para><b>修法（改道，不是跳过）</b>：按 AE-24 的处置口径"改道 &gt; 跳过 &gt; 不管" ——
/// 给它一份**自己的**队列（`ActionQueue` 是 `ActionQueueSet` 的 private 嵌套类，所以用反射建），
/// 这样入队 / 暂停 / 恢复 / 取消这些按 `ownerId` 的路径对它都能正常工作，
/// 而不是把 hook 一丢了之（那会让第三方牌的效果莫名缺失）。</para>
///
/// <para><b>⚠ 关键细节：新队列必须"跟队"现有状态</b> —— `ActionQueue.isCancellingCombatActions` 的
/// **字段初始值是 `true`**（= 取消一切战斗动作），而战斗中所有正式席位的队列都被
/// `SetUpForCombat()` / `CombatStarted()` 置成了 `false`。若直接 new 一条塞进去，
/// 该席位刚入队的 hook 会被 <c>EnqueueWithoutSynchronizing</c> 里的
/// <c>if (queue.isCancellingCombatActions …) gameAction.Cancel()</c> **当场取消**（静默失效）。
/// 所以这里**照抄**任一现存队列的 `isPaused` / `isCancellingCombatActions` /
/// `isCancellingPlayerDrivenCombatActions`；没有任何现存队列时用保守值（取消战斗动作）。</para>
///
/// <para>生命周期：队列本身**不主动摘除** —— 原版 <c>ActionQueueSet.Reset()</c>（战斗结束 / 换局）
/// 会 `_actionQueues.Clear()` 全清；战神在战斗内被摘时只 best-effort
/// `CancelNonExecutingActionsForPlayer`（清掉属于死者的未执行动作）。</para>
/// </summary>
internal static class LocalActionQueueSetBridge
{
    private static FieldInfo? _queuesField;
    private static Type? _queueType;
    private static FieldInfo? _ownerIdField;
    private static FieldInfo? _pausedField;
    private static FieldInfo? _cancellingCombatField;
    private static FieldInfo? _cancellingPlayerDrivenField;
    private static bool _resolved;
    private static bool _resolveFailed;

    /// <summary>"已补队列"日志的席位集合（只为去重；退局复位时清）。</summary>
    private static readonly HashSet<ulong> _logged = new();

    /// <summary>
    /// 确保这个临时席位在 `ActionQueueSet` 里有一条队列（幂等：已有就直接返回 <c>true</c>）。
    /// 返回 <c>false</c> = 没做（判据不成立 / 反编译目标变了 / 反射失败），调用方无需处理。
    /// </summary>
    internal static bool EnsureQueueForTemporarySeat(ulong seatId, string source)
    {
        if (!ForeignSeatGuardPolicy.ShouldProvideActionQueue(
                LocalSelfCoopContext.IsEnabled,
                LocalSelfCoopContext.IsTemporarySeat(seatId),
                seatId))
        {
            return false;
        }

        try
        {
            ActionQueueSet? queueSet = RunManager.Instance?.ActionQueueSet;
            if (queueSet == null || !Resolve())
            {
                return false;
            }

            IList? queues = _queuesField!.GetValue(queueSet) as IList;
            if (queues == null || _queueType == null || _ownerIdField == null)
            {
                return false;
            }

            foreach (object? existing in queues)
            {
                if (existing != null && (ulong)_ownerIdField.GetValue(existing)! == seatId)
                {
                    return true;   // 幂等：已经补过
                }
            }

            object created = Activator.CreateInstance(_queueType, nonPublic: true)!;
            _ownerIdField.SetValue(created, seatId);

            // ★ 跟队现有状态（见类注释：不跟队会让 hook 被静默取消）。
            object? template = queues.Count > 0 ? queues[0] : null;
            if (template != null)
            {
                _pausedField?.SetValue(created, _pausedField.GetValue(template));
                _cancellingCombatField?.SetValue(created, _cancellingCombatField.GetValue(template));
                _cancellingPlayerDrivenField?.SetValue(created, _cancellingPlayerDrivenField.GetValue(template));
            }

            queues.Add(created);

            if (_logged.Add(seatId))
            {
                LocalMultiControlLogger.Info(
                    $"临时席位（战神）已补一条本地动作队列：原版队列在开局按 runState.Players 建，"
                    + $"战神是战斗内才出现的（不在那张表里）⇒ 它入队 hook 会抛 "
                    + $"'Tried to get local action queue for nonexistent player': seat={seatId}, source={source}, "
                    + $"队列总数={queues.Count}, 跟队状态：paused={_pausedField?.GetValue(created)}, "
                    + $"cancellingCombat={_cancellingCombatField?.GetValue(created)}"
                    + "（坑见 references AE-24）");
            }

            return true;
        }
        catch (Exception exception)
        {
            _resolveFailed = true;
            LocalMultiControlLogger.Warn(
                $"给临时席位补本地动作队列失败（已忽略；原版按 ownerId 查队列的路径仍会抛）: "
                + $"seat={seatId}, error={exception.Message}");
            return false;
        }
    }

    /// <summary>
    /// 战神从战斗里被摘掉时的收尾（best-effort）：取消它队列里**尚未执行**的动作。
    /// 队列本体交给原版 `ActionQueueSet.Reset()`（战斗结束 / 换局）统一清空。
    /// </summary>
    internal static void CancelPendingForTemporarySeat(ulong seatId)
    {
        if (seatId == 0UL)
        {
            return;
        }

        try
        {
            RunManager.Instance?.ActionQueueSet?.CancelNonExecutingActionsForPlayer(seatId);
        }
        catch (Exception exception)
        {
            // 队列不存在（没补过 / 已被 Reset 清）⇒ 这里会抛，属正常，只记 DEBUG 级信息量足够。
            LocalMultiControlLogger.Info(
                $"战神动作队列收尾跳过（席位已无队列，通常=没补过或战斗已复位）: seat={seatId}, {exception.Message}");
        }
    }

    /// <summary>退局 / 战斗结束复位时清日志去重标记（与临时席位生命周期同进同退）。</summary>
    internal static void ResetLogMarkers() => _logged.Clear();

    /// <summary>
    /// 启动自检（`Entry` 初始化时调一次）：把反射面解析出来并打一条日志。
    /// 反射目标（`_actionQueues` / 嵌套 `ActionQueue` / 字段名）是**编译期看不出来的运行期目标** ——
    /// 游戏更新改名后只会表现为"补队列静默不做"，所以要在启动时就能看见（同仓库其它自检的做法）。
    /// </summary>
    internal static void SelfCheck()
    {
        bool ok = Resolve();
        if (ok)
        {
            LocalMultiControlLogger.Info(
                "动作队列反射面已就绪（临时席位可补队列）：ActionQueueSet._actionQueues + 嵌套 ActionQueue"
                + $"（字段 ownerId/isPaused/isCancellingCombatActions/isCancellingPlayerDrivenCombatActions = "
                + $"{_ownerIdField != null}/"
                + $"{_pausedField != null}/"
                + $"{_cancellingCombatField != null}/"
                + $"{_cancellingPlayerDrivenField != null}）");
        }
    }

    private static bool Resolve()
    {
        if (_resolved)
        {
            return !_resolveFailed;
        }

        _resolved = true;
        try
        {
            Type type = typeof(ActionQueueSet);
            _queuesField = AccessTools.Field(type, "_actionQueues");
            _queueType = AccessTools.Inner(type, "ActionQueue");
            _ownerIdField = _queueType != null ? AccessTools.Field(_queueType, "ownerId") : null;
            _pausedField = _queueType != null ? AccessTools.Field(_queueType, "isPaused") : null;
            _cancellingCombatField = _queueType != null ? AccessTools.Field(_queueType, "isCancellingCombatActions") : null;
            _cancellingPlayerDrivenField = _queueType != null
                ? AccessTools.Field(_queueType, "isCancellingPlayerDrivenCombatActions")
                : null;

            bool ok = _queuesField != null && _queueType != null && _ownerIdField != null;
            if (!ok)
            {
                _resolveFailed = true;
                LocalMultiControlLogger.Warn(
                    "给临时席位补队列的反射目标没找到（游戏更新后成员名可能变了）："
                    + $"field={_queuesField != null}, innerType={_queueType != null}, ownerId={_ownerIdField != null}");
            }

            return ok;
        }
        catch (Exception exception)
        {
            _resolveFailed = true;
            LocalMultiControlLogger.Warn($"解析 ActionQueueSet 内部结构失败: {exception.Message}");
            return false;
        }
    }
}
