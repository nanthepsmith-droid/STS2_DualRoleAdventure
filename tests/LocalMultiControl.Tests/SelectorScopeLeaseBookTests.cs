using LocalMultiControl.Scripts.Runtime;

using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r358 回归：**出牌作用域"在飞"必须用可剪枝的租约，而不是裸计数器**。
///
/// 实机事故（2026-11-15）：上一局留下 4 个永远不会返回的出牌循环 ⇒ `_selectorScopeInFlight` 一直是 4，
/// 而换战斗 / 退局都不重置它 ⇒ 非并发档席位（战神）每次调度都被 `selector-scope-busy` 挡回；
/// 它手上的牌打不掉 ⇒ 这一席永不 ready ⇒ 真人点「结束回合」毫无反应。
/// </summary>
[TestFixture]
public class SelectorScopeLeaseBookTests
{
    [Test]
    public void 进入与归还成对时计数归零()
    {
        SelectorScopeLeaseBook book = new SelectorScopeLeaseBook();

        long a = book.Acquire(nowMs: 1_000);
        long b = book.Acquire(nowMs: 1_100);
        Assert.That(book.CountActive(1_200, staleAfterMs: 60_000, out _), Is.EqualTo(2));

        Assert.That(book.Release(a), Is.EqualTo(1));
        Assert.That(book.Release(b), Is.EqualTo(0));
        Assert.That(book.CountActive(1_300, staleAfterMs: 60_000, out _), Is.EqualTo(0));
    }

    [Test]
    public void 超龄租约被剪掉_不再挡住其他席位()
    {
        SelectorScopeLeaseBook book = new SelectorScopeLeaseBook();

        // 一条来自"上一场战斗"的租约：永远不会归还。
        book.Acquire(nowMs: 1_000);
        // 一条当前正常在飞的租约。
        book.Acquire(nowMs: 100_000);

        int active = book.CountActive(nowMs: 101_000, staleAfterMs: 60_000, out int prunedStale);

        Assert.That(active, Is.EqualTo(1), "只有超龄的那条该失效，正常的必须继续挡（避免误清真实的出牌作用域）");
        Assert.That(prunedStale, Is.EqualTo(1));
        Assert.That(book.RawCount, Is.EqualTo(1), "剪掉的租约要从账上摘掉，否则每次都白算");
    }

    [Test]
    public void 被剪掉或被整体重置的租约再归还不会把计数减成负数()
    {
        SelectorScopeLeaseBook book = new SelectorScopeLeaseBook();
        long stale = book.Acquire(nowMs: 1_000);
        book.CountActive(nowMs: 100_000, staleAfterMs: 60_000, out _);

        // 卡死的循环若"突然"返回了，会带着旧令牌归还 —— 必须是空操作。
        Assert.That(book.Release(stale), Is.EqualTo(0));

        long live = book.Acquire(nowMs: 200_000);
        Assert.That(book.ResetAll(), Is.EqualTo(1));
        Assert.That(book.Release(live), Is.EqualTo(0));
        Assert.That(book.CountActive(200_100, staleAfterMs: 60_000, out _), Is.EqualTo(0));
    }

    [Test]
    public void 复位返回被清掉的条数供点名日志()
    {
        SelectorScopeLeaseBook book = new SelectorScopeLeaseBook();
        book.Acquire(1_000);
        book.Acquire(1_001);
        book.Acquire(1_002);

        Assert.That(book.ResetAll(), Is.EqualTo(3));
        Assert.That(book.ResetAll(), Is.EqualTo(0), "重复复位必须是无害的（换战斗与退局都会调）");
    }

    [Test]
    public void 零或负阈值表示不剪枝()
    {
        SelectorScopeLeaseBook book = new SelectorScopeLeaseBook();
        book.Acquire(1_000);

        Assert.That(book.CountActive(10_000_000, staleAfterMs: 0, out int pruned), Is.EqualTo(1));
        Assert.That(pruned, Is.EqualTo(0));
    }
}
