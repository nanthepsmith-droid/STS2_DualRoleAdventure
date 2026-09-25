using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 共享镜像席位判据测试（r147）。
///
/// 回归背景：镜像的"共享"必须**两头**都是本地多控自己的席位。r146 只补了"目标"一头，
/// 于是第三方（Co-op Bots）合成 Bot 自己打出来的遗物 / 金币被复制给两个真人
/// （2026-09-25 第一幕实机：七咒之戒的额外遗物 8 件 × 2 人 + 12 条金币镜像）。
/// </summary>
[TestFixture]
public class MirrorSeatPolicyTests
{
    private static readonly List<ulong> TwoLocalSeats = new() { 111, 222 };

    [Test]
    public void IsMirrorableSource_本地席位_可镜像()
    {
        Assert.That(MirrorSeatPolicy.IsMirrorableSource(111, TwoLocalSeats), Is.True);
    }

    [Test]
    public void IsMirrorableSource_第三方席位_不可镜像()
    {
        Assert.That(MirrorSeatPolicy.IsMirrorableSource(999, TwoLocalSeats), Is.False);
    }

    [TestCase(0UL)]
    [TestCase(333UL)]
    public void IsMirrorableSource_零或未知席位_不可镜像(ulong sourceNetId)
    {
        Assert.That(MirrorSeatPolicy.IsMirrorableSource(sourceNetId, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_两个本地席位之间_镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 222, TwoLocalSeats), Is.True);
    }

    [Test]
    public void ShouldMirrorTo_来源是第三方席位_不镜像()
    {
        // r146 只拦了目标端，这条是 r147 补上的来源端。
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(999, 111, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_目标是第三方席位_不镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 999, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_两头都是第三方席位_不镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(999, 888, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_自己镜给自己_不镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 111, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_零席位_不镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(0, 111, TwoLocalSeats), Is.False);
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 0, TwoLocalSeats), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_本地席位表为空_不镜像()
    {
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 222, new List<ulong>()), Is.False);
    }

    [Test]
    public void ShouldMirrorTo_三席局_只在本地的两席之间镜像()
    {
        // 三席局（真人 + 瓦库 + Co-op Bots 的合成 Bot）：只有本地两席互相镜像。
        List<ulong> threeSeats = new() { 111, 222, 333 };
        List<ulong> localOnly = new() { 111, 222 };

        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 222, localOnly), Is.True);
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(333, 111, localOnly), Is.False);
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 333, localOnly), Is.False);
        Assert.That(MirrorSeatPolicy.IsMirrorableSource(333, localOnly), Is.False);

        // 本地席位表若恰好等于全部三席（异常配置），来源判据仍按表内判定 —— 这里只固定当前语义。
        Assert.That(MirrorSeatPolicy.ShouldMirrorTo(111, 333, threeSeats), Is.True);
    }
}
