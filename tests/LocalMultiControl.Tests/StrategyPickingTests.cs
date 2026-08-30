using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 选牌/选择策略纯函数测试（first/last/random 排序、洗牌、火堆锻造选牌）。
/// </summary>
[TestFixture]
public class StrategyPickingTests
{
    private static readonly int[] Source = { 10, 20, 30, 40, 50 };

    // ---------------------------------------------------------------
    // PickByStrategy：效果选牌（合成二选一、开局遗物二选一等）
    // ---------------------------------------------------------------

    [Test]
    public void PickByStrategy_first_取原序列前N个()
    {
        List<int> picked = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.First, 2, new Random(1));
        Assert.That(picked, Is.EqualTo(new[] { 10, 20 }));
    }

    [Test]
    public void PickByStrategy_last_倒序后取前N_即原序列最后N张_顺序为倒序()
    {
        // 运行时既有行为：Reverse() 后 Take(N) → 取到的是"最后 N 张"但顺序反了（[50,40] 而非 [40,50]）
        List<int> picked = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.Last, 2, new Random(1));
        Assert.That(picked, Is.EqualTo(new[] { 50, 40 }));
    }

    [Test]
    public void PickByStrategy_random_返回count个且元素均来自源()
    {
        List<int> picked = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.Random, 3, new Random(42));
        Assert.That(picked, Has.Count.EqualTo(3));
        Assert.That(picked, Is.SubsetOf(Source));
    }

    [Test]
    public void PickByStrategy_random_不同种子产生不同顺序()
    {
        List<int> a = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.Random, 5, new Random(1));
        List<int> b = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.Random, 5, new Random(2));

        Assert.That(a, Is.EquivalentTo(Source)); // 集合不变
        Assert.That(a, Is.Not.EqualTo(b));       // 顺序大概率不同
    }

    [Test]
    public void PickByStrategy_count超出_返回全部()
    {
        List<int> picked = WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.First, 99, new Random(1));
        Assert.That(picked, Is.EqualTo(Source));
    }

    [Test]
    public void PickByStrategy_count为零或负数_返回空()
    {
        Assert.That(WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.First, 0, new Random(1)), Is.Empty);
        Assert.That(WakuuStrategyPicking.PickByStrategy(Source, WakuuChoiceModes.First, -3, new Random(1)), Is.Empty);
    }

    [Test]
    public void PickByStrategy_空源_返回空()
    {
        Assert.That(WakuuStrategyPicking.PickByStrategy(Array.Empty<int>(), WakuuChoiceModes.First, 3, new Random(1)), Is.Empty);
    }

    [Test]
    public void PickByStrategy_未知模式_按last兜底()
    {
        List<int> picked = WakuuStrategyPicking.PickByStrategy(Source, "middle", 2, new Random(1));
        Assert.That(picked, Is.EqualTo(new[] { 50, 40 })); // 与 last 一致（倒序取前 N）
    }

    // ---------------------------------------------------------------
    // PickIndexByStrategy：事件选项选择（first/last/random）
    // ---------------------------------------------------------------

    [Test]
    public void PickIndexByStrategy_first_返回0()
    {
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(5, WakuuChoiceModes.First, new Random(1)), Is.Zero);
    }

    [Test]
    public void PickIndexByStrategy_last_返回最后一个下标()
    {
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(5, WakuuChoiceModes.Last, new Random(1)), Is.EqualTo(4));
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(1, WakuuChoiceModes.Last, new Random(1)), Is.Zero);
    }

    [Test]
    public void PickIndexByStrategy_random_返回合法下标()
    {
        for (int seed = 0; seed < 20; seed++)
        {
            int index = WakuuStrategyPicking.PickIndexByStrategy(5, WakuuChoiceModes.Random, new Random(seed));
            Assert.That(index, Is.InRange(0, 4));
        }
    }

    [Test]
    public void PickIndexByStrategy_空候选_返回负一()
    {
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(0, WakuuChoiceModes.First, new Random(1)), Is.EqualTo(-1));
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(0, WakuuChoiceModes.Last, new Random(1)), Is.EqualTo(-1));
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(-2, WakuuChoiceModes.Random, new Random(1)), Is.EqualTo(-1));
    }

    [Test]
    public void PickIndexByStrategy_未知模式_按first兜底()
    {
        Assert.That(WakuuStrategyPicking.PickIndexByStrategy(5, "middle", new Random(1)), Is.Zero);
    }

    // ---------------------------------------------------------------
    // Shuffle：Fisher-Yates
    // ---------------------------------------------------------------

    [Test]
    public void Shuffle_保持元素集合与长度()
    {
        List<int> shuffled = WakuuStrategyPicking.Shuffle(Source, new Random(7));
        Assert.That(shuffled, Is.EquivalentTo(Source));
        Assert.That(shuffled, Has.Count.EqualTo(Source.Length));
    }

    [Test]
    public void Shuffle_不修改原集合()
    {
        int[] original = { 10, 20, 30 };
        _ = WakuuStrategyPicking.Shuffle(original, new Random(3));
        Assert.That(original, Is.EqualTo(new[] { 10, 20, 30 }));
    }

    [Test]
    public void Shuffle_空集合_返回空()
    {
        Assert.That(WakuuStrategyPicking.Shuffle(Array.Empty<int>(), new Random(1)), Is.Empty);
    }

    // ---------------------------------------------------------------
    // PickSmithCards：火堆锻造选牌（优先非打击/防御）
    // ---------------------------------------------------------------

    /// <summary>模拟卡牌：id 含 STRIKE/DEFEND 的为打击/防御。</summary>
    private static bool IsBasic(string id) => id.Contains("STRIKE") || id.Contains("DEFEND");

    [Test]
    public void PickSmithCards_优先非打击防御_取最后N张()
    {
        string[] cards = { "STRIKE_RED", "DEFEND_RED", "HEAVY_BLADE", "FLAME_BARRIER" };
        // 非 basic = HEAVY_BLADE, FLAME_BARRIER；smithCount=1 → 取 FLAME_BARRIER
        List<string> picked = WakuuStrategyPicking.PickSmithCards(cards, 1, IsBasic);
        Assert.That(picked, Is.EqualTo(new[] { "FLAME_BARRIER" }));
    }

    [Test]
    public void PickSmithCards_非basic不足_用打击防御补齐()
    {
        string[] cards = { "STRIKE_RED", "DEFEND_RED", "HEAVY_BLADE" };
        // 非 basic 只有 1 张（HEAVY_BLADE），smithCount=2 → HEAVY_BLADE + 最后一个 basic（DEFEND_RED）
        List<string> picked = WakuuStrategyPicking.PickSmithCards(cards, 2, IsBasic);
        Assert.That(picked, Is.EqualTo(new[] { "HEAVY_BLADE", "DEFEND_RED" }));
    }

    [Test]
    public void PickSmithCards_全是打击防御_取最后N张()
    {
        string[] cards = { "STRIKE_RED", "STRIKE_GREEN", "DEFEND_RED" };
        List<string> picked = WakuuStrategyPicking.PickSmithCards(cards, 2, IsBasic);
        Assert.That(picked, Is.EqualTo(new[] { "STRIKE_GREEN", "DEFEND_RED" }));
    }

    [Test]
    public void PickSmithCards_全非basic_取最后N张()
    {
        // 注意：PERFECTED_STRIKE 含 STRIKE 会被识别为打击牌，此处故意用不含 STRIKE/DEFEND 的名字
        string[] cards = { "HEAVY_BLADE", "FLAME_BARRIER", "SHRUG_IT_OFF" };
        List<string> picked = WakuuStrategyPicking.PickSmithCards(cards, 2, IsBasic);
        Assert.That(picked, Is.EqualTo(new[] { "FLAME_BARRIER", "SHRUG_IT_OFF" }));
    }

    [Test]
    public void PickSmithCards_数量超出候选_返回全部()
    {
        string[] cards = { "STRIKE_RED", "HEAVY_BLADE" };
        List<string> picked = WakuuStrategyPicking.PickSmithCards(cards, 5, IsBasic);
        Assert.That(picked, Is.EquivalentTo(cards));
    }

    [Test]
    public void PickSmithCards_空候选_返回空()
    {
        Assert.That(WakuuStrategyPicking.PickSmithCards(Array.Empty<string>(), 2, IsBasic), Is.Empty);
    }
}
