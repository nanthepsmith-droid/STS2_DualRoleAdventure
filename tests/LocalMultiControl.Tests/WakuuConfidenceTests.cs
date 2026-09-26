using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 置信度纯函数测试（M1 知识层，总规范 v2 §14/§62/§73）。
///
/// 要钉死的语义：单次观测 0.25、随观测次数递增但封顶 0.95、合成取折扣和而不是相加、
/// 低于阈值要能触发安全兜底（v2 §62 是"未知高危 ⇒ 交真人"的唯一开关）。
/// </summary>
[TestFixture]
public class WakuuConfidenceTests
{
    [Test]
    public void 夹取_处理越界与NaN()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.Clamp(-1f), Is.EqualTo(0f));
            Assert.That(WakuuConfidence.Clamp(2f), Is.EqualTo(1f));
            Assert.That(WakuuConfidence.Clamp(float.NaN), Is.EqualTo(0f));
            Assert.That(WakuuConfidence.Clamp(0.42f), Is.EqualTo(0.42f).Within(0.0001f));
        });
    }

    [Test]
    public void 已知元占比_直接映射为置信度()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.FromKnownRatio(0f), Is.EqualTo(0f));
            Assert.That(WakuuConfidence.FromKnownRatio(0.5f), Is.EqualTo(0.5f).Within(0.0001f));
            Assert.That(WakuuConfidence.FromKnownRatio(3f), Is.EqualTo(1f));
        });
    }

    [Test]
    public void 观测次数_单次零点二五_之后每步递增_封顶零点九五()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.FromObservations(0), Is.EqualTo(0f));
            Assert.That(WakuuConfidence.FromObservations(1), Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(WakuuConfidence.FromObservations(2), Is.EqualTo(0.35f).Within(0.0001f));
            Assert.That(WakuuConfidence.FromObservations(3), Is.EqualTo(0.45f).Within(0.0001f));

            // 反复观测也不许到 1.0（v2 §14：20 次观测 ≈ 0.85~0.95）
            Assert.That(WakuuConfidence.FromObservations(20), Is.EqualTo(WakuuConfidence.MaxObservation));
            Assert.That(WakuuConfidence.FromObservations(200), Is.EqualTo(WakuuConfidence.MaxObservation));
        });
    }

    [Test]
    public void 合成_取折扣和而不是相加()
    {
        // 两条各 0.6 的证据 ⇒ 1 - 0.4*0.4 = 0.84，而不是 1.2（也不会被夹成 1.0）
        Assert.That(WakuuConfidence.Combine(0.6f, 0.6f), Is.EqualTo(0.84f).Within(0.0001f));

        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.Combine(0f, 0.7f), Is.EqualTo(0.7f).Within(0.0001f));
            Assert.That(WakuuConfidence.Combine(1f, 0.3f), Is.EqualTo(1f).Within(0.0001f));
            Assert.That(WakuuConfidence.Combine(0.5f, 0.5f), Is.LessThan(1f));
        });
    }

    [Test]
    public void 安全兜底阈值_低于阈值才算需要兜底()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.NeedsSafeFallback(0.2f), Is.True);
            Assert.That(WakuuConfidence.NeedsSafeFallback(WakuuConfidence.SafeFallbackThreshold - 0.01f), Is.True);
            Assert.That(WakuuConfidence.NeedsSafeFallback(WakuuConfidence.SafeFallbackThreshold), Is.False);
            Assert.That(WakuuConfidence.NeedsSafeFallback(0.9f), Is.False);
        });
    }

    [Test]
    public void 中文档位_四档可读()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuConfidence.Describe(0.9f), Is.EqualTo("高"));
            Assert.That(WakuuConfidence.Describe(0.5f), Is.EqualTo("中"));
            Assert.That(WakuuConfidence.Describe(0.1f), Is.EqualTo("低"));
            Assert.That(WakuuConfidence.Describe(0f), Is.EqualTo("无"));
        });
    }
}
