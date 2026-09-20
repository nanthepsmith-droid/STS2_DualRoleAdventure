using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 卡牌评分与排序纯函数测试（§18.2.4「阶段 B：卡牌评分函数」，2026-09-20）。
///
/// 要钉死的语义：
/// ① 类型基础分 Skill &gt; Attack、0 费加分；
/// ② **致死线是硬门槛**（§21.4.2 #4）：不格挡会被打死且手上有格挡牌时，只从格挡牌里选，
///    哪怕攻击牌的总分更高（这是"线性总分"缺陷的兜底手段）；
/// ③ **X 费牌收尾**（§18.2.7）：只要还有非 X 费可打牌，就不选 X 费；
/// ④ 同分保留最左（与既有"最左兜底"一致）；
/// ⑤ 分数下限 0（惩罚项不产生负分）。
/// </summary>
[TestFixture]
public class WakuuCardScoringTests
{
    private static WakuuScoreInput Card(
        WakuuScoreCardKind kind = WakuuScoreCardKind.Attack,
        int cost = 1,
        bool isXCost = false,
        bool gainsBlock = false,
        WakuuScoreKeywords keywords = WakuuScoreKeywords.None,
        WakuuScoreTargetKind targetKind = WakuuScoreTargetKind.None,
        int damage = 0,
        int block = 0)
    {
        return new WakuuScoreInput(kind, cost, isXCost, gainsBlock, keywords, targetKind, damage, block);
    }

    /// <summary>平静局面：满血、2 回合、单敌、无击杀窗口、无致死线。</summary>
    private static WakuuScoreSituation Calm(int energy = 3, int enemyCount = 1, int weakestEffectiveHp = -1)
    {
        return new WakuuScoreSituation(
            playerHp: 80,
            playerMaxHp: 80,
            energy: energy,
            turnNumber: 2,
            enemyCount: enemyCount,
            weakestEnemyEffectiveHp: weakestEffectiveHp,
            lethalDanger: false);
    }

    [Test]
    public void 类型基础分_技能高于攻击_攻击高于其它()
    {
        WakuuScoreSituation situation = Calm();
        int skill = WakuuCardScoring.Score(Card(kind: WakuuScoreCardKind.Skill, cost: 2), situation);
        int attack = WakuuCardScoring.Score(Card(kind: WakuuScoreCardKind.Attack, cost: 2), situation);
        int other = WakuuCardScoring.Score(Card(kind: WakuuScoreCardKind.Other, cost: 2), situation);

        Assert.Multiple(() =>
        {
            Assert.That(skill, Is.EqualTo(35));
            Assert.That(attack, Is.EqualTo(30));
            Assert.That(other, Is.EqualTo(10));
            Assert.That(skill, Is.GreaterThan(attack));
            Assert.That(attack, Is.GreaterThan(other));
        });
    }

    [Test]
    public void 费用修正_零费加十五_一费加五_二费不加()
    {
        WakuuScoreSituation situation = Calm();
        int zero = WakuuCardScoring.Score(Card(cost: 0), situation);
        int one = WakuuCardScoring.Score(Card(cost: 1), situation);
        int two = WakuuCardScoring.Score(Card(cost: 2), situation);

        Assert.Multiple(() =>
        {
            Assert.That(zero, Is.EqualTo(45)); // 30 + 15
            Assert.That(one, Is.EqualTo(35));  // 30 + 5
            Assert.That(two, Is.EqualTo(30));  // 30
        });
    }

    [Test]
    public void 关键词修正_虚无加分_消耗与保留减分()
    {
        WakuuScoreSituation situation = Calm();
        int plain = WakuuCardScoring.Score(Card(cost: 2), situation);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardScoring.Score(Card(cost: 2, keywords: WakuuScoreKeywords.Ethereal), situation),
                Is.EqualTo(plain + 10));
            Assert.That(WakuuCardScoring.Score(Card(cost: 2, keywords: WakuuScoreKeywords.Exhaust), situation),
                Is.EqualTo(plain - 5));
            Assert.That(WakuuCardScoring.Score(Card(cost: 2, keywords: WakuuScoreKeywords.Retain), situation),
                Is.EqualTo(plain - 8));
        });
    }

    [Test]
    public void 分数下限_惩罚项再重也不为负()
    {
        WakuuScoreSituation situation = Calm();
        int score = WakuuCardScoring.Score(
            Card(kind: WakuuScoreCardKind.Other, cost: 3, keywords: WakuuScoreKeywords.Exhaust | WakuuScoreKeywords.Retain),
            situation);

        Assert.That(score, Is.EqualTo(0));
    }

    [Test]
    public void 击杀窗口_攻击牌加分且能击杀时额外加分()
    {
        WakuuScoreSituation killable = Calm(weakestEffectiveHp: 15);
        WakuuScoreSituation noWindow = Calm();

        int withWindow = WakuuCardScoring.Score(Card(damage: 20), killable);
        int without = WakuuCardScoring.Score(Card(damage: 20), noWindow);

        // +20（有击杀窗口的攻击）+40（这一张就能击杀）
        Assert.That(withWindow - without, Is.EqualTo(60));
    }

    [Test]
    public void 击杀窗口_打不死同窗口的敌人时只加二十()
    {
        WakuuScoreSituation killable = Calm(weakestEffectiveHp: 30);

        int shaky = WakuuCardScoring.Score(Card(damage: 5), killable);
        int without = WakuuCardScoring.Score(Card(damage: 5), Calm());

        Assert.That(shaky - without, Is.EqualTo(20));
    }

    [Test]
    public void 致死线_有格挡牌时硬门槛压过更高的攻击分()
    {
        WakuuScoreSituation lethal = new(
            playerHp: 5, playerMaxHp: 80, energy: 3, turnNumber: 2,
            enemyCount: 1, weakestEnemyEffectiveHp: 10, lethalDanger: true);

        // 攻击：0 费 + 有击杀窗口且能击杀 ⇒ 30+15+20+40 = 105
        WakuuScoreInput attack = Card(cost: 0, damage: 20);
        // 格挡：技能 35 + 致死线防御 30 + 格挡量 5 = 70
        WakuuScoreInput block = Card(kind: WakuuScoreCardKind.Skill, cost: 2, gainsBlock: true, block: 5);

        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardScoring.Score(attack, lethal), Is.EqualTo(105), "先确认攻击牌分更高（否则这条测试没意义）");
            Assert.That(WakuuCardScoring.Score(block, lethal), Is.EqualTo(70));
            Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { attack, block }, lethal),
                Is.EqualTo(1), "致死线优先保命：必须选格挡牌，而不是分数更高的攻击牌");
        });
    }

    [Test]
    public void 致死线_手上没有格挡牌时按分数正常选()
    {
        WakuuScoreSituation lethal = new(
            playerHp: 5, playerMaxHp: 80, energy: 3, turnNumber: 2,
            enemyCount: 1, weakestEnemyEffectiveHp: 10, lethalDanger: true);

        WakuuScoreInput big = Card(cost: 0, damage: 20);   // 105
        WakuuScoreInput small = Card(cost: 3, damage: 1);  // 30（无击杀窗口加分：1 < 10 ⇒ 仍有 +20 ⇒ 50）

        Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { small, big }, lethal),
            Is.EqualTo(1));
    }

    [Test]
    public void 非致死线_不强制格挡_按分数选()
    {
        WakuuScoreSituation calm = Calm(weakestEffectiveHp: 10);
        WakuuScoreInput attack = Card(cost: 0, damage: 20);  // 30+15+20+40 = 105
        WakuuScoreInput block = Card(kind: WakuuScoreCardKind.Skill, cost: 2, gainsBlock: true, block: 5);

        Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { attack, block }, calm),
            Is.EqualTo(0));
    }

    [Test]
    public void 血量危险_未到致死线也给格挡牌加分()
    {
        // 10/80 ⇒ 低于一半 ⇒ needsDefense
        WakuuScoreSituation dangerous = new(
            playerHp: 10, playerMaxHp: 80, energy: 3, turnNumber: 2,
            enemyCount: 1, weakestEnemyEffectiveHp: -1, lethalDanger: false);

        WakuuScoreInput attack = Card(cost: 1);                                            // 35
        WakuuScoreInput block = Card(kind: WakuuScoreCardKind.Skill, cost: 1, gainsBlock: true, block: 6); // 35+5+30+6=76

        Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { attack, block }, dangerous),
            Is.EqualTo(1));
    }

    [Test]
    public void 血量正好一半_不算危险()
    {
        WakuuScoreSituation half = new(
            playerHp: 40, playerMaxHp: 80, energy: 3, turnNumber: 2,
            enemyCount: 1, weakestEnemyEffectiveHp: -1, lethalDanger: false);

        WakuuScoreInput attack = Card(cost: 1);                                                             // 35
        WakuuScoreInput block = Card(kind: WakuuScoreCardKind.Skill, cost: 1, gainsBlock: true, block: 6);   // 35+5+6=46

        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardScoring.Score(attack, half), Is.EqualTo(35));
            Assert.That(WakuuCardScoring.Score(block, half), Is.EqualTo(40), "刚好一半不加 30 分的防御权重（技能 35 + 1 费 5）");
        });
    }

    [Test]
    public void X费牌_有非X费可打时不选X费_哪怕X分更高()
    {
        WakuuScoreSituation situation = Calm(energy: 5);

        // X 费能力牌在能量充足时分数很高：50（能力）+ 20（X 值）+ 10（能量富余打贵牌）= 80
        WakuuScoreInput xCard = Card(kind: WakuuScoreCardKind.Power, cost: 5, isXCost: true);
        WakuuScoreInput normal = Card(cost: 1); // 35

        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardScoring.Score(xCard, situation), Is.EqualTo(80));
            Assert.That(WakuuCardScoring.Score(normal, situation), Is.EqualTo(35));
            Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { xCard, normal }, situation),
                Is.EqualTo(1), "X 值应当攒到最后：先打普通牌");
        });
    }

    [Test]
    public void X费牌_只剩X费时可选()
    {
        WakuuScoreSituation situation = Calm(energy: 5);
        WakuuScoreInput xCard = Card(kind: WakuuScoreCardKind.Power, cost: 5, isXCost: true);

        Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput> { xCard }, situation), Is.EqualTo(0));
    }

    [Test]
    public void X费牌_能量为零时分数最低但不为负()
    {
        WakuuScoreSituation noEnergy = Calm(energy: 0);
        int score = WakuuCardScoring.Score(Card(kind: WakuuScoreCardKind.Other, cost: 0, isXCost: true), noEnergy);

        Assert.That(score, Is.EqualTo(10));
    }

    [Test]
    public void 同分保留最左()
    {
        WakuuScoreSituation situation = Calm();
        List<WakuuScoreInput> cards = new() { Card(cost: 1), Card(cost: 1), Card(cost: 1) };

        Assert.That(WakuuCardScoring.PickBestIndex(cards, situation), Is.EqualTo(0));
    }

    [Test]
    public void 空手牌返回负一()
    {
        Assert.That(WakuuCardScoring.PickBestIndex(new List<WakuuScoreInput>(), Calm()), Is.EqualTo(-1));
        Assert.That(WakuuCardScoring.PickBestIndex(null!, Calm()), Is.EqualTo(-1));
    }

    [Test]
    public void 第一回合_能力牌额外加分()
    {
        WakuuScoreInput power = Card(kind: WakuuScoreCardKind.Power, cost: 2);
        int round1 = WakuuCardScoring.Score(power, new WakuuScoreSituation(80, 80, 3, 1, 1, -1, false));
        int round2 = WakuuCardScoring.Score(power, Calm());

        Assert.That(round1 - round2, Is.EqualTo(20));
    }

    [Test]
    public void 群体伤害_敌人越多加分越高()
    {
        WakuuScoreInput aoe = Card(cost: 2, targetKind: WakuuScoreTargetKind.AllEnemies);
        int two = WakuuCardScoring.Score(aoe, Calm(enemyCount: 2));
        int four = WakuuCardScoring.Score(aoe, Calm(enemyCount: 4));

        Assert.Multiple(() =>
        {
            Assert.That(two - WakuuCardScoring.Score(aoe, Calm(enemyCount: 1)), Is.EqualTo(15));
            Assert.That(four - two, Is.EqualTo(10), "每多一个敌人 +5");
        });
    }

    [Test]
    public void 能量富余_贵牌加分()
    {
        WakuuScoreInput expensive = Card(cost: 3);
        int rich = WakuuCardScoring.Score(expensive, Calm(energy: 4));
        int poor = WakuuCardScoring.Score(expensive, Calm(energy: 3));

        Assert.That(rich - poor, Is.EqualTo(10));
    }

    [Test]
    public void 格挡量加分_封顶二十()
    {
        WakuuScoreSituation dangerous = new(
            playerHp: 10, playerMaxHp: 80, energy: 3, turnNumber: 2,
            enemyCount: 1, weakestEnemyEffectiveHp: -1, lethalDanger: false);

        int small = WakuuCardScoring.Score(
            Card(kind: WakuuScoreCardKind.Skill, cost: 2, gainsBlock: true, block: 5), dangerous);
        int huge = WakuuCardScoring.Score(
            Card(kind: WakuuScoreCardKind.Skill, cost: 2, gainsBlock: true, block: 500), dangerous);

        Assert.Multiple(() =>
        {
            Assert.That(small, Is.EqualTo(35 + 30 + 5));
            Assert.That(huge, Is.EqualTo(35 + 30 + 20), "格挡量加分封顶 20");
        });
    }
}
