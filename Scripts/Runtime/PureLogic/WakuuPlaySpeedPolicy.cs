namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「瓦库出牌加速」判定纯函数（改进-2 / r117）。
///
/// 背景（r116 埋点实测）：单个瓦库每张牌耗时稳定在 **1.0~1.4s**，而多瓦库出牌是**串行**的 ——
/// 后一个瓦库的「回合开始→出牌启动延迟」≈ 前序瓦库出牌耗时之和
/// （3 瓦库局实测：第 1 位 0.5s / 第 2 位 6.3s / 第 3 位 9.7s，且 `delay ≈ Σ 前序 ms` 逐条吻合）。
/// 所以「单张牌的耗时」直接决定一整回合的节奏。
///
/// 依据（反编译 sts2src `CardModel.OnPlayWrapper` / `CardCmd.AutoPlay`）：`skipCardPileVisuals: true` 会跳过
/// <code>
/// if (!skipCardPileVisuals) { await Cmd.CustomScaledWait(0.25f, 0.35f); }              // 打出时的固定等待
/// ...
/// if (!skipCardPileVisuals) { await Cmd.CustomScaledWait(0.15f - num, 0.3f - num); }  // 收尾固定等待
/// </code>
/// 以及 `CardPileCmd.Add(..., skipCardPileVisuals)` 的牌堆补间与 `OnEnqueuePlayVfx` 的烟雾 VFX。
/// 这是游戏**官方为自动出牌场景**（Havoc / 复制药水等）准备的参数，**不改变任何数据层语义**
/// （不同于方案 D 会改 `isAutoPlay` 语义），属于「只省演出等待」的安全加速。
///
/// 本判定不碰任何 Godot / 游戏类型，便于单测。
/// </summary>
internal static class WakuuPlaySpeedPolicy
{
    /// <summary>
    /// 是否给瓦库的自动出牌传 <c>skipCardPileVisuals: true</c>（跳过卡牌堆动画与固定等待）。
    /// </summary>
    /// <param name="toggleEnabled">配置开关（<c>fastWakuuPlay</c>）是否开启。</param>
    /// <param name="localMultiControlEnabled">本地多控是否生效（单人局不干预，保持原生观感）。</param>
    /// <param name="isVakuuFormPlayer">出牌者是否处于【瓦库形态】托管（只管瓦库自己的自动出牌）。</param>
    /// <returns>true = 跳过卡牌堆视觉与固定等待（加速）；false = 播完整演出（原生观感）。</returns>
    public static bool ShouldSkipCardPileVisuals(
        bool toggleEnabled,
        bool localMultiControlEnabled,
        bool isVakuuFormPlayer)
    {
        return toggleEnabled && localMultiControlEnabled && isVakuuFormPlayer;
    }

    /// <summary>
    /// **队列路径**（方案 D）的那张瓦库牌要不要强制跳过卡牌堆演出（改进-2 / 第三步）。
    ///
    /// **为什么需要单独一条**：队列路径由 `PlayCardAction.ExecuteAction` 以
    /// <c>isAutoPlay: false</c> 调 `CardModel.OnPlayWrapper`，**没有** `skipCardPileVisuals` 形参可传
    /// （那条调用是 `PlayCardAction` 自己写的，不走 `CardCmd.AutoPlay`）⇒ r117 的「传参式加速」
    /// 对它完全无效（实机实测「两个开关都开＝加速失效」，见 TODO r122 ③）。只能靠
    /// `CardModel.OnPlayWrapper` 前缀补丁把参数改成 true。
    ///
    /// **与 r117 的两点差异**：
    /// 1. 多一条 <paramref name="isWakuuQueuedPlay"/> —— 只在**我们替瓦库入队**的那次出牌上生效。
    ///    真人手动替瓦库出牌同样是 `isAutoPlay: false`，那种"真人点的牌"不该被加速（观感倒退）。
    /// 2. 收益比 r117 小：`isAutoPlay: false` 分支本来就不走 `CustomScaledWait(0.25f, 0.35f)`
    ///    与前段牌堆补间，能跳过的只有**收尾固定等待** `CustomScaledWait(0.15f - num, 0.3f - num)`
    ///    与**结算堆**（弃牌堆 / 消耗 / 移出战斗）的补间，实测约 0.15~0.3s/张。
    /// </summary>
    /// <param name="toggleEnabled">配置开关（<c>fastWakuuPlay</c>）是否开启。</param>
    /// <param name="localMultiControlEnabled">本地多控是否生效。</param>
    /// <param name="isVakuuFormPlayer">出牌者是否处于【瓦库形态】托管。</param>
    /// <param name="isWakuuQueuedPlay">这次出牌是否是"我们替该瓦库入队"的队列路径出牌。</param>
    public static bool ShouldSkipCardPileVisualsForQueuedPlay(
        bool toggleEnabled,
        bool localMultiControlEnabled,
        bool isVakuuFormPlayer,
        bool isWakuuQueuedPlay)
    {
        return ShouldSkipCardPileVisuals(toggleEnabled, localMultiControlEnabled, isVakuuFormPlayer)
               && isWakuuQueuedPlay;
    }
}
