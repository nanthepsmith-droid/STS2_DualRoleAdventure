using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 战斗中回合循环会按角色的顺序依次执行回合开始/回合结束 hook（CombatManager.SetupPlayerTurn/DoTurnEnd/FlushPlayerHand）。
/// 单人双角色模式下，后台角色触发的效果（如回合开始抽牌/保留手牌选牌）也需要该角色的顶层UI与上下文在前台，
/// 因此在这些 hook 执行前把前台自动切到对应角色。
/// 瓦库形态后台托管开启时不为该角色切换（模型层 hook 无需前台；交互式选牌由
/// CardSelectForegroundSwitchPatch 在无选择器时兜底切换）。
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

        // Bug 2 兜底：一玩家死亡后，另一存活玩家的回合开始阶段结束时按钮可能被误判为隐藏/禁用。
        // 原版 NEndTurnButton.OnTurnStarted 依赖 TurnStarted 事件；在本地多控下该路径并不可靠
        // （日志里 SetState/OnTurnStarted 探针均未触发）。此处挂在确认每次存活玩家回合开始都会
        // 触发的 SetupPlayerTurn 上，按「当前控制角色是否存活且未 ready」强制重评结束回合按钮，
        // 确保存活玩家回合开始后必然拿到 Enabled 按钮（死亡玩家不参与判定）。
        // 注意：放在瓦库前台抑制判断之前，保证真人角色回合开始也必然重评，不受瓦库托管影响。
        LocalMultiControlRuntime.ReevaluateEndTurnButtonForControlledPlayer("turn-start-setup");

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: false))
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

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: false))
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

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, onlyWhenSelectorActive: false))
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-end-flush");
    }
}
