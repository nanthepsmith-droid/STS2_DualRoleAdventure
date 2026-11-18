using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 大键盘数字键「切到第 N 号席位」的纯逻辑判定（2026-11-18 §排期 5）。
///
/// 这些边界原先只能靠实机按一遍看"切没切对/有没有提示"：号位越界、会话没初始化、
/// 本地多控没开（**必须完全静默**，否则会抢原版单人的 1~0）、战斗内席位已逃跑/阵亡。
/// </summary>
[TestFixture]
public class SeatHotkeyPolicyTests
{
    private static readonly List<ulong> FourSeats = new() { 100, 200, 300, 400 };

    private static SeatHotkeyResult Resolve(
        int seatNumber,
        IReadOnlyList<ulong>? seats = null,
        ulong? current = null,
        IReadOnlyList<ulong>? combatSeats = null,
        bool runInProgress = true,
        bool localCoopEnabled = true,
        bool sessionInitialized = true)
    {
        return SeatHotkeyPolicy.Resolve(
            seatNumber,
            runInProgress,
            localCoopEnabled,
            sessionInitialized,
            seats ?? FourSeats,
            current,
            combatSeats);
    }

    [Test]
    public void 正常解析_按会话席位顺序取第N个()
    {
        SeatHotkeyResult result = Resolve(3);

        Assert.That(result.Status, Is.EqualTo(SeatHotkeyStatus.Ok));
        Assert.That(result.PlayerId, Is.EqualTo(300UL));
        Assert.That(result.SeatNumber, Is.EqualTo(3));
        Assert.That(result.SeatCount, Is.EqualTo(4));
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.NeedsNotice, Is.False);
    }

    [Test]
    public void 首末席位边界()
    {
        Assert.That(Resolve(1).PlayerId, Is.EqualTo(100UL));
        Assert.That(Resolve(4).PlayerId, Is.EqualTo(400UL));
    }

    [Test]
    public void 号位越界_给提示而不是硬切()
    {
        SeatHotkeyResult beyondCount = Resolve(5);
        Assert.That(beyondCount.Status, Is.EqualTo(SeatHotkeyStatus.OutOfRange));
        Assert.That(beyondCount.Succeeded, Is.False);
        Assert.That(beyondCount.NeedsNotice, Is.True);
        Assert.That(beyondCount.SeatCount, Is.EqualTo(4), "提示文案要用真实席位数");

        Assert.That(Resolve(0).Status, Is.EqualTo(SeatHotkeyStatus.OutOfRange));
        Assert.That(Resolve(-1).Status, Is.EqualTo(SeatHotkeyStatus.OutOfRange));
        Assert.That(Resolve(13).Status, Is.EqualTo(SeatHotkeyStatus.OutOfRange), "上限 12（席位上限）");
    }

    [Test]
    public void 高号位_席位数够时正常解析()
    {
        List<ulong> twelveSeats = new();
        for (ulong i = 1; i <= 12; i++)
        {
            twelveSeats.Add(i);
        }

        Assert.That(Resolve(12, twelveSeats).Status, Is.EqualTo(SeatHotkeyStatus.Ok));
        Assert.That(Resolve(12, twelveSeats).PlayerId, Is.EqualTo(12UL));
    }

    [Test]
    public void 不在局内_不响应()
    {
        SeatHotkeyResult result = Resolve(1, runInProgress: false);

        Assert.That(result.Status, Is.EqualTo(SeatHotkeyStatus.RunNotInProgress));
        Assert.That(result.NeedsNotice, Is.False, "局内判定之外的按键不该弹提示");
    }

    [Test]
    public void 本地多控未开_必须完全静默()
    {
        SeatHotkeyResult result = Resolve(1, localCoopEnabled: false);

        Assert.That(result.Status, Is.EqualTo(SeatHotkeyStatus.LocalCoopDisabled));
        Assert.That(result.Succeeded, Is.False);
        Assert.That(result.NeedsNotice, Is.False, "原版单人按 1~0 不该看到任何提示");
    }

    [Test]
    public void 会话未初始化_给提示()
    {
        Assert.That(Resolve(1, sessionInitialized: false).Status,
            Is.EqualTo(SeatHotkeyStatus.SessionNotInitialized));
        Assert.That(Resolve(1, new List<ulong>()).Status,
            Is.EqualTo(SeatHotkeyStatus.SessionNotInitialized));
        Assert.That(Resolve(1, sessionInitialized: false).NeedsNotice, Is.True);
    }

    [Test]
    public void 战斗内_席位不在战斗里就不切()
    {
        // 100 已逃跑/阵亡：不在 combatSeatIds 里
        List<ulong> combatSeats = new() { 200, 300, 400 };

        SeatHotkeyResult result = Resolve(1, combatSeats: combatSeats);

        Assert.That(result.Status, Is.EqualTo(SeatHotkeyStatus.NotInCombat));
        Assert.That(result.PlayerId, Is.EqualTo(100UL), "提示要能指出是哪个号位");
        Assert.That(result.NeedsNotice, Is.True);

        Assert.That(Resolve(2, combatSeats: combatSeats).Status, Is.EqualTo(SeatHotkeyStatus.Ok));
    }

    [Test]
    public void 不在战斗时_不做战斗席位校验()
    {
        // combatSeats = null ⇒ 局外（地图/商店/事件），全部席位都可切
        Assert.That(Resolve(1, combatSeats: null).Status, Is.EqualTo(SeatHotkeyStatus.Ok));
    }

    [Test]
    public void 按的是当前席位_不算失败也不提示()
    {
        SeatHotkeyResult result = Resolve(2, current: 200);

        Assert.That(result.Status, Is.EqualTo(SeatHotkeyStatus.AlreadyCurrent));
        Assert.That(result.Succeeded, Is.True);
        Assert.That(result.NeedsNotice, Is.False);
        Assert.That(result.PlayerId, Is.EqualTo(200UL));
    }

    [Test]
    public void 当前席位未知时_照常解析()
    {
        Assert.That(Resolve(2, current: null).Status, Is.EqualTo(SeatHotkeyStatus.Ok));
        Assert.That(Resolve(2, current: 999UL).Status, Is.EqualTo(SeatHotkeyStatus.Ok));
    }

    [Test]
    public void 只有提示类状态才需要弹字()
    {
        // BlockedByCombatBusy / BlockedByMinigame 由运行时构造（守卫在运行时先行判定），这里只锁口径：
        Assert.That(SeatHotkeyResult.Of(SeatHotkeyStatus.BlockedByCombatBusy, 3, 4).NeedsNotice, Is.False,
            "敌人回合 / 非出牌相位连按几下不该刷屏（与 Tab 循环静默忽略一致）");
        Assert.That(SeatHotkeyResult.Of(SeatHotkeyStatus.BlockedByMinigame, 3, 4).NeedsNotice, Is.True);
        Assert.That(SeatHotkeyResult.Of(SeatHotkeyStatus.BlockedByCardSelection, 3, 4).NeedsNotice, Is.True);
        Assert.That(SeatHotkeyResult.Of(SeatHotkeyStatus.Ok, 3, 4).NeedsNotice, Is.False);
        Assert.That(SeatHotkeyResult.Of(SeatHotkeyStatus.AlreadyCurrent, 3, 4).NeedsNotice, Is.False);
    }
}
