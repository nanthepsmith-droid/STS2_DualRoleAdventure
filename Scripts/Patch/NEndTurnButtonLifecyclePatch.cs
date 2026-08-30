using System;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 结束回合按钮生命周期的诊断探针：记录按钮 SetState 每次切换、turn 相位入口、
/// 敌方回合切换瞬间的按钮状态/位置，用于定位第一回合结束时按钮谜之失踪的确切时序。
/// 仅在本 mod 启用时打印（无全局量的会话零开销）。
/// </summary>
// 多目标容器：类级裸 [HarmonyPatch] 让 PatchAll 处理本类，
// 具体目标由各方法级 [HarmonyPatch(...)] 指定。
// 此前缺失类级标记导致整个类被 PatchAll 静默跳过（本 mod 坑 1），探针从未触发。
[HarmonyPatch]
internal static class NEndTurnButtonLifecyclePatch
{
    private static bool IsActive()
    {
        return LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode;
    }

    private static string LogButtonState(NEndTurnButton button)
    {
        try
        {
            FieldInfo? stateField = AccessTools.Field(typeof(NEndTurnButton), "_state");
            int state = stateField != null && stateField.GetValue(button) != null ? Convert.ToInt32(stateField.GetValue(button)) : -1;
            bool inCardSel = false;
            NCombatUi? ui = button.GetParentOrNull<NCombatUi>();
            if (ui != null && ui.Hand != null)
            {
                inCardSel = ui.Hand.IsInCardSelection;
            }

            return $"state={state}, y={button.Position.Y:0.0}, inCardSel={inCardSel}, netId={LocalContext.NetId?.ToString() ?? "null"}";
        }
        catch (Exception exception)
        {
            return $"logFail: {exception.Message}";
        }
    }

    [HarmonyPatch(typeof(NEndTurnButton), "SetState")]
    [HarmonyPostfix]
    private static void SetStatePostfix(NEndTurnButton __instance)
    {
        if (!IsActive())
        {
            return;
        }

        LocalMultiControlLogger.Info($"按钮 SetState -> {LogButtonState(__instance)}");
    }

    [HarmonyPatch(typeof(NEndTurnButton), "OnTurnStarted")]
    [HarmonyPostfix]
    private static void OnTurnStartedPostfix(NEndTurnButton __instance, CombatState state)
    {
        if (!IsActive())
        {
            return;
        }

        Player? me = LocalContext.GetMe(state);
        LocalMultiControlLogger.Info($"按钮 OnTurnStarted: side={state.CurrentSide}, isInProgress={CombatManager.Instance.IsInProgress}, me={me?.NetId.ToString() ?? "null"} ({LogButtonState(__instance)})");

        // Bug 2 兜底：一玩家死亡后，另一存活玩家回合开始时按钮可能被误判为隐藏/禁用。
        // 在游戏自身 OnTurnStarted 之后，按「当前控制角色是否存活且未 ready」强制重评一次，
        // 确保存活玩家回合开始时必然能拿到 Enabled 按钮（死亡玩家不参与判定）。
        LocalMultiControlRuntime.ReevaluateEndTurnButtonForControlledPlayer("on-turn-started");
    }

    [HarmonyPatch(typeof(NEndTurnButton), "OnAboutToSwitchToEnemyTurn")]
    [HarmonyPostfix]
    private static void OnAboutToSwitchToEnemyTurnPostfix(NEndTurnButton __instance)
    {
        if (!IsActive())
        {
            return;
        }

        LocalMultiControlLogger.Info($"按钮 OnAboutToSwitchToEnemyTurn ({LogButtonState(__instance)})");
    }

    [HarmonyPatch(typeof(CombatManager), "AfterAllPlayersReadyToBeginEnemyTurn")]
    [HarmonyPostfix]
    private static void AfterAllPlayersReadyToBeginEnemyTurnPostfix(CombatManager __instance)
    {
        if (!IsActive())
        {
            return;
        }

        NCombatUi? ui = NCombatRoom.Instance?.Ui;
        if (ui != null)
        {
            LocalMultiControlLogger.Info($"切敌方前按钮 ({LogButtonState(ui.EndTurnButton)})");
        }
    }

    [HarmonyPatch(typeof(NCombatUi), "Activate")]
    [HarmonyPostfix]
    private static void NCombatUiActivatePostfix(NCombatUi __instance, CombatState state)
    {
        if (!IsActive())
        {
            return;
        }

        Player? me = LocalContext.GetMe(state);
        LocalMultiControlLogger.Info($"CombatUI Activate: netId={LocalContext.NetId?.ToString() ?? "null"}, me={me?.NetId.ToString() ?? "null"} ({LogButtonState(__instance.EndTurnButton)})");
    }
}
