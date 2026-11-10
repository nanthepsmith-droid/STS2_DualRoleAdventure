using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 诅咒/状态牌的出牌分档纯逻辑（2026-11-10 用户口径，方案 B 三档）：
/// ① 保命档（留手里会掉血/会去世、沙坑倒计时）⇒ 立即打，独占本轮候选；
/// ② 兜底档（其它可打出的诅咒/状态牌）⇒ 排在所有普通牌之后，不占能量硬预留；
/// ③ 普通牌 ⇒ 原顺序。
/// </summary>
[TestFixture]
public class WakuuHazardPlayPolicyTests
{
    private static WakuuHandTierEntry Normal() => new(WakuuHandHazardTier.Normal, canPlay: false);

    private static WakuuHandTierEntry Deferred() => new(WakuuHandHazardTier.Deferred, canPlay: false);

    private static WakuuHandTierEntry Survival(bool canPlay) => new(WakuuHandHazardTier.Survival, canPlay);

    [Test]
    public void 非诅咒状态牌_一律普通档_不看回合结束效果()
    {
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: false, hasTurnEndInHandEffect: true);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Normal), "普通牌不该被分档干扰");
    }

    [Test]
    public void 诅咒带回合结束效果_保命档()
    {
        // 第三方 mod 常见的"不打出就会去世"诅咒：只要按 HasTurnEndInHandEffect 惯例实现就能认出来
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true, hasTurnEndInHandEffect: true);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Survival));
    }

    [Test]
    public void 状态带回合结束效果_保命档()
    {
        // 原版 TOXIC（1 费·消耗，回合结束 5 点无减伤）/ BECKON（1 费，回合结束 6 点失血）
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true, hasTurnEndInHandEffect: true);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Survival));
    }

    [Test]
    public void 狂乱逃离_在沙坑里_保命档()
    {
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true,
            hasTurnEndInHandEffect: false,
            isSurvivalListed: true,
            survivalConditionMet: true);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Survival));
    }

    [Test]
    public void 沙坑倒计时阈值_剩余三回合及以下才算紧急()
    {
        // 2026-11-10 用户口径：计数多时归兜底档（省能量），计数少时才保命档。
        // 沙坑每敌方回合开始 -1，归零即被吞；狂乱逃离没有 Retain，拖到"下回合再说"要重新抽。
        Assert.That(WakuuHazardPlayPolicy.SandpitUrgentTurns, Is.EqualTo(3));

        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(0), Is.False, "0 = 没被沙坑抓");
        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(-1), Is.False);
        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(1), Is.True, "还剩 1 回合一过就被吞");
        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(3), Is.True);
        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(4), Is.False, "初始 4 = 还有 3 回合余量，先归兜底档");
        Assert.That(WakuuHazardPlayPolicy.IsSandpitUrgent(9), Is.False);
    }

    [Test]
    public void 狂乱逃离_沙坑计数还多_归兜底档()
    {
        // GetSandpitTurnsRemaining = 4 ⇒ IsSandpitUrgent=false ⇒ 条件不成立
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true,
            hasTurnEndInHandEffect: false,
            isSurvivalListed: true,
            survivalConditionMet: WakuuHazardPlayPolicy.IsSandpitUrgent(4));

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Deferred));
    }

    [Test]
    public void 狂乱逃离_沙坑计数见底_归保命档()
    {
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true,
            hasTurnEndInHandEffect: false,
            isSurvivalListed: true,
            survivalConditionMet: WakuuHazardPlayPolicy.IsSandpitUrgent(2));

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Survival));
    }

    [Test]
    public void 狂乱逃离_不在沙坑_退回兜底档()
    {
        // 没有沙坑时它打出去只是白费 1 费（还给自己 +1 费），没必要抢在前面
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true,
            hasTurnEndInHandEffect: false,
            isSurvivalListed: true,
            survivalConditionMet: false);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Deferred));
    }

    [Test]
    public void 普通可打出诅咒_兜底档()
    {
        // SPORE_MIND / ENTHRALLED / SLIMED 这类：打出去只是清手/白耗
        WakuuHandHazardTier tier = WakuuHazardPlayPolicy.Classify(
            isCurseOrStatus: true, hasTurnEndInHandEffect: false);

        Assert.That(tier, Is.EqualTo(WakuuHandHazardTier.Deferred));
    }

    [Test]
    public void 全普通牌_返回null_零拷贝走原逻辑()
    {
        IReadOnlyList<WakuuHandTierEntry> hand = new[] { Normal(), Normal(), Normal() };

        Assert.That(WakuuHazardPlayPolicy.ResolvePlayOrder(hand), Is.Null);
    }

    [Test]
    public void 普通牌加兜底牌_普通在前兜底在后()
    {
        IReadOnlyList<WakuuHandTierEntry> hand = new[] { Deferred(), Normal(), Deferred(), Normal() };

        IReadOnlyList<int>? order = WakuuHazardPlayPolicy.ResolvePlayOrder(hand);

        Assert.That(order, Is.EqualTo(new[] { 1, 3, 0, 2 }), "普通牌保持原顺序，兜底牌整体挪到最后");
    }

    [Test]
    public void 有能打的保命牌_本轮只留保命牌()
    {
        IReadOnlyList<WakuuHandTierEntry> hand = new[] { Normal(), Survival(canPlay: true), Deferred(), Normal() };

        IReadOnlyList<int>? order = WakuuHazardPlayPolicy.ResolvePlayOrder(hand);

        Assert.That(order, Is.EqualTo(new[] { 1 }), "保命牌要抢在一切之前打出去");
    }

    [Test]
    public void 两张保命牌只一张能打_仍然只留保命档()
    {
        IReadOnlyList<WakuuHandTierEntry> hand = new[] { Normal(), Survival(canPlay: false), Survival(canPlay: true) };

        IReadOnlyList<int>? order = WakuuHazardPlayPolicy.ResolvePlayOrder(hand);

        Assert.That(order, Is.EqualTo(new[] { 1, 2 }), "保命档整体独占（打不动的那张排后面）");
    }

    [Test]
    public void 保命牌打不动_普通在前_兜底在中_保命在最后()
    {
        IReadOnlyList<WakuuHandTierEntry> hand = new[] { Deferred(), Survival(canPlay: false), Normal() };

        IReadOnlyList<int>? order = WakuuHazardPlayPolicy.ResolvePlayOrder(hand);

        Assert.That(order, Is.EqualTo(new[] { 2, 0, 1 }), "打不动的保命牌留在候选末尾（中途加能量还能救）");
    }

    [Test]
    public void ShouldPrioritizeSurvival_只认能打的保命牌()
    {
        Assert.That(
            WakuuHazardPlayPolicy.ShouldPrioritizeSurvival(new[] { Normal(), Survival(canPlay: false) }),
            Is.False);
        Assert.That(
            WakuuHazardPlayPolicy.ShouldPrioritizeSurvival(new[] { Normal(), Survival(canPlay: true) }),
            Is.True);
    }

    [Test]
    public void 空手牌_不炸且返回null()
    {
        Assert.That(WakuuHazardPlayPolicy.ResolvePlayOrder(new WakuuHandTierEntry[0]), Is.Null);
        Assert.That(WakuuHazardPlayPolicy.ShouldPrioritizeSurvival(new WakuuHandTierEntry[0]), Is.False);
    }

    [Test]
    public void 保命名单id_是全大写蛇形()
    {
        // 游戏 ModelId.Entry 由 StringHelper.Slugify(类型名) 生成（FranticEscape → FRANTIC_ESCAPE），
        // 写错大小写会让"狂乱逃离"永远匹配不上。
        Assert.That(WakuuHazardPlayPolicy.FranticEscapeId, Is.EqualTo("FRANTIC_ESCAPE"));
        Assert.That(WakuuHazardPlayPolicy.FranticEscapeId.All(c => c == '_' || (c >= 'A' && c <= 'Z')), Is.True);
    }
}
