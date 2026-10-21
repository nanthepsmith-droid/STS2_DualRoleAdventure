using HextechRunesLocalCoopFix.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 海克斯符文**抓率**判据的纯逻辑测试（compat-mods/HextechRunesLocalCoopFix，v1.3.0）。
///
/// 背景（用户 2026-10-11 点名）：瓦库原来"取最左"，与真人偏好无关。现在改成按抓率挑，
/// 但**没有数据时必须逐下标退化成"取最左"** —— 否则第一次装就乱选，且回归无从对比。
///
/// ⚠ 本文件测的 `RunePickRatePolicy.cs` 是**链接源码**进测试工程的
/// （csproj 里 <c>&lt;Compile Include="..\..\compat-mods\..." Link="..." /&gt;</c>），
/// 所以它必须保持"无 Godot / 无游戏类型依赖"。
/// </summary>
[TestFixture]
public class RunePickRatePolicyTests
{
    private static RunePickStat Stat(int offered, int picked)
    {
        return new RunePickStat { Offered = offered, Picked = picked };
    }

    [Test]
    public void 没见过等于五五开()
    {
        Assert.That(RunePickRatePolicy.Score(null), Is.EqualTo(0.5).Within(1e-9));
        Assert.That(RunePickRatePolicy.Score(Stat(0, 0)), Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void 出现三次全选与一次没选()
    {
        Assert.That(RunePickRatePolicy.Score(Stat(3, 3)), Is.EqualTo(0.8).Within(1e-9));
        Assert.That(RunePickRatePolicy.Score(Stat(3, 0)), Is.EqualTo(0.2).Within(1e-9));
    }

    [Test]
    public void 脏数据不会算出越界分()
    {
        // picked > offered（手工改坏 / 旧版本遗留）：夹到 offered，分数仍 ≤1
        Assert.That(RunePickRatePolicy.Score(Stat(1, 9)), Is.LessThanOrEqualTo(1.0));
        Assert.That(RunePickRatePolicy.Score(Stat(-5, -3)), Is.EqualTo(0.5).Within(1e-9));
    }

    [Test]
    public void 没数据时退化成取最左()
    {
        string[] entries = ["A", "B", "C"];
        Assert.That(RunePickRatePolicy.ChooseIndex(entries, _ => null), Is.EqualTo(0));
        Assert.That(RunePickRatePolicy.ChooseIndex(entries, null), Is.EqualTo(0));
    }

    [Test]
    public void 并列时取最左()
    {
        string[] entries = ["A", "B", "C"];
        var stats = new Dictionary<string, RunePickStat>
        {
            ["A"] = Stat(2, 1), // 0.5
            ["B"] = Stat(2, 1), // 0.5
            ["C"] = Stat(0, 0), // 0.5
        };

        Assert.That(RunePickRatePolicy.ChooseIndex(entries, e => stats[e]), Is.EqualTo(0));
    }

    [Test]
    public void 抓率高的胜出即使在后排()
    {
        string[] entries = ["A", "B", "C"];
        var stats = new Dictionary<string, RunePickStat>
        {
            ["A"] = Stat(4, 0), // 0.2
            ["B"] = Stat(4, 1), // 0.4
            ["C"] = Stat(4, 4), // 0.8
        };

        Assert.That(RunePickRatePolicy.ChooseIndex(entries, e => stats[e]), Is.EqualTo(2));
    }

    [Test]
    public void 读统计抛异常按没见过处理()
    {
        string[] entries = ["A", "B"];
        int calls = 0;
        int index = RunePickRatePolicy.ChooseIndex(entries, _ =>
        {
            calls++;
            throw new InvalidOperationException("boom");
        });

        Assert.That(index, Is.EqualTo(0), "异常不能冒泡，退化成取最左");
        Assert.That(calls, Is.GreaterThan(0));
    }

    [Test]
    public void 空候选返回负一()
    {
        Assert.That(RunePickRatePolicy.ChooseIndex(Array.Empty<string>(), _ => null), Is.EqualTo(-1));
        Assert.That(RunePickRatePolicy.ChooseIndex(null!, _ => null), Is.EqualTo(-1));
    }

    [Test]
    public void 记账不违反选中不超过出现()
    {
        RunePickStat offered = RunePickRatePolicy.RecordOffered(null);
        Assert.That(offered.Offered, Is.EqualTo(1));
        Assert.That(offered.Picked, Is.EqualTo(0));

        RunePickStat picked = RunePickRatePolicy.RecordPicked(offered);
        Assert.That(picked.Offered, Is.EqualTo(1));
        Assert.That(picked.Picked, Is.EqualTo(1));

        // 同一屏重复按（理论上已由 ScreenObservation 拦住，这里再兜一层）
        RunePickStat again = RunePickRatePolicy.RecordPicked(picked);
        Assert.That(again.Picked, Is.EqualTo(1), "不能超过出现次数");
    }

    [Test]
    public void 落盘再读回内容一致()
    {
        var data = new RunePickStatsData();
        data.Runes["HEXTECH_RUNE_A"] = Stat(5, 3);
        data.Runes["HEXTECH_RUNE_B"] = Stat(2, 0);

        RunePickStatsData parsed = RunePickRatePolicy.Parse(RunePickRatePolicy.Serialize(data));

        Assert.That(parsed.Runes, Has.Count.EqualTo(2));
        Assert.That(parsed.Runes["HEXTECH_RUNE_A"].Offered, Is.EqualTo(5));
        Assert.That(parsed.Runes["HEXTECH_RUNE_A"].Picked, Is.EqualTo(3));
        Assert.That(parsed.Runes["HEXTECH_RUNE_B"].Picked, Is.EqualTo(0));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    [TestCase("{ not json")]
    [TestCase("null")]
    public void 坏文件按空账本处理(string? json)
    {
        RunePickStatsData data = RunePickRatePolicy.Parse(json);
        Assert.That(data.Runes, Is.Empty);
        Assert.That(data.Version, Is.EqualTo(1));
    }
}
