using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
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
        // r285：**先把战斗级台账按"战斗身份"复位**，再谈"新一段回合"。
        // 复位枢纽原本只挂在看门狗那条 100ms 重活里，而新一场战斗的第一个 SetupPlayerTurn 往往更早
        // ⇒ 上一场同号回合的记录会残留（r284 日志：`[回合归属] … 额外回合` 4 条其实是误报，
        // `检测到战斗场次切换` 反而更晚才打）。提前复位后，下面那条锚点才真的是"额外回合"。
        if (player?.Creature?.CombatState is CombatState combatStateForReset)
        {
            LocalMultiControlRuntime.RefreshCombatScopedStateAtTurnStart(combatStateForReset, "turn-start-setup");
        }

        // r283：**新的一段玩家回合开始**（普通回合或额外回合）⇒ 忘掉这一位"上一段已结束回合"的记忆。
        // 额外回合（原版帕尔之眼 / 新三国「天意」这类接管回合的效果）**不开新回合号**，
        // 不清的话旧记忆会让这一位在额外回合里永久不许自动出牌、也不被自动收口 ⇒ 额外回合卡住。
        // 普通回合里回合号变了本就没有命中；真的清掉了 ⇒ 说明这是"同一回合里的新一段"，顺手当观测锚点。
        if (player != null)
        {
            int round = player.Creature?.CombatState?.RoundNumber ?? -1;
            if (round >= 0 && WakuuTurnEndOrigin.ForgetTurnEnd(player.NetId, round))
            {
                LocalMultiControlLogger.Info(
                    $"[回合归属] 同一回合里开了新一段回合（**额外回合**）⇒ 清掉「已结束回合」记忆: "
                    + $"player={player.NetId}, round={round}");
            }
        }

        // 战灵召唤 Phase 2（正式召唤）：真人席位回合开始 ⇒ 延后一帧弹"召唤选择"（消耗计数）；
        // 开关没开时立即返回（零影响）。**不新增 Harmony 目标**（挂在既有 hook 上）。
        if (HellwarSummonCombatRuntime.OnPlayerTurnStart(player, "turn-start-setup"))
        {
            return;
        }

        // 战灵召唤 Phase 0 POC（调试）：每场战斗在玩家回合开始时（延后一帧）生成一个"哑战神"。
        // 返回值 true = 这一席就是**临时席位（战神）**：它没有手牌 UI、也永远不进前台，
        // 替它结束本回合后**直接返回**，别让下面那段前台/能量/视角逻辑把它当前台切来切去。
        if (HellwarPocRuntime.OnPlayerTurnStart(player, "turn-start-setup"))
        {
            return;
        }

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
        // R3：前台取值走统一入口；"两者都空 ⇒ null"（而不是 0）的语义保持不变
        ulong foregroundSeatId = LocalSeatSource.ForegroundSeatId();
        ulong? previousForegroundId = peek && foregroundSeatId != 0UL ? foregroundSeatId : null;

        if (!ControlContextAlignment.TryEnsureForegroundForPlayer(player, "turn-start-setup"))
        {
            return;
        }

        // BUG-39（2026-11-04）：回合开始是**一整批**逐席位切换（原版"全员同时行动"，
        // 日志里 `source=turn-start-setup` 会连跳 N 次），最后一个席位成为前台
        // ⇒ 从第 2 回合起玩家一睁眼看到的是最后一位真人的手牌/视角。
        // 记一笔"扫描开始前是谁"，等这批扫完（延时窗口内没有新的切换）再还原。
        if (foregroundSeatId != 0UL && foregroundSeatId != player.NetId)
        {
            LocalMultiControlRuntime.NoteTurnStartSweepSwitch(
                foregroundSeatId,
                player.NetId,
                "turn-start-setup");
        }

        if (peek)
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

        // BUG-39（2026-11-04）：回合结束**不再为后台席位抢前台**。
        // 原版是"全员同时行动"，回合结束会对每一席各调一次 DoTurnEnd/FlushPlayerHand，
        // 原来这里无差别切前台且切完不还 ⇒ 一回合扫 N 次，扫完停在最后一位（实测日志
        // `自动切换前台 … source=turn-end-hooks` ×N + `source=turn-end-flush` ×N）。
        // 回合结束真正需要前台的只有**交互式选牌**（保留手牌等），
        // 它由 CardSelectForegroundSwitchPatch 在 FromHand/FromHandForDiscard/… 入口**同步**切前台
        // 并登记选择者；数据类 hook 与"弃掉后台角色的手牌"都不需要前台（后台手牌本来就没建 UI 节点）。
        if (!LocalSeatSource.CurrentSeats().IsControlled(player.NetId))
        {
            return;
        }

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.TurnEnd))
        {
            return;
        }

        ControlContextAlignment.TryEnsureForegroundForPlayer(player, "turn-end-hooks");
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

        // BUG-39：口径同上（弃牌阶段也不为后台席位抢前台）。
        if (!LocalSeatSource.CurrentSeats().IsControlled(player.NetId))
        {
            return;
        }

        if (LocalWakuuRelicRuntime.ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.TurnEnd))
        {
            return;
        }

        ControlContextAlignment.TryEnsureForegroundForPlayer(player, "turn-end-flush");
    }
}
