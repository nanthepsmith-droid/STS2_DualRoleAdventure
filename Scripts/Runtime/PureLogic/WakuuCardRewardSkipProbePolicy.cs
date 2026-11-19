namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 【探针】「这条奖励要不要**不自动领、留给真人**」的纯判定 —— 讨论-1「瓦库抓牌优化」的第一步。
///
/// <para>为什么先做探针（2026-11-18 用户拍板）：原版**没有"跳过单个卡牌奖励"的公开入口** ——
/// <c>RewardsSetSynchronizer.SkipLocalRewardsSet</c> 只能整组跳（金币 / 药水 / 遗物会一起丢），
/// <c>CardReward.OnSkipped</c> 只写历史、不推进奖励组。所以整条需求能不能做，
/// 卡在"只跳过卡牌之后，同组的其它奖励还能不能照常领、奖励组还能不能正常完成"。</para>
///
/// <para>⚠ 语义（2026-11-18 实机反馈后修正）：命中 = **托管这次不替瓦库选**，奖励**照旧留在奖励屏上**
/// 让真人自己决定；不是把奖励收掉（上一版收掉会让那一行直接消失，玩家连看都看不到）。</para>
///
/// <para>本判据**刻意不含任何"抓取率 / 牌组大小"门槛**（阈值口径未拍板，TODO §讨论-1 待拍清单 ①~⑥）：
/// 开关开着就对全部瓦库卡牌奖励不自动领，用来一次性验证机制；口径定完后由正式取舍判据取代。
/// 关掉 = 零行为变化（连判据都不算）。</para>
/// </summary>
internal static class WakuuCardRewardSkipProbePolicy
{
    /// <summary>
    /// 是否对这条奖励走「不领」。
    /// </summary>
    /// <param name="probeOn">探针开关（<c>vakuuSkipCardRewardProbe</c>，默认关）。</param>
    /// <param name="isVakuuForm">奖励归属者是不是瓦库形态（真人与第三方席位的奖励一律不动）。</param>
    /// <param name="kind">奖励种类（只跳 <see cref="WakuuRewardKind.Card"/>）。</param>
    /// <param name="hasParentRewardSet">
    /// 这条奖励是不是挂在链接奖励组（<c>LinkedRewardSet</c>）里。**是 ⇒ 一律不跳**：
    /// 链接组要把内部奖励摘掉再收口整组（原版只在自己 <c>SelectUnsynchronized</c> 里做），
    /// 我们单摘不推进会把这个组卡成永远不完成。
    /// </param>
    public static bool ShouldSkip(
        bool probeOn,
        bool isVakuuForm,
        WakuuRewardKind kind,
        bool hasParentRewardSet)
    {
        if (!probeOn)
        {
            return false;
        }

        if (!isVakuuForm)
        {
            return false;
        }

        if (kind != WakuuRewardKind.Card)
        {
            // 金币 / 药水 / 遗物 / 特殊卡牌（取回被偷的牌）照旧自动领 —— 那正是本探针要验证的对照组。
            return false;
        }

        if (hasParentRewardSet)
        {
            return false;
        }

        return true;
    }
}
