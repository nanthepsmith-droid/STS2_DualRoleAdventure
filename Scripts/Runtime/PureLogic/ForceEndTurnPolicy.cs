namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「强行结束托管席位回合」被挡回的原因（r364，纯逻辑）。
/// </summary>
internal enum ForceEndTurnBlockReason
{
    /// <summary>允许执行。</summary>
    None = 0,

    /// <summary>本地多角色没开（单人 / 官方联机里不该出现这个入口）。</summary>
    LocalMultiDisabled,

    /// <summary>不在局内。</summary>
    RunNotInProgress,

    /// <summary>不在战斗内（已结算 / 正在转场）。</summary>
    CombatNotInProgress,

    /// <summary>当前不是玩家侧（敌方回合没有"结束回合"可言）。</summary>
    NotPlayerSide,

    /// <summary>不在出牌相位（动作队列正在跑结算）。</summary>
    NotPlayPhase,

    /// <summary>有出牌 / 选牌 / 弹层在飞 —— 此时强行结束会把选牌链与回合链切成两半。</summary>
    SelectionBusy,

    /// <summary>没有可结束的托管席位（都 ready 了 / 没有托管席位）。</summary>
    NothingToForce,
}

/// <summary>
/// 战斗内「强行结束回合」按钮的判据（r364，纯逻辑，可直接单测）。
///
/// **要解决的问题**（用户 2026-11-13 原话）：「不能切换到战神视角，现在只能等战神打到上限，有时候要等好久」。
/// 战神是**结构外临时席位**，视角切不过去；它一回合能打到上限（`cap=60`），中途想插一脚只能干等。
/// 这个按钮 = **让托管席位（战神 / 瓦库形态）本回合停手并直接结束它们的回合**，把回合交还给玩家侧推进。
///
/// **三条硬口径（都是踩过的坑）**：
/// <list type="bullet">
///   <item>只结束**托管席位**，绝不替真人席位结算 —— 真人可能就想自己打最后一张；</item>
///   <item>有出牌 / 选牌在飞时**必须拒绝**：r263 / r356 两次软锁都出在"回合链被从中间切开"
///     （选牌还在等作答、回合已经被结束）；</item>
///   <item>结束要按**外部结束**记账（不包 `WakuuTurnEndOrigin.BeginModIssuedEnd`），
///     这样托管链在下一张牌之前就熔断停手（BUG-10 口径）；包成"模组收口"反而会被放行继续打。</item>
/// </list>
/// </summary>
internal static class ForceEndTurnPolicy
{
    /// <summary>门：这一次点击能不能真的执行。</summary>
    /// <param name="endableSeatCount">当前"托管席位里还没 ready 且仍在回合里"的个数（调用方先数好）。</param>
    public static ForceEndTurnBlockReason EvaluateGate(
        bool localMultiEnabled,
        bool runInProgress,
        bool combatInProgress,
        bool sideIsPlayer,
        bool playPhase,
        bool selectionBusy,
        int endableSeatCount)
    {
        if (!localMultiEnabled)
        {
            return ForceEndTurnBlockReason.LocalMultiDisabled;
        }

        if (!runInProgress)
        {
            return ForceEndTurnBlockReason.RunNotInProgress;
        }

        if (!combatInProgress)
        {
            return ForceEndTurnBlockReason.CombatNotInProgress;
        }

        if (!sideIsPlayer)
        {
            return ForceEndTurnBlockReason.NotPlayerSide;
        }

        if (!playPhase)
        {
            return ForceEndTurnBlockReason.NotPlayPhase;
        }

        // 顺序讲究：先判"出牌/选牌在飞"，再判"有没有可结束的席位" ——
        // 前者是**危险**（会把回合链切一半），后者只是"没事可做"，日志要能分开。
        if (selectionBusy)
        {
            return ForceEndTurnBlockReason.SelectionBusy;
        }

        if (endableSeatCount <= 0)
        {
            return ForceEndTurnBlockReason.NothingToForce;
        }

        return ForceEndTurnBlockReason.None;
    }

    public static bool IsAllowed(ForceEndTurnBlockReason reason)
    {
        return reason == ForceEndTurnBlockReason.None;
    }

    /// <summary>
    /// 按钮要不要显示 —— 只看"有没有该被强行结束的托管席位"，**不看**出牌/选牌是否在飞
    /// （在飞时点一下会得到明确提示，比按钮忽然消失更好定位）。
    /// </summary>
    public static bool ShouldShowButton(
        bool localMultiEnabled,
        bool runInProgress,
        bool combatInProgress,
        bool sideIsPlayer,
        bool playPhase,
        int endableSeatCount)
    {
        return EvaluateGate(
            localMultiEnabled,
            runInProgress,
            combatInProgress,
            sideIsPlayer,
            playPhase,
            selectionBusy: false,
            endableSeatCount) == ForceEndTurnBlockReason.None;
    }

    /// <summary>这一席本次要不要被强行结束。</summary>
    /// <param name="hostedSeat">托管席位（接管遗物的瓦库 / 临时席位战神，见 `IsAutoplayHosted`）。</param>
    /// <param name="hasCreature">有战斗实体。</param>
    /// <param name="isAlive">还活着（死了的席位原版自己会跳过）。</param>
    /// <param name="participatesInTurn">本段玩家回合的行动参与者（额外回合里非参与者不能行动，见 `TurnParticipationPolicy`）。</param>
    /// <param name="readyToEndTurn">已经 ready —— 再发一次结束动作会搅乱回合链。</param>
    public static bool ShouldEndSeat(
        bool hostedSeat,
        bool hasCreature,
        bool isAlive,
        bool participatesInTurn,
        bool readyToEndTurn)
    {
        if (!hostedSeat || !hasCreature || !isAlive || !participatesInTurn)
        {
            return false;
        }

        return !readyToEndTurn;
    }
}
