namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「第三方自绘选牌」的判据（BUG-23 方案 B，2026-09-28）。纯布尔组合 ⇒ 进 PureLogic、可单测。
///
/// **背景**：第三方 mod 会自己实现选牌（`ReserveChoiceId` + 自绘 `NChooseACardSelectionScreen`，
/// 或者干脆 `WaitForRemoteChoice` 死等），既不读 `CardSelectCmd.Selector` 也不走 `From*`
/// ⇒ 我们的选择器等不到被问。详见证照 `TODO.md` §BUG-23 与 `references` 坑 M。
///
/// **本方案的思路**：只在**一个很窄的窗口**里承认"这一席就是我" ——
/// 「后台托管的瓦库席位」**且**「此刻正被我们的自动化驱动」（该席位在我们的
/// <see cref="WakuuSelectorRegistry"/> 里登记着托管选择器，即它正处在一次自动出牌/遗物效果/事件作答的作用域内）：
/// <list type="bullet">
/// <item>窗口内：第三方代码问 `LocalContext.IsMe(这一席)` 时回答"是" ⇒ 它走**自己的本地分支**、弹出它自己的界面
///   （而不是走无人作答的远端等待）；随后由我们驱动那个界面按瓦库策略作答（见
///   `NChooseACardSelectionShowScreenAutoAnswerPatch`）；</item>
/// <item>窗口外（真人亲自操作这一席 / 该席位没在自动化）⇒ 一律**不动**，绝不替真人做决定、也不改变原版语义。</item>
/// </list>
///
/// ⚠ **为什么必须卡这么窄**：放宽 `LocalContext.IsMe` 等于让"别人的牌当成本地玩家的牌"，
/// 而并发出牌档刻意避免钉全局上下文，正是为了不触发那类前台视觉异常（r109 教训）；
/// 因此这里只对"我们正在为这一席自动化"的窗口 + **第三方调用方**（原版调用方语义一律不动）放行。
/// </summary>
internal static class WakuuSelfDrawnChoicePolicy
{
    /// <summary>
    /// 这一席是不是「该由我们自动作答的后台托管瓦库席位」（**与是否正在出牌无关**）。
    /// 六个输入由调用方采集（便于单测钉死口径）：本地多控启用 / 单人冒险档 / 本地回环会话 /
    /// 本地席位 / 后台托管档 / 瓦库形态。
    ///
    /// ⚠ 这是既有「作用域外选牌自动作答」（`CardSelectWakuuTurnStartAutoAnswerPatch.ShouldAutoAnswer`）
    /// 的口径原语 —— 那处再叠一条「栈上不是我们的选择器」，两者共用本原语以免三处口径漂移。
    /// </summary>
    internal static bool IsManagedWakuuSeat(
        bool enabled,
        bool singleAdventureMode,
        bool loopbackSession,
        bool isLocalSeat,
        bool backgroundMode,
        bool vakuuFormMode)
    {
        return enabled
            && singleAdventureMode
            && loopbackSession
            && isLocalSeat
            && backgroundMode
            && vakuuFormMode;
    }

    /// <summary>
    /// 这一席是不是「后台托管瓦库 + **此刻正被我们的自动化驱动**」——即它当前在
    /// <see cref="WakuuSelectorRegistry"/> 里登记着托管选择器（正处在一次自动出牌 / 遗物效果 /
    /// 事件作答的作用域内）。方案 B 的两个补丁都以它为准入门槛。
    /// </summary>
    internal static bool IsAutomatedSeatInPlay(
        bool enabled,
        bool singleAdventureMode,
        bool loopbackSession,
        bool isLocalSeat,
        bool backgroundMode,
        bool vakuuFormMode,
        bool hasManagedSelector)
    {
        return IsManagedWakuuSeat(enabled, singleAdventureMode, loopbackSession, isLocalSeat, backgroundMode, vakuuFormMode)
            && hasManagedSelector;
    }

    /// <summary>
    /// 第三方问「这一席是不是本机玩家」时要不要改口成"是"。
    /// 只在①原判断为 false、②落在上面的自动化窗口内、③调用方是第三方、④调用方**不在放行黑名单**时改口
    /// （原版调用方的语义一点都不动）。
    ///
    /// ④ 是 BUG-26（2026-09-28 实机）补的：黑名单 = 那些"本地分支**不是**弹选牌界面、
    /// 而是需要前台/真人配合的转场或对话序列"的第三方调用方 —— 放行只会让它在后台席位上挂死
    /// （实证：TouhouAncients「离开梦境」<c>LeaveDreamReentry.OnChosen</c> 被放行后走进
    /// <c>await LeaveDreamSequence.Play(...)</c> 永久等待 ⇒ 事件自动选择卡死 20 秒后被安全网切人工）。
    /// 不放行 = 维持原判 false ⇒ 走原版联机的远端分支（方案 A 兜底按空结果放行），行为与 r183 前一致。
    /// </summary>
    internal static bool ShouldWidenIsMe(
        bool originalIsMe,
        bool automatedSeatInPlay,
        bool callerIsThirdParty,
        bool callerDenylisted = false)
    {
        return !originalIsMe && automatedSeatInPlay && callerIsThirdParty && !callerDenylisted;
    }

    /// <summary>
    /// 自绘选牌界面出现后要不要由我们驱动作答。
    /// 同一窗口判据 + 界面上确实有候选 + 这个界面还没被我们作答过（防重复驱动）。
    /// </summary>
    internal static bool ShouldAutoAnswerScreen(bool automatedSeatInPlay, int optionCount, bool alreadyAnswered)
    {
        return automatedSeatInPlay && !alreadyAnswered && optionCount > 0;
    }
}
