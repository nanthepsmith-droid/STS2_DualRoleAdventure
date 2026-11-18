using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>小键盘「切到第 N 号席位」的判定结果（2026-11-18 §排期 5）。</summary>
internal enum SeatHotkeyStatus
{
    /// <summary>可以切，且目标不是当前席位。</summary>
    Ok,

    /// <summary>目标就是当前前台席位 —— 什么都不用做，也不必提示。</summary>
    AlreadyCurrent,

    /// <summary>不在局内（<see cref="MegaCrit.Sts2.Core.Runs.RunManager.IsInProgress"/> 为 false）。</summary>
    RunNotInProgress,

    /// <summary>没开本地多角色 —— 数字键必须完全不响应，避免干扰原版单人的同名按键。</summary>
    LocalCoopDisabled,

    /// <summary>本地多控会话还没初始化（席位表现在是空的）。</summary>
    SessionNotInitialized,

    /// <summary>号位越界（例如 4 席的局按了 7）。</summary>
    OutOfRange,

    /// <summary>该号位此刻不在战斗里（已逃跑 / 已死亡 / 根本没参战）。</summary>
    NotInCombat,

    /// <summary>占卜小游戏 / 假商人药水投掷等"切人必软锁"的窗口。</summary>
    BlockedByMinigame,

    /// <summary>战斗内正在出牌 / 选牌 / 非玩家出牌相位 —— 与 Tab 循环同一条守卫。</summary>
    BlockedByCombatBusy,

    /// <summary>
    /// 此刻有选牌在飞（原版选牌弹层 / 托管作用域，见 <c>WakuuSelectorStackAccess.SnapshotEntries</c>）——
    /// 切人会换 owner，选牌结果会挂到错的人身上，所以一律不切。
    /// </summary>
    BlockedByCardSelection,
}

/// <summary>
/// 一次「数字键切人」判定的结果。<see cref="SeatNumber"/> / <see cref="SeatCount"/> 只用于提示文案，
/// 文案本身按语言生成（见 <c>LocalModText.SeatHotkey*</c>），本类保持语言无关。
/// </summary>
internal readonly struct SeatHotkeyResult
{
    private SeatHotkeyResult(SeatHotkeyStatus status, int seatNumber, int seatCount, ulong playerId)
    {
        Status = status;
        SeatNumber = seatNumber;
        SeatCount = seatCount;
        PlayerId = playerId;
    }

    internal SeatHotkeyStatus Status { get; }

    /// <summary>玩家按下的号位（1 起）。</summary>
    internal int SeatNumber { get; }

    /// <summary>当前会话的本地席位数（0 = 会话没初始化）。</summary>
    internal int SeatCount { get; }

    /// <summary>解析出来的席位 id（仅 <see cref="SeatHotkeyStatus.Ok"/> / <see cref="SeatHotkeyStatus.AlreadyCurrent"/> 有意义）。</summary>
    internal ulong PlayerId { get; }

    /// <summary>这次按键是否"已经处理完"（成功或无需处理）—— 调用方不再需要提示。</summary>
    internal bool Succeeded
        => Status is SeatHotkeyStatus.Ok or SeatHotkeyStatus.AlreadyCurrent;

    /// <summary>
    /// 是否值得给玩家一句提示（号位不存在 / 不在战斗 / 小游戏里）。
    ///
    /// <para><see cref="SeatHotkeyStatus.BlockedByCombatBusy"/> **刻意不提示** —— 它就是
    /// "敌人回合 / 非出牌相位"，玩家可能连按几下，弹字只会刷屏；与 Tab 循环键静默忽略的口径一致。</para>
    /// </summary>
    internal bool NeedsNotice => Status is
        SeatHotkeyStatus.SessionNotInitialized or
        SeatHotkeyStatus.OutOfRange or
        SeatHotkeyStatus.NotInCombat or
        SeatHotkeyStatus.BlockedByMinigame or
        SeatHotkeyStatus.BlockedByCardSelection;

    internal static SeatHotkeyResult Of(
        SeatHotkeyStatus status,
        int seatNumber = 0,
        int seatCount = 0,
        ulong playerId = 0UL)
        => new(status, seatNumber, seatCount, playerId);
}

/// <summary>
/// **小键盘** <c>1</c>~<c>9</c> <c>/</c> <c>*</c> <c>-</c> ⇒ 会话席位 <c>1</c>~<c>12</c>
/// 的**纯逻辑判定**（2026-11-18 §排期 5）。
///
/// <para>口径（用户 2026-11-16 原话「数字键 1234567890 和 -= 分别对应 1 到 12 号玩家」，
/// 2026-11-18 改口「严肃改成小键盘」—— 大键盘 <c>1</c>~<c>0</c> 是原版"选第 N 张手牌"）：</para>
/// <list type="bullet">
/// <item>号位按**会话表顺序**（<c>LocalMultiSessionState.OrderedPlayerIds</c>，与 Tab 循环同序），
/// **不按立绘左右** —— 立绘会被重排（实测从左到右 = 角色4 | 2 | 3 | 1）。</item>
/// <item>只在**局内**生效：局外的大键盘 <c>-</c> / <c>=</c> 是"本地人数增减"热键，一个字都不动。</item>
/// <item>没开本地多角色时完全不响应（避免抢原版单人的同名按键）。</item>
/// <item>战斗内还要求该席位**这一场确实在战斗里**（逃跑 / 阵亡的席位切过去会触发
/// "手牌 UI 与动作队列 owner 不一致"，见 r263 那类死锁）。</item>
/// </list>
///
/// <para>本类只做判定：守卫类窗口（占卜小游戏 / 出牌选牌中）由运行时先行判定并用
/// <see cref="SeatHotkeyResult.Of"/> 构造对应状态；真正的切人仍复用既有链路
/// （<c>Session.TrySetCurrentPlayer</c> + <c>ApplyControlContext</c>）。</para>
/// </summary>
internal static class SeatHotkeyPolicy
{
    /// <summary>号位下限。</summary>
    internal const int MinSeatNumber = 1;

    /// <summary>号位上限 = 本地多控席位上限（<c>SelfCoopSessionState.MaxLocalPlayerCount</c>）。</summary>
    internal const int MaxSeatNumber = 12;

    /// <summary>
    /// 解析"第 N 号席位"。
    /// </summary>
    /// <param name="seatNumber">玩家按下的号位（1 起）。</param>
    /// <param name="runInProgress">是否在局内。</param>
    /// <param name="localCoopEnabled">本地多角色是否开启。</param>
    /// <param name="sessionInitialized">本地多控会话是否已初始化。</param>
    /// <param name="orderedSeatIds">会话席位表（<c>LocalMultiSessionState.OrderedPlayerIds</c>，顺序即号位顺序）。</param>
    /// <param name="currentSeatId">当前前台席位 id（null = 未知）。</param>
    /// <param name="combatSeatIds">战斗内可出现的目标席位（null = 不在战斗，不做该项校验）。</param>
    internal static SeatHotkeyResult Resolve(
        int seatNumber,
        bool runInProgress,
        bool localCoopEnabled,
        bool sessionInitialized,
        IReadOnlyList<ulong>? orderedSeatIds,
        ulong? currentSeatId,
        IReadOnlyList<ulong>? combatSeatIds)
    {
        int seatCount = orderedSeatIds?.Count ?? 0;

        if (!runInProgress)
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.RunNotInProgress, seatNumber, seatCount);
        }

        if (!localCoopEnabled)
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.LocalCoopDisabled, seatNumber, seatCount);
        }

        if (!sessionInitialized || orderedSeatIds == null || seatCount == 0)
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.SessionNotInitialized, seatNumber, seatCount);
        }

        if (seatNumber < MinSeatNumber || seatNumber > MaxSeatNumber)
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.OutOfRange, seatNumber, seatCount);
        }

        if (seatNumber > seatCount)
        {
            // 号位存在但这一局没有这么多人 —— 与"越界"对玩家是同一件事，共用提示。
            return SeatHotkeyResult.Of(SeatHotkeyStatus.OutOfRange, seatNumber, seatCount);
        }

        ulong targetPlayerId = orderedSeatIds[seatNumber - 1];

        if (combatSeatIds != null && !Contains(combatSeatIds, targetPlayerId))
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.NotInCombat, seatNumber, seatCount, targetPlayerId);
        }

        if (currentSeatId.HasValue && currentSeatId.Value == targetPlayerId)
        {
            return SeatHotkeyResult.Of(SeatHotkeyStatus.AlreadyCurrent, seatNumber, seatCount, targetPlayerId);
        }

        return SeatHotkeyResult.Of(SeatHotkeyStatus.Ok, seatNumber, seatCount, targetPlayerId);
    }

    private static bool Contains(IReadOnlyList<ulong> seatIds, ulong playerId)
    {
        for (int i = 0; i < seatIds.Count; i++)
        {
            if (seatIds[i] == playerId)
            {
                return true;
            }
        }

        return false;
    }
}
