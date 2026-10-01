using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 诊断 / 节流窗口台账（R5-4）：把原 `LocalMultiControlRuntime` 里两处"2 秒时间片窗口"的规则钉死。
///
/// 要钉死的语义（都来自原实现的逐字行为）：
/// ① **懒开窗**：第一条记录才开窗；刚开窗那一刻 `windowMs` 还是 0 ⇒ 本次不到点、不打日志；
/// ② **到点判据 = `>= 2000ms`**，日志里的 `windowMs=` 取**翻滚前**的经过时间；
/// ③ **翻滚清什么**：看门狗清"成功计数 + 被拒表"但**保留最近身份**；流程信号清计数表但**不清去重集**
///    （同一回合同一玩家只报一次是刻意的）；
/// ④ **键格式**：被拒表用 `reason`（空则 `unknown`）、流程信号用 `signal:reason`、去重键用 `signal:round:player`
///    —— 这些键会出现在日志里，改了就等于改了 `log_scan` 的锚点；
/// ⑤ **退局复位**：两张窗口都回到"未开窗"，计数与去重清空，`lastSource` 写成传入哨兵。
/// </summary>
[TestFixture]
public class RuntimeDiagnosticsLedgerTests
{
    private const long T0 = 1000L;

    [SetUp]
    public void SetUp()
    {
        RuntimeDiagnosticsLedger.Reset("test-setup");
    }

    // ── 瓦库看门狗调度窗口 ──

    [Test]
    public void 看门狗_懒开窗_首条记录不到点()
    {
        Assert.That(RuntimeDiagnosticsLedger.WatchdogWindowStarted, Is.False);

        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 11, 1, "watchdog", T0);

        Assert.That(RuntimeDiagnosticsLedger.WatchdogWindowStarted, Is.True);
        Assert.That(RuntimeDiagnosticsLedger.IsWatchdogWindowDue(T0), Is.False, "刚开窗时 windowMs=0，不该到点");
        Assert.That(RuntimeDiagnosticsLedger.WatchdogWindowElapsedMs(T0), Is.EqualTo(0L));
    }

    [Test]
    public void 看门狗_成功计数与按原因被拒_含空原因兜底()
    {
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 11, 1, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 11, 1, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "hand-in-card-play", 11, 1, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "hand-in-card-play", 11, 1, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "", 11, 1, "watchdog", T0);

        Assert.That(RuntimeDiagnosticsLedger.WatchdogSuccessCount, Is.EqualTo(2));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogRejectCount, Is.EqualTo(2), "hand-in-card-play 与 unknown 两条");
        Assert.That(RuntimeDiagnosticsLedger.DescribeWatchdogRejects(),
            Is.EqualTo("hand-in-card-play:2,unknown:1"));
    }

    [Test]
    public void 看门狗_到点判据为2000毫秒()
    {
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 11, 1, "watchdog", T0);

        Assert.That(RuntimeDiagnosticsLedger.IsWatchdogWindowDue(T0 + 1999L), Is.False);
        Assert.That(RuntimeDiagnosticsLedger.IsWatchdogWindowDue(T0 + 2000L), Is.True);
        Assert.That(RuntimeDiagnosticsLedger.WatchdogWindowElapsedMs(T0 + 2000L), Is.EqualTo(2000L),
            "日志里的 windowMs 取翻滚前的经过时间");
    }

    [Test]
    public void 看门狗_最近身份每次都更新_翻滚时保留()
    {
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 11, 1, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "reason-a", 22, 3, "auto-end-turn-next-playable", T0 + 500L);

        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastPlayerId, Is.EqualTo(22UL));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastRound, Is.EqualTo(3));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastSource, Is.EqualTo("auto-end-turn-next-playable"));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastTargetSet, Is.True);

        RuntimeDiagnosticsLedger.RollWatchdogWindow(T0 + 2000L);

        Assert.That(RuntimeDiagnosticsLedger.WatchdogSuccessCount, Is.EqualTo(0), "翻滚清成功计数");
        Assert.That(RuntimeDiagnosticsLedger.WatchdogRejectCount, Is.EqualTo(0), "翻滚清被拒表");
        Assert.That(RuntimeDiagnosticsLedger.DescribeWatchdogRejects(), Is.EqualTo("none"));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastPlayerId, Is.EqualTo(22UL), "最近身份刻意保留");
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastRound, Is.EqualTo(3));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastSource, Is.EqualTo("auto-end-turn-next-playable"));
        Assert.That(RuntimeDiagnosticsLedger.IsWatchdogWindowDue(T0 + 2000L), Is.False, "翻滚后重新计时");
    }

    // ── 流程阻塞信号窗口 ──

    [Test]
    public void 流程信号_按键signal冒号reason计数()
    {
        RuntimeDiagnosticsLedger.NoteFlowSignal("watchdog", "hand-in-card-play", T0);
        RuntimeDiagnosticsLedger.NoteFlowSignal("watchdog", "hand-in-card-play", T0);
        RuntimeDiagnosticsLedger.NoteFlowSignal("autoplay", "not-my-turn", T0);

        Assert.That(RuntimeDiagnosticsLedger.FlowSignalCount, Is.EqualTo(2));
        Assert.That(RuntimeDiagnosticsLedger.DescribeFlowSignals(),
            Is.EqualTo("watchdog:hand-in-card-play:2,autoplay:not-my-turn:1"));
    }

    [Test]
    public void 流程信号_去重键为signal冒号round冒号player()
    {
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: true), Is.True);
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: true), Is.False,
            "同一回合同一玩家只受理一次");
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 4, 11, dedupePerRoundPlayer: true), Is.True,
            "换回合重新受理");
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 22, dedupePerRoundPlayer: true), Is.True,
            "换玩家重新受理");
        Assert.That(RuntimeDiagnosticsLedger.FlowDedupeCount, Is.EqualTo(3));
    }

    [Test]
    public void 流程信号_关闭去重或回合号为负时一律受理且不记去重()
    {
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: false), Is.True);
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: false), Is.True);
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", -1, 11, dedupePerRoundPlayer: true), Is.True);
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", -1, 11, dedupePerRoundPlayer: true), Is.True);

        Assert.That(RuntimeDiagnosticsLedger.FlowDedupeCount, Is.EqualTo(0), "这两种情形都不该写入去重集");
    }

    [Test]
    public void 流程信号_翻滚清计数但不清去重集()
    {
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: true), Is.True);
        RuntimeDiagnosticsLedger.NoteFlowSignal("watchdog", "hand-in-card-play", T0);

        Assert.That(RuntimeDiagnosticsLedger.IsFlowWindowDue(T0 + 2000L), Is.True);
        RuntimeDiagnosticsLedger.RollFlowWindow(T0 + 2000L);

        Assert.That(RuntimeDiagnosticsLedger.FlowSignalCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.DescribeFlowSignals(), Is.EqualTo("none"));
        Assert.That(RuntimeDiagnosticsLedger.FlowDedupeCount, Is.EqualTo(1), "去重集刻意不在翻滚时清");
        Assert.That(RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: true), Is.False,
            "翻滚之后同一回合同一玩家仍不再重复报");
        Assert.That(RuntimeDiagnosticsLedger.IsFlowWindowDue(T0 + 2000L), Is.False, "翻滚后重新计时");
    }

    [Test]
    public void 两张窗口互不影响()
    {
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "reason-a", 11, 1, "watchdog", T0);

        Assert.That(RuntimeDiagnosticsLedger.FlowSignalCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.FlowWindowStarted, Is.False);

        RuntimeDiagnosticsLedger.NoteFlowSignal("watchdog", "phase-blocked", T0 + 10L);

        Assert.That(RuntimeDiagnosticsLedger.WatchdogRejectCount, Is.EqualTo(1), "看门狗被拒表不受影响");
        Assert.That(RuntimeDiagnosticsLedger.WatchdogSuccessCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.FlowWindowStarted, Is.True);
    }

    // ── 退局复位（R5 复位契约的一部分）──

    [Test]
    public void 复位_两张窗口回未开窗且清空_哨兵写进lastSource()
    {
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(false, "reason-a", 22, 3, "watchdog", T0);
        RuntimeDiagnosticsLedger.NoteWatchdogSchedule(true, "", 22, 3, "watchdog", T0);
        RuntimeDiagnosticsLedger.TryAcceptFlowSignal("watchdog", 3, 11, dedupePerRoundPlayer: true);
        RuntimeDiagnosticsLedger.NoteFlowSignal("watchdog", "phase-blocked", T0);

        RuntimeDiagnosticsLedger.Reset("run-cleanup");

        Assert.That(RuntimeDiagnosticsLedger.WatchdogWindowStarted, Is.False);
        Assert.That(RuntimeDiagnosticsLedger.WatchdogSuccessCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogRejectCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastTargetSet, Is.False);
        Assert.That(RuntimeDiagnosticsLedger.WatchdogLastSource, Is.EqualTo("run-cleanup"),
            "沿用原 OnRunCleanup 的哨兵（复位自检刻意不看这个字段，因为它的干净值不是声明初值 none）");
        Assert.That(RuntimeDiagnosticsLedger.FlowWindowStarted, Is.False);
        Assert.That(RuntimeDiagnosticsLedger.FlowSignalCount, Is.EqualTo(0));
        Assert.That(RuntimeDiagnosticsLedger.FlowDedupeCount, Is.EqualTo(0));
    }
}
