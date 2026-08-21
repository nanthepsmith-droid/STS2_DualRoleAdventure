using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地双角色模式下，后台角色触发的 Hook 动作（例如回合结束阶段 Mini-Hakkero 的手牌选牌）在
/// ActionQueueSynchronizer.EnqueueHookAction 入队瞬间即可确定所属玩家（GenericHookGameAction.OwnerId）。
/// 若依赖“选牌 UI 弹出时”再切前台（CardSelectForegroundSwitchPatch / CombatManagerDoTurnEndForegroundPatch），
/// 会因回合循环执行顺序导致前台仍是另一位角色，弹窗展示的是错误角色的手牌。
/// 本类在前台在入队瞬间同步切到 Owner，早于选牌 UI 弹出，保证选择与前台/逻辑 owner 三者一致。
/// </summary>
[HarmonyPatch(typeof(ActionQueueSynchronizer), "EnqueueHookAction")]
internal static class HookEnqueueForegroundPatch
{
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static void Prefix(GenericHookGameAction gameAction)
    {
        if (gameAction == null)
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (RunManager.Instance.NetService is not LocalLoopbackHostGameService)
        {
            return;
        }

        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(gameAction.OwnerId))
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayerId(gameAction.OwnerId, $"hook-enqueue-{gameAction.HookId}");
    }
}
