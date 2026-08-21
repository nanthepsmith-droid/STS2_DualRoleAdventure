using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
internal static class CombatManagerReadyEnemyTurnPatch
{
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
        if (!LocalSelfCoopContext.IsEnabled)
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

        List<Player> pendingPlayers = state.Players
            .Where((candidate) => candidate.NetId != player.NetId)
            .Where((candidate) => !readySet.Contains(candidate))
            .ToList();
        foreach (Player pendingPlayer in pendingPlayers)
        {
            readySet.Add(pendingPlayer);
        }

        if (pendingPlayers.Count > 0)
        {
            LocalMultiControlLogger.Info(
                $"本地多控自动补齐敌方回合就绪: trigger={player.NetId}, mirrored={string.Join(",", pendingPlayers.Select((candidate) => candidate.NetId))}");
        }
    }

}
