using System;
using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 大厅席位卡「插入列」布局的**纯计算**（R1：从 <c>NRemoteLobbyPlayerSwitchPatch</c> 搬出，
/// 数值语义与原实现逐字一致，只把 Godot 的 <c>Mathf</c> 换成 <c>Math</c>）。
///
/// 为什么搬出来：这段是<strong>纯数学</strong>（列合并 / 最近列 / 列间距 / 首列锚点），
/// 却住在 517 行的补丁类里，只能靠实机回归；搬进 PureLogic 后可用单测锁住行为
/// （见 `tests/LocalMultiControl.Tests/LobbyCardLayoutPolicyTests.cs`）。
/// 背景与分层规则：`maintenance-docs/decision-records/runtime架构分层重构评估.md`（R1）。
/// </summary>
internal static class LobbyCardLayoutPolicy
{
    /// <summary>同一列的 x 容差（原 `NRemoteLobbyPlayerSwitchPatch.ColumnMergeTolerance`）。</summary>
    public const float ColumnMergeTolerance = 40f;

    /// <summary>列间距下限（原 `MinColumnGap`）。</summary>
    public const float MinColumnGap = 140f;

    /// <summary>插入列相对首列锚点的右移量（原 `ColumnRightShift`）。</summary>
    public const float ColumnRightShift = 18f;

    /// <summary>
    /// 把若干 x 坐标合并成「列」：先排序，相邻且在 <see cref="ColumnMergeTolerance"/> 内的取中点。
    /// 注意：与原实现一致，**输入列表不会被就地排序**（原实现会，但调用方总是传临时列表）。
    /// </summary>
    public static List<float> BuildColumns(IReadOnlyList<float> values)
    {
        List<float> sorted = values.ToList();
        sorted.Sort();

        List<float> columns = new();
        foreach (float value in sorted)
        {
            if (columns.Count == 0)
            {
                columns.Add(value);
                continue;
            }

            if (Math.Abs(columns[^1] - value) <= ColumnMergeTolerance)
            {
                columns[^1] = (columns[^1] + value) * 0.5f;
            }
            else
            {
                columns.Add(value);
            }
        }

        return columns;
    }

    /// <summary>离 <paramref name="x"/> 最近的列下标；同距取更靠前的列；无列时返回 0。</summary>
    public static int ResolveNearestColumnIndex(IReadOnlyList<float> columns, float x)
    {
        if (columns.Count == 0)
        {
            return 0;
        }

        int bestIndex = 0;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < columns.Count; i++)
        {
            float distance = Math.Abs(columns[i] - x);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                bestIndex = i;
            }
        }

        return bestIndex;
    }

    /// <summary>
    /// 列间距：取所有 &gt;1f 的相邻间隙的平均值，与 <see cref="MinColumnGap"/> 取大；
    /// 列数 ≤1 或没有有效间隙时返回 <see cref="MinColumnGap"/>。
    /// </summary>
    public static float ResolveColumnStep(IReadOnlyList<float> columns)
    {
        if (columns.Count <= 1)
        {
            return MinColumnGap;
        }

        List<float> gaps = new();
        for (int i = 1; i < columns.Count; i++)
        {
            float gap = columns[i] - columns[i - 1];
            if (gap > 1f)
            {
                gaps.Add(gap);
            }
        }

        if (gaps.Count == 0)
        {
            return MinColumnGap;
        }

        return Math.Max(MinColumnGap, (float)gaps.Average());
    }

    /// <summary>
    /// 「最左列」节点的最小锚点 x：以**合并后**列数组的最小值为基准，取 x 落在容差内的节点，
    /// 再取它们锚点 x 的最小值；一个都没有时返回 <paramref name="fallbackAnchorX"/>。
    /// </summary>
    /// <param name="nodes">(节点 x, 该节点的锚点 x) 列表，顺序无关。</param>
    /// <param name="columns"><see cref="BuildColumns"/> 的结果。</param>
    /// <param name="fallbackAnchorX">没有节点落在最左列时的兜底锚点 x（原实现 = 当前节点的锚点 x）。</param>
    public static float ResolveFirstColumnAnchor(
        IReadOnlyList<(float NodeX, float AnchorX)> nodes,
        IReadOnlyList<float> columns,
        float fallbackAnchorX)
    {
        if (columns.Count == 0)
        {
            return fallbackAnchorX;
        }

        float minX = columns.Min();
        float best = float.MaxValue;
        bool found = false;
        foreach ((float nodeX, float anchorX) in nodes)
        {
            if (Math.Abs(nodeX - minX) > ColumnMergeTolerance)
            {
                continue;
            }

            if (!found || anchorX < best)
            {
                best = anchorX;
                found = true;
            }
        }

        return found ? best : fallbackAnchorX;
    }

    /// <summary>插入列的 x = 首列锚点 + <see cref="ColumnRightShift"/> + 列下标 × 列间距。</summary>
    public static float ResolveColumnX(float firstColumnAnchorX, int columnIndex, float step)
    {
        return firstColumnAnchorX + ColumnRightShift + columnIndex * step;
    }
}
