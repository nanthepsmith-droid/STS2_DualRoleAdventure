namespace LocalMultiControl.Scripts.Runtime;

/// <summary><c>CardSelectCmd.Selector</c> getter 的处置方式（改进-2 / Phase 1）。</summary>
internal enum SelectorDispatchDecision
{
    /// <summary>保持原版栈顶结果（等价升级前行为）。</summary>
    KeepTop,

    /// <summary>改用该归属者登记在 <see cref="WakuuOwnerSelectorMap{TSelector}"/> 里的托管选择器。</summary>
    UseOwned,

    /// <summary>摘掉托管选择器（返回 null），让原版弹选牌 UI 交给真人。</summary>
    ReturnNull,
}

/// <summary>
/// 「选牌选择器按归属者分发」的判定纯函数（改进-2 / Phase 1，可单测）。
///
/// 输入来自三处客观事实：
/// <list type="bullet">
/// <item><c>hasChooser</c>：本异步链上是否已知选牌归属者（<c>CardSelectForegroundSwitchPatch.CurrentChoicePlayerId</c>）；</item>
/// <item><c>registryHit</c>：该归属者在 <see cref="WakuuSelectorRegistry"/> 里是否登记了托管选择器；</item>
/// <item><c>chooserIsWakuu</c>：该归属者是否处于【瓦库形态】托管。</item>
/// </list>
///
/// 路由表（与升级前的三条老路严格兼容，新增的只有「查表命中」这一条）：
/// <code>
/// hasChooser | registryHit | chooserIsWakuu | 结果
///    false   |     -       |       -        | KeepTop   （信息不足，一律不动，同旧版）
///    true    |   false     |     true       | KeepTop   （瓦库但未登记 → 退回栈顶语义，同旧版）
///    true    |   false     |     false      | ReturnNull（真人 → 摘掉托管选择器，同旧版）
///    true    |   true      |     true       | UseOwned  （★ Phase 1 新增：按归属查表）
///    true    |   true      |     false      | ReturnNull（防御：登记表只该装瓦库；非瓦库保守当真人）
/// </code>
///
/// 不变式：**先判「是不是瓦库」再判「查表是否命中」**——真人无论查表命中与否都必须走 UI，
/// 否则会重演「真人的选择被静默吃掉」（r106 工具箱的教训）。
/// </summary>
internal static class WakuuSelectorDispatch
{
    public static SelectorDispatchDecision Decide(bool hasChooser, bool registryHit, bool chooserIsWakuu)
    {
        if (!hasChooser)
        {
            // 信息不足：不动它（旧版行为；此时栈顶是谁就谁作答）
            return SelectorDispatchDecision.KeepTop;
        }

        if (!chooserIsWakuu)
        {
            // 真人在选牌：绝不自动作答，走正常 UI
            return SelectorDispatchDecision.ReturnNull;
        }

        // 瓦库形态角色：查表命中则精确分发，未登记则退回栈顶语义（与升级前一致）
        return registryHit
            ? SelectorDispatchDecision.UseOwned
            : SelectorDispatchDecision.KeepTop;
    }
}
