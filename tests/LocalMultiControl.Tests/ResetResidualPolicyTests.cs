using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「进局 / 退局复位」的残留判定（R5 第一批）。
///
/// 要钉死的语义：
/// ① 干净快照 ⇒ 空列表（进局 / 退局两个锚点在正常路径上必须**无残留**，这是可跨会话对比的锚点）；
/// ② 任一项非初值 ⇒ 点名（`键=数值` / `键=set`），漏清一项就会在日志里被看见；
/// ③ 顺序固定（战斗级在前、局级在后，按声明顺序）⇒ 日志可逐字对比；
/// ④ 局内这些计数非空是正常的 ⇒ 判据只允许在进局 / 退局那两个时刻使用（本类不做时间判断，由调用方保证）。
/// </summary>
[TestFixture]
public class ResetResidualPolicyTests
{
    private static ResetResidualSnapshot Clean => new ResetResidualSnapshot();

    [Test]
    public void 干净快照_无残留()
    {
        Assert.That(ResetResidualPolicy.FindResiduals(Clean), Is.Empty);
        Assert.That(ResetResidualPolicy.Describe(ResetResidualPolicy.FindResiduals(Clean)), Is.EqualTo("无残留"));
    }

    [Test]
    public void 战斗级计数_非零即点名并带数值()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot
        {
            WakuuAutoEndIssued = 3,
            AllPlayersAutoEndedRounds = 2,
            SkipDrawAnimLogged = 1,
            CombatEnergyDiagKeys = 4,
            EndTurnReconcileLogCount = 5,
        };

        Assert.That(ResetResidualPolicy.FindResiduals(snapshot), Is.EqualTo(new[]
        {
            "wakuu-auto-end-issued=3",
            "all-players-auto-ended-rounds=2",
            "skip-draw-anim-logged=1",
            "combat-energy-diag-keys=4",
            "end-turn-reconcile-log-count=5",
        }));
    }

    [Test]
    public void 战斗级哨兵_已写入即点名()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot
        {
            LastAutoEndCombatIdentitySet = true,
            SkipDrawAnimCombatIdentitySet = true,
            PendingManualEndTurnSet = true,
            LastEndTurnReconcileAttemptSet = true,
        };

        Assert.That(ResetResidualPolicy.FindResiduals(snapshot), Is.EqualTo(new[]
        {
            "last-auto-end-combat-identity=set",
            "skip-draw-anim-combat-identity=set",
            "pending-manual-end-turn=set",
            "end-turn-reconcile-attempt=set",
        }));
    }

    [Test]
    public void 局级状态_非初值即点名()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot
        {
            WatchdogScheduleRejectCounts = 7,
            WatchdogScheduleWindowStarted = true,
            WatchdogScheduleSuccessCount = 9,
            WatchdogScheduleLastTargetSet = true,
            FlowBlockSignalCounts = 2,
            FlowBlockSignalDedupeRoundPlayer = 1,
            FlowBlockSignalWindowStarted = true,
        };

        Assert.That(ResetResidualPolicy.FindResiduals(snapshot), Is.EqualTo(new[]
        {
            "watchdog-schedule-rejects=7",
            "watchdog-schedule-window=set",
            "watchdog-schedule-success-count=9",
            "watchdog-schedule-last-target=set",
            "flow-block-signals=2",
            "flow-block-dedupe=1",
            "flow-block-window=set",
        }));
    }

    [Test]
    public void 顺序固定_战斗级先于局级()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot
        {
            FlowBlockSignalCounts = 2,                  // 局级
            WakuuAutoEndIssued = 1,                     // 战斗级第一项
        };

        IReadOnlyList<string> residuals = ResetResidualPolicy.FindResiduals(snapshot);
        Assert.That(residuals, Is.EqualTo(new[] { "wakuu-auto-end-issued=1", "flow-block-signals=2" }));
    }

    [Test]
    public void 全满_十六项全部点名()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot
        {
            WakuuAutoEndIssued = 1,
            AllPlayersAutoEndedRounds = 1,
            SkipDrawAnimLogged = 1,
            CombatEnergyDiagKeys = 1,
            LastAutoEndCombatIdentitySet = true,
            SkipDrawAnimCombatIdentitySet = true,
            PendingManualEndTurnSet = true,
            LastEndTurnReconcileAttemptSet = true,
            EndTurnReconcileLogCount = 1,
            WatchdogScheduleRejectCounts = 1,
            WatchdogScheduleWindowStarted = true,
            WatchdogScheduleSuccessCount = 1,
            WatchdogScheduleLastTargetSet = true,
            FlowBlockSignalCounts = 1,
            FlowBlockSignalDedupeRoundPlayer = 1,
            FlowBlockSignalWindowStarted = true,
        };

        IReadOnlyList<string> residuals = ResetResidualPolicy.FindResiduals(snapshot);
        Assert.That(residuals, Has.Count.EqualTo(16));
        Assert.That(residuals.Distinct().Count(), Is.EqualTo(16), "键不得重复（否则日志点名会歧义）");
    }

    [Test]
    public void 结论文案_有残留时给出键值对()
    {
        ResetResidualSnapshot snapshot = new ResetResidualSnapshot { FlowBlockSignalCounts = 2 };
        string text = ResetResidualPolicy.Describe(ResetResidualPolicy.FindResiduals(snapshot));
        Assert.That(text, Is.EqualTo("flow-block-signals=2"));
    }
}
