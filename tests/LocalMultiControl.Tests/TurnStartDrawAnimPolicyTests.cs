using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「跳过他人回合开始抽牌演出」（改进-1）判定纯逻辑测试：
/// 开关开 + 本地多控 + 该玩家不是当前前台时跳过切前台（= 抽牌不播动画），其余情况一律按原逻辑。
/// </summary>
[TestFixture]
public class TurnStartDrawAnimPolicyTests
{
    private const ulong Foreground = 76561198422527326UL;
    private const ulong Other = 76561198422527327UL;

    [Test]
    public void 开关关闭时不跳过()
    {
        Assert.That(
            TurnStartDrawAnimPolicy.ShouldSkipSwitch(false, true, Foreground, Other),
            Is.False);
    }

    [Test]
    public void 非本地多控时不跳过()
    {
        Assert.That(
            TurnStartDrawAnimPolicy.ShouldSkipSwitch(true, false, Foreground, Other),
            Is.False);
    }

    [Test]
    public void 开关开启且不是前台玩家时跳过()
    {
        // 改进-1 的核心场景：满员局里除「当前正在看的那位」以外全部跳过演出
        Assert.That(
            TurnStartDrawAnimPolicy.ShouldSkipSwitch(true, true, Foreground, Other),
            Is.True);
    }

    [Test]
    public void 该玩家就是前台玩家时不跳过()
    {
        // 切换本身是空操作，且他的抽牌演出应当正常播（用户正看着他）
        Assert.That(
            TurnStartDrawAnimPolicy.ShouldSkipSwitch(true, true, Foreground, Foreground),
            Is.False);
    }

    [Test]
    public void 前台或目标玩家id缺失时不跳过()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TurnStartDrawAnimPolicy.ShouldSkipSwitch(true, true, 0UL, Other), Is.False);
            Assert.That(TurnStartDrawAnimPolicy.ShouldSkipSwitch(true, true, Foreground, 0UL), Is.False);
        });
    }
}
