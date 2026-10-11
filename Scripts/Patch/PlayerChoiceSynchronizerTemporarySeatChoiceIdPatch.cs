using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 「临时席位（战神）」的 choiceId 记账改道（r264；根因与实机铁证见 references **AE-24**）。
///
/// <para><b>问题</b>：原版 <c>PlayerChoiceSynchronizer</c> 按**玩家槽位索引**记账 ——
/// <c>_choiceIds[runState.GetPlayerSlotIndex(player)]</c>；而 `GetPlayerSlotIndex` 对不在
/// `runState.Players` 里的玩家返回 <c>-1</c>（接口注释原文 "or -1 if the player is not in
/// Players"）。战神刻意不进 `runState.Players`（AE-1/AE-3）⇒ <c>_choiceIds[-1]</c> ⇒
/// <c>ArgumentOutOfRangeException</c>。</para>
///
/// <para><b>实机怎么死的（2026-10-11，turn loop 直接废掉）</b>：第三方 GensokyoSpire 的抽牌前钩子
/// （Watcher「先见之明」→ BaseLib <c>ScryCmd</c> → <c>CardSelectCmd.FromSimpleGrid</c>）在**战神回合**
/// 调 <c>ReserveChoiceId(战神)</c>，异常冒穿 <c>CombatManager.StartTurn</c> ⇒ 原版打
/// <c>turn loop died … the combat is stuck until the room is restarted</c>
/// ⇒ 整场战斗冻结（不能出牌 / 不能切人 / 不能结束回合，瓦库与战神也全停）。</para>
///
/// <para><b>修法</b>：给临时席位一条**独立、单调递增**的 id 序列（<see cref="TemporarySeatChoiceIds"/>），
/// 不再碰 `_choiceIds`。`ReserveChoiceId`（分配）与 `GetChoiceId`（校验侧取当前值，
/// `ValidateChoiceId` 会经它）两处必须同进同退 —— 只改一处会变成"分配成功、校验仍抛"。</para>
///
/// <para>⚠ 只保证"不再抛"：战神**后续**的选牌流程仍是非本机玩家（`LocalContext.IsMe` 为 false）
/// ⇒ 会走 <c>WaitForRemoteChoice</c>，由 <c>PlayerChoiceSynchronizerRemoteChoiceFallbackPatch</c>
/// 按"空结果"放行（该补丁的闸门已在 r264 并入临时席位）。</para>
/// </summary>
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.ReserveChoiceId))]
internal static class PlayerChoiceSynchronizerTemporarySeatReserveChoiceIdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref uint __result)
    {
        if (player == null
            || !TemporarySeatChoiceIds.ShouldDivert(
                LocalSelfCoopContext.IsEnabled,
                LocalSelfCoopContext.IsTemporarySeat(player.NetId),
                player.NetId))
        {
            return true;
        }

        __result = TemporarySeatChoiceIds.Reserve(player.NetId);
        if (TemporarySeatChoiceIds.MarkLoggedOnce(player.NetId))
        {
            LocalMultiControlLogger.Info(
                $"临时席位（战神）的 choiceId 已改道到独立序列：原版按槽位索引记账，而战神不在 "
                + $"runState.Players 里（slotIndex = -1）会越界打死 turn loop: seat={player.NetId}, "
                + $"choiceId={__result}（坑见 references AE-24）");
        }

        return false;
    }
}

/// <summary>
/// 见 <see cref="PlayerChoiceSynchronizerTemporarySeatReserveChoiceIdPatch"/>：
/// 另一半 —— 校验侧取当前 choiceId（<c>ValidateChoiceId</c> → <c>GetChoiceId</c>）。
/// 不同步改这一处的话，`SyncLocalChoice` / `WaitForRemoteChoice` 仍会 `_choiceIds[-1]`。
/// </summary>
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), "GetChoiceId", new Type[] { typeof(Player) })]
internal static class PlayerChoiceSynchronizerTemporarySeatGetChoiceIdPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref uint __result)
    {
        if (player == null
            || !TemporarySeatChoiceIds.ShouldDivert(
                LocalSelfCoopContext.IsEnabled,
                LocalSelfCoopContext.IsTemporarySeat(player.NetId),
                player.NetId))
        {
            return true;
        }

        __result = TemporarySeatChoiceIds.Current(player.NetId);
        return false;
    }
}
