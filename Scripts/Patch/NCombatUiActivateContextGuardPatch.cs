using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗界面初始化（<c>NCombatUi.Activate</c>）时的**上下文兜底**：2026-11-20 黑屏事故。
///
/// <para><b>症状</b>：进一个战斗房间直接黑屏，日志里唯一一条硬错误是</para>
/// <code>
/// System.InvalidOperationException: Local player not found in combat.
///   at LocalContext.GetMe(ICombatState)
///   at NCombatUi.Activate(CombatState state)    // 原版第一行就是 `Player me = LocalContext.GetMe(_state);`
///   at NCombatRoom.OnCombatSetUp
///   at CombatManager.SetUpCombat → CombatRoom.StartCombat → RunManager.EnterRoom
/// </code>
///
/// <para><b>为什么会抛</b>：`LocalContext.GetMe(ICombatState)` 只在两种情况下安全 ——
/// <c>NetId</c> 为空（返回 null）或该 id 的**生物确实在这份战斗状态里**。
/// 而 <c>CombatState.Players</c> 是从 <c>Creatures</c> 反查出来的
/// （<c>PlayerCreatures = Creatures.Where(c =&gt; c.IsPlayer)</c>）⇒ 只要这个席位这次没被加进战斗
/// （或者上下文仍停在上一场的临时席位 / 已离场席位上），`GetMe` 就直接抛。
/// 这一抛发生在 `OnCombatSetUp` 里 ⇒ 房间只装了一半 ⇒ **整屏黑**、只能退。</para>
///
/// <para><b>做法</b>：与 r356 的 <c>NEndTurnButtonOnTurnStartedContextGuardPatch</c> 同一套模板 ——
/// **只在"上下文有值却不在本场战斗里"时**临时把 <c>LocalContext.NetId</c> 钉到本场确实存在的本地席位，
/// 方法返回（或抛异常）立刻还原。正常路径**零改动**（判据不过就直接放行）。
/// 命中时打一条 WARN 把「原值 / 候选 / 本场战斗玩家 / run 玩家」全写出来 ——
/// 下一次再出现就能直接定性是谁把上下文留在场外，而不是只看到一条黑屏。</para>
/// </summary>
[HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
internal static class NCombatUiActivateContextGuardPatch
{
    private static readonly AsyncLocal<ulong?> PinnedFromNetId = new();

    private static readonly AsyncLocal<bool> PinActive = new();

    private static bool IsActive()
    {
        return LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode;
    }

    [HarmonyPrefix]
    private static void Prefix(CombatState state)
    {
        PinActive.Value = false;
        PinnedFromNetId.Value = null;

        if (!IsActive() || state == null)
        {
            return;
        }

        ulong? netId = LocalContext.NetId;
        if (!netId.HasValue)
        {
            // 上下文为空时原版 GetMe 返回 null（后面 `me.Character` 才会 NRE），不在本次兜底范围内：
            // 只由 r356 那条「回合开始临时钉住」负责，这里不扩大改动面。
            return;
        }

        if (state.GetPlayer(netId.Value) != null)
        {
            return; // 正常路径：上下文席位确实在本场战斗里，一个字节都不改
        }

        // 2026-11-20 第二次实机（黑屏复现）：这里读到 `Players` 为空，但 `Allies` 未必为空 ——
        // `Players` / `PlayerCreatures` / `Creatures` 三个 getter 被我们自己的
        // `CombatStateListCachePatch` 按 15ms 分桶缓存；`Allies` 直读原版字段、**不走缓存**。
        // ⇒ "Allies 有生物但 Players 读空" 就是缓存端了 Setup 之前的旧值的铁证。
        // 处理：强制失效一次再重读；真修好了就照常返回（这一屏就不会黑）。
        int rawAllies = SafeAlliesCount(state);
        if (rawAllies > 0)
        {
            CombatStateListCachePatch.InvalidateFromGuard("combat-ui-activate");
            if (state.GetPlayer(netId.Value) != null)
            {
                LocalMultiControlLogger.Warn(
                    "[上下文兜底] `Players` 读数为空但原始 `Allies` 有生物 ⇒ 列表缓存端了旧值，"
                    + "已强制失效并重读（本次黑屏已避免）: "
                    + $"原值={netId.Value}, 原始 Allies={rawAllies}, 同一房间状态={IsSameAsCurrentRoomState(state)}");
                return;
            }
        }

        List<ulong> candidates = CollectCandidatesPresentInCombat(state);
        ulong? fallback = ContextSeatRecoveryPolicy.Decide(netId, candidates);
        string details = $"原值={netId.Value}, 候选=[{string.Join(",", candidates)}], "
                         + $"本场战斗玩家=[{string.Join(",", state.Players.Select((p) => p.NetId))}], "
                         + $"原始 Allies={rawAllies}, 同一房间状态={IsSameAsCurrentRoomState(state)}, "
                         + $"run 玩家=[{DescribeRunPlayers()}]";

        if (!fallback.HasValue)
        {
            LocalMultiControlLogger.Warn(
                "[上下文兜底] 战斗界面初始化时上下文席位不在本场战斗里，且没有可用的本地席位可钉（本次可能黑屏）: "
                + details);
            return;
        }

        PinnedFromNetId.Value = netId;
        PinActive.Value = true;
        LocalContext.NetId = fallback.Value;

        LocalMultiControlLogger.Warn(
            "[上下文兜底] 战斗界面初始化时上下文席位不在本场战斗里，已临时钉到本地席位（本次 Activate 返回后还原）: "
            + $"临时={fallback.Value}, " + details
            + " —— 2026-11-20 黑屏即此处 `LocalContext.GetMe` 抛异常所致");
    }

    /// <summary>原始友方生物数（<c>Allies</c> 不走我们的列表缓存，是"数据到底在不在"的真值）。</summary>
    private static int SafeAlliesCount(CombatState state)
    {
        try
        {
            return state.Allies?.Count ?? 0;
        }
        catch
        {
            return -1;
        }
    }

    /// <summary>这份状态是不是 run 当前房间（CombatRoom）自己的状态 —— 区分"状态对象被换了"与"数据真的空"。</summary>
    private static bool IsSameAsCurrentRoomState(CombatState state)
    {
        try
        {
            return RunManager.Instance.DebugOnlyGetState()?.CurrentRoom is CombatRoom room
                   && ReferenceEquals(room.CombatState, state);
        }
        catch
        {
            return false;
        }
    }

    // 排在最后还原：其它补丁在这个方法上的 Postfix（切人按钮刷新 / 按钮探针）仍然看到兜底后的值。
    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        Restore();
    }

    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Activate))]
    [HarmonyFinalizer]
    private static Exception? Finalizer(Exception? __exception)
    {
        Restore();
        return __exception;
    }

    private static void Restore()
    {
        if (!PinActive.Value)
        {
            return;
        }

        PinActive.Value = false;
        LocalContext.NetId = PinnedFromNetId.Value;
        PinnedFromNetId.Value = null;
    }

    /// <summary>按「前台 → 受控 → 主席位 → 其它本地席位」收集**确实在这份战斗状态里**的本地席位。</summary>
    private static List<ulong> CollectCandidatesPresentInCombat(CombatState state)
    {
        SeatRegistry seats = LocalSeatSource.CurrentSeats();
        List<ulong> ordered = new();
        AddIfUsable(ordered, state, seats.ForegroundSeatId);
        if (seats.ControlledSeatId.HasValue)
        {
            AddIfUsable(ordered, state, seats.ControlledSeatId.Value);
        }

        AddIfUsable(ordered, state, seats.PrimarySeatId);

        foreach (ulong seatId in seats.LocalSeatIds)
        {
            AddIfUsable(ordered, state, seatId);
        }

        return ordered;
    }

    private static void AddIfUsable(List<ulong> ordered, CombatState state, ulong seatId)
    {
        if (seatId == 0UL || ordered.Contains(seatId))
        {
            return;
        }

        if (!LocalSeatSource.IsLocalSeat(seatId))
        {
            return;
        }

        if (state.GetPlayer(seatId) == null)
        {
            return;
        }

        ordered.Add(seatId);
    }

    /// <summary>run 里的玩家名（判读用：区分"本场战斗少了谁"与"run 里本来就没有"）。</summary>
    private static string DescribeRunPlayers()
    {
        try
        {
            return string.Join(",", RunManager.Instance.DebugOnlyGetState()?.Players.Select((p) => p.NetId)
                                       ?? Enumerable.Empty<ulong>());
        }
        catch
        {
            return "?";
        }
    }
}
