using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 选择器栈「按引用摘除」纯逻辑测试（改进-2 / 方案 D 第二步）。
///
/// 钉死的不变式：**并发出牌档下，作用域释放 ⇒ 自己的选择器一定不在全局栈里**。
/// 原版 <c>StackedSelectorScope.Dispose</c> 只在「自己仍是栈顶」时弹栈，所以先压入、先释放的那一个
/// 会被永久留在栈里（之后"栈上无选择器"的判定全部失效）—— 本纯函数就是补这一刀。
/// 入参 / 返回都用「栈顶 → 栈底」顺序（与 <c>Stack&lt;T&gt;</c> 的枚举顺序一致）。
/// 用自定义引用类型（而不是 string）做用例，避免字符串驻留把"按引用比较"验成"按值比较"。
/// </summary>
[TestFixture]
public class WakuuSelectorStackSurgeryTests
{
    private sealed class Item
    {
        internal Item(string name)
        {
            Name = name;
        }

        internal string Name { get; }

        public override string ToString() => Name;
    }

    private static List<Item> Stack(params Item[] topToBottom) => new(topToBottom);

    [Test]
    public void 摘掉中间的残留项_其余顺序不变()
    {
        // 模拟：A 先压入、B 后压入（栈顶 → 栈底 = B, A）；A 先释放 → 原版不弹 A → 这里补摘。
        Item a = new("A");
        Item b = new("B");
        List<Item> result = WakuuSelectorStackSurgery.RemoveByReference(Stack(b, a), a);

        Assert.That(result.Select(item => item.Name), Is.EqualTo(new[] { "B" }));
    }

    [Test]
    public void 摘掉栈顶项_等价于原版弹栈()
    {
        Item a = new("A");
        Item b = new("B");
        List<Item> result = WakuuSelectorStackSurgery.RemoveByReference(Stack(b, a), b);

        Assert.That(result.Select(item => item.Name), Is.EqualTo(new[] { "A" }));
    }

    [Test]
    public void 不在栈里_原样返回_调用方可据此判定空操作()
    {
        // 正常单作用域路径：原版 scope 已经弹出 → 本函数必须原样返回（长度相同 ⇒ 调用方不动栈）。
        Item a = new("A");
        Item b = new("B");
        List<Item> stack = Stack(b);
        List<Item> result = WakuuSelectorStackSurgery.RemoveByReference(stack, a);

        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => item.Name), Is.EqualTo(new[] { "B" }));
            Assert.That(result, Has.Count.EqualTo(stack.Count));
        });
    }

    [Test]
    public void 按引用比较_内容相同的不同实例不受影响()
    {
        // 关键：绝不能按内容相等去摘 —— 两个瓦库各自 New 出的选择器类型相同、内容也相同。
        Item keep = new("A");
        Item target = new("A");
        List<Item> result = WakuuSelectorStackSurgery.RemoveByReference(Stack(keep, target), target);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result[0], Is.SameAs(keep));
        });
    }

    [Test]
    public void 只摘一个_重复压入的同一实例仍留一份()
    {
        // 防御性：即使同一实例被压入两次（理论上不会），也只摘一份，避免把别的引用一起干掉。
        Item a = new("A");
        Item b = new("B");
        List<Item> result = WakuuSelectorStackSurgery.RemoveByReference(Stack(a, b, a), a);

        Assert.That(result.Select(item => item.Name), Is.EqualTo(new[] { "B", "A" }));
        Assert.That(result[1], Is.SameAs(a));
    }

    [Test]
    public void 空栈_返回空()
    {
        Assert.That(WakuuSelectorStackSurgery.RemoveByReference(new List<Item>(), new Item("A")), Is.Empty);
    }

    [Test]
    public void 泛型经引用实例_可摘掉指定实例()
    {
        // 运行层实际传的是 object（选择器实例）；确认泛型不引入相等性语义。
        object first = new();
        object second = new();
        List<object> result = WakuuSelectorStackSurgery.RemoveByReference(new List<object> { second, first }, first);

        Assert.Multiple(() =>
        {
            Assert.That(result, Has.Count.EqualTo(1));
            Assert.That(result.Single(), Is.SameAs(second));
        });
    }
}
