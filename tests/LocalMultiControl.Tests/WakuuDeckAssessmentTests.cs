using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 牌组层评估测试（M1 知识层，总规范 v2 §24/§25/§43/§45）。
///
/// 要钉死的语义：均卡度必须是 `avg(质量) × avg(可靠) × (1 − avg(条件))` 这个乘积形式
/// （任一项为 0 就整体归 0 —— 这正是"高条件牌多的牌组不健康"的数学表达），
/// 且费用压力**不许**用"高费占比 30% 硬阈值"（v2 §45/§90 第 9 条）。
/// </summary>
[TestFixture]
public class WakuuDeckAssessmentTests
{
    private static WakuuCardFeature Card(
        string id,
        int cost = 1,
        float quality = 1f,
        float reliability = 1f,
        float conditionality = 0f,
        bool unknown = false,
        float damagePort = 1f)
    {
        return new WakuuCardFeature(
            id: id,
            kind: WakuuScoreCardKind.Attack,
            cost: cost,
            isXCost: false,
            keywords: WakuuCardKeywords.None,
            effects: new List<WakuuEffectFeature>(),
            directDamage: 14,
            directBlock: 0,
            draw: 0,
            energy: 0,
            ports: new WakuuPortProfile(damagePort, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f),
            knownRatio: unknown ? 0f : 1f,
            unknownPotential: unknown ? WakuuCardBaseline.UnknownPotential(1) : 0f,
            confidence: unknown ? 0f : 1f,
            source: WakuuEffectSource.Parsed,
            quality: quality,
            reliability: reliability,
            conditionality: conditionality,
            risk: 0f,
            unknownEffectCount: unknown ? 1 : 0);
    }

    [Test]
    public void 空牌组_返回全零且不抛异常()
    {
        WakuuDeckAssessment empty = WakuuDeckAssessment.Assess(new List<WakuuCardFeature>());
        WakuuDeckAssessment nullCase = WakuuDeckAssessment.Assess(null!);

        Assert.Multiple(() =>
        {
            Assert.That(empty.TotalCards, Is.EqualTo(0));
            Assert.That(empty.Uniformity, Is.EqualTo(0f));
            Assert.That(empty.OpportunityCost, Is.EqualTo(0f));
            Assert.That(empty.Ports.Total, Is.EqualTo(0f));
            Assert.That(empty.UnknownCardRate, Is.EqualTo(0f));
            Assert.That(nullCase.TotalCards, Is.EqualTo(0));
        });
    }

    [Test]
    public void 均卡度_等于质量乘可靠乘一减条件()
    {
        List<WakuuCardFeature> deck = new()
        {
            Card("A", quality: 1f, reliability: 0.8f, conditionality: 0.2f),
            Card("B", quality: 0.5f, reliability: 0.6f, conditionality: 0.4f),
        };

        WakuuDeckAssessment assessment = WakuuDeckAssessment.Assess(deck);

        // avg质量 = 0.75, avg可靠 = 0.7, avg条件 = 0.3 ⇒ 0.75 × 0.7 × 0.7 = 0.3675
        Assert.That(assessment.Uniformity, Is.EqualTo(0.3675f).Within(0.0005f));
    }

    [Test]
    public void 高条件牌组_均卡度显著低于低条件牌组()
    {
        List<WakuuCardFeature> conditional = new()
        {
            Card("A", conditionality: 0.9f),
            Card("B", conditionality: 0.9f),
        };
        List<WakuuCardFeature> solid = new()
        {
            Card("A"),
            Card("B"),
        };

        Assert.That(WakuuDeckAssessment.Assess(conditional).Uniformity,
            Is.LessThan(WakuuDeckAssessment.Assess(solid).Uniformity));
    }

    [Test]
    public void 端口与未知卡_被正确累计()
    {
        List<WakuuCardFeature> deck = new()
        {
            Card("A", damagePort: 1f),
            Card("B", damagePort: 0.5f, unknown: true),
        };

        WakuuDeckAssessment assessment = WakuuDeckAssessment.Assess(deck);

        Assert.Multiple(() =>
        {
            Assert.That(assessment.TotalCards, Is.EqualTo(2));
            Assert.That(assessment.Ports.Damage, Is.EqualTo(1.5f).Within(0.0001f));
            Assert.That(assessment.UnknownEffectCards, Is.EqualTo(1));
            Assert.That(assessment.UnknownCardRate, Is.EqualTo(0.5f).Within(0.0001f));
        });
    }

    [Test]
    public void 费用压力_用平均费用超出基准能量_而不是高费占比硬阈值()
    {
        // 全是 1 费 ⇒ 平均费用 1 < 基准能量 3 ⇒ 压力 0
        List<WakuuCardFeature> cheap = new() { Card("A", cost: 1), Card("B", cost: 1) };
        Assert.That(WakuuDeckAssessment.Assess(cheap).CostCurveStress, Is.EqualTo(0f));

        // 平均 3 费 ⇒ 压力 = 3 − 3 = 0；平均 4 费 ⇒ 压力 = 1
        List<WakuuCardFeature> expensive = new() { Card("A", cost: 4), Card("B", cost: 4) };
        Assert.That(WakuuDeckAssessment.Assess(expensive).CostCurveStress, Is.EqualTo(1f).Within(0.0001f));
    }

    [Test]
    public void 零费与X费_不计入费用压力分母()
    {
        List<WakuuCardFeature> deck = new()
        {
            Card("A", cost: 5),
            new WakuuCardFeature("X", WakuuScoreCardKind.Attack, 0, true, WakuuCardKeywords.None,
                new List<WakuuEffectFeature>(), 0, 0, 0, 0, WakuuPortProfile.Zero, 1f, 0f, 1f,
                WakuuEffectSource.Parsed, 0f, 1f, 0f, 0f, 0),
        };

        // 只有 5 费那张进入分母 ⇒ 压力 = 5 − 3 = 2（X 费不参与）
        Assert.That(WakuuDeckAssessment.Assess(deck).CostCurveStress, Is.EqualTo(2f).Within(0.0001f));
    }

    [Test]
    public void 瘦身信号_牌组偏大且结实才为真()
    {
        List<WakuuCardFeature> small = new();
        for (int i = 0; i < 10; i++)
        {
            small.Add(Card($"C{i}"));
        }

        List<WakuuCardFeature> big = new();
        for (int i = 0; i < 35; i++)
        {
            big.Add(Card($"C{i}"));
        }

        Assert.Multiple(() =>
        {
            Assert.That(WakuuDeckAssessment.Assess(small).NeedsThinning, Is.False);
            Assert.That(WakuuDeckAssessment.Assess(big).NeedsThinning, Is.True);
        });
    }

    [Test]
    public void 日志短描述_中文可读()
    {
        List<WakuuCardFeature> deck = new() { Card("A"), Card("B") };
        string text = WakuuDeckAssessment.Assess(deck).DescribeShort();

        Assert.Multiple(() =>
        {
            Assert.That(text, Does.Contain("牌组=2 张"));
            Assert.That(text, Does.Contain("均卡度="));
            Assert.That(text, Does.Contain("机会成本="));
        });
    }
}
