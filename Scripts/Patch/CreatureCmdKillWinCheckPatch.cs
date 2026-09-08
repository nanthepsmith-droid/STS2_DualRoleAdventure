using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
/// 修复（2026-09-05 实测修正）：**不能在 Kill 后缀里等待**。
/// 本补丁跑在击杀牌的 PlayCardAction 执行栈内部（出牌链 → … → CreatureCmd.Kill），
/// 而该 PlayCardAction 必须等 Kill 返回才算执行完 —— 任何「等到当前动作结束」的等待都是自死锁，
/// 判据 `ActionExecutor.CurrentlyRunningAction == null` 在等待窗口内永远不成立。
/// 原实现的实测结果（2026-09-05 双人局）：`检测到敌方已全部死亡` 12 次、等待超时也是 12 次，
/// 100% 跑满轮数上限，白等 600ms 之后又是在动作仍 Executing 时结算 —— 正好撞上本注释开头
/// 想规避的那个竞态，兜底设计完全失效。
///
/// 现在的做法：后缀**不阻塞**。Kill 正常返回 → 击杀牌动作链收尾 → 由 ActionExecutor 在动作跑完后
/// 自行 CheckWinCondition（ActionExecutor.cs:170），主路径零额外延迟。
/// 本补丁只挂一个非阻塞的兜底观察器，覆盖执行器会跳过 CheckWinCondition 的场景
/// （EndPlayerTurnAction / ReadyToBeginEnemyTurnAction，见 ActionExecutor.cs:165），
/// 并且只在「没有任何动作在执行」时才结算，天然避开 Executing 动作被跳过的竞态。
/// 每轮都重新核验「仍在战斗 / 敌全灭」，敌人被钩子复活或战斗已结束时立即收工，绝不无限等待。
/// </summary>
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Kill), new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) })]
internal static class CreatureCmdKillWinCheckPatch
{
    /// <summary>
    /// 兜底观察器的轮数上限与间隔（25ms×40=1s）。
    /// 这段等待是**非阻塞**的（跑在后台观察器里，不占 Kill 调用栈），主路径上游戏自己会在
    /// 动作跑完的同一帧结算，玩家感知延迟为 0；观察器通常在第一轮就发现「战斗已结算」并收工。
    /// </summary>
    private const int MaxWaitPasses = 40;
    private const int WaitIntervalMs = 25;

    /// <summary>同时只允许一个兜底观察器在跑，避免多杀时重复结算 / 日志刷屏。</summary>
    private static int _pendingWatchers;

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

        // 关键：这里**不等待**。Kill 必须尽快返回，击杀牌的 PlayCardAction 才能跑完，
        // ActionExecutor 才会在其后调用 CheckWinCondition（ActionExecutor.cs:170）自行结算。
        // 在本调用栈内等待「当前动作执行完」是自死锁（详见类注释）。
        if (Interlocked.Increment(ref _pendingWatchers) > 1)
        {
            // 已有观察器在跑，它会重新核验当前状态，无需重复登记
            Interlocked.Decrement(ref _pendingWatchers);
            return;
        }

        LocalMultiControlLogger.Info("检测到敌方已全部死亡，登记延迟胜利结算观察器（不阻塞击杀动作链）。");
        // 显式丢弃：观察器故意不 await（await 就等于回到阻塞的老路），TaskHelper 负责兜异常不打穿游戏
        _ = TaskHelper.RunSafely(WatchForIdleThenWinCheckAsync());
    }

    /// <summary>
    /// 非阻塞兜底观察器：轮询到「战斗仍在进行 + 敌全灭 + 没有任何动作在执行」时补一次
    /// CheckWinCondition。战斗已被游戏自身动作链结算 / 敌人复活 → 立即收工。
    /// </summary>
    private static async Task WatchForIdleThenWinCheckAsync()
    {
        try
        {
            for (int pass = 0; pass < MaxWaitPasses; pass++)
            {
                await Task.Delay(WaitIntervalMs);

                // 战斗已被（游戏自身动作链或其它路径）结算：立即收工
                if (!CombatManager.Instance.IsInProgress || !RunManager.Instance.IsInProgress)
                {
                    return;
                }

                // 敌人被钩子复活：不该结算
                if (AnyPrimaryEnemyAlive())
                {
                    return;
                }

                // 击杀动作链（及其连锁）还在跑：等它跑完，避免在动作 Executing 时翻 NotInCombat
                if (IsAnyCombatActionRunning())
                {
                    continue;
                }

                await TryFinishCombatAsync("击杀动作链已走完（当前无动作在执行）", isWarning: false);
                return;
            }

            // 超时：动作链异常挂起（清道夫场景）。仍兜底结算一次 —— 「全灭必结算」是硬要求，
            // 不能因为等待策略改成非阻塞而退化成「等不到就不结算」。
            await TryFinishCombatAsync("等待击杀动作链空闲超时（轮数上限），按旧行为兜底结算", isWarning: true);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingWatchers);
        }
    }

    /// <summary>复核战斗仍在进行、仍是玩家侧、敌人已全灭，然后触发一次胜利结算。</summary>
    private static async Task TryFinishCombatAsync(string reason, bool isWarning)
    {
        if (!CombatManager.Instance.IsInProgress || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        CombatState? state = CombatManager.Instance.DebugOnlyGetState();
        if (state == null
            || state.CurrentSide != CombatSide.Player
            || state.Enemies.Any((enemy) => enemy != null && enemy.IsAlive && enemy.IsPrimaryEnemy))
        {
            return;
        }

        string message = $"{reason}，触发战斗胜利结算。";
        if (isWarning)
        {
            LocalMultiControlLogger.Warn(message);
        }
        else
        {
            LocalMultiControlLogger.Info(message);
        }

        await CombatManager.Instance.CheckWinCondition();
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
