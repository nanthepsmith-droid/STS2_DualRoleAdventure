using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库奖励自动领取判据测试（r150）。
///
/// 回归背景（2026-09-26 实机）：玩家报「瓦库似乎不会取回自己被偷走的牌」。
/// 根因 = `SpecialCardReward`（原版用它把**被跳虫偷走的牌**还给失主，源码注释点名）原先落在
/// `default: return false`（"删牌/特殊奖励等保持人工"）⇒ 只能靠真人在合并奖励屏上手动点那一条，
/// 真人没点就永远拿不回。本判据把「特定卡牌」归到与卡牌奖励同一个开关 `autoClaimCards`。
/// </summary>
[TestFixture]
public class WakuuRewardClaimPolicyTests
{
    [Test]
    public void 特定卡牌奖励_瓦库_开关开_自动领取()
    {
        Assert.That(
            WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.SpecialCard, true, true, true, true),
            Is.True);
    }

    [Test]
    public void 特定卡牌奖励_卡牌开关关_不自动领取()
    {
        Assert.That(
            WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.SpecialCard, true, false, true, true),
            Is.False);
    }

    [Test]
    public void 特定卡牌奖励_非瓦库_永不自动领取()
    {
        Assert.That(
            WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.SpecialCard, false, true, true, true),
            Is.False);
    }

    [Test]
    public void 删牌类奖励_其它种类_保持人工()
    {
        // 刻意的口径：CardRemovalReward 这类归 Other，必须留给真人（避免用错选牌规则）。
        Assert.That(
            WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Other, true, true, true, true),
            Is.False);
    }

    [TestCase(true, true)]
    [TestCase(false, false)]
    public void 金币与遗物_跟随遗物金币开关(bool flag, bool expected)
    {
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Gold, true, true, flag, true), Is.EqualTo(expected));
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Relic, true, true, flag, true), Is.EqualTo(expected));
    }

    [Test]
    public void 药水_跟随药水开关()
    {
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Potion, true, true, true, true), Is.True);
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Potion, true, true, true, false), Is.False);
    }

    [Test]
    public void 卡牌奖励_与非瓦库角色_口径不变()
    {
        // 回归：原有三种类型的判定不能被 r150 的改动带偏。
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Card, true, true, false, false), Is.True);
        Assert.That(WakuuRewardClaimPolicy.ShouldAutoClaim(WakuuRewardKind.Card, false, true, true, true), Is.False);
    }
}
