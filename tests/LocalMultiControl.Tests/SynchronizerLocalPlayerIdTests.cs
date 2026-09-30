using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 反射身份读写唯一入口（R3 B3）的纯逻辑测试：
/// 钉住「按运行期类型找私有 `_localPlayerId` 字段 / 缺失即 null 或 false / 值不是 ulong 时不炸」三条口径。
///
/// 用测试内的假同步器（带私有字段）验证 —— 不需要游戏安装、不碰 Godot，
/// 而真实同步器（`EventSynchronizer` / `RestSiteSynchronizer` / `RewardsSetSynchronizer` /
/// `HookPlayerChoiceContext` / `RewardSynchronizer`）的字段名与"private ulong"形状与假类型一致。
/// </summary>
[TestFixture]
public class SynchronizerLocalPlayerIdTests
{
    private class FakeSynchronizer
    {
        // ReSharper disable once NotAccessedField.Local —— 只为反射而存在
        private ulong _localPlayerId;

        internal FakeSynchronizer(ulong localPlayerId) => _localPlayerId = localPlayerId;

        internal ulong RawLocalPlayerId => _localPlayerId;
    }

    private class DerivedSynchronizer : FakeSynchronizer
    {
        internal DerivedSynchronizer(ulong localPlayerId) : base(localPlayerId)
        {
        }
    }

    private class NoFieldSynchronizer
    {
        private ulong _somethingElse;

        internal NoFieldSynchronizer() => _somethingElse = 42;

        internal ulong RawSomethingElse => _somethingElse;
    }

    private class WrongTypeSynchronizer
    {
        private int _localPlayerId;

        internal WrongTypeSynchronizer(int localPlayerId) => _localPlayerId = localPlayerId;

        internal int RawLocalPlayerId => _localPlayerId;
    }

    [Test]
    public void 按运行期类型读取私有字段()
    {
        Assert.That(SynchronizerLocalPlayerId.TryRead(new FakeSynchronizer(3)), Is.EqualTo(3UL));
        Assert.That(SynchronizerLocalPlayerId.TryRead(new FakeSynchronizer(0)), Is.EqualTo(0UL));
    }

    [Test]
    public void 显式类型读取与运行期类型读取等价()
    {
        var synchronizer = new FakeSynchronizer(7);
        Assert.That(SynchronizerLocalPlayerId.TryRead(synchronizer, typeof(FakeSynchronizer)), Is.EqualTo(7UL));
    }

    [Test]
    public void 字段声明在基类时按派生运行期类型也能读到()
    {
        // AccessTools.Field 会向上搜基类 —— 旧写法用的是 target.GetType()，本入口必须保持同一口径。
        Assert.That(SynchronizerLocalPlayerId.TryRead(new DerivedSynchronizer(5)), Is.EqualTo(5UL));
    }

    [Test]
    public void 写回后立刻能读到新值_缓存不能变成值快照()
    {
        var synchronizer = new FakeSynchronizer(2);
        Assert.That(SynchronizerLocalPlayerId.TryWrite(synchronizer, 9), Is.True);
        Assert.That(synchronizer.RawLocalPlayerId, Is.EqualTo(9UL));
        Assert.That(SynchronizerLocalPlayerId.TryRead(synchronizer), Is.EqualTo(9UL));

        Assert.That(SynchronizerLocalPlayerId.TryWrite(synchronizer, typeof(FakeSynchronizer), 4), Is.True);
        Assert.That(SynchronizerLocalPlayerId.TryRead(synchronizer), Is.EqualTo(4UL));
    }

    [Test]
    public void 没有该字段时读为null写为false且不抛()
    {
        var synchronizer = new NoFieldSynchronizer();
        Assert.That(SynchronizerLocalPlayerId.TryRead(synchronizer), Is.Null);
        Assert.That(SynchronizerLocalPlayerId.ReadOrZero(synchronizer, typeof(NoFieldSynchronizer)), Is.EqualTo(0UL));
        Assert.That(SynchronizerLocalPlayerId.TryWrite(synchronizer, 1), Is.False);
        Assert.That(synchronizer.RawSomethingElse, Is.EqualTo(42UL));
    }

    [Test]
    public void 字段不是ulong时读为null而不是抛()
    {
        Assert.That(SynchronizerLocalPlayerId.TryRead(new WrongTypeSynchronizer(3)), Is.Null);
        Assert.That(SynchronizerLocalPlayerId.ReadOrZero(new WrongTypeSynchronizer(3), typeof(WrongTypeSynchronizer)),
            Is.EqualTo(0UL));
    }

    [Test]
    public void 空目标读为null写为false且不抛()
    {
        Assert.That(SynchronizerLocalPlayerId.TryRead(null), Is.Null);
        Assert.That(SynchronizerLocalPlayerId.TryRead(null, typeof(FakeSynchronizer)), Is.Null);
        Assert.That(SynchronizerLocalPlayerId.ReadOrZero(null, typeof(FakeSynchronizer)), Is.EqualTo(0UL));
        Assert.That(SynchronizerLocalPlayerId.TryWrite(null, 1), Is.False);
        Assert.That(SynchronizerLocalPlayerId.TryWrite(null, typeof(FakeSynchronizer), 1), Is.False);
    }
}
