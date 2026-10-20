namespace LocalMultiControl.Scripts.Runtime.PureLogic;

/// <summary>
/// 「这一席此刻有没有**行动权**」的纯判定（r283，2026-10-19 用户要求：「注意瓦库托管和其他 mod 的
/// 接管回合效果是否有冲突，如新三国 mod 的天意侵蚀」）。
///
/// **原版语义**（`CombatManager.IsPartOfPlayerTurn`，反编译 `sts2src/src/Core/Combat/CombatManager.cs`）：
/// <list type="bullet">
/// <item>不是玩家侧 ⇒ <c>false</c>；</item>
/// <item>本回合**没有**额外回合参与者 ⇒ **所有**玩家都算"在回合里"（普通玩家侧回合）；</item>
/// <item>本回合**有**额外回合参与者（`PlayersTakingExtraTurn`，来源可以是原版【帕尔之眼】
///   `PaelsEye.AfterTakingExtraTurn`，也可以是第三方 mod 的 `ShouldTakeExtraTurn` ——
///   例如新三国「天意」`HeavensForcePower.ShouldTakeExtraTurn`）⇒ **只有名单里的那些席位**算在回合里。</item>
/// </list>
///
/// **为什么本地多控必须显式问它**：瓦库 / 战神是"同侧的多个玩家"，我们的托管链、
/// 自动结束回合、战神空闲收口、看门狗调度都是**按 `CombatState.Players` 全量**扫的 ——
/// 一旦进入"别人的额外回合"，那些**没有行动权**的席位仍会被我们驱动：
/// 替它出牌（`PlayCardAction` 不校验行动权）、替它 `PlayerCmd.EndTurn`
/// （对非参与者发结束回合动作）⇒ 轻则白打一张牌、重则把额外回合的回合链搅乱（表现就是"回合卡住"）。
///
/// 判定本身不收游戏类型，便于单测；两个入参由调用方从
/// <c>combatState.CurrentSide == CombatSide.Player</c> 与 <c>CombatManager.Instance.IsPartOfPlayerTurn(player)</c> 取。
/// </summary>
internal static class TurnParticipationPolicy
{
    /// <summary>该席位此刻可以被托管链驱动（出牌 / 收口 / 调度）。</summary>
    internal static bool CanDriveSeat(bool currentSideIsPlayer, bool isPartOfPlayerTurn)
        => currentSideIsPlayer && isPartOfPlayerTurn;
}
