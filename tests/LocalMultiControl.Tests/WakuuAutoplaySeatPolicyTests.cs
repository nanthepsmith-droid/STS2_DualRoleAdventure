using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 战灵召唤 Phase 3（R11）：**托管出牌席位判据 + 每回合出牌上限**的用例。
///
/// 这一层的价值：把"战神（临时席位）也算托管席位""战神也按全量出牌（60）"钉成断言 ——
/// 这两条一旦被回退，战神就既不会被托管出牌、也打不完先古那一把手牌（Phase 2b 的档位强度体现不出来）。
/// </summary>
[TestFixture]
public class WakuuAutoplaySeatPolicyTests
{
    [Test]
    public void 持接管遗物的席位由托管出牌()
    {
        Assert.That(
            WakuuAutoplaySeatPolicy.ShouldAutoplay(hasTakeoverRelic: true, isTemporarySeat: false),
            Is.True);
    }

    [Test]
    public void 临时席位没有遗物也由托管出牌()
    {
        // Phase 3 的核心：战神刻意**不持有**瓦库遗物（挂上去会带上"回合开始接管前台 / 加能量"等副作用）
        // ⇒ 判据必须显式认临时席位，不能靠"持有遗物"这一条蕴含。
        Assert.That(
            WakuuAutoplaySeatPolicy.ShouldAutoplay(hasTakeoverRelic: false, isTemporarySeat: true),
            Is.True);
    }

    [Test]
    public void 两者都不是则不托管()
    {
        Assert.That(
            WakuuAutoplaySeatPolicy.ShouldAutoplay(hasTakeoverRelic: false, isTemporarySeat: false),
            Is.False);
    }

    [Test]
    public void 战神按全量出牌上限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuAutoplaySeatPolicy.UsesFullPlayLimit(vakuuFormMode: false, isTemporarySeat: true, playAllCards: true),
                Is.True);
            Assert.That(
                WakuuAutoplaySeatPolicy.ResolveMaxCardsPerTurn(fullPlay: true),
                Is.EqualTo(WakuuAutoplaySeatPolicy.MaxCardsFull));
        });
    }

    [Test]
    public void 关掉打光手牌开关时战神也沿用十三张上限()
    {
        // 与瓦库形态同款口径：开关关掉 ⇒ 一律回落到原版 13 张（别在这里偷偷开绿灯）。
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuAutoplaySeatPolicy.UsesFullPlayLimit(vakuuFormMode: true, isTemporarySeat: true, playAllCards: false),
                Is.False);
            Assert.That(
                WakuuAutoplaySeatPolicy.ResolveMaxCardsPerTurn(fullPlay: false),
                Is.EqualTo(WakuuAutoplaySeatPolicy.MaxCardsDefault));
        });
    }

    [Test]
    public void 普通席位用默认上限()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                WakuuAutoplaySeatPolicy.UsesFullPlayLimit(vakuuFormMode: false, isTemporarySeat: false, playAllCards: true),
                Is.False);
            Assert.That(
                WakuuAutoplaySeatPolicy.ResolveMaxCardsPerTurn(fullPlay: false),
                Is.EqualTo(13));
        });
    }

    [Test]
    public void 战神走托管出牌路径()
    {
        // r251 的核心：临时席位必须走队列/并发档（不抢闸门、不钉 LocalContext）——
        // 否则它出牌的那几秒里真人点不出牌。
        Assert.That(
            WakuuAutoplaySeatPolicy.UsesHostedPlayPath(vakuuFormMode: false, isTemporarySeat: true),
            Is.True);
        Assert.That(
            WakuuAutoplaySeatPolicy.UsesHostedPlayPath(vakuuFormMode: true, isTemporarySeat: false),
            Is.True);
    }

    [Test]
    public void 普通席位不走托管出牌路径()
    {
        Assert.That(
            WakuuAutoplaySeatPolicy.UsesHostedPlayPath(vakuuFormMode: false, isTemporarySeat: false),
            Is.False);
    }
}
