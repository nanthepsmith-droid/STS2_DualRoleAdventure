using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 读档恢复瓦库席位的过滤规则测试（r166）。
/// 起因：2026-09-27 实机「读档后瓦库不再自动选事件 / 不出牌」——
/// 读档路径只恢复玩家 id、不恢复 `wakuu=` 段，席位空了 ⇒ `IsVakuuFormMode=false`。
/// 这条测试保证恢复时只认本地席位、丢掉占位值、不重复。
/// </summary>
[TestFixture]
public class WakuuSeatRestorePolicyTests
{
    [Test]
    public void 只保留本地席位里的瓦库id()
    {
        List<ulong> local = new() { 1001, 1002, 1003 };
        List<ulong> saved = new() { 1002, 9999 };

        Assert.That(WakuuSeatRestorePolicy.FilterToLocalSeats(local, saved), Is.EqualTo(new ulong[] { 1002 }));
    }

    [Test]
    public void 丢掉占位0()
    {
        List<ulong> local = new() { 1001, 1002 };
        List<ulong> saved = new() { 0, 1002 };

        Assert.That(WakuuSeatRestorePolicy.FilterToLocalSeats(local, saved), Is.EqualTo(new ulong[] { 1002 }));
    }

    [Test]
    public void 保持存档里的顺序()
    {
        List<ulong> local = new() { 1001, 1002, 1003 };
        List<ulong> saved = new() { 1003, 1001 };

        Assert.That(
            WakuuSeatRestorePolicy.FilterToLocalSeats(local, saved),
            Is.EqualTo(new ulong[] { 1003, 1001 }));
    }

    [Test]
    public void 去重()
    {
        List<ulong> local = new() { 1001, 1002 };
        List<ulong> saved = new() { 1002, 1002, 1001 };

        Assert.That(
            WakuuSeatRestorePolicy.FilterToLocalSeats(local, saved),
            Is.EqualTo(new ulong[] { 1002, 1001 }));
    }

    [Test]
    public void 存档没有瓦库段_返回空()
    {
        List<ulong> local = new() { 1001, 1002 };

        Assert.That(WakuuSeatRestorePolicy.FilterToLocalSeats(local, new List<ulong>()), Is.Empty);
    }

    [Test]
    public void 本地席位为空_返回空()
    {
        Assert.That(
            WakuuSeatRestorePolicy.FilterToLocalSeats(new List<ulong>(), new List<ulong> { 1001 }),
            Is.Empty);
    }

    [Test]
    public void 读档典型场景_两席位里第二个是瓦库()
    {
        // 存档标记 v3:players=1001,1002;wakuu=1002
        List<ulong> local = new() { 1001, 1002 };
        List<ulong> saved = new() { 1002 };

        Assert.That(WakuuSeatRestorePolicy.FilterToLocalSeats(local, saved), Is.EqualTo(new ulong[] { 1002 }));
    }
}
