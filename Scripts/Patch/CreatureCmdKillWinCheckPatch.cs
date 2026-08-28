using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地多角色下的「全灭立即结算」入口（2026-03 修复全灭不结算）。
///
/// 原实现：CreatureCmd.Kill 的 postfix 在 await 原方法（仅标志敌人死亡）后，立即调用
/// CombatManager.CheckWinCondition() 把战斗提前翻 NotInCombat。
///
/// 竞态根因（2026-08-28 击杀后战斗不结束）：击杀牌（如观者宣泄）的整条 PlayCardAction
/// 执行链此刻仍在 Executing。立即结算会让 CombatManager.EndCombatInternal → SetCombatState(NotInCombat)
/// → ActionQueueSet.CombatEnded() 跳过 Executing 中的动作（"Not cancelling action ... state: Executing"），
/// 战斗上下文/玩家回合状态未正确收尾。本场幸运走完，但同一会话内间歇性出现：随后为玩家生成战后
/// 卡牌奖励时 Character.CardPool / UnlockState 处于被提前终结破坏的异常态，Card pool 为空 →
/// CardFactory 抛 InvalidOperationException → 该玩家卡牌奖励整体中止 → 表象「击杀后战斗不结束」。
///
/// 修复：改为「延迟结算」。立即结算入口只负责发现「最后敌人已死」这一信号；真正的结束由游戏自己的
/// 动作链收尾——ActionExecutor.ExecuteActions 在每条动作执行完后都会调 CheckWinCondition
/// （ActionExecutor.cs:170）。这里用一个有界轮询循环等待当前执行中的击杀动作链走完，再兜底调一次
/// CheckWinCondition（已结算则幂等 no-op）。每轮都重新核验「仍在战斗 / 敌全灭」，敌人被钩子复活或
/// 战斗已结束时立即收工，绝不无限等待。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) })]
internal static class CreatureCmdKillWinCheckPatch
{
    /// <summary>等待击杀动作链走完的轮数上限与间隔。动作链（含连锁/后续动作）通常几帧内完成。</summary>
    private const int MaxWaitPasses = 60;
    private const int WaitIntervalMs = 150;

    [HarmonyPostfix]
    private static void Postfix(IReadOnlyCollection<Creature> creatures, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled || creatures.Count == 0)
        {
            return;
        }

        bool hasEnemy = creatures.Any((creature) => creature != null && creature.IsEnemy);
        if (!hasEnemy)
        {
            return;
        }

        __result = WrapWithDeferredWinCheck(__result);
    }

    private static async Task WrapWithDeferredWinCheck(Task originalTask)
    {
        await originalTask;

        // 原方法（CreatureCmd.Kill）仅标志敌人死亡。战斗可能在等待期间已被其它路径结算/翻转。
        if (!CombatManager.Instance.IsInProgress || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        CombatState? state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null || state.CurrentSide != CombatSide.Player)
        {
            return;
        }

        if (state.Enemies.Any((enemy) => enemy != null && enemy.IsAlive && enemy.IsPrimaryEnemy))
        {
            return;
        }

        LocalMultiControlLogger.Info("检测到敌方已全部死亡，等待当前击杀动作走完再触发战斗胜利结算。");

        // 不在此刻立即 CheckWinCondition（击杀牌动作链仍在 Executing，提前翻 NotInCombat 会破坏
        // 战斗/玩家状态，见类注释）。改为有界轮询：等当前执行中的动作（击杀牌执行链）走完再兜底结算。
        await WaitForRunningActionToSettleAsync();
        if (!CombatManager.Instance.IsInProgress || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        CombatState? stateAfter = CombatManager.Instance.DebugOnlyGetState();
        if (stateAfter == null
            || stateAfter.CurrentSide != CombatSide.Player
            || stateAfter.Enemies.Any((enemy) => enemy != null && enemy.IsAlive && enemy.IsPrimaryEnemy))
        {
            return;
        }

        LocalMultiControlLogger.Info("击杀动作链已走完，触发战斗胜利结算。");
        await CombatManager.Instance.CheckWinCondition();
    }

    /// <summary>
    /// 有界等待：直到所有玩家队列不再有 Executing / 就绪可执行的动作，或战斗已不再进行 / 敌人复活。
    /// 若超过轮数上限仍未清空（例如还有非战斗动作或异常挂起），放弃本次兜底，交给 StaleCombatActionJanitor
    /// 与游戏自身动作链处理，避免无限等待。
    /// </summary>
    private static async Task WaitForRunningActionToSettleAsync()
    {
        for (int pass = 0; pass < MaxWaitPasses; pass++)
        {
            await Task.Delay(WaitIntervalMs);

            // 战斗已被（游戏或其它路径）结算 / 敌人复活：立即收工
            if (!CombatManager.Instance.IsInProgress
                || !RunManager.Instance.IsInProgress
                || AnyPrimaryEnemyAlive())
            {
                return;
            }

            // 没有正在执行的动作 = 击杀动作链（及其连锁）已收尾，可以结算
            if (!IsAnyCombatActionRunning())
            {
                return;
            }
        }

        LocalMultiControlLogger.Warn(
            "等待击杀动作链走完超时（轮数上限），本次兜底结算已放弃，交由游戏自身动作链/清道夫处理。");
    }

    private static bool AnyPrimaryEnemyAlive()
    {
        CombatState? state = CombatManager.Instance.DebugOnlyGetState();
        return state != null
            && state.Enemies.Any((enemy) => enemy != null && enemy.IsAlive && enemy.IsPrimaryEnemy);
    }

    /// <summary>是否有战斗类型动作正在执行中（击杀牌执行链）。优先用 ActionExecutor.CurrentlyRunningAction。</summary>
    private static bool IsAnyCombatActionRunning()
    {
        try
        {
            ActionExecutor executor = RunManager.Instance.ActionExecutor;
            GameAction? running = executor.CurrentlyRunningAction;
            if (running != null)
            {
                return true;
            }

            return false;
        }
        catch
        {
            // 读取失败时保守视为「仍在执行」，下轮再试，避免提前结算
            return true;
        }
    }
}
