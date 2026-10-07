using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「瓦库的爹」（瓦库四功能之四，遗物 + 三张占位牌）的纯逻辑测试。
///
/// 口径（2026-10-07，提案 §5.1 原话「真人玩家开局获得」）：开关开 **且** 该席位属于本地多控会话
/// **且** 不是瓦库托管席位时才发 —— 瓦库席位已有托管遗物，不再叠一件"每场战斗塞 3 张牌"的遗物。
/// </summary>
[TestFixture]
public class WakuuDaddyPolicyTests
{
    [Test]
    public void 开关关_任何席位都不发()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuDaddyPolicy.ShouldGrantRelic(false, true, false), Is.False);
            Assert.That(WakuuDaddyPolicy.ShouldGrantRelic(false, true, true), Is.False);
            Assert.That(WakuuDaddyPolicy.ShouldGrantRelic(false, false, false), Is.False);
        });
    }

    [Test]
    public void 开关开_真人席位发放()
    {
        Assert.That(WakuuDaddyPolicy.ShouldGrantRelic(true, true, false), Is.True);
    }

    [Test]
    public void 开关开_瓦库席位不发()
    {
        Assert.That(
            WakuuDaddyPolicy.ShouldGrantRelic(true, true, true),
            Is.False,
            "瓦库席位身上已有托管遗物，再叠一件塞牌的遗物只会给托管出牌添乱");
    }

    [Test]
    public void 开关开_非本地会话席位不发()
    {
        Assert.That(WakuuDaddyPolicy.ShouldGrantRelic(true, false, false), Is.False);
    }

    [Test]
    public void 每场战斗给的牌数_为3()
    {
        Assert.That(WakuuDaddyPolicy.CardsPerCombat, Is.EqualTo(3), "我挡 / 你攻 / 合体 各 1 张");
    }
}
