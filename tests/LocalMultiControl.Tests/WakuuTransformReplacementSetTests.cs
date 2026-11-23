using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 变化牌登记表（r398：变化成指定一张牌 ⇒ 出牌按消耗牌处理）的纯逻辑部分。
///
/// 关键不变量：**按引用相等**判定 —— 同名牌在牌组里可能同时存在"随机变化来的"与
/// "指定变化来的"两张，按 id 记会把前者一起标上（用户口径明确区分这两类）。
/// </summary>
[TestFixture]
public class WakuuTransformReplacementSetTests
{
    private sealed class FakeCard
    {
        public FakeCard(string id)
        {
            Id = id;
        }

        public string Id { get; }
    }

    [Test]
    public void 登记后能查到且只认同一个实例()
    {
        WakuuTransformReplacementSet set = new();
        FakeCard registered = new("MINION_STRIKE");
        FakeCard lookalike = new("MINION_STRIKE");

        Assert.That(set.Add(registered), Is.True);
        Assert.That(set.Contains(registered), Is.True);
        Assert.That(set.Contains(lookalike), Is.False, "同 id 的不同实例不能被当成已登记");
        Assert.That(set.Count, Is.EqualTo(1));
    }

    [Test]
    public void 重复登记幂等()
    {
        WakuuTransformReplacementSet set = new();
        FakeCard card = new("MINION_DIVE_BOMB");

        Assert.That(set.Add(card), Is.True);
        Assert.That(set.Add(card), Is.False);
        Assert.That(set.Count, Is.EqualTo(1));
    }

    [Test]
    public void 按引用移除且不影响其它实例()
    {
        WakuuTransformReplacementSet set = new();
        FakeCard first = new("A");
        FakeCard second = new("B");
        set.Add(first);
        set.Add(second);

        Assert.That(set.Remove(new FakeCard("A")), Is.False, "内容相同的不同实例不应被移除");
        Assert.That(set.Remove(first), Is.True);
        Assert.That(set.Contains(first), Is.False);
        Assert.That(set.Contains(second), Is.True);
        Assert.That(set.Count, Is.EqualTo(1));
    }

    [Test]
    public void 清空返回清掉的条数()
    {
        WakuuTransformReplacementSet set = new();
        set.Add(new FakeCard("A"));
        set.Add(new FakeCard("B"));

        Assert.That(set.Clear(), Is.EqualTo(2));
        Assert.That(set.Count, Is.Zero);
        Assert.That(set.Clear(), Is.Zero, "空表再清返回 0");
    }

    [Test]
    public void 空入参一律安全()
    {
        WakuuTransformReplacementSet set = new();

        Assert.That(set.Add(null), Is.False);
        Assert.That(set.Contains(null), Is.False);
        Assert.That(set.Remove(null), Is.False);
        Assert.That(set.Count, Is.Zero);
    }
}
