using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「临时席位（战神）」自己的 choiceId 计数器（r264，坑见 references AE-24）。
///
/// <para><b>为什么需要</b>：原版 <c>PlayerChoiceSynchronizer</c> 按**玩家槽位索引**记账
/// （<c>_choiceIds[runState.GetPlayerSlotIndex(player)]</c>），而
/// <c>GetPlayerSlotIndex</c> 对不在 <c>runState.Players</c> 里的玩家返回 <c>-1</c>
/// （接口注释原文 "or -1 if the player is not in Players"）。战神**刻意不进** `runState.Players`
/// （AE-1/AE-3）⇒ `_choiceIds[-1]` ⇒ <c>ArgumentOutOfRangeException</c>。
/// 2026-10-11 实机就是这么死的：第三方 GensokyoSpire 在**战神回合的抽牌前钩子**里走
/// 「占卜 → <c>CardSelectCmd.FromSimpleGrid</c> → <c>ReserveChoiceId(战神)</c>」，
/// 异常冒穿 <c>CombatManager.StartTurn</c> ⇒ 原版打 <c>turn loop died … the combat is stuck
/// until the room is restarted</c> ⇒ **整场战斗废掉**（不能出牌 / 不能切人 / 不能结束回合）。</para>
///
/// <para><b>语义</b>：给每个临时席位一条**独立、单调递增**的 id 序列，与 `_choiceIds` 完全解耦
/// （不进那张表 ⇒ 不影响原版的 <c>ChoiceIds</c> 快照 / 校验和 / 存档）。
/// `ValidateChoiceId` 判的是 <c>choiceId &lt; 当前值</c>，所以只要单调递增就语义正确；
/// 临时席位是**本机回环**、不参与跨端一致性校验，因此不进那张表是安全的。</para>
///
/// <para>纯逻辑（无 Godot / 游戏类型依赖）⇒ 可单测。</para>
/// </summary>
internal static class TemporarySeatChoiceIds
{
    /// <summary>席位 → 下一个待分配的 choiceId（= 已分配个数）。</summary>
    private static readonly Dictionary<ulong, uint> _next = new();

    /// <summary>
    /// 该席位要不要把 choiceId 记账**改道到本类**（两个补丁共用这一条判据）。
    ///
    /// 只认「本地多控开启 + 登记过的临时席位（战神）+ 有效 id」：
    /// <list type="bullet">
    /// <item>正式席位（含真人与瓦库）在 `runState.Players` 里，slotIndex 正常 ⇒ **不动**；</item>
    /// <item>第三方合成 Bot 席位也在 `runState.Players` 里 ⇒ 不该被我们改道；</item>
    /// <item>非本地多控（原版 / 真联机）⇒ 完全不介入。</item>
    /// </list>
    /// </summary>
    internal static bool ShouldDivert(bool localMultiEnabled, bool isTemporarySeat, ulong seatId)
    {
        return localMultiEnabled && isTemporarySeat && seatId != 0UL;
    }

    /// <summary>分配一个 choiceId（返回旧值并自增）。</summary>
    internal static uint Reserve(ulong seatId)
    {
        _next.TryGetValue(seatId, out uint current);
        _next[seatId] = current + 1;
        return current;
    }

    /// <summary>取"当前值"（= 下一个待分配的 id；未登记过 = 0）——对应原版 <c>GetChoiceId</c>。</summary>
    internal static uint Current(ulong seatId)
    {
        return _next.TryGetValue(seatId, out uint current) ? current : 0u;
    }

    /// <summary>这个席位是否已经分配过（仅供日志/诊断）。</summary>
    internal static bool HasAny(ulong seatId) => _next.ContainsKey(seatId);

    /// <summary>"首次改道"日志的席位集合（收在这里，好让 <see cref="Reset"/> 一次清干净）。</summary>
    private static readonly HashSet<ulong> _loggedOnce = new();

    /// <summary>该席位是否要打"已改道"日志（**首次**为 true，之后 false ⇒ 不刷屏）。</summary>
    internal static bool MarkLoggedOnce(ulong seatId) => _loggedOnce.Add(seatId);

    /// <summary>换局 / 退局复位（与其它按局状态同一个复位枢纽调用）。</summary>
    internal static void Reset()
    {
        _next.Clear();
        _loggedOnce.Clear();
    }
}
