using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using LocalMultiControl.Scripts.Patch;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalControlSwitchGuard
{
    public static bool TrySwitchNext(string source)
    {
        if (!CanSwitchNow(source))
        {
            return false;
        }

        LocalMultiControlRuntime.SwitchNextControlledPlayer(source);
        return true;
    }

    public static bool TrySwitchPrevious(string source)
    {
        if (!CanSwitchNow(source))
        {
            return false;
        }

        LocalMultiControlRuntime.SwitchPreviousControlledPlayer(source);
        return true;
    }

    public static bool TrySwitchTo(ulong playerId, string source)
    {
        if (!CanSwitchNow(source))
        {
            return false;
        }

        LocalMultiControlRuntime.SwitchControlledPlayerTo(playerId, source);
        return true;
    }

    private static bool CanSwitchNow(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return false;
        }

        // ⚠ 这两道门必须放在「非战斗直接放行」之前：占卜与假商人投掷都发生在**非战斗房间**。
        // BUG-35（2026-11-04 日志实证）：占卜锁原来只写在「下一个/上一个」两个入口，
        // 血条旁按钮与右键走的是 TrySwitchTo → SwitchControlledPlayerTo，一条腿被锁、另一条腿敞开
        // ⇒ 占卜弹层开着也能切走 ⇒ owner 校验抛 `Trying to sync crystal sphere rewards for non-local player`（软锁）。
        // 这里把「手动切人」的三条入口（按钮 / 右键 / 热键）统一收到同一口径。
        if (CrystalSphereMirrorRuntime.HasActiveDivinationOverlay())
        {
            LocalMultiControlLogger.Info($"占卜小游戏进行中，已忽略切人请求({source})。");
            return false;
        }

        if (FoulPotionOnUsePatch.IsThrowInProgress)
        {
            LocalMultiControlLogger.Info($"假商人药水投掷结算进行中，已忽略切人请求({source})。");
            return false;
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        if (combatUi.Hand.InCardPlay || combatUi.Hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            LocalMultiControlLogger.Info($"忽略切人请求({source})：当前有进行中的出牌/选牌流程。");
            return false;
        }

        if (RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            LocalMultiControlLogger.Info(
                $"忽略切人请求({source})：战斗同步阶段={RunManager.Instance.ActionQueueSynchronizer.CombatState}。");
            return false;
        }

        return true;
    }
}
