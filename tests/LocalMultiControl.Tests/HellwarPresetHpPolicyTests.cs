#nullable enable
using LocalMultiControl.Scripts.Runtime.PureLogic;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 预设战神的**额外血上限**（r289，2026-10-20 用户拍板）：<c>bonusHp</c> 正数加、**负数减**，
/// 减到不够时收在**最少 1 点**。
///
/// 为什么下限是 1：原版 <c>Creature.SetMaxHpInternal</c> 对负数直接抛
/// <c>ArgumentException("amount must be non-negative")</c>，而 0 上限的怪会当场进死亡流程
/// ⇒ 归一阶段算不了（拿不到角色基础血量），只能在施加时按这条规则收。
/// </summary>
[TestFixture]
public class HellwarPresetHpPolicyTests
{
    [Test]
    public void 零表示不调整()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetHpPolicy.ShouldApply(0), Is.False);
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(70, 0), Is.EqualTo(70));
        });
    }

    [Test]
    public void 正数加血上限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetHpPolicy.ShouldApply(25), Is.True);
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(70, 25), Is.EqualTo(95));
            Assert.That(HellwarPresetHpPolicy.WasClampedToMinimum(70, 25), Is.False);
        });
    }

    [Test]
    public void 负数减血上限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetHpPolicy.ShouldApply(-39), Is.True);
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(70, -39), Is.EqualTo(31));
            Assert.That(HellwarPresetHpPolicy.WasClampedToMinimum(70, -39), Is.False);
        });
    }

    [Test]
    public void 扣得比血还多时收在1点()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(30, -39), Is.EqualTo(HellwarPresetHpPolicy.MinimumMaxHp));
            Assert.That(HellwarPresetHpPolicy.WasClampedToMinimum(30, -39), Is.True);
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(39, -39), Is.EqualTo(1), "刚好扣到 0 也要收在 1");
        });
    }

    [Test]
    public void 刚好扣到1点不算被收住()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HellwarPresetHpPolicy.ResolveMaxHp(40, -39), Is.EqualTo(1));
            Assert.That(HellwarPresetHpPolicy.WasClampedToMinimum(40, -39), Is.False, "这是算出来的 1，不是兜底收的");
        });
    }
}
