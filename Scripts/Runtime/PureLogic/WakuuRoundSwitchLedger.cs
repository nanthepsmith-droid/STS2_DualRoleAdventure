using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「瓦库 → 非瓦库自动切人」的**每回合一次名额**与**待处理请求**台账（R4 第四刀，2026-09-30）。
///
/// 用途：原来这套状态（<c>_wakuuToNonWakuuSwitchedRounds</c> + <c>_pendingWakuuAutoSwitchRoundKey</c> /
/// <c>_pendingWakuuAutoSwitchSource</c>）散在 <see cref="LocalMultiControlRuntime"/> 的字段区、由 5 个方法
/// 各自增删，语义（谁占名额、什么情况作废、失败时保不保留）只能靠通读那几个方法才看得出来。
/// 抽成台账后**语义集中在一处、可直接单测**，Runtime 侧只剩"读游戏状态 + 叫切换"。
///
/// 被记录的语义（都是原实现的既有行为，本刀逐字保留）：
/// <list type="number">
/// <item><b>名额按「战斗身份 + 回合号」记</b>（<see cref="BuildRoundKey"/>）；换战斗时整体 <see cref="Reset"/>。</item>
/// <item><b>手动切到瓦库也算用掉名额</b>：<c>NoteManualSwitchToWakuu</c> 会先 <see cref="MarkSwitched"/>，
///   避免自动化立刻以「瓦库无牌可出」把玩家弹回自己（r103）。</item>
/// <item><b>请求是"下一帧再执行"的延迟意图</b>：登记时只记 key+来源（<see cref="TryRegisterPending"/>），
///   真正执行由战斗 tick 消费；期间若被 <c>LocalManualPlayGuard</c> 拦住或切换失败，请求**保留**，
///   下个 tick 还会再试（<see cref="ClearPending"/> 只在真正切成功后调用）。</item>
/// <item><b>请求跨回合即作废</b>：回合号对不上、或该回合名额已被别处用掉 ⇒ 直接清空请求并返回"不可执行"。</item>
/// </list>
///
/// 本文件不依赖任何 Godot / 游戏类型（只处理字符串键），可直接单测。
/// </summary>
internal static class WakuuRoundSwitchLedger
{
    /// <summary>已用掉名额的回合（键 = <see cref="BuildRoundKey"/>）。</summary>
    private static readonly HashSet<string> _switchedRounds = new(StringComparer.Ordinal);

    /// <summary>待处理请求所属的回合键；null = 当前无请求。</summary>
    private static string? _pendingRoundKey;

    /// <summary>待处理请求的来源（日志用）；null = 未指定 ⇒ 见 <see cref="PendingSource"/>。</summary>
    private static string? _pendingSource;

    /// <summary>日志里"未指定来源"时的兜底文案（原实现逐字保留）。</summary>
    public const string DefaultPendingSource = "wakuu-pending";

    /// <summary>
    /// 回合键 = 战斗身份 + 回合号。回合号只在战斗内有意义，所以键里必须带战斗身份
    /// （否则换战斗后同号回合会被误判成"本轮已切过"）。
    /// </summary>
    public static string BuildRoundKey(int combatIdentity, int roundNumber)
    {
        return $"{combatIdentity}:{roundNumber}";
    }

    /// <summary>该回合的"自动切非瓦库"名额是否已用掉。</summary>
    public static bool HasSwitched(string roundKey)
    {
        return _switchedRounds.Contains(roundKey);
    }

    /// <summary>用掉该回合的名额；返回 true = 本次是新占用（供"已触发"类日志判断是否该打）。</summary>
    public static bool MarkSwitched(string roundKey)
    {
        return _switchedRounds.Add(roundKey);
    }

    /// <summary>当前是否有待处理的自动切换请求。</summary>
    public static bool HasPending => _pendingRoundKey != null;

    /// <summary>待处理请求的来源（未指定时给 <see cref="DefaultPendingSource"/>）。</summary>
    public static string PendingSource => _pendingSource ?? DefaultPendingSource;

    /// <summary>
    /// 登记一次"下一帧再执行"的自动切换请求。
    /// 该回合名额已被用掉时不登记（原实现：直接 return，连日志都不打）⇒ 返回 false。
    /// </summary>
    public static bool TryRegisterPending(string roundKey, string source)
    {
        if (HasSwitched(roundKey))
        {
            return false;
        }

        _pendingRoundKey = roundKey;
        _pendingSource = source;
        return true;
    }

    /// <summary>
    /// 待处理请求**在当前回合是否仍可执行**（等价于原实现消费点前的那串判定）：
    /// <list type="bullet">
    /// <item>没有请求 ⇒ false；</item>
    /// <item>请求不属于当前回合、或当前回合名额已用掉 ⇒ 请求作废（<see cref="ClearPending"/>）并返回 false；</item>
    /// <item>其余 ⇒ true，且**保留**请求（调用方切成功后须自己 <see cref="ClearPending"/>）。</item>
    /// </list>
    /// </summary>
    public static bool IsPendingValidFor(string currentRoundKey)
    {
        if (_pendingRoundKey == null)
        {
            return false;
        }

        if (!string.Equals(_pendingRoundKey, currentRoundKey, StringComparison.Ordinal) || HasSwitched(currentRoundKey))
        {
            ClearPending();
            return false;
        }

        return true;
    }

    /// <summary>请求已执行完毕（切换成功）⇒ 清空。</summary>
    public static void ClearPending()
    {
        _pendingRoundKey = null;
        _pendingSource = null;
    }

    /// <summary>换战斗 / 退出本局时清空全部状态。</summary>
    public static void Reset()
    {
        _switchedRounds.Clear();
        ClearPending();
    }
}
