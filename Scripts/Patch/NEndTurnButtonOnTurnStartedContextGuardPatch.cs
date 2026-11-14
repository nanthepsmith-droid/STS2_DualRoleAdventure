using System;
using System.Collections.Generic;
using System.Threading;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 回合开始时的「上下文席位兜底」（r356，2026-11-14 实机：牌停屏 + 无法结束回合 + `turn loop died`）。
///
/// <para>原版 <c>NEndTurnButton.OnTurnStarted</c> 第一件事就是
/// <c>Player me = LocalContext.GetMe(state); _endTurnLoc.Add("turnNumber", me.PlayerCombatState.TurnNumber)</c>；
/// 而 <c>LocalContext.GetMe</c> 在 <c>NetId == null</c> 时**返回 null 而不是抛**
/// ⇒ 直接 NRE ⇒ `turn loop died` ⇒ 战斗死锁（结束回合按钮点了没反应）。</para>
///
/// <para><b>为什么会是 null</b>：`CardTransformNetIdPinPatch` 在托管席位换牌时会**全局**把
/// `LocalContext.NetId` 让到 null（防原版视觉去前台手牌找托管席位的牌节点而抛），正常路径靠
/// await 变换 Task 之后还原；一旦那条 Task 被"停在 waiting for player choice 的动作"拖住，
/// null 就会跨过回合边界。详见 <see cref="ContextSeatRecoveryPolicy"/> 的根因链。</para>
///
/// <para><b>为什么在这里兜底而不是改正换牌钉扎</b>：钉扎是"让开"语义、跨了若干 await 点，
/// 无法保证永远短命；而这里只要保证**这一次调用**里 `GetMe` 有值即可，返回后立刻还原，
/// 不会和换牌视觉抢全局状态（改道 &gt; 跳过 &gt; 不管）。</para>
/// </summary>
// 多目标容器写法与其它补丁一致：类级裸 [HarmonyPatch] 让 PatchAll 处理本类，
// 具体目标由方法级 [HarmonyPatch(...)] 指定（漏了类级标记会被静默跳过，见 mod 坑 1）。
[HarmonyPatch]
internal static class NEndTurnButtonOnTurnStartedContextGuardPatch
{
    private static readonly AsyncLocal<ulong?> PinnedFromNetId = new();
    private static readonly AsyncLocal<bool> PinActive = new();

    private static bool IsActive()
    {
        return LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode;
    }

    [HarmonyPatch(typeof(NEndTurnButton), "OnTurnStarted")]
    [HarmonyPrefix]
    private static void Prefix(CombatState state)
    {
        PinActive.Value = false;
        PinnedFromNetId.Value = null;

        if (!IsActive() || state == null)
        {
            return;
        }

        // 原版只在自己的回合分支里查 GetMe；敌方回合不必动（动了反而多一层面）。
        if (state.CurrentSide != CombatSide.Player)
        {
            return;
        }

        List<ulong> candidates = CollectCandidatesPresentInCombat(state);
        ulong? fallback = ContextSeatRecoveryPolicy.Decide(LocalContext.NetId, candidates);
        if (!fallback.HasValue)
        {
            return;
        }

        PinnedFromNetId.Value = LocalContext.NetId;
        PinActive.Value = true;
        LocalContext.NetId = fallback.Value;

        LocalMultiControlLogger.Warn(
            "[上下文兜底] 回合开始时上下文席位为空/不在战斗里，已临时钉到本地席位（本次 OnTurnStarted 返回后还原）: "
            + $"原值={PinnedFromNetId.Value?.ToString() ?? "null"}, 临时={fallback.Value}, 候选=[{string.Join(",", candidates)}] —— "
            + "常见原因：手牌变换把 NetId 让到 null 而变换跨了回合边界（r356，见 references BUG-25 同族）");
    }

    // 排在最后还原：其它补丁在这个方法上的 Postfix（探针 / 按钮重评）仍然看到兜底后的值。
    [HarmonyPatch(typeof(NEndTurnButton), "OnTurnStarted")]
    [HarmonyPostfix]
    [HarmonyPriority(Priority.Last)]
    private static void Postfix()
    {
        Restore();
    }

    [HarmonyPatch(typeof(NEndTurnButton), "OnTurnStarted")]
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

    /// <summary>
    /// 按「前台 → 受控 → 主席位 → 其它本地席位」收集**确实在这份战斗状态里**的本地席位。
    /// 顺序即优先级（<see cref="ContextSeatRecoveryPolicy.Decide"/> 直接取第一个）。
    /// </summary>
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
}
