using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 选择器归属者登记表纯逻辑测试（改进-2 / Phase 1）。
///
/// 覆盖：基本登记/退订、同归属者嵌套取最内层、**乱序释放**（内层还没释放就释放外层）、
/// 重复释放幂等、多归属者隔离、Reset 与快照。
/// </summary>
[TestFixture]
public class WakuuOwnerSelectorMapTests
{
    [Test]
    public void 登记后可按归属者取回()
    {
        WakuuOwnerSelectorMap<string> map = new();
        using (map.Register(326, "A"))
        {
            Assert.Multiple(() =>
            {
                Assert.That(map.TryGet(326, out string? selector), Is.True);
                Assert.That(selector, Is.EqualTo("A"));
                Assert.That(map.Contains(326), Is.True);
                Assert.That(map.Count, Is.EqualTo(1));
                Assert.That(map.OwnerCount, Is.EqualTo(1));
            });
        }
    }

    [Test]
    public void 释放后退订_且不影响其它归属者()
    {
        WakuuOwnerSelectorMap<string> map = new();
        IDisposable a = map.Register(326, "A");
        using (map.Register(327, "B"))
        {
            a.Dispose();
            Assert.Multiple(() =>
            {
                Assert.That(map.TryGet(326, out _), Is.False);
                Assert.That(map.TryGet(327, out string? b), Is.True);
                Assert.That(b, Is.EqualTo("B"));
                Assert.That(map.OwnerCount, Is.EqualTo(1));
            });
        }
    }

    [Test]
    public void 同一归属者嵌套登记_取最内层()
    {
        WakuuOwnerSelectorMap<string> map = new();
        using (map.Register(326, "外层"))
        {
            using (map.Register(326, "内层"))
            {
                Assert.Multiple(() =>
                {
                    Assert.That(map.TryGet(326, out string? selector), Is.True);
                    Assert.That(selector, Is.EqualTo("内层"));
                    Assert.That(map.Count, Is.EqualTo(2), "嵌套登记应保留两条");
                    Assert.That(map.OwnerCount, Is.EqualTo(1), "同一归属者只算一个");
                });
            }

            // 内层释放后回落到外层
            Assert.That(map.TryGet(326, out string? outer), Is.True);
            Assert.That(outer, Is.EqualTo("外层"));
        }
    }

    [Test]
    public void 乱序释放_内层仍能正确定位自己的条目()
    {
        WakuuOwnerSelectorMap<string> map = new();
        IDisposable outer = map.Register(326, "外层");
        using (map.Register(326, "内层"))
        {
            // 外层先释放（异步链被放弃 / 作用域交错），此时内层仍应生效
            outer.Dispose();
            Assert.That(map.TryGet(326, out string? selector), Is.True);
            Assert.That(selector, Is.EqualTo("内层"), "按引用移除不能误删别人的条目");
            Assert.That(map.Count, Is.EqualTo(1));
        }

        Assert.That(map.Contains(326), Is.False);
        Assert.That(map.Count, Is.EqualTo(0));
    }

    [Test]
    public void 重复释放_幂等无副作用()
    {
        WakuuOwnerSelectorMap<string> map = new();
        IDisposable registration = map.Register(326, "A");
        registration.Dispose();
        registration.Dispose();
        registration.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(map.Contains(326), Is.False);
            Assert.That(map.Count, Is.EqualTo(0));
        });
    }

    [Test]
    public void 不存在的归属者_取不到且Contains为假()
    {
        WakuuOwnerSelectorMap<string> map = new();
        Assert.Multiple(() =>
        {
            Assert.That(map.TryGet(999, out string? selector), Is.False);
            Assert.That(selector, Is.Null);
            Assert.That(map.Contains(999), Is.False);
        });
    }

    [Test]
    public void Reset_清空全部并返回条目数()
    {
        WakuuOwnerSelectorMap<string> map = new();
        for (int i = 0; i < 3; i++)
        {
            map.Register(326, $"A{i}");
        }

        map.Register(327, "B");

        Assert.That(map.Reset(), Is.EqualTo(4));
        Assert.Multiple(() =>
        {
            Assert.That(map.Count, Is.EqualTo(0));
            Assert.That(map.OwnerCount, Is.EqualTo(0));
            Assert.That(map.Contains(326), Is.False);
        });
    }

    [Test]
    public void 快照_按归属者升序且只给最内层()
    {
        WakuuOwnerSelectorMap<string> map = new();
        using (map.Register(327, "B"))
        {
            using (map.Register(326, "A外层"))
            {
                using (map.Register(326, "A内层"))
                {
                    IReadOnlyList<KeyValuePair<ulong, string>> snapshot = map.SnapshotTop();
                    Assert.Multiple(() =>
                    {
                        Assert.That(snapshot.Select(pair => pair.Key), Is.EqualTo(new ulong[] { 326, 327 }));
                        Assert.That(snapshot[0].Value, Is.EqualTo("A内层"));
                        Assert.That(snapshot[1].Value, Is.EqualTo("B"));
                    });
                }
            }
        }
    }

    [Test]
    public void 空选择器_登记抛出参数异常()
    {
        WakuuOwnerSelectorMap<string> map = new();
        Assert.That(() => map.Register(326, null!), Throws.ArgumentNullException);
    }
}
