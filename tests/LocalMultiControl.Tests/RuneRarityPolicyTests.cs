using System;
using System.Collections.Generic;
using HextechRunesLocalCoopFix.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 海克斯符文**稀有度优先**判据的纯逻辑测试（compat-mods/HextechRunesLocalCoopFix，v1.8.0）。
///
/// 背景（用户 2026-10-31 新口径，订正 2026-10-11 的「3 个选项稀有度一致」）：
/// 选择顺序 = **稀有度更高者优先 → 同档按抓率 → 再并列取最左**；
/// 并且「roll 有机会出更高稀有度」也要算进自主 reroll 的判据。
///
/// ⚠ 本文件测的 `RuneRarityPolicy.cs` 是**链接源码**进测试工程的，
/// 所以它必须保持"无 Godot / 无游戏类型依赖"。
/// </summary>
[TestFixture]
public class RuneRarityPolicyTests
{
    private static RunePickStat Stat(int offered, int picked)
    {
        return new RunePickStat { Offered = offered, Picked = picked };
    }

    private static readonly string[] Entries = ["A", "B", "C"];

    // ---- 选择顺序 ---------------------------------------------------------------------------------

    [Test]
    public void 稀有度高者胜出即使抓率更低()
    {
        // A = 金但没人选过（0.2），C = 银但抓率很高（0.8）⇒ 仍选 A
        int[] ranks = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.SilverRank, RuneRarityPolicy.SilverRank];
        var stats = new Dictionary<string, RunePickStat>
        {
            ["A"] = Stat(4, 0),
            ["B"] = Stat(2, 1),
            ["C"] = Stat(4, 4),
        };

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, ranks, e => stats[e]), Is.EqualTo(0));
    }

    [Test]
    public void 同档才回到按抓率()
    {
        int[] ranks = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank, RuneRarityPolicy.SilverRank];
        var stats = new Dictionary<string, RunePickStat>
        {
            ["A"] = Stat(4, 0), // 0.2
            ["B"] = Stat(4, 4), // 0.8
            ["C"] = Stat(4, 4), // 0.8（银，被稀有度淘汰）
        };

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, ranks, e => stats[e]), Is.EqualTo(1));
    }

    [Test]
    public void 同档同抓率取最左()
    {
        int[] ranks = [RuneRarityPolicy.SilverRank, RuneRarityPolicy.SilverRank, RuneRarityPolicy.SilverRank];

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, ranks, _ => null), Is.EqualTo(0));
    }

    [Test]
    public void 稀有度全未知时退化成纯抓率()
    {
        int[] unknown = [RuneRarityPolicy.UnknownRank, RuneRarityPolicy.UnknownRank, RuneRarityPolicy.UnknownRank];
        var stats = new Dictionary<string, RunePickStat>
        {
            ["A"] = Stat(4, 0), // 0.2
            ["B"] = Stat(4, 1), // 0.4
            ["C"] = Stat(4, 4), // 0.8
        };

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, unknown, e => stats[e]), Is.EqualTo(2));
        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, unknown, _ => null), Is.EqualTo(0), "无统计时仍是取最左");
    }

    [Test]
    public void 未知档位排在已知档位之后()
    {
        int[] ranks = [RuneRarityPolicy.UnknownRank, RuneRarityPolicy.SilverRank, RuneRarityPolicy.UnknownRank];

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, ranks, _ => null), Is.EqualTo(1));
    }

    [Test]
    public void 稀有度数组长度不符时忽略稀有度()
    {
        int[] ranks = [RuneRarityPolicy.PrismaticRank]; // 少了两项

        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, ranks, _ => null), Is.EqualTo(0));
        Assert.That(RuneRarityPolicy.ChooseIndex(Entries, null, _ => null), Is.EqualTo(0));
    }

    [Test]
    public void 空候选返回负一()
    {
        Assert.That(RuneRarityPolicy.ChooseIndex(Array.Empty<string>(), Array.Empty<int>(), _ => null), Is.EqualTo(-1));
        Assert.That(RuneRarityPolicy.ChooseIndex(null, null, _ => null), Is.EqualTo(-1));
    }

    [Test]
    public void 读统计抛异常不影响稀有度比较()
    {
        int[] ranks = [RuneRarityPolicy.PrismaticRank, RuneRarityPolicy.SilverRank, RuneRarityPolicy.GoldRank];

        int index = RuneRarityPolicy.ChooseIndex(Entries, ranks, _ => throw new InvalidOperationException("boom"));

        Assert.That(index, Is.EqualTo(0), "异常按没见过（0.5）处理，稀有度仍然生效");
    }

    // ---- reroll 升档机会 --------------------------------------------------------------------------

    [Test]
    public void 有升档机会时选那一槽即使都不烂()
    {
        int[] current = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank, RuneRarityPolicy.SilverRank];
        int[] targets = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank, RuneRarityPolicy.PrismaticRank];
        bool[] canReroll = [true, true, true];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets, canReroll), Is.EqualTo(2));
    }

    [Test]
    public void 升档达不到本屏最高档就不动()
    {
        // 已达棱彩 ⇒ 没有任何槽能更高
        int[] current = [RuneRarityPolicy.PrismaticRank, RuneRarityPolicy.GoldRank];
        int[] targets = [RuneRarityPolicy.PrismaticRank, RuneRarityPolicy.GoldRank];
        bool[] canReroll = [true, true];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets, canReroll), Is.EqualTo(-1));
    }

    [Test]
    public void 次数用完的槽不参与升档()
    {
        int[] current = [RuneRarityPolicy.SilverRank, RuneRarityPolicy.SilverRank];
        int[] targets = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank];
        bool[] canReroll = [false, true];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets, canReroll), Is.EqualTo(1));
    }

    [Test]
    public void 可达档更高者优先再比当前档最低()
    {
        // 槽0：银→金；槽1：金→棱彩 ⇒ 选槽1（可达档最高）
        int[] current = [RuneRarityPolicy.SilverRank, RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank];
        int[] targets = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.PrismaticRank, RuneRarityPolicy.PrismaticRank];
        bool[] canReroll = [true, true, true];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets, canReroll), Is.EqualTo(1));

        // 槽1/槽2 可达档与当前档都相同 ⇒ 取最左
        int[] targets2 = [RuneRarityPolicy.GoldRank, RuneRarityPolicy.PrismaticRank, RuneRarityPolicy.PrismaticRank];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets2, canReroll), Is.EqualTo(1));
    }

    [Test]
    public void 升档参数不齐一律不动()
    {
        int[] current = [RuneRarityPolicy.SilverRank];
        int[] targets = [RuneRarityPolicy.GoldRank];

        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, targets, null), Is.EqualTo(-1));
        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, null, [true]), Is.EqualTo(-1));
        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(Array.Empty<int>(), Array.Empty<int>(), Array.Empty<bool>()), Is.EqualTo(-1));
        Assert.That(RuneRarityPolicy.ChooseUpgradeSlot(current, [RuneRarityPolicy.GoldRank, RuneRarityPolicy.GoldRank], [true]), Is.EqualTo(-1));
    }

    [Test]
    public void 档位名用于日志()
    {
        Assert.That(RuneRarityPolicy.RankName(RuneRarityPolicy.SilverRank), Is.EqualTo("银"));
        Assert.That(RuneRarityPolicy.RankName(RuneRarityPolicy.GoldRank), Is.EqualTo("金"));
        Assert.That(RuneRarityPolicy.RankName(RuneRarityPolicy.PrismaticRank), Is.EqualTo("棱彩"));
        Assert.That(RuneRarityPolicy.RankName(RuneRarityPolicy.UnknownRank), Is.EqualTo("?"));
    }
}
