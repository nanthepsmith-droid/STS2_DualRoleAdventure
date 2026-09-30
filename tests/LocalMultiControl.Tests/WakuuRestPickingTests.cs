using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 瓦库火堆决策的纯函数测试：
/// ① 「全员血量比例」判定（用户拍板：全员 ≥50% 时愈合优先级放最后）；
/// ② 「选项是否由游戏本体提供」的来源判定（BUG-27：第三方休息区选项不得被当成"遗物选项"）。
/// </summary>
[TestFixture]
public class WakuuRestPickingTests
{
    [Test]
    public void 全员血量都达到50以上返回true()
    {
        List<(decimal, decimal)> players = new()
        {
            (50m, 100m), // 正好 50%
            (80m, 100m),
            (30m, 40m),  // 75%
        };
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(players), Is.True);
    }

    [Test]
    public void 有一人血量低于50返回false()
    {
        List<(decimal, decimal)> players = new()
        {
            (90m, 100m),
            (49m, 100m), // 低于 50%
        };
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(players), Is.False);
        // 边界：49.9 仍算不达标
        List<(decimal, decimal)> borderline = new() { (49.9m, 100m) };
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(borderline), Is.False);
    }

    [Test]
    public void 空集合或无效上限视为不满足()
    {
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(null!), Is.False);
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(new List<(decimal, decimal)>()), Is.False);
        // 上限为 0（异常/未初始化）忽略，不据此改变优先级
        List<(decimal, decimal)> invalid = new() { (0m, 0m) };
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(invalid), Is.True);
    }

    [Test]
    public void 自定义比例门槛生效()
    {
        List<(decimal, decimal)> players = new() { (60m, 100m) };
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(players, 0.5m), Is.True);
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(players, 0.7m), Is.False);
        Assert.That(WakuuRestPicking.IsAllAboveHpRatio(players, 0.6m), Is.True);
    }

    [Test]
    public void 选项来源判定_只有与游戏程序集同名才算游戏本体()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("sts2", "sts2"), Is.True);
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("STS2", "sts2"), Is.True, "程序集名大小写不敏感");
            // 实机踩到的那一个（CalypsosHappyHour 的 CHH_MUTUAL_AID）—— 必须判"不是游戏本体"，
            // 否则它会被当成"遗物选项"、把决策短路成随机（BUG-27）。
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("CalypsosHappyHour", "sts2"), Is.False);
        });
    }

    [Test]
    public void 选项来源判定_拿不到来源时按非游戏本体处理()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource(null, "sts2"), Is.False, "来源未知不劫持决策");
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("sts2", null), Is.False);
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("", ""), Is.False);
            Assert.That(WakuuRestPicking.IsGameProvidedOptionSource("  ", "sts2"), Is.False);
        });
    }
}
