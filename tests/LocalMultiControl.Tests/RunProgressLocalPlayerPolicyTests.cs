using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 整局进度写入的本地玩家识别判定（r155，BUG-20）纯逻辑测试：
/// 只在「原平台认不到本地玩家、备用平台能认到」时才要求改写平台，其余一律保持原样。
/// </summary>
[TestFixture]
public class RunProgressLocalPlayerPolicyTests
{
    private const ulong SteamId = 76561198422527326UL;
    private const ulong SecondaryId = 76561198422527327UL;

    /// <summary>本地多控真实场景：回环平台（None）解析出的占位 id = 1，run 里是 Steam 双席位。</summary>
    private static readonly List<ulong> LoopbackPlayers = new() { SteamId, SecondaryId };

    private static RunProgressLocalPlayerPolicy.Action Decide(
        IReadOnlyList<ulong> players,
        ulong runPlatformLocalId,
        bool hasAlternativePlatform,
        ulong alternativePlatformLocalId,
        out ulong resolvedNetId)
    {
        return RunProgressLocalPlayerPolicy.Decide(
            players,
            runPlatformLocalId,
            hasAlternativePlatform,
            alternativePlatformLocalId,
            out resolvedNetId);
    }

    [Test]
    public void 回环占位id认不到而主平台能认到时改写成主平台()
    {
        // BUG-20 现场：platform=None → id=1，run 里没有 NetId=1 的玩家；PrimaryPlatform(Steam) → SteamId 命中
        RunProgressLocalPlayerPolicy.Action action = Decide(
            LoopbackPlayers,
            runPlatformLocalId: 1UL,
            hasAlternativePlatform: true,
            alternativePlatformLocalId: SteamId,
            out ulong resolvedNetId);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.Rewrite));
        Assert.That(resolvedNetId, Is.EqualTo(SteamId));
    }

    [Test]
    public void 原平台已能认到玩家时不动()
    {
        RunProgressLocalPlayerPolicy.Action action = Decide(
            LoopbackPlayers,
            runPlatformLocalId: SteamId,
            hasAlternativePlatform: true,
            alternativePlatformLocalId: 1UL,
            out ulong resolvedNetId);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.Keep));
        Assert.That(resolvedNetId, Is.EqualTo(SteamId));
    }

    [Test]
    public void 单人局不动()
    {
        // 单人局原版走 Players.First()，与平台识别无关
        RunProgressLocalPlayerPolicy.Action action = Decide(
            new List<ulong> { SteamId },
            runPlatformLocalId: 1UL,
            hasAlternativePlatform: true,
            alternativePlatformLocalId: SteamId,
            out _);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.Keep));
    }

    [Test]
    public void 没有备用平台且原平台认不到时判为无法识别()
    {
        RunProgressLocalPlayerPolicy.Action action = Decide(
            LoopbackPlayers,
            runPlatformLocalId: 1UL,
            hasAlternativePlatform: false,
            alternativePlatformLocalId: 0UL,
            out _);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.CannotResolve));
    }

    [Test]
    public void 备用平台也认不到时判为无法识别()
    {
        // 例如 run 里根本没有本机 Steam 账号（他机存档 / 异常 run 数据）
        RunProgressLocalPlayerPolicy.Action action = Decide(
            LoopbackPlayers,
            runPlatformLocalId: 1UL,
            hasAlternativePlatform: true,
            alternativePlatformLocalId: 999UL,
            out _);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.CannotResolve));
    }

    [Test]
    public void 空玩家列表不动()
    {
        RunProgressLocalPlayerPolicy.Action action = Decide(
            new List<ulong>(),
            runPlatformLocalId: 1UL,
            hasAlternativePlatform: true,
            alternativePlatformLocalId: SteamId,
            out _);

        Assert.That(action, Is.EqualTo(RunProgressLocalPlayerPolicy.Action.Keep));
    }
}
