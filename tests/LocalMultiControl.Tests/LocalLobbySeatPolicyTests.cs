using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 本地多控大厅席位判定（R2 第四项：Daily / Custom 共用一份）纯逻辑测试：
/// 席位计划只取「已知本地席位表的前 N 个（又被页面上限截断）」；
/// reconcile 只在「目标席位缺失 / 有多余本地席位」时动手（不得误删真联机玩家）；
/// 角色分配只在「席位指纹变化」时重跑（避免每帧重分配）。
/// </summary>
[TestFixture]
public class LocalLobbySeatPolicyTests
{
    private static readonly List<ulong> KnownLocalIds = new() { 100, 101, 102, 103, 104 };
    private static readonly List<ulong> TargetTwoSeats = new() { 100, 101 };

    [Test]
    public void 目标席位取已知席位表的前N个()
    {
        Assert.That(
            LocalLobbySeatPolicy.ResolveTargetSeats(KnownLocalIds, 3, LocalSelfCoopContext.MaxLocalPlayerCount),
            Is.EqualTo(new List<ulong> { 100, 101, 102 }));
    }

    [Test]
    public void 目标席位被页面上限截断()
    {
        // Daily 页上限写死 4：即便目标 5 人也只能进 4 个席位
        Assert.That(
            LocalLobbySeatPolicy.ResolveTargetSeats(
                KnownLocalIds,
                5,
                LocalLobbySeatPolicy.ClampSeatCount(5)),
            Is.EqualTo(new List<ulong> { 100, 101, 102, 103 }));
    }

    [Test]
    public void 目标人数超过已知席位表时只取现有席位()
    {
        Assert.That(
            LocalLobbySeatPolicy.ResolveTargetSeats(new List<ulong> { 100, 101 }, 5, 12),
            Is.EqualTo(new List<ulong> { 100, 101 }));
    }

    [Test]
    public void 目标人数或上限非正时不解析席位()
    {
        Assert.That(LocalLobbySeatPolicy.ResolveTargetSeats(KnownLocalIds, 0, 12), Is.Empty);
        Assert.That(LocalLobbySeatPolicy.ResolveTargetSeats(KnownLocalIds, -1, 12), Is.Empty);
        Assert.That(LocalLobbySeatPolicy.ResolveTargetSeats(KnownLocalIds, 3, 0), Is.Empty);
        Assert.That(LocalLobbySeatPolicy.ResolveTargetSeats(null!, 3, 12), Is.Empty);
    }

    [Test]
    public void 本地席位顺序保持大厅顺序并剔除真联机玩家()
    {
        // 102 排在大厅第一个（游戏就是按这个顺序 roll 每日角色）；999 不是本地席位 ⇒ 剔除
        Assert.That(
            LocalLobbySeatPolicy.OrderedLocalSeats(new List<ulong> { 102, 999, 100 }, KnownLocalIds),
            Is.EqualTo(new List<ulong> { 102, 100 }));
        Assert.That(LocalLobbySeatPolicy.OrderedLocalSeats(new List<ulong> { 999 }, KnownLocalIds), Is.Empty);
        Assert.That(LocalLobbySeatPolicy.OrderedLocalSeats(new List<ulong>(), KnownLocalIds), Is.Empty);
    }

    [Test]
    public void 目标席位缺失时需要reconcile()
    {
        // 大厅只有主席位（异步建厅后刚 AddLocalHostPlayer）
        Assert.That(LocalLobbySeatPolicy.NeedsReconcile(new List<ulong> { 100 }, TargetTwoSeats, KnownLocalIds), Is.True);
    }

    [Test]
    public void 有多余本地席位时需要reconcile()
    {
        // 上次开了 4 席，这次目标 2 席 → 必须把 102/103 移除
        Assert.That(
            LocalLobbySeatPolicy.NeedsReconcile(new List<ulong> { 100, 101, 102, 103 }, TargetTwoSeats, KnownLocalIds),
            Is.True);
    }

    [Test]
    public void 席位完全匹配时不动()
    {
        Assert.That(LocalLobbySeatPolicy.NeedsReconcile(new List<ulong> { 100, 101 }, TargetTwoSeats, KnownLocalIds), Is.False);
    }

    [Test]
    public void 目标不足两人时不动()
    {
        Assert.That(
            LocalLobbySeatPolicy.NeedsReconcile(new List<ulong> { 100 }, new List<ulong> { 100 }, KnownLocalIds),
            Is.False);
    }

    [Test]
    public void 非本地伪玩家不会被当成多余席位()
    {
        // 999 不在 knownLocalIds 里（真联机玩家 / 未知来源）→ 不该被我们删掉
        Assert.That(
            LocalLobbySeatPolicy.NeedsReconcile(new List<ulong> { 100, 101, 999 }, TargetTwoSeats, KnownLocalIds),
            Is.False);
    }

    [Test]
    public void 席位指纹与顺序相关且空序列为零()
    {
        Assert.That(LocalLobbySeatPolicy.ComputeSeatSignature(new List<ulong>()), Is.EqualTo(0));
        Assert.That(
            LocalLobbySeatPolicy.ComputeSeatSignature(new List<ulong> { 100, 101 }),
            Is.Not.EqualTo(LocalLobbySeatPolicy.ComputeSeatSignature(new List<ulong> { 101, 100 })));
    }

    [Test]
    public void 未分配过角色时需要分配()
    {
        Assert.That(LocalLobbySeatPolicy.NeedsCharacterAssignment(new List<ulong> { 100, 101 }, 0), Is.True);
    }

    [Test]
    public void 指纹不变时不重复分配()
    {
        List<ulong> seats = new() { 100, 101 };
        int signature = LocalLobbySeatPolicy.ComputeSeatSignature(seats);
        Assert.That(LocalLobbySeatPolicy.NeedsCharacterAssignment(seats, signature), Is.False);
    }

    [Test]
    public void 人数变化后需要重新分配()
    {
        // 人数变了 ⇒ 每日种子也变（游戏用 dd_MM_yyyy_{count}p）⇒ 每席位角色都要重算
        List<ulong> seats = new() { 100, 101, 102 };
        int signatureTwoSeats = LocalLobbySeatPolicy.ComputeSeatSignature(new List<ulong> { 100, 101 });
        Assert.That(LocalLobbySeatPolicy.NeedsCharacterAssignment(seats, signatureTwoSeats), Is.True);
    }

    [Test]
    public void 空席位不触发分配()
    {
        Assert.That(LocalLobbySeatPolicy.NeedsCharacterAssignment(new List<ulong>(), 0), Is.False);
    }

    [Test]
    public void 人数上限收在四人()
    {
        Assert.That(LocalLobbySeatPolicy.MaxDailyLocalPlayerCount, Is.EqualTo(4));
        Assert.That(LocalLobbySeatPolicy.ClampSeatCount(5), Is.EqualTo(4));
        Assert.That(LocalLobbySeatPolicy.ClampSeatCount(3), Is.EqualTo(3));
        Assert.That(LocalLobbySeatPolicy.ClampSeatCount(0), Is.EqualTo(0));
    }
}
