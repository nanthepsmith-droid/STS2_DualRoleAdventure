using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 【探针】瓦库卡牌奖励不领的判据测试（TODO §讨论-1 第一步）。
///
/// 探针的目的只有一个：把「只跳过卡牌之后，同组金币 / 药水 / 遗物照常能领、奖励组正常完成」
/// 在实机上验证出来。所以判据必须**只**命中「瓦库形态 + 卡牌 + 不在链接奖励组」这一种组合，
/// 其余一律不动 —— 那些正是对照组，被跳掉就验不出任何东西。
/// </summary>
[TestFixture]
public class WakuuCardRewardSkipProbePolicyTests
{
    [Test]
    public void 开关开_瓦库卡牌奖励_跳过()
    {
        Assert.That(
            WakuuCardRewardSkipProbePolicy.ShouldSkip(
                probeOn: true,
                isVakuuForm: true,
                kind: WakuuRewardKind.Card,
                hasParentRewardSet: false),
            Is.True);
    }

    [Test]
    public void 开关关_零行为变化()
    {
        Assert.That(
            WakuuCardRewardSkipProbePolicy.ShouldSkip(
                probeOn: false,
                isVakuuForm: true,
                kind: WakuuRewardKind.Card,
                hasParentRewardSet: false),
            Is.False);
    }

    [Test]
    public void 非瓦库角色的卡牌奖励_不动()
    {
        // 真人 / 第三方席位的奖励一律不碰：它们在合并屏上得能照常点。
        Assert.That(
            WakuuCardRewardSkipProbePolicy.ShouldSkip(
                probeOn: true,
                isVakuuForm: false,
                kind: WakuuRewardKind.Card,
                hasParentRewardSet: false),
            Is.False);
    }

    [Test]
    public void 非卡牌奖励_照旧自动领_保持对照组()
    {
        // 金币 / 药水 / 遗物 / 特殊卡牌（取回被偷的牌）就是本探针要看的对照组，一张都不能跳。
        foreach (WakuuRewardKind kind in new[]
                 {
                     WakuuRewardKind.Gold,
                     WakuuRewardKind.Relic,
                     WakuuRewardKind.Potion,
                     WakuuRewardKind.SpecialCard,
                     WakuuRewardKind.Other,
                 })
        {
            Assert.That(
                WakuuCardRewardSkipProbePolicy.ShouldSkip(
                    probeOn: true,
                    isVakuuForm: true,
                    kind: kind,
                    hasParentRewardSet: false),
                Is.False,
                $"kind={kind} 不该被探针跳过");
        }
    }

    [Test]
    public void 链接奖励组内部_一律不跳()
    {
        // LinkedRewardSet 要把内部奖励摘掉再收口整组（原版只在自己 SelectUnsynchronized 里做）；
        // 我们单摘不推进会把这个组卡成永远不完成。
        Assert.That(
            WakuuCardRewardSkipProbePolicy.ShouldSkip(
                probeOn: true,
                isVakuuForm: true,
                kind: WakuuRewardKind.Card,
                hasParentRewardSet: true),
            Is.False);
    }
}
