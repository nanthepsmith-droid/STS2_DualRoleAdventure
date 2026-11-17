using LocalMultiControl.Scripts.Runtime;

using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r366：战神立绘的绘制层级 —— 召唤物排在「本体之下」，同一组里**后召唤的更靠下层**。
///
/// 容器子节点顺序 = 绘制顺序（后面的盖前面的），所以"排得越靠前 = 越在下面"。
/// </summary>
[TestFixture]
public class WarGodLayerPolicyTests
{
    [Test]
    public void 后召唤的战神排在更前面_即更下层()
    {
        // 下标 0/1 是两只战神（0 = 先召唤），2 是真人。
        List<int> order = WarGodLayerPolicy.BuildDrawOrder(
            nonCreatureIndices: new List<int>(),
            warGodIndicesOldestFirst: new List<int> { 0, 1 },
            otherCreatureIndices: new List<int> { 2 });

        Assert.That(order, Is.EqualTo(new[] { 1, 0, 2 }), "后召唤的 1 在 0 前面（更下层），真人 2 在最上层");
    }

    [Test]
    public void 非生物节点留在最前_其它生物留在最后()
    {
        List<int> order = WarGodLayerPolicy.BuildDrawOrder(
            nonCreatureIndices: new List<int> { 0, 1 },
            warGodIndicesOldestFirst: new List<int> { 2, 3 },
            otherCreatureIndices: new List<int> { 4, 5 });

        Assert.That(order, Is.EqualTo(new[] { 0, 1, 3, 2, 4, 5 }));
    }

    [Test]
    public void 三组都是原顺序时结果仍是全排列()
    {
        List<int> nonCreature = new() { 0 };
        List<int> warGods = new() { 1, 3, 5 };
        List<int> others = new() { 2, 4 };

        List<int> order = WarGodLayerPolicy.BuildDrawOrder(nonCreature, warGods, others);

        Assert.That(order, Is.Unique);
        Assert.That(order, Is.EquivalentTo(new[] { 0, 1, 2, 3, 4, 5 }));
        Assert.That(order[0], Is.EqualTo(0), "非生物节点不会被战神盖住");
        Assert.That(order[^1], Is.EqualTo(4));
    }

    [Test]
    public void 召唤顺序与容器顺序不一致时_仍按召唤顺序倒排()
    {
        // r367 的实机坑：调用方曾经把"容器里现在的顺序"当成召唤顺序传进来 ⇒
        // 倒序只是在把当前顺序反转一次，每次重排都把两只互换（sync/deferred 互相翻回去）。
        // 这里钉住口径：入参必须是**召唤顺序**，与容器里排第几无关。
        List<int> order = WarGodLayerPolicy.BuildDrawOrder(
            nonCreatureIndices: new List<int>(),
            warGodIndicesOldestFirst: new List<int> { 5, 2 },
            otherCreatureIndices: new List<int> { 0, 1 });

        Assert.That(order, Is.EqualTo(new[] { 2, 5, 0, 1 }), "后召唤的那只（容器 2 号）排到最前 = 最下层");
    }

    [Test]
    public void 已经排好之后再算一次是恒等的()
    {
        // 容器已经是 [最新, 次新, 其它…]（下标 0,1 是战神、2 是真人）：
        // 召唤顺序 = [1(旧), 0(新)] ⇒ 期望顺序必须还是 0,1,2 ⇒ 幂等，不会来回翻。
        List<int> order = WarGodLayerPolicy.BuildDrawOrder(
            nonCreatureIndices: new List<int>(),
            warGodIndicesOldestFirst: new List<int> { 1, 0 },
            otherCreatureIndices: new List<int> { 2 });

        Assert.That(order, Is.EqualTo(new[] { 0, 1, 2 }));
    }

    [Test]
    public void 没有战神时保持原有相对顺序()
    {
        List<int> order = WarGodLayerPolicy.BuildDrawOrder(
            nonCreatureIndices: new List<int> { 0 },
            warGodIndicesOldestFirst: new List<int>(),
            otherCreatureIndices: new List<int> { 1, 2, 3 });

        Assert.That(order, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void 空输入返回空()
    {
        Assert.That(
            WarGodLayerPolicy.BuildDrawOrder(new List<int>(), new List<int>(), new List<int>()),
            Is.Empty);
    }
}
