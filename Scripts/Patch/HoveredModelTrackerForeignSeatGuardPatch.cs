using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 「悬停模型追踪」的结构外成员护栏（r265；根因见 references **AE-24**）；
/// 判据在纯逻辑 <see cref="ForeignSeatGuardPolicy"/>（有单测）。
///
/// <para><b>为什么它也在雷区里</b>：`HoveredModelTracker` 用
/// <c>_hoveredModels[runState.GetPlayerSlotIndex(player)]</c> 记账，而该索引对不在 `runState.Players`
/// 里的玩家是 **-1**（接口注释原文 "or -1 if the player is not in Players"）。
/// 战神（临时席位）刻意不进那张表（AE-1/AE-3）⇒ 三个方法都会踩：
/// <list type="bullet">
/// <item><c>OnPlayerStateRemoved</c> / <c>GetHoveredModel</c> ⇒ 直接 <c>_hoveredModels[-1]</c> 抛；
///   （`GetHoveredModel` 的唯一调用方是多人意图气泡 `NMultiplayerPlayerIntentHandler`）；</item>
/// <item><c>OnPlayerStateChanged</c> ⇒ 先 <c>GetPlayer(战神)</c> 得到 null、再 <c>player.Relics</c> NRE。</item>
/// </list></para>
///
/// <para><b>现状（2026-10-11 扫描）**实机从未触发**</b>：`_playerCollection` 是 `RunState`
/// （构造点 `RunManager.cs` 的 `new HoveredModelTracker(InputSynchronizer, State)`），
/// 而 `InputSynchronizer.StateChanged` 只对**有输入通道的 peer** 触发，战神没有。
/// 本护栏是"防万一"（用户 2026-10-11 拍板）：结构外成员一旦被某个机制带上，也不会把
/// `turn loop` 打死（同 r264 那次的形态）。</para>
///
/// <para><b>跳过是安全的</b>：悬停模型是**纯表现层**状态 —— 不更新 / 读不到就是"这个玩家头上没有意图气泡"，
/// 没有任何"计数必填项"语义（对比：`EventCombatSynchronizer.ReadyToEnterCombat` 就**不能**这样跳，
/// 它的 `_states[slot]` 是"全员就位才进战斗"的计数，跳过会让 `_states.All(s => s != null)` 永远不满足
/// ⇒ 反而卡住事件）。所以本护栏只覆盖 `HoveredModelTracker`。</para>
/// </summary>
[HarmonyPatch(typeof(HoveredModelTracker))]
internal static class HoveredModelTrackerForeignSeatGuardPatch
{
    /// <summary>每个被拦的席位只报一条"护栏生效"（只为日志去重；跨局不清无碍）。</summary>
    private static readonly HashSet<ulong> _logged = new();

    [HarmonyPatch("OnPlayerStateChanged", new Type[] { typeof(ulong) })]
    [HarmonyPrefix]
    private static bool OnPlayerStateChangedPrefix(ulong playerId)
    {
        return !ShouldSkip(playerId, "OnPlayerStateChanged");
    }

    [HarmonyPatch("OnPlayerStateRemoved", new Type[] { typeof(ulong) })]
    [HarmonyPrefix]
    private static bool OnPlayerStateRemovedPrefix(ulong playerId)
    {
        return !ShouldSkip(playerId, "OnPlayerStateRemoved");
    }

    [HarmonyPatch(nameof(HoveredModelTracker.GetHoveredModel))]
    [HarmonyPrefix]
    private static bool GetHoveredModelPrefix(ulong playerId, ref AbstractModel? __result)
    {
        if (!ShouldSkip(playerId, "GetHoveredModel"))
        {
            return true;
        }

        // 结构外成员没有悬停模型 ⇒ 照"没有"返回（调用方本来就接受 null）。
        __result = null;
        return false;
    }

    private static bool ShouldSkip(ulong playerId, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return false;
        }

        RunState? runState = RunManager.Instance?.DebugOnlyGetState();
        // 拿不到 runState 时按"在场"传 ⇒ 不拦（宁可少拦也不误拦真人）。
        bool playerInRunState = runState == null || runState.GetPlayer(playerId) != null;

        bool skip = ForeignSeatGuardPolicy.ShouldSkipHoverTracking(
            LocalSelfCoopContext.IsEnabled,
            LocalSelfCoopContext.IsTemporarySeat(playerId),
            playerInRunState,
            playerId);

        if (skip && _logged.Add(playerId))
        {
            LocalMultiControlLogger.Info(
                $"结构外成员（临时席位/不在 runState.Players）跳过悬停模型追踪（防 -1 越界打死 turn loop）: "
                + $"source={source}, player={playerId}（坑见 references AE-24）");
        }

        return skip;
    }
}
