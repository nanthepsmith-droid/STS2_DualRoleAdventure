using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「结构外成员（战神）撞上按槽位索引记账的原版代码」护栏判据的纯逻辑测试（r265）。
///
/// 背景：`HoveredModelTracker` 用 `_hoveredModels[runState.GetPlayerSlotIndex(player)]` 记账，
/// 而战神不在 `runState.Players` 里 ⇒ slotIndex = **-1** ⇒ `_hoveredModels[-1]` 抛
/// （`OnPlayerStateRemoved` / `GetHoveredModel` 会直接抛，`OnPlayerStateChanged` 会先 NRE）。
/// 判据错的两个方向都糟：拦少了照样炸；拦多了会把真人的悬停/意图气泡吞掉。
/// </summary>
[TestFixture]
public class ForeignSeatGuardPolicyTests
{
    private static bool Skip(bool enabled, bool temporary, bool inRunState, ulong id = 4096)
    {
        return ForeignSeatGuardPolicy.ShouldSkipHoverTracking(enabled, temporary, inRunState, id);
    }

    [Test]
    public void 本地多控下的临时席位要拦()
    {
        Assert.That(Skip(enabled: true, temporary: true, inRunState: false), Is.True);
    }

    [Test]
    public void 通用兜底_不在正式席位表里的结构外成员也拦()
    {
        // 未登记的第三方结构外成员：不当 -1 判一样会抛 ⇒ 一起兜住
        Assert.That(Skip(enabled: true, temporary: false, inRunState: false), Is.True);
    }

    [Test]
    public void 正式席位一律不拦()
    {
        Assert.That(Skip(enabled: true, temporary: false, inRunState: true), Is.False, "真人/瓦库席位");
        // 极端：登记的临时席位却又在 runState 里（不该发生）⇒ 仍按"临时席位"拦，语义以席位类型为准
        Assert.That(Skip(enabled: true, temporary: true, inRunState: true), Is.True);
    }

    [Test]
    public void 非本地多控一律不介入()
    {
        Assert.That(Skip(enabled: false, temporary: true, inRunState: false), Is.False, "原版 / 真联机");
    }

    [Test]
    public void 空玩家id与拿不到runState时不拦()
    {
        Assert.That(Skip(enabled: true, temporary: false, inRunState: true, id: 0), Is.False);
        // 调用方拿不到 runState 时按"在场"传 ⇒ 不拦（宁可少拦也不误拦真人）
        Assert.That(Skip(enabled: true, temporary: false, inRunState: true), Is.False);
    }

    // ---------------------------------------------------------------- 补动作队列判据（r266）
    // 原版 ActionQueueSet 的队列开局按 runState.Players 建，战神是战斗内才出现的 ⇒
    // 它入队 hook 会在 GetQueue(ownerId) 处抛 "Tried to get local action queue for nonexistent player"。

    [Test]
    public void 只给本地多控下的登记临时席位补动作队列()
    {
        Assert.That(ForeignSeatGuardPolicy.ShouldProvideActionQueue(true, true, 4096), Is.True);
        // 非本地多控（原版 / 真联机）不介入
        Assert.That(ForeignSeatGuardPolicy.ShouldProvideActionQueue(false, true, 4096), Is.False);
        // 正式席位（开局就有队列）不动
        Assert.That(ForeignSeatGuardPolicy.ShouldProvideActionQueue(true, false, 76561198422527326), Is.False);
        // ★ 刻意收窄：**不**用"不在 runState.Players"的通用判据（给任意结构外成员造队列风险更高）
        Assert.That(ForeignSeatGuardPolicy.ShouldProvideActionQueue(true, false, 4096), Is.False);
        Assert.That(ForeignSeatGuardPolicy.ShouldProvideActionQueue(true, true, 0), Is.False);
    }
}
