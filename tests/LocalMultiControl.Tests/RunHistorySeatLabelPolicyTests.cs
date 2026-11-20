using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 回放面板的席位序判定（2026-11-20 收工 ④）：本地席位 ID 是「本机平台 ID + 0..N-1」的连续号，
/// 靠这个把存档里的 id 反推成席位序；官方联机队友不会被误判。
/// </summary>
[TestFixture]
public class RunHistorySeatLabelPolicyTests
{
    private static readonly ulong[] ThreeSeats = { 100UL, 101UL, 102UL };

    [Test]
    public void 连续席位按偏移给出序号()
    {
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 100UL), Is.EqualTo(0));
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 101UL), Is.EqualTo(1));
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 102UL), Is.EqualTo(2));
    }

    [Test]
    public void 官方联机队友不会被误判成席位()
    {
        ulong[] online = { 100UL, 999UL };
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(online, 100UL, 999UL), Is.Null);

        // 偏移对上了但那个位置上的 id 不是它 —— 也不能算
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(online, 100UL, 101UL), Is.Null);
    }

    [Test]
    public void 单人局与空输入一律不判定()
    {
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(new[] { 100UL }, 100UL, 100UL), Is.Null);
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(null, 100UL, 100UL), Is.Null);
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(new ulong[0], 100UL, 100UL), Is.Null);
    }

    [Test]
    public void 非法id与越界返回空()
    {
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 0UL), Is.Null);
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 0UL, 101UL), Is.Null);
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 99UL), Is.Null);
        Assert.That(RunHistorySeatLabelPolicy.ResolveSlotIndex(ThreeSeats, 100UL, 103UL), Is.Null);
    }
}
