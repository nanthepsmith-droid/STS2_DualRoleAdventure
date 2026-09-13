using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 选择器栈的「按引用摘除」纯逻辑（改进-2 / 方案 D 第二步）。
///
/// **为什么需要它**：原版 <c>CardSelectCmd.PushSelector</c> 返回的 scope 只在「自己仍是栈顶」时才弹栈
/// （`StackedSelectorScope.Dispose`：`if (_stack.Count > 0 && _stack.Peek() == _selector) Pop()`）。
/// 单个作用域下没问题，但**并发出牌**档里两个瓦库的作用域会交错（先压入的先释放）：
/// 释放时自己已不在栈顶 → 原版 scope **什么都不做** → 该选择器被**永久留在全局选择器栈里**
/// → 之后所有「栈上无选择器」的判定（作用域外自动作答、防软锁兜底切前台）都会失效。
///
/// 因此作用域释放时按**引用**把自己从栈里摘掉（不在栈里 = 空操作、幂等）。
/// 单个作用域的正常路径下，scope 已经弹过 → 本函数原样返回，行为零变化。
///
/// 本文件不依赖任何游戏类型（泛型 + 只读列表），可直接单测。
/// </summary>
internal static class WakuuSelectorStackSurgery
{
    /// <summary>
    /// 从「栈顶 → 栈底」顺序的序列里摘掉 <paramref name="target"/>（**按引用**比较，只摘第一个命中）。
    /// 返回**新列表**（保持原顺序）；未命中时返回与输入等价的副本（长度相同，调用方可据此判断"不在栈里"）。
    /// </summary>
    /// <param name="topToBottom">栈内容的枚举顺序（`Stack&lt;T&gt;` 的枚举顺序即"栈顶 → 栈底"）。</param>
    /// <param name="target">要摘掉的选择器实例（按引用比较，不做相等性判断）。</param>
    public static List<T> RemoveByReference<T>(IReadOnlyList<T> topToBottom, T target)
    {
        List<T> result = new(topToBottom.Count);
        bool removed = false;
        foreach (T item in topToBottom)
        {
            if (!removed && ReferenceEquals(item, target))
            {
                removed = true;
                continue;
            }

            result.Add(item);
        }

        return result;
    }
}
