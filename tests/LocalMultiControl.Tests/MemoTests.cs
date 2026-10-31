using ThirdPartyHotPathFix.Scripts.Memo;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// `compat-mods/ThirdPartyHotPathFix` 里热路径记忆化的单测（源码以链接方式编进本工程）。
///
/// 为什么这些语义必须锁死：这些函数被我们接管后每秒调用数千次，一旦「缓存了 null / 缓存了异常 /
/// 键为 null 也去查表」，就会把「类型还没加载」这类一次性失败永久钉死，或者把玩法改掉。
/// </summary>
[TestFixture]
public sealed class MemoTests
{
    [Test]
    public void Memo_命中后不再计算()
    {
        var memo = new Memo<string>();
        Assert.That(memo.TryGet("a", out _), Is.False, "没算过就该 miss");
        Assert.That(memo.Misses, Is.EqualTo(1));

        memo.Store("a", "A");

        Assert.That(memo.TryGet("a", out string value), Is.True);
        Assert.That(value, Is.EqualTo("A"));
        Assert.That(memo.Hits, Is.EqualTo(1), "命中计数用来确认补丁真的生效");
    }

    [Test]
    public void Memo_不缓存null值()
    {
        // FindType 找不到类型会返回 null，但「程序集还没加载」之后可能就能找到 ⇒ 绝不能钉死
        var memo = new Memo<object>();
        memo.Store("Missing.Type", null);

        Assert.That(memo.Count, Is.EqualTo(0));
        Assert.That(memo.TryGet("Missing.Type", out _), Is.False);
    }

    [Test]
    public void Memo_键为null时既不命中也不缓存()
    {
        var memo = new Memo<string>();
        memo.Store(null, "A");
        Assert.That(memo.Count, Is.EqualTo(0));

        Assert.That(memo.TryGet(null, out _), Is.False);
        Assert.That(memo.Hits, Is.EqualTo(0));
    }

    [Test]
    public void Memo_到容量上限后不再新增但仍可更新()
    {
        var memo = new Memo<string>(maxEntries: 2);
        memo.Store("a", "A");
        memo.Store("b", "B");
        memo.Store("c", "C");

        Assert.That(memo.Count, Is.EqualTo(2), "到顶后不再新增，避免长存档把字典撑大");
        Assert.That(memo.TryGet("c", out _), Is.False);

        memo.Store("a", "A2");
        Assert.That(memo.TryGet("a", out string value), Is.True);
        Assert.That(value, Is.EqualTo("A2"), "已有键仍然可以更新");
    }

    [Test]
    public void Memo_大小写不同视为不同键()
    {
        // 序数比较：mod id / 类型名是 ASCII，不能按语言文化折叠
        var memo = new Memo<string>();
        memo.Store("Slug", "1");

        Assert.That(memo.TryGet("slug", out _), Is.False);
    }
}
