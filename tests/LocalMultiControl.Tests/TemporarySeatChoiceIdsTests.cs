using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「临时席位（战神）的 choiceId 独立序列」纯逻辑测试（r264，坑见 references AE-24）。
///
/// 背景：原版 `PlayerChoiceSynchronizer.ReserveChoiceId` 用
/// `_choiceIds[runState.GetPlayerSlotIndex(player)]` 记账，而战神不在 `runState.Players` 里
/// ⇒ `GetPlayerSlotIndex` 返回 **-1** ⇒ `_choiceIds[-1]` 抛 `ArgumentOutOfRangeException`
/// ⇒ 冒穿 `CombatManager.StartTurn` ⇒ 原版打 "turn loop died …" ⇒ 整场战斗废掉。
/// 这里钉住"改道"那条判据与计数器的语义（判据错 = 要么没救到战神、要么误伤正式席位）。
/// </summary>
[TestFixture]
public class TemporarySeatChoiceIdsTests
{
    [SetUp]
    public void SetUp() => TemporarySeatChoiceIds.Reset();

    [Test]
    public void 只有本地多控下的临时席位才改道()
    {
        Assert.That(TemporarySeatChoiceIds.ShouldDivert(localMultiEnabled: true, isTemporarySeat: true, seatId: 4096), Is.True);

        // 本地多控没开（原版 / 真联机）⇒ 完全不介入
        Assert.That(TemporarySeatChoiceIds.ShouldDivert(localMultiEnabled: false, isTemporarySeat: true, seatId: 4096), Is.False);
        // 正式席位（真人在 runState.Players 里，slotIndex 正常）⇒ 不动
        Assert.That(TemporarySeatChoiceIds.ShouldDivert(localMultiEnabled: true, isTemporarySeat: false, seatId: 76561198422527326), Is.False);
        // 无效 id（0）
        Assert.That(TemporarySeatChoiceIds.ShouldDivert(localMultiEnabled: true, isTemporarySeat: true, seatId: 0), Is.False);
    }

    [Test]
    public void 分配单调递增且按席位独立()
    {
        Assert.That(TemporarySeatChoiceIds.Reserve(4096), Is.EqualTo(0u));
        Assert.That(TemporarySeatChoiceIds.Reserve(4096), Is.EqualTo(1u));
        Assert.That(TemporarySeatChoiceIds.Reserve(4096), Is.EqualTo(2u));

        // 另一个战神有自己的序列（两个战神交替占卜时不能互相影响 —— 与 r263 的"单槽名单互相清空"同族的坑）
        Assert.That(TemporarySeatChoiceIds.Reserve(4097), Is.EqualTo(0u));
        Assert.That(TemporarySeatChoiceIds.Current(4096), Is.EqualTo(3u));
        Assert.That(TemporarySeatChoiceIds.Current(4097), Is.EqualTo(1u));
    }

    [Test]
    public void 未登记的席位当前值为零()
    {
        Assert.That(TemporarySeatChoiceIds.Current(4096), Is.EqualTo(0u));
        Assert.That(TemporarySeatChoiceIds.HasAny(4096), Is.False);
    }

    [Test]
    public void 复位后序列与首见日志标记一起清空()
    {
        TemporarySeatChoiceIds.Reserve(4096);
        Assert.That(TemporarySeatChoiceIds.MarkLoggedOnce(4096), Is.True, "首见要打日志");
        Assert.That(TemporarySeatChoiceIds.MarkLoggedOnce(4096), Is.False, "同席位不刷屏");

        TemporarySeatChoiceIds.Reset();

        Assert.That(TemporarySeatChoiceIds.Current(4096), Is.EqualTo(0u));
        Assert.That(TemporarySeatChoiceIds.HasAny(4096), Is.False);
        Assert.That(TemporarySeatChoiceIds.MarkLoggedOnce(4096), Is.True, "换局后重新打一次");
    }
}
