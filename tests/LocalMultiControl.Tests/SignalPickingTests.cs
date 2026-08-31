using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 社区统计信号决策纯函数测试（可行性分析 §8.2 的第②③级：社区统计 → 兜底策略）。
/// 覆盖点：量纲归一、卡牌信号选优与样本量门槛、事件文本模糊匹配、事件按胜率选优。
/// </summary>
[TestFixture]
public class SignalPickingTests
{
    private static WakuuCardSignal Card(double pickRate, double held, double skipped, long offerCount, string id = "CARD")
        => new(id, pickRate, held, skipped, offerCount);

    private static WakuuEventSignal Event_(string text, double winRate, long count)
        => new(text, winRate, count);

    // ---------------------------------------------------------------
    // NormalizeRate：统一 0~1 量纲
    // ---------------------------------------------------------------

    [Test]
    public void NormalizeRate_比例值保持不变()
    {
        Assert.That(WakuuSignalPicking.NormalizeRate(0.42), Is.EqualTo(0.42).Within(1e-9));
    }

    [Test]
    public void NormalizeRate_百分比换算为比例()
    {
        Assert.That(WakuuSignalPicking.NormalizeRate(42.0), Is.EqualTo(0.42).Within(1e-9));
        Assert.That(WakuuSignalPicking.NormalizeRate(100.0), Is.EqualTo(1.0).Within(1e-9));
    }

    [TestCase(0.0)]
    [TestCase(-5.0)]
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    public void NormalizeRate_非正数与非有限值归零(double value)
    {
        Assert.That(WakuuSignalPicking.NormalizeRate(value), Is.EqualTo(0.0));
    }

    // ---------------------------------------------------------------
    // PickBestCardIndex：主信号 PickRate + 因果增益
    // ---------------------------------------------------------------

    [Test]
    public void PickBestCardIndex_选取得分最高者()
    {
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.20, held: 0.50, skipped: 0.50, offerCount: 1000),
            Card(pickRate: 0.30, held: 0.60, skipped: 0.55, offerCount: 1000), // 0.35 最高
            Card(pickRate: 0.10, held: 0.55, skipped: 0.50, offerCount: 1000),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(1));
    }

    [Test]
    public void PickBestCardIndex_因果增益可翻盘低选取率牌()
    {
        // 0 号：选取率 0.10、增益 +0.08 → 0.18；1 号：选取率 0.20、增益 −0.10 → 0.10
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.10, held: 0.58, skipped: 0.50, offerCount: 1000),
            Card(pickRate: 0.20, held: 0.40, skipped: 0.50, offerCount: 1000),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(0));
    }

    [Test]
    public void PickBestCardIndex_样本量不足视为无数据()
    {
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.90, held: 0.9, skipped: 0.1, offerCount: 10),  // 样本太少
            Card(pickRate: 0.20, held: 0.5, skipped: 0.5, offerCount: 500),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals, minOfferCount: 200), Is.EqualTo(1));
    }

    [Test]
    public void PickBestCardIndex_全部无数据返回负一()
    {
        List<WakuuCardSignal?> signals = new()
        {
            null,
            Card(pickRate: 0.9, held: 0.9, skipped: 0.1, offerCount: 5),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(-1));
    }

    [Test]
    public void PickBestCardIndex_同分保留最左()
    {
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.30, held: 0.5, skipped: 0.5, offerCount: 1000),
            Card(pickRate: 0.30, held: 0.5, skipped: 0.5, offerCount: 1000),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(0));
    }

    [Test]
    public void PickBestCardIndex_唯一有数据的候选信号为负时不采用()
    {
        // 实机案例：DEADLY_POISON pickRate=0.127 / gain=-0.115，是唯一有数据的候选，
        // 但"拿了反而更容易输"，应回退最左而不是选它。
        List<WakuuCardSignal?> signals = new()
        {
            null,
            null,
            Card(pickRate: 0.127, held: 0.385, skipped: 0.500, offerCount: 45834),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(-1));
    }

    [Test]
    public void PickBestCardIndex_信号为正才采用()
    {
        List<WakuuCardSignal?> signals = new()
        {
            null,
            Card(pickRate: 0.308, held: 0.573, skipped: 0.500, offerCount: 16296), // gain=+0.073
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(1));
    }

    [Test]
    public void PickBestCardIndex_有数据但为负时会让位给信号为正的候选()
    {
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.40, held: 0.30, skipped: 0.50, offerCount: 9000), // gain=-0.20 → 剔除
            Card(pickRate: 0.10, held: 0.55, skipped: 0.50, offerCount: 9000), // gain=+0.05 → 胜出
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(1));
    }

    [Test]
    public void PickBestCardIndex_降低增益门槛可放行负面信号()
    {
        List<WakuuCardSignal?> signals = new()
        {
            null,
            Card(pickRate: 0.127, held: 0.385, skipped: 0.500, offerCount: 45834),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals, minGain: -1.0), Is.EqualTo(1));
    }

    [Test]
    public void PickBestCardIndex_空集合与空引用返回负一()
    {
        Assert.That(WakuuSignalPicking.PickBestCardIndex(new List<WakuuCardSignal?>()), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.PickBestCardIndex(null!), Is.EqualTo(-1));
    }

    [Test]
    public void PickBestCardIndex_增益权重为零时退化为纯选取率()
    {
        List<WakuuCardSignal?> signals = new()
        {
            Card(pickRate: 0.10, held: 0.58, skipped: 0.50, offerCount: 1000),
            Card(pickRate: 0.20, held: 0.40, skipped: 0.50, offerCount: 1000),
        };

        Assert.That(WakuuSignalPicking.PickBestCardIndex(signals, gainWeight: 0.0), Is.EqualTo(1));
    }

    // ---------------------------------------------------------------
    // MatchEventOptionIndex：归一化后的文本匹配
    // ---------------------------------------------------------------

    [Test]
    public void MatchEventOptionIndex_忽略大小写与标点完全相等()
    {
        List<WakuuEventSignal> stats = new() { Event_("Leave.", 0.4, 500), Event_("Take the gold", 0.6, 500) };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("take the gold!", stats), Is.EqualTo(1));
    }

    [Test]
    public void MatchEventOptionIndex_剔除BBCode富文本标记后命中()
    {
        List<WakuuEventSignal> stats = new() { Event_("Take the gold", 0.6, 500) };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("[b]Take the gold[/b]", stats), Is.EqualTo(0));
    }

    [Test]
    public void MatchEventOptionIndex_包含匹配_选项文本更长()
    {
        List<WakuuEventSignal> stats = new() { Event_("Take the gold", 0.6, 500) };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("Take the gold and leave", stats), Is.EqualTo(0));
    }

    [Test]
    public void MatchEventOptionIndex_中文对英文数据集整体未命中()
    {
        // §8.4 风险 2 的已知限制：中文界面下文本匹配必然 miss，必须无害回退
        List<WakuuEventSignal> stats = new() { Event_("Take the gold", 0.6, 500) };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("拿走金币", stats), Is.EqualTo(-1));
    }

    [Test]
    public void MatchEventOptionIndex_空文本与过短文本不参与匹配()
    {
        List<WakuuEventSignal> stats = new() { Event_("Leave", 0.4, 500) };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex(null, stats), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("  ", stats), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("abc", stats), Is.EqualTo(-1));
    }

    [Test]
    public void MatchEventOptionIndex_完全相等优先于包含匹配()
    {
        List<WakuuEventSignal> stats = new()
        {
            Event_("Take the gold", 0.6, 500),      // 短：会被包含
            Event_("Take the gold and leave", 0.7, 500),
        };

        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("take the gold and leave", stats), Is.EqualTo(1));
    }

    [Test]
    public void MatchEventOptionIndex_空数据集返回负一()
    {
        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("Take the gold", new List<WakuuEventSignal>()), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.MatchEventOptionIndex("Take the gold", null!), Is.EqualTo(-1));
    }

    // ---------------------------------------------------------------
    // PickBestEventIndex：按胜率选优
    // ---------------------------------------------------------------

    [Test]
    public void PickBestEventIndex_选胜率最高者()
    {
        List<WakuuEventSignal> stats = new()
        {
            Event_("a", 0.40, 500),
            Event_("b", 0.65, 500),
            Event_("c", 0.55, 500),
        };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { 0, 1, 2 }, stats), Is.EqualTo(1));
    }

    [Test]
    public void PickBestEventIndex_样本量不足的条目被跳过()
    {
        List<WakuuEventSignal> stats = new()
        {
            Event_("a", 0.95, 3),     // 样本太少
            Event_("b", 0.50, 5000),
        };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { 0, 1 }, stats, minEventCount: 200), Is.EqualTo(1));
    }

    [Test]
    public void PickBestEventIndex_全部样本不足返回负一()
    {
        List<WakuuEventSignal> stats = new() { Event_("a", 0.95, 3) };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { 0 }, stats), Is.EqualTo(-1));
    }

    [Test]
    public void PickBestEventIndex_未命中项与越界下标被忽略()
    {
        List<WakuuEventSignal> stats = new() { Event_("a", 0.50, 500) };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { -1, -1 }, stats), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { 0, 99 }, stats), Is.EqualTo(0));
    }

    [Test]
    public void PickBestEventIndex_同胜率保留更靠前的选项()
    {
        List<WakuuEventSignal> stats = new() { Event_("a", 0.5, 500), Event_("b", 0.5, 500) };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new[] { 1, 0 }, stats), Is.EqualTo(1));
    }

    [Test]
    public void PickBestEventIndex_空输入返回负一()
    {
        List<WakuuEventSignal> stats = new() { Event_("a", 0.5, 500) };

        Assert.That(WakuuSignalPicking.PickBestEventIndex(new int[0], stats), Is.EqualTo(-1));
        Assert.That(WakuuSignalPicking.PickBestEventIndex(null!, stats), Is.EqualTo(-1));
    }
}
