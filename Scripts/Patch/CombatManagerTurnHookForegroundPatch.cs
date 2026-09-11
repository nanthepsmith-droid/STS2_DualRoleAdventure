using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
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

        // BUG-1：能量球必须与当前展示的手牌同属一个玩家。进战斗前前台停在瓦库托管角色时，
        // 会出现「手牌是真人玩家、能量球是瓦库的（含 +1 能量数值）」。这里在回合开始校正一次，
        // 无论本回合是否要切前台；只在真的不一致时重建能量球（基准 = 手牌区里实际卡牌的持有者）。
        LocalMultiControlRuntime.EnsureCombatEnergyMatchesHand("turn-start-setup");
        // 回合开始瞬间手牌可能还没发出来（读不到真实归属）→ 帧末再校一次。
        LocalMultiControlRuntime.ScheduleEnsureCombatEnergyMatchesHand("turn-start-deferred");

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.TurnStart))
        {
            return;
        }

        // 改进-1：跳过「其他人」的回合开始抽牌演出——只保留「当前正在看的那位」的演出，
        // 其他人不再切前台（其抽牌按原版规则对非本地玩家不做动画，数据照常）。
        // 注意：后台托管的瓦库形态角色**不由此开关管辖**（见 ShouldSkipTurnStartDrawAnimationFor），
        // 它的回合开始视角完全由「瓦库托管视角」档位决定，否则 keyNodes 会被本开关静默吃掉。
        if (LocalMultiControlRuntime.ShouldSkipTurnStartDrawAnimationFor(player, "turn-start-setup"))
        {
            return;
        }

        // 改进-2「仅关键节点」：切过去看一眼（peek），随后延时自动切回原先的真人视角。
        bool peek = WakuuViewPolicy.ShouldPeekAtTurnStart(
            LocalWakuuAutopilotConfig.ViewMode,
            LocalWakuuAutopilotConfig.BackgroundMode,
            LocalWakuuRelicRuntime.IsVakuuFormMode(player));
        ulong? previousForegroundId = peek
            ? LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId ?? LocalContext.NetId
            : null;

        if (LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-start-setup") && peek)
        {
            LocalMultiControlRuntime.ScheduleReturnToForegroundAfterPeek(
                player.NetId, previousForegroundId, "turn-start-setup");
        }
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

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.TurnEnd))
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

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.TurnEnd))
        {
            return;
        }

        LocalMultiControlRuntime.TryEnsureForegroundForPlayer(player, "turn-end-flush");
    }
}
