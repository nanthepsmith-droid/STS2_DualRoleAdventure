using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
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
        WarnIfPlayersCombatStateMissing(state);
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

    /// <summary>
    /// 2026-11-20 第三层（"能进战斗但什么都干不了"）：如果真有玩家在战斗建立后没有
    /// `PlayerCombatState`（= 没有抽牌堆/手牌），当场打一条 WARN 把现场写清楚 ——
    /// 这一局已经没法救（手牌永远是空的），但至少不用再靠猜。
    /// 已定位到根因 = `CombatState` 列表缓存把建战斗前的空 `Players` 端给了 `SetUpCombat`，
    /// 该缓存现已默认关闭（<c>CombatStateListCachePatch</c>）。
    /// </summary>
    private static void WarnIfPlayersCombatStateMissing(CombatState state)
    {
        try
        {
            if (state?.Players == null)
            {
                return;
            }

            List<string> missing = new();
            foreach (Player player in state.Players)
            {
                if (player?.PlayerCombatState == null)
                {
                    missing.Add(player?.NetId.ToString() ?? "null");
                }
            }

            if (missing.Count > 0)
            {
                LocalMultiControlLogger.Warn(
                    "战斗建立后仍有玩家没有战斗态（手牌将永远为空）: "
                    + $"缺失=[{string.Join(",", missing)}], 本场战斗玩家=[{string.Join(",", state.Players.Select((p) => p.NetId))}] —— "
                    + "若再出现请把这一行连同上下文发回（r385 起列表缓存默认关闭，正常不该出现）");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"战斗态完整性检查失败（忽略）: {exception.Message}");
        }
    }
}
