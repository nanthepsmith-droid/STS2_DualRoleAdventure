using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 原版低语耳环钩子守卫。
/// 旧路径（永久低语耳环/未开瓦库形态）：完全放行原版行为。
/// 瓦库形态模式：持有【瓦库形态】的角色不再触发原版耳环的自动出牌（避免双接管、双台词），
/// 但其 +1 能量来自 ModifyMaxEnergy，不受本补丁影响，照常生效。
/// </summary>
[HarmonyPatch(typeof(WhisperingEarring), nameof(WhisperingEarring.AfterAutoPrePlayPhaseEnteredLate))]
internal static class WhisperingEarringPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player player, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (!LocalWakuuAutopilotConfig.SuppressVanillaEarring)
        {
            return true;
        }

        if (!LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return true;
        }

        __result = Task.CompletedTask;
        LocalMultiControlLogger.Info(
            $"瓦库形态模式：已压制原版低语耳环自动出牌钩子（+1 能量保留）: player={player.NetId}");
        return false;
    }
}
