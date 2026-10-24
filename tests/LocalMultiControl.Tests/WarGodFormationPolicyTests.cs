using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// r301 战神立绘站位（按绑主归位）纯逻辑。
/// </summary>
[TestFixture]
public class WarGodFormationPolicyTests
{
    [Test]
    public void 零个_不产生落点()
    {
        Assert.That(WarGodFormationPolicy.Build(0, 500f, 400f), Is.Empty);
    }

    [Test]
    public void 单个_正对绑主上方()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(1, 500f, 400f);

        Assert.That(points, Has.Count.EqualTo(1));
        Assert.That(points[0].X, Is.EqualTo(500f).Within(0.01f));
        Assert.That(points[0].Y, Is.EqualTo(400f + WarGodFormationPolicy.FirstRowOffsetY).Within(0.01f));
    }

    [Test]
    public void 三个_按这一排的数量居中()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(3, 500f, 400f);

        Assert.That(points, Has.Count.EqualTo(3));
        Assert.That(points[0].X, Is.EqualTo(500f - WarGodFormationPolicy.SpacingX).Within(0.01f));
        Assert.That(points[1].X, Is.EqualTo(500f).Within(0.01f));
        Assert.That(points[2].X, Is.EqualTo(500f + WarGodFormationPolicy.SpacingX).Within(0.01f));
        // 都在同一排 ⇒ y 相同
        Assert.That(points[1].Y, Is.EqualTo(points[0].Y).Within(0.01f));
        Assert.That(points[2].Y, Is.EqualTo(points[0].Y).Within(0.01f));
    }

    [Test]
    public void 超过每行上限_往上叠一行且该排单独居中()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(5, 500f, 400f);

        Assert.That(points, Has.Count.EqualTo(5));
        // 前 4 个一排
        Assert.That(points[3].Y, Is.EqualTo(points[0].Y).Within(0.01f));
        // 第 5 个在上一排，且因为这一排只有 1 个 ⇒ 与绑主同 x
        Assert.That(points[4].Y, Is.LessThan(points[0].Y));
        Assert.That(points[4].X, Is.EqualTo(500f).Within(0.01f));
        Assert.That(
            points[0].Y - points[4].Y,
            Is.EqualTo(-WarGodFormationPolicy.RowOffsetY).Within(0.01f));
    }

    [Test]
    public void 九连锁的9个_排成三行不重叠()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(9, 960f, 500f, 1920f, 1080f);

        Assert.That(points, Has.Count.EqualTo(9));
        HashSet<string> unique = new();
        foreach (FormationPoint point in points)
        {
            unique.Add($"{point.X:0.0},{point.Y:0.0}");
        }

        Assert.That(unique, Has.Count.EqualTo(9), "每个战神必须落在不同的点（不再重叠）");
    }

    [Test]
    public void 靠屏幕左缘时_夹取到安全边距内()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(4, 10f, 400f, 1920f, 1080f);

        foreach (FormationPoint point in points)
        {
            Assert.That(point.X, Is.GreaterThanOrEqualTo(WarGodFormationPolicy.ScreenMarginX));
        }
    }

    [Test]
    public void 靠屏幕右缘时_夹取到安全边距内()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(4, 1910f, 400f, 1920f, 1080f);

        foreach (FormationPoint point in points)
        {
            Assert.That(point.X, Is.LessThanOrEqualTo(1920f - WarGodFormationPolicy.ScreenMarginX));
        }
    }

    [Test]
    public void 靠顶部时_y不越界()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(9, 960f, 100f, 1920f, 1080f);

        foreach (FormationPoint point in points)
        {
            Assert.That(point.Y, Is.GreaterThanOrEqualTo(WarGodFormationPolicy.ScreenMarginTop));
        }
    }

    [Test]
    public void 不传屏幕尺寸_不做夹取()
    {
        List<FormationPoint> points = WarGodFormationPolicy.Build(4, 10f, 400f);

        Assert.That(points[0].X, Is.LessThan(WarGodFormationPolicy.ScreenMarginX));
    }

    // ---- 真人间距（r303：用了【无尽战神】后两个真人突然离得很近）----

    [Test]
    public void 真人间距够宽_一个都不动()
    {
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            new List<float> { 400f, 800f },
            WarGodFormationPolicy.DefaultMinPlayerSpacing,
            WarGodFormationPolicy.ScreenMarginX,
            1800f);

        Assert.That(result, Is.EqualTo(new List<float> { 400f, 800f }));
    }

    [Test]
    public void 真人被挤到一起_拉开到下限且中点不变()
    {
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            new List<float> { 600f, 640f },
            232f,
            120f,
            1800f);

        Assert.That(result[1] - result[0], Is.EqualTo(232f).Within(0.01f));
        Assert.That((result[0] + result[1]) * 0.5f, Is.EqualTo(620f).Within(0.01f));
    }

    [Test]
    public void 拉开后保持左右相对顺序()
    {
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            new List<float> { 300f, 320f, 310f },
            232f,
            120f,
            1800f);

        Assert.That(result[0], Is.LessThan(result[2]));
        Assert.That(result[2], Is.LessThan(result[1]));
    }

    [Test]
    public void 靠左边界时整组右移不越界()
    {
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            new List<float> { 130f, 140f },
            232f,
            120f,
            1800f);

        foreach (float value in result)
        {
            Assert.That(value, Is.GreaterThanOrEqualTo(120f));
        }
    }

    [Test]
    public void 单人_原样返回()
    {
        Assert.That(
            WarGodFormationPolicy.SpreadEvenly(new List<float> { 500f }, 232f, 120f, 1800f),
            Is.EqualTo(new List<float> { 500f }));
    }

    [Test]
    public void 空列表_返回空()
    {
        Assert.That(WarGodFormationPolicy.SpreadEvenly(new List<float>(), 232f, 120f, 1800f), Is.Empty);
    }

    // ---- 人数多到装不下（r318，2026-10-24 实机：12 个真人战斗 ⇒ 站位 A/B 两态无限震荡）----

    /// <summary>12 个真人（原版排成 120…2672、间距 232）超出可用带 120…1800 时的落点。</summary>
    private static List<float> TwelvePlayersOutOfBand()
    {
        List<float> centers = new();
        for (int i = 0; i < 12; i++)
        {
            centers.Add(120f + i * 232f);
        }

        return centers;
    }

    [Test]
    public void 人数装不下_全部落在可用带内不推出屏幕()
    {
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            TwelvePlayersOutOfBand(),
            WarGodFormationPolicy.DefaultMinPlayerSpacing,
            120f,
            1800f);

        Assert.That(result, Has.Count.EqualTo(12));
        foreach (float value in result)
        {
            Assert.That(value, Is.GreaterThanOrEqualTo(120f - 0.01f), "不能把人推到屏幕左侧外");
            Assert.That(value, Is.LessThanOrEqualTo(1800f + 0.01f), "不能把人推到屏幕右侧外");
        }

        // 铺满可用带
        Assert.That(result[0], Is.EqualTo(120f).Within(0.01f));
        Assert.That(result[11], Is.EqualTo(1800f).Within(0.01f));
    }

    [Test]
    public void 人数装不下_第二次调用结果不变_不会来回震荡()
    {
        List<float> input = TwelvePlayersOutOfBand();
        List<float> first = WarGodFormationPolicy.SpreadEvenly(
            input, WarGodFormationPolicy.DefaultMinPlayerSpacing, 120f, 1800f);
        List<float> second = WarGodFormationPolicy.SpreadEvenly(
            first, WarGodFormationPolicy.DefaultMinPlayerSpacing, 120f, 1800f);

        Assert.That(second, Is.EqualTo(first), "装不下时必须幂等，否则每帧在两态之间震荡");
    }

    [Test]
    public void 人数刚好装得下_仍按最小间距且不越界()
    {
        // 7 人 × 232 = 1392 ≤ 1680 ⇒ 走原逻辑：间距仍是 232、整组夹进带内
        List<float> centers = new() { 500f, 520f, 540f, 560f, 580f, 600f, 620f };
        List<float> result = WarGodFormationPolicy.SpreadEvenly(
            centers, WarGodFormationPolicy.DefaultMinPlayerSpacing, 120f, 1800f);

        Assert.That(result[6] - result[0], Is.EqualTo(6 * 232f).Within(0.01f));
        foreach (float value in result)
        {
            Assert.That(value, Is.GreaterThanOrEqualTo(120f - 0.01f));
            Assert.That(value, Is.LessThanOrEqualTo(1800f + 0.01f));
        }
    }

}
