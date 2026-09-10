namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 结束回合按钮「自愈」判定的纯函数（BUG-2：真人先结束回合后，切到瓦库点结束回合无效）。
///
/// 背景：本地多控下回合结束按钮的状态机绑定在「当前前台角色」上。原版按钮只在
/// <c>TurnStarted</c> / <c>PlayerEndedTurn</c> 等事件里切状态，而这些事件在本 mod 的
/// 本地多控流程里并不可靠（自动切人、瓦库自动结束回合都会绕开它），于是会出现
/// 「前台角色明明还能出牌/还能结束回合，按钮却停在禁用或隐藏状态」——
/// 表现为点结束回合没反应（按钮被 Disable 时连点击事件都不会派发），
/// 而切回自己再切回来（会重建战斗UI并重评按钮）又能用了。
///
/// 这里只判定「要不要动手重评」，不碰任何 Godot / 游戏类型，便于单测。
/// 口径刻意保守：只在「前台角色确认可操作但按钮不可用」时才返回 true，
/// 其余情况一律不动，避免与游戏自身的状态机打架。
/// </summary>
internal static class EndTurnButtonReconcilePolicy
{
    /// <summary>
    /// 是否需要为「前台可操作角色」自愈结束回合按钮。
    /// </summary>
    /// <param name="playerSideActive">当前是玩家方回合（<c>CombatState.CurrentSide == Player</c>）。</param>
    /// <param name="combatInProgress">战斗进行中。</param>
    /// <param name="foregroundAlive">前台玩家存在且存活。</param>
    /// <param name="foregroundReady">前台玩家已结束本回合（已 ready）。</param>
    /// <param name="inPickFlow">当前有进行中的出牌/选牌/瞄准流程（此时不动按钮）。</param>
    /// <param name="buttonStateEnabled">按钮内部状态是否为 Enabled（非 Disabled / Hidden）。</param>
    /// <param name="buttonInputEnabled">按钮输入是否可用（<c>NClickableControl.IsEnabled</c>，禁用时点击不会派发）。</param>
    /// <returns>true = 需要重评一次结束回合按钮；false = 保持现状。</returns>
    public static bool ShouldReconcile(
        bool playerSideActive,
        bool combatInProgress,
        bool foregroundAlive,
        bool foregroundReady,
        bool inPickFlow,
        bool buttonStateEnabled,
        bool buttonInputEnabled)
    {
        // 不在玩家回合 / 战斗没开 / 没有可操作的前台角色：不动
        if (!playerSideActive || !combatInProgress || !foregroundAlive)
        {
            return false;
        }

        // 前台角色自己已经结束回合（按钮此时本该是「撤销结束回合」态）或正处于
        // 出牌/选牌流程中：交给游戏自己的状态机，别抢
        if (foregroundReady || inPickFlow)
        {
            return false;
        }

        // 状态与输入都正常 → 零开销放过（绝大多数帧走这里）
        return !buttonStateEnabled || !buttonInputEnabled;
    }
}
