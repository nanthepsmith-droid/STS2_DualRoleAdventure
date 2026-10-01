using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「前台 / 归属上下文」的判定与写入（R4 收尾刀：从 <c>LocalMultiControlRuntime</c> 抽出的独立职责）。
///
/// **为什么需要**：本 mod 的"谁是当前操作者"有**三个并行载体** —— 会话受控位（<see cref="LocalMultiSessionState"/>）、
/// 回环上下文（<c>LocalContext.NetId</c> + 回环服务的当前发送者）、Run 级同步器的私有 `_localPlayerId`。
/// 凡是"把归属对齐到某个席位"的入口都必须按同一顺序写这三处；散在 God class 里极易漏写一处，
/// 漏掉的症状是"奖励 / 事件 / 商店认错人"甚至软锁（`SelectLocalReward` 校验 `reward.Player == LocalPlayer` 失败）。
/// 本单元把这类入口收成一处，写入统一走 <see cref="WriteSeatContext"/>。
///
/// **本刀搬走的（行为与日志文案逐字不变）**：
/// <list type="bullet">
/// <item><see cref="AlignContextForActionOwner"/> —— 动作入队前把上下文让给动作主人；</item>
/// <item><see cref="TryEnsureForegroundForPlayer"/> / <see cref="TryEnsureForegroundForPlayerId"/> ——
/// 后台角色触发战斗效果 / 选牌时把前台切过去（含失败回滚）；</item>
/// <item><see cref="AlignLocalContextToForegroundForEndTurn"/> —— 结束回合点击前按前台校正上下文（r104，BUG-2）。</item>
/// </list>
///
/// **刻意留在 Runtime 的**：<c>ApplyControlContext</c>（写上下文 + 战斗 UI / 顶栏 / 牌组 / 房间刷新 + 回滚的**编排**）
/// 与各 <c>Refresh*</c> 呈现刷新 —— 那属于"来源自身 + 编排"；本单元只做判定与写入，
/// <see cref="TryEnsureForegroundForPlayer"/> 通过 <c>LocalMultiControlRuntime.ApplyControlContext</c> 触发刷新链
/// （与 <see cref="LocalSeatSource"/> 回调 Runtime 同一种既有形态）。
///
/// **与 R3 的关系**：席位判定一律走 <see cref="LocalSeatSource"/> 快照（不再裸比 `LocalContext.NetId`）、
/// 同步器写入走 R4 第一刀的 <see cref="RunSynchronizerSeatSync"/> —— 本单元不自带反射、不另造身份判据。
/// </summary>
internal static class ControlContextAlignment
{
    /// <summary>
    /// 把"归属"一次性写到 <paramref name="seatId"/>：回环上下文 + 回环服务当前发送者 + Run 级同步器私有字段。
    ///
    /// **三者必须同进同退**（只写一半 ⇒ 出牌入队到别人的队列、奖励 / 事件 / 商店认错人）；
    /// 写入顺序与原实现逐字一致：先上下文、再发送者、最后同步器。
    /// </summary>
    internal static void WriteSeatContext(ulong seatId)
    {
        LocalContext.NetId = seatId;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(seatId);
        RunSynchronizerSeatSync.Apply(seatId);
    }

    /// <summary>
    /// 动作入队前把上下文让给**动作主人**（r104 起，原 `LocalMultiControlRuntime.AlignContextForActionOwner`）。
    /// 上下文已经对着这一位时只补同步器归属，不重复写上下文。
    /// </summary>
    public static void AlignContextForActionOwner(ulong playerId, string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        // R3：口径统一为「回环上下文是不是这个席位」（不再裸比 LocalContext.NetId）
        if (LocalSeatSource.CurrentSeats().IsContext(playerId))
        {
            RunSynchronizerSeatSync.Apply(playerId);
            return;
        }

        ulong? previousNetId = LocalContext.NetId;
        WriteSeatContext(playerId);

        // 默认档（未开「【实验】瓦库并发出牌」）下瓦库是内联出牌，出牌循环会把 LocalContext.NetId
        // 钉在瓦库自己身上；此时真人中途按牌 / 点结束回合，上下文"漂移"是**预期**的——本来就该让给真人。
        // 记 INFO 即可，别每局刷十几条 WARN 把真问题淹掉（2026-09-13 实机：默认档一局 12 条全是这种）。
        if (previousNetId.HasValue && LocalWakuuRelicRuntime.IsVakuuFormModeById(previousNetId.Value))
        {
            LocalMultiControlLogger.Info(
                $"后台瓦库出牌钉住的上下文已让给真人（默认档预期路径）: "
                + $"{previousNetId.Value} -> {playerId}, source={source}");
            return;
        }

        LocalMultiControlLogger.Warn(
            $"检测到手动出牌上下文漂移，已强制校正: {previousNetId?.ToString() ?? "null"} -> {playerId}, source={source}");
    }

    /// <summary>
    /// 后台角色触发战斗效果 / 选牌时把前台切到它（原 `LocalMultiControlRuntime.TryEnsureForegroundForPlayer`）。
    /// 已有进行中的出牌 / 选牌 / 瞄准流程时**不抢前台**（只记一条 INFO）；
    /// 切完由 <c>ApplyControlContext</c> 刷新呈现，刷新后受控位与上下文仍不是目标则回滚会话控制索引。
    /// </summary>
    public static bool TryEnsureForegroundForPlayer(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        if (player?.Creature == null || player.Creature.CombatState == null)
        {
            return false;
        }

        if (!LocalSeatSource.IsLocalSeat(player.NetId))
        {
            return false;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            LocalMultiControlLogger.Info(
                $"自动切前台延后（当前有进行中的出牌/选牌/瞄准流程）: target={player.NetId}, source={source}");
            return false;
        }

        // R3：`受控位 ?? 上下文 ?? 主席位` 的拼法收进快照口径（ForegroundOrPrimarySeatId）
        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        ulong previousPlayerId = seats.ForegroundOrPrimarySeatId;
        if (previousPlayerId == player.NetId)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"检测到后台角色触发战斗效果/选牌，自动切换前台: from={previousPlayerId}, to={player.NetId}, source={source}");

        if (!LocalMultiControlRuntime.SessionState.TrySetCurrentPlayer(player.NetId))
        {
            return false;
        }

        LocalMultiControlRuntime.ApplyControlContext($"auto-foreground-{source}");

        // ⚠ 这里是"写后读"：ApplyControlContext 刚改过受控位与上下文，快照的权威命中校验会据此重建
        SeatRegistry seatsAfterSwitch = LocalSeatSource.CurrentSeats();
        bool switched = seatsAfterSwitch.IsControlled(player.NetId) && seatsAfterSwitch.IsContext(player.NetId);
        if (!switched)
        {
            LocalMultiControlLogger.Warn(
                $"自动切前台失败，已回滚会话控制索引: target={player.NetId}, source={source}");
            LocalMultiControlRuntime.SessionState.TrySetCurrentPlayer(previousPlayerId);
        }

        return switched;
    }

    /// <summary>
    /// 按玩家ID把前台/控制上下文切到指定角色，适用于只有 NetId、没有现成 Player 引用的挂点
    /// （如 ActionQueueSynchronizer.EnqueueHookAction 入队瞬间，仅有 GenericHookGameAction.OwnerId）。
    /// </summary>
    internal static bool TryEnsureForegroundForPlayerId(ulong playerId, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        // R3：本地席位判定走统一入口（不再裸比席位表）
        if (!LocalSeatSource.IsLocalSeat(playerId))
        {
            return false;
        }

        Player? player = LocalMultiControlRuntime.TryGetCombatPlayer(playerId);
        if (player == null)
        {
            return false;
        }

        return TryEnsureForegroundForPlayer(player, source);
    }

    /// <summary>
    /// 结束回合按钮点击前的归属校正（r104，BUG-2）。
    ///
    /// 原版 <c>NEndTurnButton.CallReleaseLogic</c> 用 <c>LocalContext.GetMe(...)</c> 决定「这次点击是
    /// 结束谁的回合」；而本 mod 的 <c>LocalContext</c> 会为**瓦库后台出牌的动作归属**临时漂移。
    /// 一旦漂移到「已经结束回合的角色」身上，点击就会被当成「撤销结束回合」处理，
    /// 表现为**点结束回合完全没反应**；切回自己再切到瓦库（重新对齐上下文）才恢复。
    ///
    /// 这里在点击瞬间把上下文校正到**前台玩家**（<see cref="LocalMultiSessionState"/>.CurrentControlledPlayerId，
    /// 不受漂移影响），保证后续原版逻辑结算的是玩家正在看的那个角色。
    /// 返回校正后的前台玩家 id；无战斗中前台角色时返回 null（交回原版自行处理）。
    /// </summary>
    internal static ulong? AlignLocalContextToForegroundForEndTurn()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return null;
        }

        Player? foreground = LocalMultiControlRuntime.TryGetForegroundPlayer();
        if (foreground == null)
        {
            return null;
        }

        ulong playerId = foreground.NetId;
        if (LocalSeatSource.CurrentSeats().IsContext(playerId))
        {
            return playerId;
        }

        ulong? previousNetId = LocalContext.NetId;
        WriteSeatContext(playerId);
        LocalMultiControlLogger.Info(
            $"结束回合点击：上下文已校正到前台玩家 {previousNetId?.ToString() ?? "null"} -> {playerId}");
        return playerId;
    }
}
