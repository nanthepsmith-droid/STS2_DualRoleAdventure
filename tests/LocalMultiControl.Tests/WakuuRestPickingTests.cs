using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>瓦库火堆「全员血量比例」判定纯函数测试（用户拍板：全员 ≥50% 时愈合优先级放最后）。</summary>
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
}
