namespace LocalMultiControl.Scripts.Runtime;

/// <summary>瓦库出牌的两种实现路径。</summary>
internal enum WakuuPlayPath
{
    /// <summary>现状：<c>CardCmd.AutoPlay</c>（inline 执行，<c>isAutoPlay: true</c>，外层先 SpendResources）。</summary>
    InlineAutoPlay = 0,

    /// <summary>
    /// 方案 D 实验档：构造 <c>PlayCardAction</c> 入**该瓦库自己的**动作队列
    /// （<c>isAutoPlay: false</c>，扣费由动作自己完成）。
    /// </summary>
    ActionQueue = 1,
}

/// <summary>
/// 「瓦库出牌走动作队列」（改进-2 / 方案 D 实验档）的判定纯函数。
///
/// **为什么需要一条独立路径**：瓦库现在走 inline 的 <c>CardCmd.AutoPlay</c>，**不进动作队列**，
/// 又为了保护全局选择器栈而在外面套了全局 1 槽的 `SelectorScopeGate`（方案 §2.2 / §11.2）——
/// 「一个瓦库打完才轮到下一个」是**这个闸门的产物**，不是游戏机制。
/// 原版多人模式的不互相卡由三件套实现（方案 §11.1）：**每人一条队列** + 全局递增 action ID 决定顺序 +
/// **等选择的队列被跳过**（<c>ActionQueueSet.GetReadyAction</c>）。三件套在单进程内即成立。
/// 走 <c>PlayCardAction</c> 入队 = 直接复用这套原生语义。
///
/// **三条刻意的语义迁移**（方案 §12.2，实验档的核心观察对象）：
/// 1. <c>isAutoPlay</c> true→false：与真人出牌同语义，`VoidFormPower` / `PaelsEye` / `UnceasingTop`
///    等对"自动出牌"特判的牌会开始把瓦库的出牌算进去，且 `Hook.BeforeCardAutoPlayed` 不再触发
///    （`BeforeCardPlayed` / `AfterCardPlayed` 两条路径都触发，无差异）；
/// 2. `Any` 类目标必须**构造前**解析好：解析不到时动作会被 `Cancel()`，牌**留在手牌**、不入堆不消耗
///    （比 AutoPlay 的"随机取目标，取不到就进堆"更严格，对瓦库更安全）；另外牌必须还在 `Hand` 堆；
/// 3. <c>SpendResources()</c> 由 `PlayCardAction.ExecuteAction` 自己调用 ⇒ **调用方绝不能再花一次**
///    （现状 AutoPlay 传的 <c>skipXCapture: true</c> 语义就是"调用方已花过"）。
///
/// 本文件不依赖任何游戏类型，可直接单测。
/// </summary>
internal static class WakuuPlayQueuePolicy
{
    /// <summary>
    /// 本次出牌走哪条路径。三条条件（开关 / 本地多控生效 / 出牌者确实是瓦库形态托管）全部满足才走队列，
    /// 任何一条不满足都保持既有 inline 路径 —— 与 <c>WakuuPlaySpeedPolicy</c> 同口径。
    /// </summary>
    /// <param name="toggleEnabled">配置开关（<c>wakuuPlayQueue</c>）是否开启（默认关）。</param>
    /// <param name="localMultiControlEnabled">本地多控是否生效（单人局不干预，保持原生行为）。</param>
    /// <param name="isVakuuFormPlayer">出牌者是否处于【瓦库形态】托管（只管瓦库自己的出牌）。</param>
    public static WakuuPlayPath DecidePath(
        bool toggleEnabled,
        bool localMultiControlEnabled,
        bool isVakuuFormPlayer)
    {
        return toggleEnabled && localMultiControlEnabled && isVakuuFormPlayer
            ? WakuuPlayPath.ActionQueue
            : WakuuPlayPath.InlineAutoPlay;
    }

    /// <summary>
    /// 是否需要由调用方先 <c>await card.SpendResources()</c>。
    ///
    /// **只有 inline AutoPlay 需要**（它随后以 <c>skipXCapture: true</c> 被调用，即"调用方已花过"）。
    /// 动作队列路径由 `PlayCardAction` 自己扣费，外层再花一次就是**双重扣能量**（方案 §12.2 ③）。
    /// 把这条不变式放进纯函数，是为了让"扣费归属"只有一个来源、可被单测钉死。
    /// </summary>
    public static bool NeedsExternalSpendResources(WakuuPlayPath path)
    {
        return path == WakuuPlayPath.InlineAutoPlay;
    }

    /// <summary>
    /// 传给 <c>PlayCardAction</c> 的目标要不要用大脑解析出来的那个。
    ///
    /// 原版真人出牌路径（`NCardPlay.TryPlayCard`）只对 `AnyEnemy` / `AnyAlly` 传目标，
    /// **其余目标类型一律传 null**（`TargetType.AnyPlayer` 在单机下"不做目标选择"，文档见 `TargetType`）。
    /// 这一点在队列路径上不是风格问题而是**正确性问题**：`PlayCardAction.ExecuteAction` 会用
    /// <c>CardModel.IsValidTarget(target)</c> 校验，而它对"非 Any 的牌 + 非空目标"**返回 false** →
    /// 动作被 `Cancel()`、牌打不出去。所以必须按原版口径把非 Any 牌的目标清成 null。
    /// </summary>
    public static bool ShouldUseResolvedTarget(bool isAnyTargetCard)
    {
        return isAnyTargetCard;
    }
}
