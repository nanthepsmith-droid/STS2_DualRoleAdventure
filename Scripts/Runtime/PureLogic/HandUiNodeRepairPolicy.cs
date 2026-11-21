namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 手牌 UI「**缺节点**」的单点补建闸门（r389，纯逻辑）。
///
/// <para><b>为什么只补不删、还要这么多门槛</b>：r111/r112 的教训是「差异 ⇒ 整表重建」会在出牌结算途中
/// 把出牌链打断（打出的牌数据卡在中间不消耗）。所以本策略只放行**补建缺失节点**这一种最小动作，
/// 并且必须同时满足：手牌空闲、没有托管出牌作用域在飞、同一差异已持续够久、距上次尝试已过冷却。</para>
///
/// <para><b>为什么"持续够久"的门槛 = 1.5s</b>：原版手牌变换的视觉更新**故意延迟约 0.9s**
/// （<c>NCardTransformShineVfx.PlayUntilCardUpdate</c>），窗口里"数据是新牌、UI 还是旧牌"是正常态。</para>
///
/// <para><b>为什么要冷却</b>：补建失败（例如原版那条卡面资源还没就绪）时不能每 250ms 重试 ——
/// 那会变成每帧建节点/丢节点的抖动，比缺一张更难查。</para>
///
/// 纯函数（无 Godot / 游戏类型依赖），可直接单测。
/// </summary>
internal static class HandUiNodeRepairPolicy
{
    /// <summary>同一差异要持续这么久才允许补建（与手牌诊断的 WARN 门槛同一口径）。</summary>
    public const long MinPersistMs = 1500L;

    /// <summary>两次补建尝试之间的最小间隔（失败后不能每帧重试）。</summary>
    public const long RetryCooldownMs = 5000L;

    /// <summary>
    /// 现在能不能补建缺失的手牌节点。
    /// </summary>
    /// <param name="missingCount">数据里有、UI 里没有节点的牌数（0 = 无需动作）。</param>
    /// <param name="handBusy">手牌忙（出牌中 / 选牌中 / 不在 Play 模式）—— 忙碌时顺序与内容都不可比。</param>
    /// <param name="playScopeBusy">有托管出牌作用域在飞（<c>SelectorScopeInFlight() &gt; 0</c>）。</param>
    /// <param name="nowMs">当前时刻（<c>Time.GetTicksMsec()</c>，测试里任意单调值）。</param>
    /// <param name="missingSinceMs">同一差异**首次**被看到的时刻；0 = 还没开始计时。</param>
    /// <param name="lastAttemptMs">上次补建尝试的时刻；0 = 还没试过。</param>
    public static bool ShouldRepair(
        int missingCount,
        bool handBusy,
        bool playScopeBusy,
        long nowMs,
        long missingSinceMs,
        long lastAttemptMs)
    {
        if (missingCount <= 0)
        {
            return false;
        }

        if (handBusy || playScopeBusy)
        {
            return false;
        }

        if (missingSinceMs <= 0 || nowMs - missingSinceMs < MinPersistMs)
        {
            return false;
        }

        if (lastAttemptMs > 0 && nowMs - lastAttemptMs < RetryCooldownMs)
        {
            return false;
        }

        return true;
    }
}
