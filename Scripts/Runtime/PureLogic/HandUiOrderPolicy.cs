using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>手牌 UI 节点顺序与手牌堆数据顺序不一致时的处置（r111/r112，BUG-7）。</summary>
internal enum HandUiOrderAction
{
    /// <summary>不处理：顺序一致，或存在「多余/缺节点」这类**不能安全处理**的差异。</summary>
    None,

    /// <summary>牌是同一批、只是顺序不同 —— 按手牌堆顺序重排现有节点（不建节点、不删节点）。</summary>
    Reorder,
}

/// <summary>
/// 手牌 UI 顺序自愈判定纯函数（r111，BUG-7「手牌排序与数据不同步」）。
///
/// 背景：本地多控下「显示的手牌」只有一份（前台角色的），而每个角色各自有一份手牌堆数据；
/// 一旦两者顺序脱节，表现就是「牌都对但顺序不对，切一次角色（整表重建）才恢复」。
///
/// ⚠ r112 重要收敛（r111 实机回归的教训）：本判定**只允许「重排」这一种动作**。
/// r111 曾对「UI 多出陈旧节点 / 两边都缺」升级为「清多余节点 / 整表重建」，实机立刻出问题：
/// 原版手牌变换的**视觉更新是故意延迟的**（`NCardTransformShineVfx.PlayUntilCardUpdate` 先等
/// `0.75 + 0.125` 秒才 `UpdateCard`），这段窗口里「数据已是新牌、UI 还是旧牌」是**完全正常的动画态**；
/// 据此触发整表重建会在出牌结算途中把出牌链打断 —— 实测「打出的数据链停在屏幕中间不消耗」。
/// 所以这里对「多余 / 缺失」一律返回 <see cref="HandUiOrderAction.None"/>（只由调用方记诊断日志），
/// 只有**多重集完全相同、仅顺序不同**时才重排（此时动画早已落定，重排只是调整节点次序，零风险）。
///
/// 安全底线：**只有数据侧多**（数据有、UI 没有）= 抽牌/生成牌的节点还由补间回调稍后加入 → 一律不干预。
/// </summary>
internal static class HandUiOrderPolicy
{
    /// <summary>
    /// 判定本次要做什么。<paramref name="pileOrder"/> / <paramref name="uiOrder"/> 是**同一套键**的两个序列：
    /// 前者 = 手牌堆顺序，后者 = 手牌区节点顺序；键必须能区分同名重复牌（运行时用「牌名#实例标识」）。
    /// </summary>
    public static HandUiOrderAction Decide(IReadOnlyList<string> pileOrder, IReadOnlyList<string> uiOrder)
    {
        if (SequenceEqual(pileOrder, uiOrder))
        {
            return HandUiOrderAction.None;
        }

        Count(pileOrder, uiOrder, out List<string> uiExtras, out List<string> missingInUi);

        // 有任何「多余 / 缺失」都不动手：手牌变换的视觉更新故意延迟约 0.9s，
        // 那段窗口里数据/UI 必然对不上，据此重建/删节点会打断正在结算的出牌（r111 实机回归）。
        if (uiExtras.Count > 0 || missingInUi.Count > 0)
        {
            return HandUiOrderAction.None;
        }

        // 牌是同一批、只是顺序不同 → 按数据顺序重排
        return HandUiOrderAction.Reorder;
    }

    /// <summary>UI 上多出来（手牌堆里已经没有）的键，按 UI 顺序返回，供诊断日志使用。</summary>
    public static List<string> ListUiExtras(IReadOnlyList<string> pileOrder, IReadOnlyList<string> uiOrder)
    {
        Count(pileOrder, uiOrder, out List<string> uiExtras, out _);
        return uiExtras;
    }

    /// <summary>手牌堆里有、UI 上还没有对应节点的键，供诊断日志使用。</summary>
    public static List<string> ListMissingInUi(IReadOnlyList<string> pileOrder, IReadOnlyList<string> uiOrder)
    {
        Count(pileOrder, uiOrder, out _, out List<string> missingInUi);
        return missingInUi;
    }

    private static bool SequenceEqual(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        for (int i = 0; i < left.Count; i++)
        {
            if (!string.Equals(left[i], right[i], System.StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// 多重集比较（同名重复牌按次数算）：<paramref name="uiExtras"/> = UI 多出来的，
    /// <paramref name="missingInUi"/> = 手牌堆里有但 UI 没有的。
    /// </summary>
    private static void Count(
        IReadOnlyList<string> pileOrder,
        IReadOnlyList<string> uiOrder,
        out List<string> uiExtras,
        out List<string> missingInUi)
    {
        Dictionary<string, int> remaining = new();
        foreach (string key in pileOrder)
        {
            remaining[key] = remaining.TryGetValue(key, out int count) ? count + 1 : 1;
        }

        uiExtras = new List<string>();
        foreach (string key in uiOrder)
        {
            if (remaining.TryGetValue(key, out int count) && count > 0)
            {
                remaining[key] = count - 1;
            }
            else
            {
                uiExtras.Add(key);
            }
        }

        missingInUi = new List<string>();
        foreach (KeyValuePair<string, int> pair in remaining)
        {
            for (int i = 0; i < pair.Value; i++)
            {
                missingInUi.Add(pair.Key);
            }
        }
    }
}
