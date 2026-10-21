using System.Collections.Generic;
using HextechRunesLocalCoopFix.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 海克斯符文「选项很烂时自主 roll」判据的纯逻辑测试（compat-mods/HextechRunesLocalCoopFix，v1.6.0）。
///
/// 口径（用户 2026-10-11 点名）：抓率全低才 roll；**稀有度不能当判据**（用户实测每次 3 个选项稀有度一致）；
/// 没见过（offered=0 ⇒ 0.5）算"未知"，**不判烂**（别为了赌一把换掉可能是新好东西的选项）。
///
/// ⚠ 本文件测的 `RuneRerollPolicy.cs` 是**链接源码**进测试工程的（同 RunePickRatePolicy），
/// 所以它必须保持"无 Godot / 无游戏类型依赖"。
/// </summary>
[TestFixture]
public class RuneRerollPolicyTests
{
    private static RunePickStat Stat(int offered, int picked) => new() { Offered = offered, Picked = picked };

    private static Dictionary<string, RunePickStat> Ledger(params (string Entry, int Offered, int Picked)[] items)
    {
        Dictionary<string, RunePickStat> map = new();
        foreach ((string entry, int offered, int picked) in items)
        {
            map[entry] = Stat(offered, picked);
        }

        return map;
    }

    [Test]
    public void 有数据且抓率低才算烂()
    {
        Assert.That(RuneRerollPolicy.IsPoor(null), Is.False, "没数据 = 未知，不算烂");
        Assert.That(RuneRerollPolicy.IsPoor(Stat(0, 0)), Is.False);
        Assert.That(RuneRerollPolicy.IsPoor(Stat(1, 0)), Is.True, "0/1 = 0.33");
        Assert.That(RuneRerollPolicy.IsPoor(Stat(3, 0)), Is.True, "0/3 = 0.20");
        Assert.That(RuneRerollPolicy.IsPoor(Stat(3, 1)), Is.True, "1/3 = 0.40（阈值边界算烂）");
        Assert.That(RuneRerollPolicy.IsPoor(Stat(2, 1)), Is.False, "1/2 = 0.50");
        Assert.That(RuneRerollPolicy.IsPoor(Stat(1, 1)), Is.False, "1/1 = 0.67");
    }

    [Test]
    public void 三张全烂时换最烂的那张()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(
            ("A", 3, 1),  // 0.40
            ("B", 3, 0),  // 0.20 ← 最烂
            ("C", 2, 0)); // 0.25

        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
            new[] { true, true, true });

        Assert.That(slot, Is.EqualTo(1));
    }

    [Test]
    public void 有一张不烂就不赌()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(
            ("A", 3, 0),   // 0.20
            ("B", 3, 2),   // 0.60 ← 不烂
            ("C", 3, 0));  // 0.20

        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
            new[] { true, true, true });

        Assert.That(slot, Is.EqualTo(-1), "有一张不烂 ⇒ 直接选它");
    }

    [Test]
    public void 没见过的新符文也不赌()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(
            ("A", 3, 0),
            ("B", 3, 0)); // C 没有数据（新符文）= 0.5 未知

        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
            new[] { true, true, true });

        Assert.That(slot, Is.EqualTo(-1));
    }

    [Test]
    public void 最烂的那张没次数就换次烂的()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(
            ("A", 3, 1),  // 0.40
            ("B", 3, 0),  // 0.20 ← 最烂但没次数
            ("C", 2, 0)); // 0.25

        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
            new[] { true, false, true });

        Assert.That(slot, Is.EqualTo(2));
    }

    [Test]
    public void 全都没次数就不roll()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(("A", 3, 0), ("B", 3, 0), ("C", 3, 0));

        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
            new[] { false, false, false });

        Assert.That(slot, Is.EqualTo(-1));
    }

    [Test]
    public void 没统计来源时不动()
    {
        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B", "C" },
            lookup: null,
            new[] { true, true, true });

        Assert.That(slot, Is.EqualTo(-1));
    }

    [Test]
    public void 参数不一致或为空一律不动且不抛()
    {
        Assert.That(RuneRerollPolicy.ChooseSlotToReroll(null, null, null), Is.EqualTo(-1));
        Assert.That(RuneRerollPolicy.ChooseSlotToReroll(new[] { "A" }, null, new bool[0]), Is.EqualTo(-1));
        Assert.That(RuneRerollPolicy.ChooseSlotToReroll(new string[0], null, new bool[0]), Is.EqualTo(-1));
    }

    [Test]
    public void 统计读取抛异常时按没数据处理()
    {
        int slot = RuneRerollPolicy.ChooseSlotToReroll(
            new[] { "A", "B" },
            _ => throw new System.InvalidOperationException("坏账本"),
            new[] { true, true });

        Assert.That(slot, Is.EqualTo(-1));
    }

    [Test]
    public void 自定义阈值生效()
    {
        Dictionary<string, RunePickStat> ledger = Ledger(("A", 3, 1), ("B", 3, 1), ("C", 3, 1)); // 全 0.40

        Assert.That(
            RuneRerollPolicy.ChooseSlotToReroll(
                new[] { "A", "B", "C" },
                e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
                new[] { true, true, true }),
            Is.EqualTo(0),
            "默认阈值 0.40 ⇒ 算烂");

        Assert.That(
            RuneRerollPolicy.ChooseSlotToReroll(
                new[] { "A", "B", "C" },
                e => ledger.TryGetValue(e, out RunePickStat? s) ? s : null,
                new[] { true, true, true },
                threshold: 0.3),
            Is.EqualTo(-1),
            "阈值收紧到 0.30 ⇒ 0.40 不算烂");
    }
}
