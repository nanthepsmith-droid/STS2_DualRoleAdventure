using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace LocalMultiControl.Scripts.Patch;

[HarmonyPatch(typeof(NEndTurnButton), "CallReleaseLogic")]
internal static class NEndTurnButtonPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NEndTurnButton __instance)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        CombatState? combatState = AccessTools.Field(typeof(NEndTurnButton), "_combatState")?.GetValue(__instance) as CombatState;
        if (combatState == null)
        {
            return true;
        }

        // r104（BUG-2）：点击目标一律取**前台玩家**，不再直接信会为瓦库后台出牌漂移的 LocalContext。
        // 漂移到「已经结束回合的角色」身上时，原版会把这次点击当成「撤销结束回合」处理——
        // 观感就是点结束回合完全没反应（切回自己再切到瓦库、上下文重新对齐后才恢复）。
        // 这里顺带把上下文校正到前台玩家，保证后续原版逻辑结算的正是玩家正在看的角色。
        ulong? clickTargetId = LocalMultiControlRuntime.AlignLocalContextToForegroundForEndTurn();
        Player? me = clickTargetId.HasValue
            ? combatState.GetPlayer(clickTargetId.Value)
            : LocalContext.GetMe(combatState);

        bool handled = LocalMultiControlRuntime.TryManualEndTurnAutoCloseAllPlayers();
        if (handled)
        {
            LocalMultiControlLogger.Info("回合结束点击已按“全员无牌可出”规则处理，跳过原始结束逻辑。");
            return false;
        }

        if (me != null)
        {
            LocalMultiControlRuntime.RecordManualEndTurnIntent(me.NetId, "end-turn-button");
        }

        if (me != null && CombatManager.Instance.IsPlayerReadyToEndTurn(me))
        {
            // 本地多控下这个分支只在「点击目标自己已经结束回合」时命中；
            // 一旦是上下文漂移导致的误判，r104 的前台校正已经把目标修正回前台玩家，
            // 所以不会再把正常点结束吞掉。
            LocalMultiControlLogger.Info(
                $"忽略结束回合回退点击（点击目标已结束回合）: player={me.NetId}, context={LocalContext.NetId?.ToString() ?? "null"}");
            return false;
        }

        // 诊断：走到这里仍未被接管（返回 true 交回原版）时打出全部门禁条件，
        // 便于下次复现直接定位是「按钮被禁用（连这里都进不来）」还是「判定取错玩家」。
        LocalMultiControlRuntime.LogEndTurnButtonClickGate(me, __instance, "end-turn-button");
        return true;
    }
}
