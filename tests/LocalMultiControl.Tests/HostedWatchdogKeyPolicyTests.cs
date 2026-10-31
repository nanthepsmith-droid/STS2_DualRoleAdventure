using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r325 回归：**换战斗之后，同一席位同一回合号必须拿到不同的看门狗记账键**。
///
/// 实机事故（2026-10-28）：旧键 `回合:席位` 遇上"上一场战斗结束时还卡在 await 里、永不返回"的循环，
/// 残键留在"在飞"集合里；下一场战斗回合号又从 1 重来 ⇒ 键撞上 ⇒ 那一席被 `watchdog-in-flight`
/// 永久挡回、整局不再出牌（11 个瓦库里 8 个从第 3 场战斗起不出牌，且都活着、有手牌有能量）。
/// </summary>
[TestFixture]
public class HostedWatchdogKeyPolicyTests
{
    [Test]
    public void 换战斗后同回合同席位不再撞键()
    {
        string fight1 = HostedWatchdogKeyPolicy.BuildKey(combatId: 1, roundNumber: 1, seatId: 327UL);
        string fight3 = HostedWatchdogKeyPolicy.BuildKey(combatId: 3, roundNumber: 1, seatId: 327UL);

        Assert.That(fight1, Is.Not.EqualTo(fight3),
            "不同战斗的同一席、同一回合必须拿到不同键，否则上一场卡死的残键会把这一席永久挡回");
    }

    [Test]
    public void 同战斗同回合同席位键稳定()
    {
        string a = HostedWatchdogKeyPolicy.BuildKey(combatId: 7, roundNumber: 2, seatId: 331UL);
        string b = HostedWatchdogKeyPolicy.BuildKey(combatId: 7, roundNumber: 2, seatId: 331UL);

        Assert.That(a, Is.EqualTo(b), "同一战斗内必须稳定，否则在飞去重失效、会重复调度同一席位");
    }

    [Test]
    public void 同战斗内回合与席位不同则键不同()
    {
        string baseKey = HostedWatchdogKeyPolicy.BuildKey(combatId: 7, roundNumber: 2, seatId: 331UL);

        Assert.That(HostedWatchdogKeyPolicy.BuildKey(7, 3, 331UL), Is.Not.EqualTo(baseKey));
        Assert.That(HostedWatchdogKeyPolicy.BuildKey(7, 2, 332UL), Is.Not.EqualTo(baseKey));
    }

    [Test]
    public void 战斗ID变化即清账()
    {
        Assert.That(HostedWatchdogKeyPolicy.ShouldResetNamespace(lastNameSpace: 3, currentCombatId: 4), Is.True);
        Assert.That(HostedWatchdogKeyPolicy.ShouldResetNamespace(lastNameSpace: 4, currentCombatId: 4), Is.False);
        // 首次进入（未初始化）也必须清一次，避免把上一局的内存状态带进来
        Assert.That(
            HostedWatchdogKeyPolicy.ShouldResetNamespace(int.MinValue, HostedWatchdogKeyPolicy.UnknownCombatId),
            Is.True);
    }

    [Test]
    public void 在飞未到阈值不算卡死()
    {
        Assert.That(HostedWatchdogKeyPolicy.IsStuckInFlight(startedAtMs: 1_000, nowMs: 1_000 + 179_999, thresholdMs: 180_000),
            Is.False);
    }

    [Test]
    public void 在飞到阈值即判卡死()
    {
        Assert.That(HostedWatchdogKeyPolicy.IsStuckInFlight(startedAtMs: 1_000, nowMs: 1_000 + 180_000, thresholdMs: 180_000),
            Is.True);
        Assert.That(HostedWatchdogKeyPolicy.IsStuckInFlight(startedAtMs: 1_000, nowMs: 999_999, thresholdMs: 180_000),
            Is.True);
    }

    [Test]
    public void 缺开始时刻时不判死()
    {
        // 宁可多等一轮，也不要误杀刚起的循环（记录缺失 = 当作刚开始）。
        Assert.That(HostedWatchdogKeyPolicy.IsStuckInFlight(startedAtMs: 0, nowMs: 9_999_999, thresholdMs: 180_000),
            Is.False);
    }

    [Test]
    public void 时钟回拨时不判死()
    {
        Assert.That(HostedWatchdogKeyPolicy.IsStuckInFlight(startedAtMs: 5_000, nowMs: 4_000, thresholdMs: 180_000),
            Is.False);
    }
}
