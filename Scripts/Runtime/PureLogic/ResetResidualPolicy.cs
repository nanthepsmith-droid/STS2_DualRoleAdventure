using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「退局复位」的一次快照（R5 第一批）。
///
/// 只收**初值可判定**的计数器 / 标志位：集合类记元素个数、哨兵类记"是否已被写入"。
/// 刻意不含两处：
/// ① `_watchdogScheduleLastSource` —— 它的"干净值"是复位时写进去的 `run-cleanup` 哨兵
///   （不等于声明初值 `none`），拿它做判据必然假阳性；
/// ② `_combatEnergyContainerDefaultPosition` —— 进程级 UI 兜底缓存（"抓到一次就复用"），
///   跨局保留是**有意的**，不进复位契约。
///
/// 也不含会话 / 席位类状态（那批归 <see cref="SeatRegistry"/> 与 `LocalSelfCoopContext` 的会话契约管），
/// 本快照只覆盖 `LocalMultiControlRuntime` 自己持有的局级与战斗级状态。
/// </summary>
internal readonly struct ResetResidualSnapshot
{
    // ── 战斗级（换战斗时重置；退局同样必须为空）──
    /// <summary>`_wakuuAutoEndIssued`（瓦库自动结束回合去重，按"战斗身份:回合:玩家"）。</summary>
    public int WakuuAutoEndIssued { get; init; }

    /// <summary>`_allPlayersAutoEndedRounds`（"全员无牌可出"已处理的回合号）。</summary>
    public int AllPlayersAutoEndedRounds { get; init; }

    /// <summary>`_skipDrawAnimLogged`（跳过回合开始抽牌演出的日志去重）。</summary>
    public int SkipDrawAnimLogged { get; init; }

    /// <summary>`_combatEnergyDiagKeys`（战斗能量归属诊断去重）。</summary>
    public int CombatEnergyDiagKeys { get; init; }

    /// <summary>`_lastAutoEndCombatIdentity` 是否已被写入（初值 `-1`）。</summary>
    public bool LastAutoEndCombatIdentitySet { get; init; }

    /// <summary>`_skipDrawAnimCombatIdentity` 是否已被写入（初值 `int.MinValue`）。</summary>
    public bool SkipDrawAnimCombatIdentitySet { get; init; }

    /// <summary>`_pendingManualEndTurnPlayerId` / `_pendingManualEndTurnRound` 是否有未消费的意图。</summary>
    public bool PendingManualEndTurnSet { get; init; }

    /// <summary>`_lastEndTurnReconcileAttemptMs` 是否非初值（结束回合按钮自愈节流）。</summary>
    public bool LastEndTurnReconcileAttemptSet { get; init; }

    /// <summary>`_endTurnReconcileLogCount`（自愈失败日志条数上限计数）。</summary>
    public int EndTurnReconcileLogCount { get; init; }

    // ── 局级（只在退局时复位；R5-4 起由 `RuntimeDiagnosticsLedger` 持有）──
    /// <summary>看门狗调度被拒的按来源计数（`RuntimeDiagnosticsLedger.WatchdogRejectCount`）。</summary>
    public int WatchdogScheduleRejectCounts { get; init; }

    /// <summary>看门狗窗口是否已开（`RuntimeDiagnosticsLedger.WatchdogWindowStarted`）。</summary>
    public bool WatchdogScheduleWindowStarted { get; init; }

    /// <summary>窗口内成功调度次数（`RuntimeDiagnosticsLedger.WatchdogSuccessCount`）。</summary>
    public int WatchdogScheduleSuccessCount { get; init; }

    /// <summary>最近一次调度的身份 / 回合是否非初值（`RuntimeDiagnosticsLedger.WatchdogLastTargetSet`）。</summary>
    public bool WatchdogScheduleLastTargetSet { get; init; }

    /// <summary>流程阻塞信号按来源计数（`RuntimeDiagnosticsLedger.FlowSignalCount`）。</summary>
    public int FlowBlockSignalCounts { get; init; }

    /// <summary>流程阻塞信号去重键数（`RuntimeDiagnosticsLedger.FlowDedupeCount`）。</summary>
    public int FlowBlockSignalDedupeRoundPlayer { get; init; }

    /// <summary>流程阻塞窗口是否已开（`RuntimeDiagnosticsLedger.FlowWindowStarted`）。</summary>
    public bool FlowBlockSignalWindowStarted { get; init; }
}

/// <summary>
/// R5 第一批：「退局 / 进局复位」的**判定**（纯逻辑，可单测）。
///
/// **为什么**：跨局残留这一族问题（会话残留 / 幽灵弹层 / AsyncLocal 残留 / 守卫卡住）在结构上是同一件事 ——
/// 静态可变状态没有**显式复位契约**，复位只靠 `OnRunCleanup()` 一处"记得写"。把"退局后应当为空的状态"
/// 写成可单测的判据 + 在进局 / 退局各打一条自检锚点，漏清一项就会在日志里被点名，
/// 而不是等玩家遇到"上一局的残留"。
///
/// **判据口径**：只判"初值可判定"的项；集合判元素个数、哨兵判是否等于初值。
/// 局内这些计数**非空是正常的**（它们本来就是窗口 / 回合级去重），所以"非空即残留"只在
/// **进局（`RunManager.Launch` 之前）与退局（`RunManager.CleanUp` 之后）**这两个时刻成立 ——
/// 调用方负责只在那一刻调用本类。
/// </summary>
internal static class ResetResidualPolicy
{
    /// <summary>
    /// 返回"此刻不该还有值"的项（`键=数值` 形式，顺序固定 ⇒ 日志可逐字对比）。
    /// 全部干净 ⇒ 空列表。
    /// </summary>
    internal static IReadOnlyList<string> FindResiduals(ResetResidualSnapshot snapshot)
    {
        List<string> residuals = new List<string>();

        // 战斗级
        AddIfNonZero(residuals, "wakuu-auto-end-issued", snapshot.WakuuAutoEndIssued);
        AddIfNonZero(residuals, "all-players-auto-ended-rounds", snapshot.AllPlayersAutoEndedRounds);
        AddIfNonZero(residuals, "skip-draw-anim-logged", snapshot.SkipDrawAnimLogged);
        AddIfNonZero(residuals, "combat-energy-diag-keys", snapshot.CombatEnergyDiagKeys);
        AddIfSet(residuals, "last-auto-end-combat-identity", snapshot.LastAutoEndCombatIdentitySet);
        AddIfSet(residuals, "skip-draw-anim-combat-identity", snapshot.SkipDrawAnimCombatIdentitySet);
        AddIfSet(residuals, "pending-manual-end-turn", snapshot.PendingManualEndTurnSet);
        AddIfSet(residuals, "end-turn-reconcile-attempt", snapshot.LastEndTurnReconcileAttemptSet);
        AddIfNonZero(residuals, "end-turn-reconcile-log-count", snapshot.EndTurnReconcileLogCount);

        // 局级
        AddIfNonZero(residuals, "watchdog-schedule-rejects", snapshot.WatchdogScheduleRejectCounts);
        AddIfSet(residuals, "watchdog-schedule-window", snapshot.WatchdogScheduleWindowStarted);
        AddIfNonZero(residuals, "watchdog-schedule-success-count", snapshot.WatchdogScheduleSuccessCount);
        AddIfSet(residuals, "watchdog-schedule-last-target", snapshot.WatchdogScheduleLastTargetSet);
        AddIfNonZero(residuals, "flow-block-signals", snapshot.FlowBlockSignalCounts);
        AddIfNonZero(residuals, "flow-block-dedupe", snapshot.FlowBlockSignalDedupeRoundPlayer);
        AddIfSet(residuals, "flow-block-window", snapshot.FlowBlockSignalWindowStarted);

        return residuals;
    }

    /// <summary>
    /// 一条自检结论文案：干净 ⇒ `无残留`，否则把命中项按固定顺序拼成 `键=值, …`
    /// （调用方拼上下文前缀，如 `进局复位自检: 无残留` / `退局复位自检发现残留: …`）。
    /// </summary>
    internal static string Describe(IReadOnlyList<string> residuals)
    {
        return residuals.Count == 0 ? "无残留" : string.Join(", ", residuals);
    }

    private static void AddIfNonZero(List<string> residuals, string key, int value)
    {
        if (value != 0)
        {
            residuals.Add($"{key}={value}");
        }
    }

    private static void AddIfSet(List<string> residuals, string key, bool isSet)
    {
        if (isSet)
        {
            residuals.Add($"{key}=set");
        }
    }
}
