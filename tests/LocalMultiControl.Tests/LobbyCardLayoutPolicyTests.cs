using System.Collections.Generic;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 大厅席位卡「插入列」布局纯逻辑测试（R1：把这段数学从
/// `NRemoteLobbyPlayerSwitchPatch` 搬进 <see cref="LobbyCardLayoutPolicy"/>）。
///
/// 目的：这段数值语义过去**只能靠实机看有没有串位**，现在用单测钉死 ——
/// 列合并容差、最近列、列间距下限、首列锚点、插入列公式。
/// 注意：搬出后 `BuildColumns` **不再就地排序调用方列表**（原实现会 `values.Sort()`，
/// 调用方总是传临时列表，故行为等价）。
/// </summary>
[TestFixture]
public class LobbyCardLayoutPolicyTests
{
    [Test]
    public void 空输入_没有列()
    {
        Assert.That(LobbyCardLayoutPolicy.BuildColumns(new List<float>()), Is.Empty);
    }

    [Test]
    public void 单点_一列()
    {
        Assert.That(LobbyCardLayoutPolicy.BuildColumns(new List<float> { 50f }), Is.EqualTo(new[] { 50f }));
    }

    [Test]
    public void 容差内相邻_合并取中点()
    {
        // |100 - 130| = 30 ≤ 40 → 合并成 (100+130)/2 = 115
        Assert.That(LobbyCardLayoutPolicy.BuildColumns(new List<float> { 100f, 130f }), Is.EqualTo(new[] { 115f }));
    }

    [Test]
    public void 超出容差_分成两列()
    {
        Assert.That(
            LobbyCardLayoutPolicy.BuildColumns(new List<float> { 100f, 200f }),
            Is.EqualTo(new[] { 100f, 200f }));
    }

    [Test]
    public void 输入乱序_先排序再合并()
    {
        Assert.That(
            LobbyCardLayoutPolicy.BuildColumns(new List<float> { 200f, 100f, 400f }),
            Is.EqualTo(new[] { 100f, 200f, 400f }));
    }

    [Test]
    public void 输入列表不被就地排序()
    {
        List<float> input = new() { 200f, 100f };
        _ = LobbyCardLayoutPolicy.BuildColumns(input);
        Assert.That(input, Is.EqualTo(new[] { 200f, 100f }), "纯函数不得改动调用方数据");
    }

    [Test]
    public void 最近列_取距离最小()
    {
        List<float> columns = new() { 100f, 200f };
        Assert.That(LobbyCardLayoutPolicy.ResolveNearestColumnIndex(columns, 140f), Is.EqualTo(0));
        Assert.That(LobbyCardLayoutPolicy.ResolveNearestColumnIndex(columns, 180f), Is.EqualTo(1));
    }

    [Test]
    public void 同距_取更靠前的列()
    {
        Assert.That(
            LobbyCardLayoutPolicy.ResolveNearestColumnIndex(new List<float> { 100f, 300f }, 200f),
            Is.EqualTo(0));
    }

    [Test]
    public void 没有列时_下标为0()
    {
        Assert.That(LobbyCardLayoutPolicy.ResolveNearestColumnIndex(new List<float>(), 123f), Is.EqualTo(0));
    }

    [Test]
    public void 单列_间距取下限()
    {
        Assert.That(
            LobbyCardLayoutPolicy.ResolveColumnStep(new List<float> { 100f }),
            Is.EqualTo(LobbyCardLayoutPolicy.MinColumnGap));
        Assert.That(
            LobbyCardLayoutPolicy.ResolveColumnStep(new List<float>()),
            Is.EqualTo(LobbyCardLayoutPolicy.MinColumnGap));
    }

    [Test]
    public void 间隙平均低于下限_取下限()
    {
        // 间距 100 < 140（列本身的分列容差是 40，但这里直接给列数组）
        Assert.That(
            LobbyCardLayoutPolicy.ResolveColumnStep(new List<float> { 100f, 200f }),
            Is.EqualTo(LobbyCardLayoutPolicy.MinColumnGap));
    }

    [Test]
    public void 只有1像素以内间隙_不计入平均_回落下限()
    {
        Assert.That(
            LobbyCardLayoutPolicy.ResolveColumnStep(new List<float> { 100f, 100.5f }),
            Is.EqualTo(LobbyCardLayoutPolicy.MinColumnGap));
    }

    [Test]
    public void 间隙平均高于下限_取平均()
    {
        Assert.That(
            LobbyCardLayoutPolicy.ResolveColumnStep(new List<float> { 100f, 300f, 500f }),
            Is.EqualTo(200f));
    }

    [Test]
    public void 首列锚点_取最左列节点的最小锚点()
    {
        List<(float, float)> nodes = new() { (100f, 10f), (110f, 40f), (400f, 999f) };
        List<float> columns = LobbyCardLayoutPolicy.BuildColumns(new List<float> { 100f, 110f, 400f });

        Assert.That(
            LobbyCardLayoutPolicy.ResolveFirstColumnAnchor(nodes, columns, fallbackAnchorX: 7f),
            Is.EqualTo(10f));
    }

    [Test]
    public void 最左列没有节点_用兜底锚点()
    {
        List<(float, float)> nodes = new() { (500f, 999f) };
        List<float> columns = LobbyCardLayoutPolicy.BuildColumns(new List<float> { 100f, 500f });

        Assert.That(
            LobbyCardLayoutPolicy.ResolveFirstColumnAnchor(nodes, columns, fallbackAnchorX: 7f),
            Is.EqualTo(7f));
    }

    [Test]
    public void 没有列时_首列锚点也用兜底()
    {
        Assert.That(
            LobbyCardLayoutPolicy.ResolveFirstColumnAnchor(new List<(float, float)>(), new List<float>(), 7f),
            Is.EqualTo(7f));
    }

    [Test]
    public void 插入列x_等于首列锚点加右移加列下标乘步长()
    {
        Assert.That(LobbyCardLayoutPolicy.ResolveColumnX(50f, 2, 200f), Is.EqualTo(468f));
    }

    [Test]
    public void 回归锚点_三节点两列的整段布局()
    {
        // 局部节点 x = 300 / 310 / 600：前两个合并成一列（(300+310)/2 = 305），第三个自成列
        // ⇒ 当前节点在 600 时列下标 = 1，列间距 = max(140, 600-305) = 295
        // ⇒ 插入列 x = 首列最小锚点 100 + 右移 18 + 1 × 295 = 413
        List<float> columns = LobbyCardLayoutPolicy.BuildColumns(new List<float> { 300f, 310f, 600f });
        Assert.That(columns, Is.EqualTo(new[] { 305f, 600f }));

        int columnIndex = LobbyCardLayoutPolicy.ResolveNearestColumnIndex(columns, 600f);
        float step = LobbyCardLayoutPolicy.ResolveColumnStep(columns);
        List<(float, float)> nodes = new() { (300f, 100f), (310f, 120f), (600f, 900f) };
        float firstColumnAnchorX = LobbyCardLayoutPolicy.ResolveFirstColumnAnchor(nodes, columns, 0f);

        Assert.That(columnIndex, Is.EqualTo(1));
        Assert.That(step, Is.EqualTo(295f));
        Assert.That(LobbyCardLayoutPolicy.ResolveColumnX(firstColumnAnchorX, columnIndex, step), Is.EqualTo(413f));
    }
}
