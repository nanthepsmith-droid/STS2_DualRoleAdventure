using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗中回合循环会按角色的顺序依次执行回合开始/回合结束 hook（CombatManager.SetupPlayerTurn/DoTurnEnd/FlushPlayerHand）。
/// 单人双角色模式下，后台角色触发的效果（如回合开始抽牌/保留手牌选牌）也需要该角色的顶层UI与上下文在前台，
/// 因此在这些 hook 执行前把前台自动切到对应角色。
/// </summary>
[HarmonyPatch(typeof(CombatManager), "SetupPlayerTurn")]
internal static class CombatManagerSetupPlayerTurnForegroundPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player)
    {
        if (player?.Creature == null || !player.Creature.IsAlive)
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-start-setup");
    }
}

[HarmonyPatch(typeof(CombatManager), "DoTurnEnd")]
internal static class CombatManagerDoTurnEndForegroundPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player)
    {
        if (player?.Creature == null)
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-end-hooks");
    }
}

[HarmonyPatch(typeof(CombatManager), "FlushPlayerHand")]
internal static class CombatManagerFlushPlayerHandForegroundPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player)
    {
        if (player?.Creature == null)
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-end-flush");
    }
}