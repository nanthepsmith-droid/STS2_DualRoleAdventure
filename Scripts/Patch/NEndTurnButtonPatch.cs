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

        // r128：真人点结束回合 / 撤销也必须**立刻**看起来生效。原版 `CallReleaseLogic` 会把
        // `EndPlayerTurnAction` 入队（按全局 action ID 排序），而并发出牌档下瓦库常年在队列前排占着位置 ⇒
        // 真人的点击要排在它们后面（实机 r127 日志：行 8184 入队 id 24 → 行 8413 才执行，
        // 其间还夹着 id 25/26/27 三张瓦库牌，用户观感就是"点了没反应、要等瓦库打完"）。
        // 与真人出牌同一条"让真人插队"通道：撤掉瓦库尚未开始执行的入队动作。
        if (me != null)
        {
            LocalWakuuRelicRuntime.YieldPendingQueuePlaysToHuman(me.NetId, "end-turn-button");
        }

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
