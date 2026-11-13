using System;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗**胜利结算**四段探针（2026-11-13 实机：战神打出带 HitCount=2 的【君王之剑】打死最后一个敌人后，
/// 敌人死了、`CombatManager.IsInProgress` 已变 false（= `EndCombatInternal` 已开始），
/// 但奖励界面不出现、回合推不动 ⇒ 整场卡在"结束中"）。
///
/// 为什么这么量：`CombatManager.EndCombatInternal`（`sts2src/src/Core/Combat/CombatManager.cs:1377`）
/// 是一条长 await 链，任一步挂住都不会打 ERROR（所以日志里"零异常却卡死"）：
/// <code>
/// 1393  await player.ReviveBeforeCombatEnd()      // 逐玩家（**含战神这类结构外席位**）
/// 1395  await Hook.AfterCombatEnd(...)            // 第三方 listener 都在这里，**没有保命补丁**
/// 1397  room.OnCombatEnded()                      // 奖励从这里开始
/// 1406  await Hook.AfterCombatVictory(...)        // 已有 CombatVictoryHookGuard 兜
/// </code>
/// 我们原有的保命补丁只盖了最后一段（`AfterCombatVictory`），前一段一挂就永远到不了它。
///
/// 探针**不改行为**：只在本地多控会话里打「进入 / 完成（耗时 ms）/ 抛异常」三类锚点，
/// 异常仍然照原样抛出（`throw`），以免掩盖真因。
/// </summary>
internal static class CombatEndProbePatch
{
    internal static long NowMs() => (long)Time.GetTicksMsec();

    internal static void LogEnter(string tag)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalMultiControlLogger.Info($"[战斗结束探针] {tag} 进入");
    }

    internal static void LogSyncDone(string tag, long startMs)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        LocalMultiControlLogger.Info($"[战斗结束探针] {tag} 完成: 耗时={NowMs() - startMs}ms");
    }

    /// <summary>把返回的 Task 包一层，只观测（异常继续上抛，行为不变）。</summary>
    internal static Task Observe(Task task, string tag, long startMs)
    {
        if (!LocalSelfCoopContext.IsEnabled || task == null)
        {
            return task!;
        }

        return ObserveAsync(task, tag, startMs);
    }

    private static async Task ObserveAsync(Task task, string tag, long startMs)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"[战斗结束探针] {tag} 抛异常（耗时={NowMs() - startMs}ms）: "
                + $"{exception.GetType().Name}: {exception.Message}");
            throw;
        }

        LocalMultiControlLogger.Info($"[战斗结束探针] {tag} 完成: 耗时={NowMs() - startMs}ms");
    }
}

/// <summary>
/// ① 整条胜利结算链的入口/出口 —— 卡住时这条永远等不到"完成"。
///
/// ⚠ 2026-11-13 第二次实机：挂**无参重载**一条都没打 ⇒ 胜利实际走的是
/// `private async Task EndCombatInternal(CombatTurnState)`（`CombatManager.cs:1377`，由 `CheckWinCondition` 调），
/// 无参版只是给测试/外部用的包装。而 `CombatTurnState` 是 `internal` ⇒ 不能在 attribute 里写 `typeof`，
/// 所以这里用 <see cref="TargetMethod"/> 运行时解析参数类型。两个重载各挂一个类，避免再漏。
/// </summary>
[HarmonyPatch]
internal static class CombatManagerEndCombatProbePatch
{
    private static MethodBase? TargetMethod()
    {
        Type? turnStateType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Combat.CombatTurnState");
        if (turnStateType == null)
        {
            LocalMultiControlLogger.Warn(
                "[战斗结束探针] 解析不到 CombatTurnState 类型，EndCombatInternal(带参) 探针不生效");
            return null;
        }

        MethodBase? method = AccessTools.Method(typeof(CombatManager), "EndCombatInternal", new[] { turnStateType });
        if (method == null)
        {
            LocalMultiControlLogger.Warn(
                "[战斗结束探针] 解析不到 EndCombatInternal(CombatTurnState)，探针不生效");
        }

        return method;
    }

    [HarmonyPrefix]
    private static void Prefix(ref long __state)
    {
        __state = CombatEndProbePatch.NowMs();
        CombatEndProbePatch.LogEnter("CombatManager.EndCombatInternal(turnState)");
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task __result, long __state)
    {
        __result = CombatEndProbePatch.Observe(__result, "CombatManager.EndCombatInternal(turnState)", __state);
    }
}

/// <summary>①b 无参包装（测试/外部路径）。两个重载都挂，避免"以为挂上了其实没走"。</summary>
[HarmonyPatch(typeof(CombatManager), "EndCombatInternal", new Type[0])]
internal static class CombatManagerEndCombatNoArgProbePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref long __state)
    {
        __state = CombatEndProbePatch.NowMs();
        CombatEndProbePatch.LogEnter("CombatManager.EndCombatInternal()");
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task __result, long __state)
    {
        __result = CombatEndProbePatch.Observe(__result, "CombatManager.EndCombatInternal()", __state);
    }
}

/// <summary>② 逐玩家复活（**战神也在 combatState.Players 里**，这一步是结构外成员风险点）。</summary>
[HarmonyPatch(typeof(Player), nameof(Player.ReviveBeforeCombatEnd))]
internal static class PlayerReviveBeforeCombatEndProbePatch
{
    [HarmonyPrefix]
    private static void Prefix(Player __instance, ref long __state)
    {
        __state = CombatEndProbePatch.NowMs();
        if (LocalSelfCoopContext.IsEnabled)
        {
            LocalMultiControlLogger.Info(
                $"[战斗结束探针] Player.ReviveBeforeCombatEnd 进入: player={__instance?.NetId}, "
                + $"isDead={__instance?.Creature?.IsDead}, 在runState.Players={IsRunPlayer(__instance)}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(Player __instance, ref Task __result, long __state)
    {
        __result = CombatEndProbePatch.Observe(
            __result,
            $"Player.ReviveBeforeCombatEnd(player={__instance?.NetId})",
            __state);
    }

    private static bool IsRunPlayer(Player? player)
    {
        try
        {
            return player != null
                && RunManager.Instance?.DebugOnlyGetState()?.GetPlayerSlotIndex(player) >= 0;
        }
        catch
        {
            return false;
        }
    }
}

/// <summary>③ 第三方 listener 最集中的一段（原先**没有**任何保命补丁覆盖）。</summary>
[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatEnd))]
internal static class HookAfterCombatEndProbePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref long __state)
    {
        __state = CombatEndProbePatch.NowMs();
        CombatEndProbePatch.LogEnter("Hook.AfterCombatEnd");
    }

    [HarmonyPostfix]
    private static void Postfix(ref Task __result, long __state)
    {
        __result = CombatEndProbePatch.Observe(__result, "Hook.AfterCombatEnd", __state);
    }
}

/// <summary>④ 战斗房间收尾（奖励从这里开始）：走到这里说明前面都过了。</summary>
[HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.OnCombatEnded))]
internal static class CombatRoomOnCombatEndedProbePatch
{
    [HarmonyPrefix]
    private static void Prefix(ref long __state)
    {
        __state = CombatEndProbePatch.NowMs();
        CombatEndProbePatch.LogEnter("CombatRoom.OnCombatEnded");
    }

    [HarmonyPostfix]
    private static void Postfix(long __state)
    {
        CombatEndProbePatch.LogSyncDone("CombatRoom.OnCombatEnded", __state);
    }
}
