using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗外残留战斗动作清道夫（2026-08-26 修复"地图点节点不跟投/不出发"）。
///
/// 根因（实测日志）：击杀最后一人的卡牌动作（如 HEADBUTT）仍在 Executing 时，
/// 本 mod 的立即胜利结算让战斗状态翻转为 NotInCombat；游戏的 CombatEnded 清理
/// 会跳过 Executing 状态的动作（"Not cancelling action ... state: Executing"）；
/// 该动作随后进入"等待玩家选择→恢复执行"流程并换发新 id，但此刻已不在战斗——
/// CombatPlayPhaseOnly 类型的它永远不会再被执行，且卡在队列头部，把后续所有动作
/// （包括地图投票 VoteForMapCoordAction）全部堵死：表现为点节点只入队不执行、
/// 瓦库不跟投、也不自动出发。
///
/// 对策：CombatEnded 后延迟多轮扫描各玩家队列，凡处于非战斗状态仍残留的
/// Combat / CombatPlayPhaseOnly 类型且未在执行中的动作，按游戏自己的清理语义
/// （Cancel + 从队列移除）清掉。新战斗开始立即停止扫描，不影响正常战斗内动作。
/// </summary>
[HarmonyPatch(typeof(ActionQueueSet), nameof(ActionQueueSet.CombatEnded))]
internal static class StaleCombatActionJanitorTriggerPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        StaleCombatActionJanitor.SchedulePurgeAfterCombatEnd();
    }
}

internal static class StaleCombatActionJanitor
{
    /// <summary>清理轮数与间隔：覆盖"击杀动作暂停→玩家选择→恢复换 id"的完整窗口。</summary>
    private const int PassCount = 8;
    private const int PassIntervalMs = 750;

    private static readonly FieldInfo? QueueSetField =
        AccessTools.Field(typeof(ActionQueueSynchronizer), "_actionQueueSet");
    private static readonly FieldInfo? QueuesField =
        AccessTools.Field(typeof(ActionQueueSet), "_actionQueues");

    private static int _purgeLoopsInFlight;

    public static void SchedulePurgeAfterCombatEnd()
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        // 单飞：一场战斗结束只需要一个清理循环；重复触发直接忽略
        if (Interlocked.CompareExchange(ref _purgeLoopsInFlight, 1, 0) != 0)
        {
            return;
        }

        TaskHelper.RunSafely(PurgeLoopAsync());
    }

    private static async Task PurgeLoopAsync()
    {
        try
        {
            for (int pass = 0; pass < PassCount; pass++)
            {
                await Task.Delay(PassIntervalMs);

                // 已开局新战斗 / 不在对局中：立即收工（战斗内的战斗动作是合法的）
                if (!IsSafeToPurge())
                {
                    return;
                }

                bool removed = TryPurgeOnce(out int removedCount);
                if (removed)
                {
                    LocalMultiControlLogger.Warn(
                        $"清道夫已移除战斗外的残留战斗动作: count={removedCount}, pass={pass + 1}/{PassCount}");
                }
                else if (pass >= 2)
                {
                    // 连续几轮都没有残留，提前结束
                    return;
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"残留战斗动作清理异常: {exception.Message}");
        }
        finally
        {
            Interlocked.Decrement(ref _purgeLoopsInFlight);
        }
    }

    private static bool IsSafeToPurge()
    {
        try
        {
            return RunManager.Instance != null
                && RunManager.Instance.IsInProgress
                && !CombatManager.Instance.IsInProgress;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>单轮扫描：移除所有非执行中的战斗类型残留动作。返回是否清理过。</summary>
    private static bool TryPurgeOnce(out int removedCount)
    {
        removedCount = 0;
        ActionQueueSynchronizer synchronizer = RunManager.Instance.ActionQueueSynchronizer;
        object? queueSet = QueueSetField?.GetValue(synchronizer);
        if (queueSet == null)
        {
            return false;
        }

        if (QueuesField?.GetValue(queueSet) is not IEnumerable queues)
        {
            return false;
        }

        foreach (object? queue in queues)
        {
            if (queue == null)
            {
                continue;
            }

            if (AccessTools.Field(queue.GetType(), "actions")?.GetValue(queue) is not IList<GameAction> actions)
            {
                continue;
            }

            for (int i = actions.Count - 1; i >= 0; i--)
            {
                GameAction action = actions[i];
                GameActionType actionType = action.ActionType;
                bool combatTyped = actionType is GameActionType.Combat or GameActionType.CombatPlayPhaseOnly;
                if (!combatTyped || action.State == GameActionState.Executing)
                {
                    continue;
                }

                LocalMultiControlLogger.Info(
                    $"发现战斗外残留动作: {action}, state={action.State}");
                action.Cancel();
                actions.RemoveAt(i);
                removedCount++;
            }
        }

        return removedCount > 0;
    }
}
