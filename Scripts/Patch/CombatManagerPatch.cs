using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
internal static class CombatManagerPatch
{
    [HarmonyPrefix]
    private static void Prefix(Player player, ref bool canBackOut, ref Func<Task>? actionDuringEnemyTurn)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        // BUG-10 收口第二版：登记"这一位的回合结束是谁造成的"。
        // `CombatManager.SetReadyToEndTurn` 是所有"结束回合"的唯一落点（按钮/卡牌效果/模组收口都走它），
        // 在这里归因最可靠：`WakuuTurnEndOrigin.BeginModIssuedEnd()` 包裹的 = 模组自己收口，
        // 其余（虚空形态这类卡牌效果、原版逻辑）= 外部强行结束。
        WakuuTurnEndOrigin.Record(player.NetId, player.Creature?.CombatState?.RoundNumber ?? -1);

        if (canBackOut)
        {
            canBackOut = false;
            LocalMultiControlLogger.Info($"本地双人模式禁用回合回退: player={player.NetId}");
        }
    }
}
