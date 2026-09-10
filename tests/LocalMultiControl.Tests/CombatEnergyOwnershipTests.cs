using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战斗能量球归属判定（r96，BUG-1 战斗第一回合能量不同步）纯逻辑测试：
/// 不变量是「能量球所属玩家 == 当前展示的手牌所属玩家」，手牌归属取不到时才退化到受控玩家。
/// </summary>
[TestFixture]
public class CombatEnergyOwnershipTests
{
    [Test]
    public void 归属一致时不重建()
    {
        // 正常回合的绝大多数情况：能量球与手牌同属一个玩家 → 零开销直接放过
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 326UL, handPlayerId: 326UL, controlledPlayerId: 326UL, out _),
            Is.False);
    }

    [Test]
    public void 能量属于瓦库而手牌属于真人时按手牌重建()
    {
        // BUG-1 现场：手牌是真人（326），能量球还挂在瓦库（327）上
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 327UL, handPlayerId: 326UL, controlledPlayerId: 326UL, out ulong target),
            Is.True);
        Assert.That(target, Is.EqualTo(326UL));
    }

    [Test]
    public void 手牌归属缺失时退化到受控玩家()
    {
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 327UL, handPlayerId: null, controlledPlayerId: 326UL, out ulong target),
            Is.True);
        Assert.That(target, Is.EqualTo(326UL));
    }

    [Test]
    public void 手牌归属优先于受控玩家()
    {
        // 手牌还没跟上受控玩家的切换 → 必须跟手牌，不能跟受控玩家（否则正是 BUG-1 的分家态）
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 326UL, handPlayerId: 327UL, controlledPlayerId: 326UL, out ulong target),
            Is.True);
        Assert.That(target, Is.EqualTo(327UL));
    }

    [Test]
    public void 无任何归属信息时不做无依据的重建()
    {
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 326UL, handPlayerId: null, controlledPlayerId: null, out _),
            Is.False);
    }

    [Test]
    public void 归属为0视为无效不做重建()
    {
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: 326UL, handPlayerId: 0UL, controlledPlayerId: 0UL, out _),
            Is.False);
    }

    [Test]
    public void 能量球尚未创建时按手牌归属创建()
    {
        Assert.That(
            CombatEnergyOwnership.TryResolveMismatch(
                energyPlayerId: null, handPlayerId: 327UL, controlledPlayerId: 326UL, out ulong target),
            Is.True);
        Assert.That(target, Is.EqualTo(327UL));
    }
}
