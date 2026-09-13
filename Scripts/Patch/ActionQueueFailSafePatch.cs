using System;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// `NCardPlayQueue.OnActionEnqueued` 的**前置守卫**（r124）：在会抛空引用之前就干净跳过。
///
/// 原版这个方法把「别人打出的牌」画进出牌队列，两条分支：
/// ① `LocalContext.IsMe(action.Player)` → 取本地手牌 holder（需要 `NPlayerHand.Instance`）；
/// ② 否则 → 取该玩家 creature 节点上的 `PlayerIntentHandler.CardIntent` 当作飞牌起点。
/// 而**本 mod 主动关掉了远端意图 UI**（<see cref="NMultiplayerPlayerIntentHandlerPatch"/> 让
/// `NMultiplayerPlayerIntentHandler.Create` 返回 null）⇒ 分支② **必然**空引用。
///
/// 以前这条只靠本文件的 Finalizer 兜（吞异常 + 强制对齐上下文），代价是每次都要给
/// `ActionQueueSet.ActionEnqueued` 抛一次异常、刷一条 WARN，并无谓地触发一次上下文校正。
/// 方案 D 实验档（`wakuuPlayQueue`）让**瓦库的出牌也走 `PlayCardAction` 入队** ⇒ 这条路径
/// 从"偶发"变成"每次瓦库出牌都可能撞"（实机 2026-09-13 日志 6 条，已按 600ms 限流）。
///
/// 这里改为**先判后跳**：拿不到绘制所需的对象就 `return false` ——
/// 等价于"这张牌不出现在出牌队列里"（与最终被 Finalizer 吞掉的可见结果一致），但没有异常、没有副作用。
/// Finalizer 仍保留，作为最后一道网。
/// </summary>
[HarmonyPatch(typeof(NCardPlayQueue), "OnActionEnqueued")]
internal static class NCardPlayQueueActionEnqueuedGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (action is not PlayCardAction playCardAction)
        {
            return true;
        }

        if (LocalContext.IsMe(playCardAction.Player))
        {
            // 本地分支要取手牌节点；战斗 UI 尚未就绪时原版会空引用。
            return NPlayerHand.Instance != null;
        }

        // 远端分支要玩家 creature 节点上的意图 UI（本 mod 已把它整个关掉 → 必为空）。
        NCreature? creatureNode = NCombatRoom.Instance?.GetCreatureNode(playCardAction.Player.Creature);
        return creatureNode?.PlayerIntentHandler != null;
    }
}

[HarmonyPatch(typeof(NCardPlayQueue), "OnActionEnqueued")]
internal static class NCardPlayQueueOnActionEnqueuedFailSafePatch
{
    private static ulong _lastLogAtMs;

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled || __exception is not NullReferenceException)
        {
            return __exception;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastLogAtMs >= 600)
        {
            _lastLogAtMs = now;
            LocalMultiControlLogger.Warn(
                $"动作队列UI入队触发空引用，已拦截避免阻塞: action={action?.ToString() ?? "null"}, context={LocalContext.NetId?.ToString() ?? "null"}");
        }

        TryRecoverActionOwnerContext();
        return null;
    }

    private static void TryRecoverActionOwnerContext()
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        ulong playerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (playerId == 0)
        {
            return;
        }

        LocalMultiControlRuntime.AlignContextForActionOwner(playerId, "action-queue-ui-failsafe");
    }
}

[HarmonyPatch(typeof(ActionQueueSynchronizer), "RequestEnqueue", new[] { typeof(GameAction) })]
internal static class ActionQueueSynchronizerRequestEnqueueFailSafePatch
{
    private static ulong _lastLogAtMs;

    [HarmonyPrefix]
    private static void Prefix(GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        ActionSynchronizerCombatState syncState = RunManager.Instance.ActionQueueSynchronizer.CombatState;
        if (syncState == ActionSynchronizerCombatState.PlayPhase)
        {
            return;
        }

        if (action is not PlayCardAction && action is not EndPlayerTurnAction)
        {
            return;
        }

        int round = NCombatRoom.Instance?.Ui != null
            ? (AccessTools.Field(typeof(NEndTurnButton), "_combatState")?.GetValue(NCombatRoom.Instance.Ui.EndTurnButton) as CombatState)?.RoundNumber ?? -1
            : -1;
        ulong playerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
        LocalMultiControlRuntime.RecordFlowBlockSignal(
            "deferred_play_detected_during_enemy_turn",
            syncState.ToString(),
            playerId,
            "RequestEnqueue",
            round);
    }

    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception, GameAction action)
    {
        if (!LocalSelfCoopContext.IsEnabled || __exception is not NullReferenceException)
        {
            return __exception;
        }

        ulong now = Time.GetTicksMsec();
        if (now - _lastLogAtMs >= 600)
        {
            _lastLogAtMs = now;
            LocalMultiControlLogger.Warn(
                $"RequestEnqueue 空引用已拦截，避免阻塞: action={action?.ToString() ?? "null"}, context={LocalContext.NetId?.ToString() ?? "null"}");
        }

        ulong playerId = LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (playerId != 0)
        {
            LocalMultiControlRuntime.AlignContextForActionOwner(playerId, "request-enqueue-failsafe");
        }

        return null;
    }
}
