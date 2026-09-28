using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 席位身份唯一取数入口（R3）纯逻辑测试：
/// 钉住「谁是我们席位 / 谁在前台 / 这一席由谁驱动 / 席位表自检」四类口径，
/// 避免以后接线时又出现"四路各自猜身份"。
/// </summary>
[TestFixture]
public class SeatRegistryTests
{
    private static readonly List<ulong> LocalSeats = new() { 100, 101, 102, 103 };

    private static SeatRegistry Create(
        IEnumerable<ulong>? wakuu = null,
        IEnumerable<ulong>? coopBots = null,
        ulong primary = 100,
        ulong? controlled = null,
        ulong? context = null)
    {
        return SeatRegistry.Create(LocalSeats, wakuu, coopBots, primary, controlled, context);
    }

    [Test]
    public void 本地席位判定排除零与第三方席位()
    {
        SeatRegistry registry = Create();
        Assert.That(registry.IsLocalSeat(101), Is.True);
        Assert.That(registry.IsLocalSeat(0), Is.False);
        Assert.That(registry.IsLocalSeat(999), Is.False);
    }

    [Test]
    public void 主席位判定()
    {
        SeatRegistry registry = Create(primary: 101);
        Assert.That(registry.IsPrimary(101), Is.True);
        Assert.That(registry.IsPrimary(100), Is.False);
        Assert.That(registry.IsPrimary(0), Is.False);
        Assert.That(registry.PrimarySeatId, Is.EqualTo(101UL));
    }

    [Test]
    public void 未知席位与远程玩家是两回事()
    {
        SeatRegistry registry = Create();
        SeatIdentity unknown = registry.Resolve(0);
        SeatIdentity remote = registry.Resolve(999);

        Assert.That(unknown.IsUnknown, Is.True);
        Assert.That(unknown.IsRemotePlayer, Is.False);
        Assert.That(remote.IsUnknown, Is.False);
        Assert.That(remote.IsRemotePlayer, Is.True);
        Assert.That(remote.Describe(), Does.Contain("非本地"));
    }

    [Test]
    public void 驱动三态默认真人()
    {
        SeatRegistry registry = Create();
        Assert.That(registry.DriverOf(101), Is.EqualTo(SeatDriverMode.Human));
        Assert.That(registry.IsWakuuDriven(101), Is.False);
        Assert.That(registry.IsCoopBotsDriven(101), Is.False);
    }

    [Test]
    public void 瓦库与联机机器人各自判定()
    {
        SeatRegistry registry = Create(wakuu: new ulong[] { 101 }, coopBots: new ulong[] { 102 });
        Assert.That(registry.DriverOf(101), Is.EqualTo(SeatDriverMode.Wakuu));
        Assert.That(registry.IsWakuuDriven(101), Is.True);
        Assert.That(registry.DriverOf(102), Is.EqualTo(SeatDriverMode.CoopBots));
        Assert.That(registry.IsCoopBotsDriven(102), Is.True);
        Assert.That(registry.IsWakuuDriven(102), Is.False);
    }

    [Test]
    public void 非本地席位的驱动是未知而不是真人()
    {
        SeatRegistry registry = Create(wakuu: new ulong[] { 999 });
        Assert.That(registry.DriverOf(999), Is.Null);
        Assert.That(registry.IsWakuuDriven(999), Is.False);
        Assert.That(registry.IsCoopBotsDriven(999), Is.False);
    }

    [Test]
    public void 三态冲突时联机机器人优先且报自检问题()
    {
        SeatRegistry registry = Create(wakuu: new ulong[] { 101 }, coopBots: new ulong[] { 101 });
        Assert.That(registry.DriverOf(101), Is.EqualTo(SeatDriverMode.CoopBots));
        Assert.That(
            registry.Conflicts.Any(conflict => conflict.Kind == SeatConflictKind.WakuuAndCoopBots
                                               && conflict.SeatId == 101UL),
            Is.True);
    }

    [Test]
    public void 前台受控位优先于上下文()
    {
        SeatRegistry registry = Create(controlled: 102, context: 103);
        Assert.That(registry.ForegroundSeatId, Is.EqualTo(102UL));
        Assert.That(registry.IsForeground(102), Is.True);
        Assert.That(registry.IsForeground(103), Is.False);
        Assert.That(registry.IsControlled(102), Is.True);
        Assert.That(registry.IsContext(103), Is.True);
    }

    [Test]
    public void 没有受控位时前台回落到上下文()
    {
        SeatRegistry registry = Create(context: 103);
        Assert.That(registry.ForegroundSeatId, Is.EqualTo(103UL));
        Assert.That(registry.IsForeground(103), Is.True);
        Assert.That(registry.IsControlled(103), Is.False);
    }

    [Test]
    public void 受控位与上下文都空时前台为零且不等于主席位()
    {
        SeatRegistry registry = Create(primary: 101);
        Assert.That(registry.ForegroundSeatId, Is.EqualTo(0UL));
        Assert.That(registry.ForegroundOrPrimarySeatId, Is.EqualTo(101UL));
        // 严格语义：没有受控位也没有上下文 ⇒ 谁都不是前台（哪怕它是主席位）
        Assert.That(registry.IsForeground(101), Is.False);
    }

    [Test]
    public void 前台回退口径受控位存在时不会再回主席位()
    {
        SeatRegistry registry = Create(primary: 101, controlled: 102, context: null);
        Assert.That(registry.ForegroundOrPrimarySeatId, Is.EqualTo(102UL));
    }

    [Test]
    public void 身份快照把四类事实一起给出()
    {
        SeatRegistry registry = Create(wakuu: new ulong[] { 101 }, primary: 100, controlled: 101, context: 100);
        SeatIdentity identity = registry.Resolve(101);

        Assert.That(identity.SeatId, Is.EqualTo(101UL));
        Assert.That(identity.IsLocalSeat, Is.True);
        Assert.That(identity.IsPrimary, Is.False);
        Assert.That(identity.IsControlled, Is.True);
        Assert.That(identity.IsContext, Is.False);
        Assert.That(identity.IsForeground, Is.True);
        Assert.That(identity.Driver, Is.EqualTo(SeatDriverMode.Wakuu));
        Assert.That(identity.Describe(), Does.Contain("本地").And.Contain("瓦库").And.Contain("前台"));
    }

    [Test]
    public void 主席位快照描述带主席位标记()
    {
        SeatRegistry registry = Create(primary: 100, controlled: 100);
        Assert.That(registry.Resolve(100).Describe(), Does.Contain("主席位").And.Contain("受控"));
        Assert.That(registry.Resolve(0).Describe(), Is.EqualTo("seat=0(未知)"));
    }

    [Test]
    public void 本地席位表归一化丢零去重并保持顺序()
    {
        SeatRegistry registry = SeatRegistry.Create(
            new ulong[] { 0, 103, 101, 103, 0, 102 },
            null,
            null,
            primarySeatId: 103);

        Assert.That(registry.LocalSeatIds, Is.EqualTo(new List<ulong> { 103, 101, 102 }));
        Assert.That(registry.Conflicts.Any(c => c.Kind == SeatConflictKind.ZeroSeat), Is.True);
        Assert.That(
            registry.Conflicts.Any(c => c.Kind == SeatConflictKind.DuplicateLocalSeat && c.SeatId == 103UL),
            Is.True);
    }

    [Test]
    public void 主席位不在本地席位表时报自检问题()
    {
        SeatRegistry registry = SeatRegistry.Create(new ulong[] { 100, 101 }, null, null, primarySeatId: 777);
        Assert.That(
            registry.Conflicts.Any(c => c.Kind == SeatConflictKind.PrimaryNotLocalSeat && c.SeatId == 777UL),
            Is.True);
        Assert.That(registry.IsPrimary(777), Is.True);
        Assert.That(registry.IsLocalSeat(777), Is.False);
    }

    [Test]
    public void 孤儿驱动席位报自检问题()
    {
        SeatRegistry registry = Create(
            wakuu: new ulong[] { 101, 888 },
            coopBots: new ulong[] { 102, 777 });

        Assert.That(
            registry.Conflicts.Any(c => c.Kind == SeatConflictKind.WakuuNotLocalSeat && c.SeatId == 888UL),
            Is.True);
        Assert.That(
            registry.Conflicts.Any(c => c.Kind == SeatConflictKind.CoopBotsNotLocalSeat && c.SeatId == 777UL),
            Is.True);
    }

    [Test]
    public void 空表与空入参不产生问题()
    {
        SeatRegistry registry = SeatRegistry.Create(null, null, null, primarySeatId: 0);
        Assert.That(registry.LocalSeatIds, Is.Empty);
        Assert.That(registry.Conflicts, Is.Empty);
        Assert.That(registry.DescribeConflicts(), Is.EqualTo("席位表自检通过"));
        Assert.That(registry.ForegroundSeatId, Is.EqualTo(0UL));
        Assert.That(registry.ForegroundOrPrimarySeatId, Is.EqualTo(0UL));
    }

    [Test]
    public void 自检结果按类型与席位排序且可拼成一行()
    {
        SeatRegistry registry = SeatRegistry.Create(
            new ulong[] { 100, 101, 0 },
            new ulong[] { 101, 888 },
            new ulong[] { 101 },
            primarySeatId: 999);

        List<SeatConflictKind> kinds = registry.Conflicts.Select(c => c.Kind).ToList();
        Assert.That(kinds, Is.Ordered);
        Assert.That(kinds, Does.Contain(SeatConflictKind.ZeroSeat));
        Assert.That(kinds, Does.Contain(SeatConflictKind.PrimaryNotLocalSeat));
        Assert.That(kinds, Does.Contain(SeatConflictKind.WakuuNotLocalSeat));
        Assert.That(kinds, Does.Contain(SeatConflictKind.WakuuAndCoopBots));

        string described = registry.DescribeConflicts();
        Assert.That(described, Does.Contain("三态互斥"));
        Assert.That(described, Does.Not.EqualTo("席位表自检通过"));
    }
}
