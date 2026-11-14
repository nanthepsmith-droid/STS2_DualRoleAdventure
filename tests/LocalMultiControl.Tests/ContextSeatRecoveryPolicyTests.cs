using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「回合开始时上下文席位为空 ⇒ 临时钉哪个席位」纯逻辑测试（r356，2026-11-14 实机：`turn loop died`）。
///
/// 钉住三条：
/// ① 现状可用（上下文席位就在战斗里）⇒ **绝不动手**（返回 null）；
/// ② 上下文为空 / 不在战斗里 ⇒ 取候选表里的第一个（候选表已按优先级排好）；
/// ③ 一个候选都没有 ⇒ 保持现状（不乱钉）。
/// </summary>
[TestFixture]
public class ContextSeatRecoveryPolicyTests
{
    [Test]
    public void 上下文席位有效时不动手()
    {
        List<ulong> candidates = new() { 100UL, 200UL };

        Assert.That(ContextSeatRecoveryPolicy.Decide(200UL, candidates), Is.Null);
    }

    [Test]
    public void 上下文为空时取第一个候选()
    {
        List<ulong> candidates = new() { 100UL, 200UL };

        Assert.That(ContextSeatRecoveryPolicy.Decide(null, candidates), Is.EqualTo(100UL));
    }

    [Test]
    public void 上下文席位不在战斗里时也要改道()
    {
        // 例：上下文还停在已撤离/已阵亡的席位（GetMe 会抛 InvalidOperationException 而不是返回 null）
        List<ulong> candidates = new() { 100UL };

        Assert.That(ContextSeatRecoveryPolicy.Decide(999UL, candidates), Is.EqualTo(100UL));
    }

    [Test]
    public void 没有可用候选时保持现状()
    {
        Assert.That(ContextSeatRecoveryPolicy.Decide(null, new List<ulong>()), Is.Null);
        Assert.That(ContextSeatRecoveryPolicy.Decide(null, null), Is.Null);
        Assert.That(ContextSeatRecoveryPolicy.Decide(100UL, new List<ulong>()), Is.Null);
    }

    [Test]
    public void 候选顺序即优先级_不做二次排序()
    {
        List<ulong> candidates = new() { 700UL, 100UL, 200UL };

        Assert.That(ContextSeatRecoveryPolicy.Decide(null, candidates), Is.EqualTo(700UL));
    }
}
