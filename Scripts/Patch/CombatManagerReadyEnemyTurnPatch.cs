using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地多控：把「进入敌方回合」的就绪补齐到**我们自己的本地席位**。
///
/// 为什么需要：`CombatManager` 的回合循环只在 `LocalContext.GetMe(...)` 这一位身上
/// `RequestEnqueue(new ReadyToBeginEnemyTurnAction(...))`（CombatManager.cs:1469），
/// 也就是说一个进程只会替「当前前台」那一个席位报就绪；其余本地席位没有对端来报，
/// 于是 `BeginEnemyTurnSignalSource` 永远等不齐、敌回合卡死。本补丁把其余本地席位补上。
///
/// ⚠ **r144 实机事故（3 席：真人 + 瓦库 + Co-op Bots 合成 Bot，回合结束不了）**：
/// 旧实现是**直接 `readySet.Add(player)`**，绕开了游戏自己的完成判定
/// —— `SetReadyToBeginEnemyTurn` 只在 `Add(...)` 返回 true 的那一次调用里检查
/// `PlayersReadyToBeginEnemyTurn.Count == State.Players.Count`（CombatManager.cs:1018-1026），
/// 于是：
/// <list type="number">
/// <item>第三方的 Bot 会把「补齐 Bot 就绪」做成**嵌套调用**（Co-op Bots 的
/// `EnemyTurnReadyBotPatch` 在别人的就绪动作里调 `SetReadyToBeginEnemyTurn(bot)`）；</item>
/// <item>旧实现会在那次嵌套调用里把**触发者自己**也直接塞进集合 ⇒ 集合满了，
/// 但真正那次 `Add(触发者)` 返回 false ⇒ 提前 return ⇒ **完成判定一次都没跑**；</item>
/// <item>两个 TaskCompletionSource 都不完成 ⇒ 界面停在 `EndTurnPhaseOne`，表现就是「回合结束不了」。</item>
/// </list>
/// 两席局之所以一直没事：只有「触发者自己」是最后一个 Add，完成判定恰好在它那次调用里跑到。
///
/// 现在改成三条硬规矩：
/// <list type="number">
/// <item>**只补我们自己的席位**（<see cref="LocalSelfCoopContext.LocalPlayerIds"/>）——
/// 第三方席位（合成 Bot / 被接管席）归第三方 mod 自己补，绝不由我们代劳；</item>
/// <item>**只在我们自己的席位被触发时才补**（触发者是第三方席位时一律不干预，
/// 免得把触发者自己塞进集合）；</item>
/// <item>**补就绪必须走真实方法调用**（`SetReadyToBeginEnemyTurn`）而不是直接改集合，
/// 这样「最后一个 Add」永远发生在真实方法内，游戏的完成判定一定会跑到。</item>
/// </list>
/// 重入由 <see cref="_mirroring"/> 挡住（ThreadStatic，与第三方同款写法）。
/// </summary>
[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
internal static class CombatManagerReadyEnemyTurnPatch
{
    /// <summary>补齐期间的递归保护：嵌套调用不再补齐（避免自我叠加）。</summary>
    [ThreadStatic]
    private static bool _mirroring;

    private static object? GetTurnState(CombatManager combatManager)
    {
        return AccessTools.Field(typeof(CombatManager), "_turnState")?.GetValue(combatManager);
    }

    private static HashSet<Player>? GetPlayersReadyToBeginEnemyTurn(CombatManager combatManager)
    {
        // beta110 moved the ready set off CombatManager onto the turn state; check the modern path first,
        // fall back to the pre-beta110 field for older game builds.
        object? turnState = GetTurnState(combatManager);
        if (turnState != null)
        {
            HashSet<Player>? viaProperty = AccessTools.Property(turnState.GetType(), "PlayersReadyToBeginEnemyTurn")?.GetValue(turnState) as HashSet<Player>;
            if (viaProperty != null)
            {
                return viaProperty;
            }
        }

        return AccessTools.Field(typeof(CombatManager), "_playersReadyToBeginEnemyTurn")?.GetValue(combatManager) as HashSet<Player>;
    }

    [HarmonyPrefix]
    private static void Prefix(CombatManager __instance, Player player, Func<Task>? actionDuringEnemyTurn)
    {
        if (_mirroring || !LocalSelfCoopContext.IsEnabled || player == null)
        {
            return;
        }

        // 规矩 ②：触发者必须是我们自己的席位（第三方席位的就绪由第三方 mod 负责）。
        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(player.NetId))
        {
            return;
        }

        CombatState? state = __instance.DebugOnlyGetState();
        if (state == null || state.CurrentSide != CombatSide.Player || state.Players.Count < 2)
        {
            return;
        }

        HashSet<Player>? readySet = GetPlayersReadyToBeginEnemyTurn(__instance);
        if (readySet == null)
        {
            return;
        }

        // 规矩 ①：只补我们自己的其他席位。
        List<Player> pendingPlayers = state.Players
            .Where((candidate) => candidate.NetId != player.NetId)
            .Where((candidate) => LocalSelfCoopContext.LocalPlayerIds.Contains(candidate.NetId))
            .Where((candidate) => !readySet.Contains(candidate))
            .ToList();
        if (pendingPlayers.Count == 0)
        {
            return;
        }

        // 规矩 ③：走真实方法（而不是直接 Add）——「最后一个 Add」必须发生在游戏自己的方法里，
        // 否则完成判定（Count == Players.Count ⇒ 完成 BeginEnemyTurnSignalSource）永远跑不到。
        _mirroring = true;
        try
        {
            foreach (Player pendingPlayer in pendingPlayers)
            {
                __instance.SetReadyToBeginEnemyTurn(pendingPlayer, actionDuringEnemyTurn);
            }
        }
        finally
        {
            _mirroring = false;
        }

        LocalMultiControlLogger.Info(
            $"本地多控自动补齐敌方回合就绪: trigger={player.NetId}, mirrored={string.Join(",", pendingPlayers.Select((candidate) => candidate.NetId))}");
    }
}
