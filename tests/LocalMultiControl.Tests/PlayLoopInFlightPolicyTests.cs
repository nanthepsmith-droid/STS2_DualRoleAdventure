using System;
using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r326：结束回合门要按「这一席有没有出牌循环在飞」决定要不要整桌扫手牌 —— 后缀匹配错了就会
/// 少扫 / 多扫（少扫 = 提前收口，多扫 = 白吃 8~17ms/帧，最坏单帧 111ms）。这里钉死边界。
/// </summary>
[TestFixture]
public class PlayLoopInFlightPolicyTests
{
    private static readonly Dictionary<string, long> Started = new()
    {
        ["7:1:327"] = 10_000L,
        ["7:2:328"] = 1_000L,
    };

    [Test]
    public void 没有在飞记账时不算在飞()
    {
        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(Array.Empty<string>(), Started, seatId: 327UL, nowMs: 10_500L, maxAgeMs: 60_000L),
            Is.False);
    }

    [Test]
    public void 命中同席位且未超龄算在飞()
    {
        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(new[] { "7:1:327" }, Started, 327UL, 10_500L, 60_000L),
            Is.True);
    }

    [Test]
    public void 命中同席位但超龄不算在飞()
    {
        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(new[] { "7:2:328" }, Started, 328UL, 1_000L + 60_001L, 60_000L),
            Is.False);
    }

    [Test]
    public void 必须按后缀精确匹配不能误命中更长席位号()
    {
        // "…:3270" / "…:1327" 都不该被当成 327。
        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(new[] { "7:1:3270", "7:1:1327" }, Started, 327UL, 10_500L, 60_000L),
            Is.False);
    }

    [Test]
    public void 记账存在但缺开始时刻按在飞处理()
    {
        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(new[] { "7:9:999" }, Started, 999UL, 10_500L, 60_000L),
            Is.True);
    }

    [Test]
    public void 多条同席位任一年轻即算在飞()
    {
        var started = new Dictionary<string, long>
        {
            ["7:1:330"] = 1_000L, // 很老
            ["9:3:330"] = 9_900L, // 年轻
        };

        Assert.That(
            HostedWatchdogKeyPolicy.IsSeatInFlight(new[] { "7:1:330", "9:3:330" }, started, 330UL, 10_500L, 60_000L),
            Is.True);
    }
}
