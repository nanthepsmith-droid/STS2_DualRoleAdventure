using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 静态语义表测试（M1 知识层，实施草案 §4.2 / 总规范 v2 §9）。
///
/// 三条哨兵（写错表会把评分带偏，必须由单测兜住）：
/// ① 条目数 ≤ 50（用户拍板 Q3 的"最小语义集"上限）；
/// ② 表里**不许出现 Unknown 占位条目**（未知就该不在表里，由未知潜能承接）；
/// ③ 有前置条件的条目必须写明前置说明（v2 §39 孤儿组件判定的输入，不许空着）。
/// </summary>
[TestFixture]
public class WakuuStaticEffectTableTests
{
    [Test]
    public void 表规模不超过五十条()
    {
        Assert.That(WakuuStaticEffectTable.Count, Is.GreaterThan(10), "表太空说明知识层没有覆盖面");
        Assert.That(WakuuStaticEffectTable.Count, Is.LessThanOrEqualTo(WakuuStaticEffectTable.MaxEntries));
    }

    [Test]
    public void 表内条目_无Unknown占位_行为与置信度合规()
    {
        foreach (KeyValuePair<string, WakuuStaticEffect> pair in WakuuStaticEffectTable.Entries)
        {
            WakuuStaticEffect effect = pair.Value;

            Assert.That(effect.Id, Is.EqualTo(pair.Key), $"键与条目 id 必须一致（{pair.Key}）");
            Assert.That(effect.Behavior, Is.Not.EqualTo(WakuuGenericBehavior.Unknown),
                $"{pair.Key} 不该以 Unknown 入表（未知应由未知潜能承接）");
            Assert.That(effect.Confidence, Is.InRange(0.5f, 1f), $"{pair.Key} 的置信度应在 0.5~1");
            Assert.That(effect.Note, Is.Not.Empty, $"{pair.Key} 必须写明归类口径");
        }
    }

    [Test]
    public void 有前置的条目_必须写明前置说明()
    {
        foreach (KeyValuePair<string, WakuuStaticEffect> pair in WakuuStaticEffectTable.Entries)
        {
            if (pair.Value.HasPrerequisite)
            {
                Assert.That(pair.Value.Requires, Is.Not.Empty, $"{pair.Key} 标了前置就必须写清楚是什么");
            }
        }
    }

    [Test]
    public void 查表_大小写不敏感且空值安全()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuStaticEffectTable.TryGet("PoisonPower", out WakuuStaticEffect poison), Is.True);
            Assert.That(poison.Behavior, Is.EqualTo(WakuuGenericBehavior.DamageOverTime));

            Assert.That(WakuuStaticEffectTable.TryGet("poisonpower", out WakuuStaticEffect lower), Is.True);
            Assert.That(lower.Id, Is.EqualTo("PoisonPower"));

            Assert.That(WakuuStaticEffectTable.TryGet("某个mod自定义状态", out _), Is.False);
            Assert.That(WakuuStaticEffectTable.TryGet(null, out _), Is.False);
            Assert.That(WakuuStaticEffectTable.TryGet("  ", out _), Is.False);
        });
    }

    [Test]
    public void 关键语义_毒是持续伤害_力量是成长_虚弱是减益()
    {
        Assert.Multiple(() =>
        {
            WakuuStaticEffectTable.TryGet("PoisonPower", out WakuuStaticEffect poison);
            WakuuStaticEffectTable.TryGet("StrengthPower", out WakuuStaticEffect strength);
            WakuuStaticEffectTable.TryGet("WeakPower", out WakuuStaticEffect weak);
            WakuuStaticEffectTable.TryGet("VulnerablePower", out WakuuStaticEffect vulnerable);

            Assert.That(poison.Behavior, Is.EqualTo(WakuuGenericBehavior.DamageOverTime));
            Assert.That(poison.PortOverride.Damage, Is.GreaterThan(0f), "毒必须给出伤害端口（否则等于判 0，v2 §5）");

            Assert.That(strength.Behavior, Is.EqualTo(WakuuGenericBehavior.Scaling));
            Assert.That(strength.PortOverride.Scaling, Is.GreaterThan(0f));

            Assert.That(weak.Behavior, Is.EqualTo(WakuuGenericBehavior.Debuff));
            Assert.That(weak.PortOverride.Defense, Is.GreaterThan(0f), "虚弱等价于减伤（v2 §33）");

            Assert.That(vulnerable.Behavior, Is.EqualTo(WakuuGenericBehavior.Debuff));
            Assert.That(vulnerable.PortOverride.Control, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void 孤儿组件候选_至少覆盖已知的几个前置型能力()
    {
        string[] expected = { "DarkEmbracePower", "FeelNoPainPower", "EnvenomPower", "DoubleDamagePower" };

        foreach (string id in expected)
        {
            Assert.That(WakuuStaticEffectTable.TryGet(id, out WakuuStaticEffect effect), Is.True, $"{id} 应在表内");
            Assert.That(effect.HasPrerequisite, Is.True, $"{id} 属于「要靠前置才成立」的牌（v2 §39）");
        }
    }
}
