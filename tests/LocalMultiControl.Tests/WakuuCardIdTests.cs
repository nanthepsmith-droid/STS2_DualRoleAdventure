using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 基础打击/防御卡 id 判定纯函数测试（火堆休息自动选择用）。
/// </summary>
[TestFixture]
public class WakuuCardIdTests
{
    [TestCase("STRIKE_RED", true)]
    [TestCase("STRIKE_GREEN", true)]
    [TestCase("STRIKE_BLUE", true)]
    [TestCase("STRIKE_PURPLE", true)]
    [TestCase("DEFEND_RED", true)]
    [TestCase("DEFEND_GREEN", true)]
    [TestCase("DEFEND_BLUE", true)]
    [TestCase("DEFEND_PURPLE", true)]
    [TestCase("STRIKE", true)]
    [TestCase("DEFEND", true)]
    [TestCase("PERFECTED_STRIKE", true)] // 打击类变体
    [TestCase("SELF_DEFEND", true)]      // 防御类变体
    [TestCase("HEAVY_BLADE", false)]
    [TestCase("FLAME_BARRIER", false)]
    [TestCase("CARBON_FIBER", false)]
    [TestCase("", false)]
    public void IsBasicStrikeOrDefendId_大小写无关命中与未命中(string id, bool expected)
    {
        Assert.That(WakuuCardId.IsBasicStrikeOrDefendId(id), Is.EqualTo(expected));
    }

    [Test]
    public void IsBasicStrikeOrDefendId_小写id也命中()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuCardId.IsBasicStrikeOrDefendId("strike_red"), Is.True);
            Assert.That(WakuuCardId.IsBasicStrikeOrDefendId("defend_red"), Is.True);
            Assert.That(WakuuCardId.IsBasicStrikeOrDefendId("heavy_blade"), Is.False);
        });
    }

    [Test]
    public void IsBasicStrikeOrDefendId_混合大小写命中()
    {
        Assert.That(WakuuCardId.IsBasicStrikeOrDefendId("StRiKe_ReD"), Is.True);
    }

    [Test]
    public void IsBasicStrikeOrDefendId_null抛异常()
    {
        Assert.That(() => WakuuCardId.IsBasicStrikeOrDefendId(null!), Throws.TypeOf<ArgumentNullException>());
    }
}
