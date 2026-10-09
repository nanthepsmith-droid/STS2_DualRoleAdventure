using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 休息处**选项横向滚动条**的纯算术（无 Godot 依赖，可单测）：
/// 内容宽 = Σ(按钮最小宽) + (n−1)×间隔；要不要滚；滚动的窗口开多宽。
///
/// 为什么单独抽出来测：r241 首测的三条症状全由**一个算术输入错了**引起
/// （把 `QueueFree` 待销毁的旧按钮也数进 n ⇒ n=21 而不是 7 ⇒ 宽度算成 7183 ⇒ 居中排布左侧多出 2400+px 空白）。
/// 数节点那一步在 <see cref="RestSiteOptionScrollRuntime"/> 里（要碰 Godot），
/// 但**公式与阈值**放这里，能被 `RestSiteOptionScrollPolicyTests` 钉住。
/// </summary>
internal static class RestSiteOptionScrollPolicy
{
    /// <summary>
    /// 内容宽 = Σ(有效按钮宽) + (n−1)×间隔。空表 / 全非法宽 ⇒ 0（调用方据此判"还没就绪"）。
    /// 间隔为负数按 0 处理（主题取值异常时不要算出负宽度）。
    /// </summary>
    internal static float ResolveContentWidth(IReadOnlyList<float>? buttonWidths, float separation)
    {
        if (buttonWidths == null || buttonWidths.Count == 0)
        {
            return 0f;
        }

        float usableSeparation = separation < 0f ? 0f : separation;
        float sum = 0f;
        int count = 0;
        foreach (float width in buttonWidths)
        {
            if (width <= 0f)
            {
                continue;
            }

            sum += width;
            count++;
        }

        return count == 0 ? 0f : sum + (count - 1) * usableSeparation;
    }

    /// <summary>
    /// 要不要启用滚动条 = **内容宽超出屏幕可用宽**（不是"超出原版容器 799px"）——
    /// 原版 3~5 个选项本来就摆得下（3 个 = 941、5 个 = 1635，都在 1920 屏幕内）⇒ 不该动原版观感。
    /// </summary>
    internal static bool ShouldEnableScroll(float contentWidth, float visibleLimit)
    {
        return visibleLimit > 0f && contentWidth > visibleLimit;
    }

    /// <summary>滚动窗口宽度 = min(内容宽 + 余量, 屏幕可用宽)：能一屏看多少看多少，剩下的才滚。</summary>
    internal static float ResolveWindowWidth(float contentWidth, float visibleLimit, float contentPadding)
    {
        if (visibleLimit <= 0f)
        {
            return 0f;
        }

        float padding = contentPadding < 0f ? 0f : contentPadding;
        return Math.Min(contentWidth + padding, visibleLimit);
    }
}
