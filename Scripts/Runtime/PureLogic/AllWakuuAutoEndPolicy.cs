namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「全瓦库局托管全部打完 ⇒ 自动收口」被挡回的原因（r370，纯逻辑）。
/// </summary>
internal enum AllWakuuAutoEndBlockReason
{
    /// <summary>允许收口。</summary>
    None = 0,

    /// <summary>本地多角色没开。</summary>
    LocalMultiDisabled,

    /// <summary>不在局内。</summary>
    RunNotInProgress,

    /// <summary>不在战斗内（已结算 / 转场中）。</summary>
    CombatNotInProgress,

    /// <summary>当前不是玩家侧。</summary>
    NotPlayerSide,

    /// <summary>不在出牌相位（动作队列正在跑结算）。</summary>
    NotPlayPhase,

    /// <summary>真人正处于手动出牌临界区（可能刚想接管）。</summary>
    ManualPlayGuard,

    /// <summary>有出牌 / 选牌 / 弹层在飞 —— 此时收口会把选牌链与回合链切成两半（r263 / r356）。</summary>
    SelectionBusy,

    /// <summary>某一席的托管出牌循环还挂着（它正打着，等它自己收手）。</summary>
    PlayLoopInFlight,

    /// <summary>选择器作用域在飞（同一件事的另一层记账，见 <see cref="SelectorScopeLeaseBook"/>）。</summary>
    SelectorScopeInFlight,

    /// <summary>不是全瓦库局：有真人 / 第三方 Bot / 临时席位以外的活神位（**绝不抢真人的最后一张牌**）。</summary>
    NotAllSeatsHosted,

    /// <summary>还有托管席位手上有托管能打的牌（判据必须走 `HasHostedPlayableCards`，不许绕开）。</summary>
    HostedSeatHasPlayableCard,

    /// <summary>该结束的席位都已经 ready 了（没有可收口的对象，避免 800ms 一次空转刷日志）。</summary>
    NoPendingSeat,

    /// <summary>本回合已经收口过一次（`_allPlayersAutoEndedRounds` 台账，同回合不重复收）。</summary>
    AlreadyAutoEndedThisRound,
}

/// <summary>
/// 一次「全瓦库自动收口」判定的输入（r370）。**全部字段都写成"满足 = 放行"的正向语义**，
/// 这样漏填一个字段的结果是"拒绝"而不是"误收"（漏填默认 false ⇒ 不放行）。
/// </summary>
internal readonly record struct AllWakuuAutoEndInputs(
    bool LocalMultiEnabled,
    bool RunInProgress,
    bool CombatInProgress,
    bool SideIsPlayer,
    bool PlayPhase,
    bool ManualPlayGuardActive,
    bool SelectionBusy,
    bool PlayLoopInFlight,
    bool SelectorScopeInFlight,
    bool AllAliveSeatsHosted,
    bool NoHostedSeatHasPlayableCard,
    bool HasPendingSeatToEnd,
    bool AlreadyAutoEndedThisRound);

/// <summary>
/// 全瓦库局「所有瓦库打完 ⇒ 自动结束回合」的判据（r370，纯逻辑，可直接单测）。
///
/// **要解决的问题**（用户 2026-11-16 原话）：「以后做全瓦库局所有瓦库打完自动结束回合」。
/// 实测（4 席全瓦库局）：`检测到全员无牌可出，已自动结束全部角色回合` 出现 99 次，其中 96 次
/// `source=manual-end-turn`、只有 3 次 `auto-end-turn-fallback` —— 回合链实际上靠**真人点按钮**在推，
/// 没人点就停在原地。原因是 `TryEndAllPlayersWhenNoCards` 只有两个调用点：真人点击、以及
/// "已经有人结束了回合"之后的兜底；没有任何"自由跑"的 tick 在托管打光后主动收口。
///
/// **四条硬口径（每条都对应一次踩过的坑）**：
/// <list type="bullet">
///   <item>四项条件缺一不可：全员无托管可打牌 + 无在飞出牌租约 + 无待作答选择器 + 前台/真人席位也没牌
///   （r263 / r358 两次死锁都是"还该打的时候被抢收"）；</item>
///   <item>只在**所有活着的可行动席位都是托管席位**时生效 —— 有真人席位的局不许抢
///   （真人可能就想自己打最后一张）；</item>
///   <item>判据里的"有没有牌可打"必须来自 `HasHostedPlayableCards`（托管口径：把"需要目标却没目标"
///   和"托管打它会抛"的牌剔掉），**不许绕开**；</item>
///   <item>命中后只调 `TryEndAllPlayersWhenNoCards`（照走台账 + `WakuuTurnEndOrigin.BeginModIssuedEnd()`），
///   不另写一套收口。</item>
/// </list>
/// </summary>
internal static class AllWakuuAutoEndPolicy
{
    /// <summary>
    /// 观察窗：所有条件第一次同时成立后，再等这么久才真的收口。
    ///
    /// 为什么要等：托管刚打完最后一张牌的**同一瞬间**玩家可能正想接管（点手牌 / 切人），
    /// 立刻收口会把人从"我正要打"变成"我的回合没了"。0.8s 足够覆盖这个反应窗，
    /// 又短到不让人觉得"打完了怎么还卡着"。
    /// </summary>
    public const long QuietWindowMs = 800L;

    /// <summary>门：这一次 tick 能不能开始/维持观察窗。</summary>
    public static AllWakuuAutoEndBlockReason Evaluate(AllWakuuAutoEndInputs inputs)
    {
        // 环境四连：顺序与 `ForceEndTurnPolicy` 一致，日志里能一眼看出卡在哪一层。
        if (!inputs.LocalMultiEnabled)
        {
            return AllWakuuAutoEndBlockReason.LocalMultiDisabled;
        }

        if (!inputs.RunInProgress)
        {
            return AllWakuuAutoEndBlockReason.RunNotInProgress;
        }

        if (!inputs.CombatInProgress)
        {
            return AllWakuuAutoEndBlockReason.CombatNotInProgress;
        }

        if (!inputs.SideIsPlayer)
        {
            return AllWakuuAutoEndBlockReason.NotPlayerSide;
        }

        if (!inputs.PlayPhase)
        {
            return AllWakuuAutoEndBlockReason.NotPlayPhase;
        }

        if (inputs.ManualPlayGuardActive)
        {
            return AllWakuuAutoEndBlockReason.ManualPlayGuard;
        }

        // 危险类先判：回合链被从中间切开（r263 / r356）比"少收一次"代价大得多。
        if (inputs.SelectionBusy)
        {
            return AllWakuuAutoEndBlockReason.SelectionBusy;
        }

        if (inputs.PlayLoopInFlight)
        {
            return AllWakuuAutoEndBlockReason.PlayLoopInFlight;
        }

        if (inputs.SelectorScopeInFlight)
        {
            return AllWakuuAutoEndBlockReason.SelectorScopeInFlight;
        }

        // 范围：有真人席位就一律不抢（真人可能想自己打）。
        if (!inputs.AllAliveSeatsHosted)
        {
            return AllWakuuAutoEndBlockReason.NotAllSeatsHosted;
        }

        if (!inputs.NoHostedSeatHasPlayableCard)
        {
            return AllWakuuAutoEndBlockReason.HostedSeatHasPlayableCard;
        }

        if (!inputs.HasPendingSeatToEnd)
        {
            return AllWakuuAutoEndBlockReason.NoPendingSeat;
        }

        if (inputs.AlreadyAutoEndedThisRound)
        {
            return AllWakuuAutoEndBlockReason.AlreadyAutoEndedThisRound;
        }

        return AllWakuuAutoEndBlockReason.None;
    }

    public static bool IsAllowed(AllWakuuAutoEndBlockReason reason)
    {
        return reason == AllWakuuAutoEndBlockReason.None;
    }

    /// <summary>
    /// 观察窗是否到期。<paramref name="quietSinceMs"/> ≤ 0 表示"还没开始计时"。
    /// 时钟回绕/相同刻（<paramref name="nowMs"/> 不大于起点）一律视为未到期 —— 宁可不收，也别乱收。
    /// </summary>
    public static bool ShouldFire(long quietSinceMs, long nowMs, long windowMs)
    {
        if (quietSinceMs <= 0L || nowMs <= quietSinceMs)
        {
            return false;
        }

        return nowMs - quietSinceMs >= windowMs;
    }
}
