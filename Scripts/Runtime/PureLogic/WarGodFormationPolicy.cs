using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>一个目标落点（屏幕全局坐标，像素）。</summary>
internal readonly struct FormationPoint
{
    internal FormationPoint(float x, float y)
    {
        X = x;
        Y = y;
    }

    internal float X { get; }

    internal float Y { get; }
}

/// <summary>
/// 「战神立绘站位」纯逻辑（2026-10-22，r301）。
///
/// **为什么必须自己摆**：原版 <c>NCombatRoom.PositionPlayersAndPets</c> 的列数是
/// <c>ceil(sqrt(玩家数))</c>、行距/列宽只按**前几列**的宽度算，注释里也写着
/// "Technically we only support four players"。本地多控下真人 2 + 战神 N（九连环能到 9）
/// 很容易超过 4 ⇒ 实测（r300 局）：4 个战神里
/// 两个只隔 **36px**（几乎完全重叠）、一个被推到 **x≈-150（屏幕外）**，
/// 玩家观感就是"招了 4 个只看到 3 个"。
///
/// 本策略改成「**按绑主归位**」：同一绑主的战神在**绑主立绘上方**排成一个扇形阵列
/// （每行 <see cref="PerRow"/> 个、逐行往上），这样：
/// <list type="bullet">
/// <item>永远跟着"是谁召的"走 —— 2 个玩家各招 2 个时不会混在一起；</item>
/// <item>落点锚在**绑主自己的屏幕坐标**上 ⇒ 绑主在画面内 ⇒ 战神也在画面内（再做一次边界夹取兜底）；</item>
/// <item>不依赖原版那套"前 N 列宽度"的算错公式。</item>
/// </list>
/// </summary>
internal static class WarGodFormationPolicy
{
    /// <summary>每行最多摆几个（再多就往上叠一行）。</summary>
    internal const int PerRow = 4;

    /// <summary>同排横向间距（像素）。</summary>
    internal const float SpacingX = 92f;

    /// <summary>第一排相对绑主立绘的纵向偏移（负数 = 往上）。</summary>
    internal const float FirstRowOffsetY = -118f;

    /// <summary>每多一排再往上偏移多少。</summary>
    internal const float RowOffsetY = -104f;

    /// <summary>两个真人立绘之间至少留这么宽（r303：原版在人多时会把真人挤到一起）。</summary>
    internal const float DefaultMinPlayerSpacing = 232f;


    /// <summary>距离屏幕左右边缘 / 顶部的安全边距（立绘有半宽，留大一点）。</summary>
    internal const float ScreenMarginX = 120f;

    internal const float ScreenMarginTop = 140f;

    /// <summary>
    /// 算出 <paramref name="count"/> 个战神相对绑主 (<paramref name="baseX"/>, <paramref name="baseY"/>)
    /// 的落点列表（按输入顺序；第 0 个在最下面一排的**最左**）。
    /// 结果已按屏幕边界夹取（<paramref name="screenWidth"/>/<paramref name="screenHeight"/> 传 0 表示不夹取）。
    /// </summary>
    internal static List<FormationPoint> Build(
        int count,
        float baseX,
        float baseY,
        float screenWidth = 0f,
        float screenHeight = 0f)
    {
        List<FormationPoint> points = new();
        if (count <= 0)
        {
            return points;
        }

        for (int i = 0; i < count; i++)
        {
            int row = i / PerRow;
            int indexInRow = i % PerRow;
            int itemsInRow = Math.Min(PerRow, count - row * PerRow);

            // 每一排按"这一排实际有几个"居中，避免 3 个时整体偏右。
            float centeredOffset = (indexInRow - (itemsInRow - 1) * 0.5f) * SpacingX;
            float x = baseX + centeredOffset;
            float y = baseY + FirstRowOffsetY + row * RowOffsetY;

            if (screenWidth > 0f)
            {
                float minX = ScreenMarginX;
                float maxX = Math.Max(minX, screenWidth - ScreenMarginX);
                x = Math.Clamp(x, minX, maxX);
            }

            if (screenHeight > 0f)
            {
                float minY = Math.Min(ScreenMarginTop, Math.Max(0f, screenHeight - 1f));
                float maxY = Math.Max(minY, screenHeight - ScreenMarginTop);
                y = Math.Clamp(y, minY, maxY);
            }

            points.Add(new FormationPoint(x, y));
        }

        return points;
    }

    /// <summary>
    /// 把**真人**的横坐标拉开到至少 <paramref name="minSpacing"/>（r303，用户实测：
    /// 「用了【无尽战神】后两个玩家突然离得很近」—— 原版站位在人数变多时会把真人挤到一起）。
    ///
    /// 规则：**只在间距不足时干预**（够宽就原样返回，避免和第三方布局打架）；
    /// 以这组人的**中点**为中心左右展开；最后整组夹进 <c>[bandLeft, bandRight]</c>。
    /// 返回列表与输入**同序**。
    /// </summary>
    internal static List<float> SpreadEvenly(
        IReadOnlyList<float> centers,
        float minSpacing,
        float bandLeft,
        float bandRight)
    {
        List<float> result = new();
        int count = centers?.Count ?? 0;
        if (count == 0)
        {
            return result;
        }

        for (int i = 0; i < count; i++)
        {
            result.Add(centers![i]);
        }

        if (count == 1)
        {
            return result;
        }

        float spacing = Math.Max(0f, minSpacing);

        // 按当前横坐标排序（保持"谁在左谁在右"不变）
        List<int> order = new(count);
        for (int i = 0; i < count; i++)
        {
            order.Add(i);
        }

        order.Sort((a, b) => centers![a].CompareTo(centers[b]));

        float first = centers![order[0]];
        float last = centers[order[count - 1]];
        float requiredSpan = (count - 1) * spacing;

        if (last - first < requiredSpan)
        {
            float middle = (first + last) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float offset = (i - (count - 1) * 0.5f) * spacing;
                result[order[i]] = middle + offset;
            }
        }

        // 整组夹进可用带（不够宽就以左边界为准，别把内容推出屏幕）
        if (bandRight > bandLeft)
        {
            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float value in result)
            {
                min = Math.Min(min, value);
                max = Math.Max(max, value);
            }

            float shift = 0f;
            if (min < bandLeft)
            {
                shift = bandLeft - min;
            }
            else if (max > bandRight)
            {
                shift = bandRight - max;
            }

            if (Math.Abs(shift) > 0.01f)
            {
                for (int i = 0; i < count; i++)
                {
                    result[i] += shift;
                }
            }
        }

        return result;
    }
}
