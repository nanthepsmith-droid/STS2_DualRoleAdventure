using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 卡特征纯结构测试（M1 知识层，总规范 v2 §18/§19/§27）。
///
/// 要钉死的语义：异常入参不产生 NaN/负数；"未知效果"必须能被单独特判（v2 §5）；
/// 端口结构可加/可缩放/可点积（后续评分的数学基础）。
/// </summary>
[TestFixture]
public class WakuuCardFeatureTests
{
    private static WakuuCardFeature Make(
        int cost = 1,
        int damage = 14,
        int block = 0,
        int unknownCount = 0,
        float unknownPotential = 0f,
        WakuuCardKeywords keywords = WakuuCardKeywords.None)
    {
        return new WakuuCardFeature(
            id: "TEST_CARD",
            kind: WakuuScoreCardKind.Attack,
            cost: cost,
            isXCost: false,
            keywords: keywords,
            effects: new List<WakuuEffectFeature>
            {
                new(WakuuEffectKind.DirectDamage, WakuuEffectTarget.Enemy, "Damage", damage,
                    WakuuGenericBehavior.DirectDamage, WakuuEffectSource.Parsed, 1f),
            },
            directDamage: damage,
            directBlock: block,
            draw: 0,
            energy: 0,
            ports: new WakuuPortProfile(damage: 1f, defense: 0f, draw: 0f, energy: 0f, scaling: 0f,
                control: 0f, aoe: 0f, deckControl: 0f, resourceGeneration: 0f),
            knownRatio: 1f,
            unknownPotential: unknownPotential,
            confidence: 1f,
            source: WakuuEffectSource.Parsed,
            quality: 1f,
            reliability: 1f,
            conditionality: 0f,
            risk: 0f,
            unknownEffectCount: unknownCount);
    }

    [Test]
    public void 未知效果_可单独识别且潜能恒正()
    {
        WakuuCardFeature unknown = new(
            id: "MOD_UNKNOWN",
            kind: WakuuScoreCardKind.Skill,
            cost: 1,
            isXCost: false,
            keywords: WakuuCardKeywords.None,
            effects: new List<WakuuEffectFeature>
            {
                new(WakuuEffectKind.ApplyStatus, WakuuEffectTarget.Enemy, "某个mod状态", 10,
                    WakuuGenericBehavior.Unknown, WakuuEffectSource.DefaultHeuristic, 0f),
            },
            directDamage: 0,
            directBlock: 0,
            draw: 0,
            energy: 0,
            ports: WakuuPortProfile.Zero,
            knownRatio: 0f,
            unknownPotential: WakuuCardBaseline.UnknownPotential(1),
            confidence: 0f,
            source: WakuuEffectSource.DefaultHeuristic,
            quality: 0f,
            reliability: 0f,
            conditionality: 1f,
            risk: 0.5f,
            unknownEffectCount: 1);

        Assert.Multiple(() =>
        {
            Assert.That(unknown.HasUnknownEffect, Is.True);
            Assert.That(unknown.DirectDamage, Is.EqualTo(0), "毒牌的直接伤害本来就是 0");

            // 关键：直接伤害为 0，但**潜能不为 0**（v2 §5/§19：绝不能判成"没效果的垃圾牌"）
            Assert.That(unknown.UnknownPotential, Is.GreaterThan(0f));
            Assert.That(unknown.Confidence, Is.EqualTo(0f));
            Assert.That(WakuuConfidence.NeedsSafeFallback(unknown.Confidence), Is.True,
                "信息全无 ⇒ 应能被安全兜底判据识别（v2 §62）");
        });
    }

    [Test]
    public void 入参夹取_负数与NaN不进入结构()
    {
        WakuuCardFeature feature = new(
            id: null!,
            kind: WakuuScoreCardKind.Other,
            cost: -5,
            isXCost: false,
            keywords: WakuuCardKeywords.None,
            effects: null!,
            directDamage: -3,
            directBlock: -3,
            draw: -3,
            energy: -3,
            ports: WakuuPortProfile.Zero,
            knownRatio: 5f,
            unknownPotential: float.NaN,
            confidence: -1f,
            source: WakuuEffectSource.DefaultHeuristic,
            quality: -1f,
            reliability: 9f,
            conditionality: -9f,
            risk: float.NaN,
            unknownEffectCount: -2);

        Assert.Multiple(() =>
        {
            Assert.That(feature.Id, Is.Empty);
            Assert.That(feature.Cost, Is.EqualTo(0));
            Assert.That(feature.Effects, Is.Empty);
            Assert.That(feature.DirectDamage, Is.EqualTo(0));
            Assert.That(feature.KnownRatio, Is.EqualTo(1f));
            Assert.That(feature.UnknownPotential, Is.EqualTo(0f));
            Assert.That(feature.Confidence, Is.EqualTo(0f));
            Assert.That(feature.Reliability, Is.EqualTo(1f));
            Assert.That(feature.Conditionality, Is.EqualTo(0f));
            Assert.That(feature.Risk, Is.EqualTo(0f));
            Assert.That(feature.UnknownEffectCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void 不可打出的牌_标记可识别()
    {
        WakuuCardFeature curse = Make(keywords: WakuuCardKeywords.Unplayable);

        Assert.Multiple(() =>
        {
            Assert.That(curse.IsUnplayable, Is.True);
            Assert.That(Make().IsUnplayable, Is.False);
        });
    }

    [Test]
    public void 日志短描述_含关键字段且未知时写明潜能()
    {
        string plain = Make().DescribeShort();
        string unknown = Make(unknownCount: 2, unknownPotential: 0.5f).DescribeShort();

        Assert.Multiple(() =>
        {
            Assert.That(plain, Does.Contain("TEST_CARD"));
            Assert.That(plain, Does.Contain("伤=14"));
            Assert.That(plain, Does.Not.Contain("未知×"), "读得懂的卡不该出现未知标记");

            Assert.That(unknown, Does.Contain("未知×2"));
            Assert.That(unknown, Does.Contain("0.50"));
        });
    }

    [Test]
    public void 端口结构_可加可缩放可点积()
    {
        WakuuPortProfile attack = new(1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);
        WakuuPortProfile defense = new(0f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        WakuuPortProfile sum = attack.Add(defense);
        WakuuPortProfile scaled = attack.Scale(2f);
        WakuuPortProfile needs = new(1.5f, 1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        Assert.Multiple(() =>
        {
            Assert.That(sum.Damage, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(sum.Defense, Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(sum.Total, Is.EqualTo(1.5f).Within(0.0001f));

            Assert.That(scaled.Damage, Is.EqualTo(2f).Within(0.0001f));
            Assert.That(scaled.Total, Is.EqualTo(2f).Within(0.0001f));

            // NeedFit = Σ 供给 × 需求（v2 §29）：1×1.5 + 0.5×1 = 2.0
            Assert.That(sum.FitWith(needs), Is.EqualTo(2f).Within(0.0001f));

            // 负系数/NaN 缩放不产生负数与 NaN
            Assert.That(attack.Scale(-3f).Total, Is.EqualTo(0f));
            Assert.That(attack.Scale(float.NaN).Total, Is.EqualTo(0f));
        });
    }

    [Test]
    public void 端口描述_中文可读()
    {
        WakuuPortProfile profile = new(1f, 0.5f, 0f, 0f, 0f, 0f, 0f, 0f, 0f);

        Assert.That(profile.ToString(), Does.Contain("攻=1.0"));
        Assert.That(profile.ToString(), Does.Contain("防=0.5"));
    }
}
