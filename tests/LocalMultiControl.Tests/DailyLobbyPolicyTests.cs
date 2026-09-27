using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 每日挑战（Daily）本地多控的大厅判定（r156）纯逻辑测试：
/// 席位 reconcile 只在「目标席位缺失 / 有多余本地席位」时动手（不得误删真联机玩家），
/// 角色分配只在「席位指纹变化」时重跑（避免每帧重分配）。
/// </summary>
[TestFixture]
public class DailyLobbyPolicyTests
{
    private static readonly List<ulong> KnownLocalIds = new() { 100, 101, 102, 103, 104 };
    private static readonly List<ulong> TargetTwoSeats = new() { 100, 101 };

    [Test]
    public void 目标席位缺失时需要reconcile()
    {
        // 大厅只有主席位（异步建厅后刚 AddLocalHostPlayer）
        Assert.That(DailyLobbyPolicy.NeedsReconcile(new List<ulong> { 100 }, TargetTwoSeats, KnownLocalIds), Is.True);
    }

    [Test]
    public void 有多余本地席位时需要reconcile()
    {
        // 上次开了 4 席，这次目标 2 席 → 必须把 102/103 移除
        Assert.That(
            DailyLobbyPolicy.NeedsReconcile(new List<ulong> { 100, 101, 102, 103 }, TargetTwoSeats, KnownLocalIds),
            Is.True);
    }

    [Test]
    public void 席位完全匹配时不动()
    {
        Assert.That(DailyLobbyPolicy.NeedsReconcile(new List<ulong> { 100, 101 }, TargetTwoSeats, KnownLocalIds), Is.False);
    }

    [Test]
    public void 目标不足两人时不动()
    {
        Assert.That(
            DailyLobbyPolicy.NeedsReconcile(new List<ulong> { 100 }, new List<ulong> { 100 }, KnownLocalIds),
            Is.False);
    }

    [Test]
    public void 非本地伪玩家不会被当成多余席位()
    {
        // 999 不在 knownLocalIds 里（真联机玩家 / 未知来源）→ 不该被我们删掉
        Assert.That(
            DailyLobbyPolicy.NeedsReconcile(new List<ulong> { 100, 101, 999 }, TargetTwoSeats, KnownLocalIds),
            Is.False);
    }

    [Test]
    public void 席位指纹与顺序相关且空序列为零()
    {
        Assert.That(DailyLobbyPolicy.ComputeSeatSignature(new List<ulong>()), Is.EqualTo(0));
        Assert.That(
            DailyLobbyPolicy.ComputeSeatSignature(new List<ulong> { 100, 101 }),
            Is.Not.EqualTo(DailyLobbyPolicy.ComputeSeatSignature(new List<ulong> { 101, 100 })));
    }

    [Test]
    public void 未分配过角色时需要分配()
    {
        Assert.That(DailyLobbyPolicy.NeedsCharacterAssignment(new List<ulong> { 100, 101 }, 0), Is.True);
    }

    [Test]
    public void 指纹不变时不重复分配()
    {
        List<ulong> seats = new() { 100, 101 };
        int signature = DailyLobbyPolicy.ComputeSeatSignature(seats);
        Assert.That(DailyLobbyPolicy.NeedsCharacterAssignment(seats, signature), Is.False);
    }

    [Test]
    public void 人数变化后需要重新分配()
    {
        // 人数变了 ⇒ 每日种子也变（游戏用 dd_MM_yyyy_{count}p）⇒ 每席位角色都要重算
        List<ulong> seats = new() { 100, 101, 102 };
        int signatureTwoSeats = DailyLobbyPolicy.ComputeSeatSignature(new List<ulong> { 100, 101 });
        Assert.That(DailyLobbyPolicy.NeedsCharacterAssignment(seats, signatureTwoSeats), Is.True);
    }

    [Test]
    public void 空席位不触发分配()
    {
        Assert.That(DailyLobbyPolicy.NeedsCharacterAssignment(new List<ulong>(), 0), Is.False);
    }

    [Test]
    public void 人数上限收在四人()
    {
        Assert.That(DailyLobbyPolicy.MaxDailyLocalPlayerCount, Is.EqualTo(4));
        Assert.That(DailyLobbyPolicy.ClampSeatCount(5), Is.EqualTo(4));
        Assert.That(DailyLobbyPolicy.ClampSeatCount(3), Is.EqualTo(3));
        Assert.That(DailyLobbyPolicy.ClampSeatCount(0), Is.EqualTo(0));
    }
}
