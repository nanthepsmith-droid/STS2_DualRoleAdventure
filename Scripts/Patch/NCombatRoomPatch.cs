using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NCombatRoom), "OnCombatSetUp")]
internal static class NCombatRoomPatch
{
    [HarmonyPostfix]
    private static void Postfix(CombatState state)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        // 每场战斗清一次能量归属诊断去重，保证下一场战斗的核对日志照样打得出（BUG-1 排查用）。
        LocalMultiControlRuntime.ResetCombatUiDiagnostics("combat-setup");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(LocalSelfCoopContext.PrimaryPlayerId, "combat-setup");
        LocalMultiControlRuntime.RefreshSharedTopBarForCombat("combat-setup");
        LocalMultiControlRuntime.RefreshCombatEnergyForCurrentPlayer("combat-setup");
        Callable.From(delegate
        {
            LocalMultiControlRuntime.RefreshSharedTopBarForCombat("combat-setup-deferred");
            LocalMultiControlRuntime.RefreshCombatEnergyForCurrentPlayer("combat-setup-deferred");

            Callable.From(delegate
            {
                LocalMultiControlRuntime.RefreshCombatEnergyForCurrentPlayer("combat-setup-deferred-2");
            }).CallDeferred();
        }).CallDeferred();
    }
}
