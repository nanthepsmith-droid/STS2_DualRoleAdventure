using HarmonyLib;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 守卫本 mod 的瓦库托管遗物（【瓦库形态】/【永久低语耳环】）不被第三方效果移除（r83）。
///
/// 实证案例（2026-09-08 实机日志，marker r82）：TouhouAncients 的遗物
/// 【无底之胃】（TOUHOUANCIENTS-BOTTOMLESS_STOMACH，拾起时"吞噬你初始遗物与先古遗物
/// 以外的全部遗物"）把 2 号玩家的【瓦库形态】一并吃掉（遗物栏 19 → 4），
/// 托管判据只剩"是否持有形态遗物"，遗物没了 → 瓦库彻底停止自动操作。
///
/// 对策：待移除的遗物是本 mod 托管遗物、且该玩家仍在瓦库名单中时，跳过本次移除
/// （不吞噬、也不参与对方的结算）。玩家已被取消瓦库勾选时按原版正常移除，不留残留。
///
/// 移除链路唯一出口是 <see cref="Player.RemoveRelicInternal"/>（原版 RelicCmd.Remove
/// 也只是转调它），因此在此拦截即可覆盖所有第三方"移除/吞噬遗物"效果。
/// </summary>
[HarmonyPatch(typeof(Player), nameof(Player.RemoveRelicInternal))]
internal static class PlayerRemoveRelicInternalGuardPatch
{
    [HarmonyPrefix]
    private static bool Prefix(Player __instance, RelicModel relic)
    {
        if (__instance == null || relic == null)
        {
            return true;
        }

        if (relic is not LocalWakuuFormRelic && relic is not LocalWakuuStarterRelic)
        {
            return true;
        }

        if (!LocalWakuuAutopilotConfig.KeepWakuuFormRelic)
        {
            return true;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.IsWakuuEnabled(__instance.NetId))
        {
            return true;
        }

        LocalMultiControlLogger.Warn(
            $"已阻止第三方效果移除瓦库托管遗物（保持托管不中断）: player={__instance.NetId}, relic={relic.Id.Entry}");
        return false;
    }
}
