namespace LocalMultiControl.Scripts.Runtime;

/// <summary>手牌缺节点补建闸门的判定结果（r389 引入，r390 增细化原因）。</summary>
internal enum HandUiNodeRepairDecision
{
    /// <summary>可以补建。</summary>
    Repair,

    /// <summary>本来就没缺。</summary>
    NoMissing,

    /// <summary>手牌忙（出牌中 / 选牌中 / 非 Play 模式）。</summary>
    HandBusy,

    /// <summary>同一差异还没持续够久（避开原版变换视觉那 ~0.9s 的延迟窗口）。</summary>
    PersistPending,

    /// <summary>距上次补建尝试还在冷却里。</summary>
    Cooldown,
}

/// <summary>
/// 手牌 UI「**缺节点**」的单点补建闸门（r389，纯逻辑）。
///
/// <para><b>为什么只补不删、还要这么多门槛</b>：r111/r112 的教训是「差异 ⇒ 整表重建」会在出牌结算途中
/// 把出牌链打断（打出的牌数据卡在中间不消耗）。所以本策略只放行**补建缺失节点**这一种最小动作，
/// 并且必须同时满足：手牌空闲、同一差异已持续够久、距上次尝试已过冷却。</para>
///
/// <para><b>为什么"持续够久"的门槛 = 1.5s</b>：原版手牌变换的视觉更新**故意延迟约 0.9s**
/// （<c>NCardTransformShineVfx.PlayUntilCardUpdate</c>），窗口里"数据是新牌、UI 还是旧牌"是正常态。</para>
///
/// <para><b>为什么要冷却</b>：补建失败（例如那条卡面资源还没就绪）时不能每 250ms 重试 ——
/// 那会变成每帧建节点/丢节点的抖动，比缺一张更难查。</para>
///
/// <para><b>r390 及其教训</b>：第一版还额外要求「没有任何托管出牌作用域在飞」（<c>SelectorScopeInFlight()==0</c>）。
/// 2026-11-22 实机：一局 153 次差异、**补建 0 次** —— 因为这种缺节点恰恰只发生在**别的席位正在出牌**的窗口里
/// （抽到牌那一刻归属不是受控席位 ⇒ 守卫跳过建节点），等出牌链结束差异往往已经被别的重建路径消掉。
/// 也就是说那道闸门把唯一该修的时刻全挡掉了。现在只保留「手牌自己不忙」（<c>InCardPlay</c> / 选牌 / 非 Play
/// 由调用方先判）+ 持续时长 + 冷却；另外**每次被挡都记一条原因**，避免下次再靠猜。</para>
///
/// 纯函数（无 Godot / 游戏类型依赖），可直接单测。
/// </summary>
internal static class HandUiNodeRepairPolicy
{
    /// <summary>同一差异要持续这么久才允许补建（与手牌诊断的 WARN 门槛同一口径）。</summary>
    public const long MinPersistMs = 1500L;

    /// <summary>两次补建尝试之间的最小间隔（失败后不能每帧重试）。</summary>
    public const long RetryCooldownMs = 5000L;

    /// <summary>判定"现在能不能补"，并给出**不能补的原因**（原因用于日志锚点，不当行为用）。</summary>
    /// <param name="missingCount">数据里有、UI 里没有节点的牌数（0 = 无需动作）。</param>
    /// <param name="handBusy">手牌忙（出牌中 / 选牌中 / 不在 Play 模式）—— 忙碌时顺序与内容都不可比。</param>
    /// <param name="nowMs">当前时刻（<c>Time.GetTicksMsec()</c>，测试里任意单调值）。</param>
    /// <param name="missingSinceMs">同一差异**首次**被看到的时刻；0 = 还没开始计时。</param>
    /// <param name="lastAttemptMs">上次补建尝试的时刻；0 = 还没试过。</param>
    public static HandUiNodeRepairDecision Evaluate(
        int missingCount,
        bool handBusy,
        long nowMs,
        long missingSinceMs,
        long lastAttemptMs)
    {
        if (missingCount <= 0)
        {
            return HandUiNodeRepairDecision.NoMissing;
        }

        if (handBusy)
        {
            return HandUiNodeRepairDecision.HandBusy;
        }

        if (missingSinceMs <= 0 || nowMs - missingSinceMs < MinPersistMs)
        {
            return HandUiNodeRepairDecision.PersistPending;
        }

        if (lastAttemptMs > 0 && nowMs - lastAttemptMs < RetryCooldownMs)
        {
            return HandUiNodeRepairDecision.Cooldown;
        }

        return HandUiNodeRepairDecision.Repair;
    }

    /// <summary>能不能补（<see cref="Evaluate"/> 的布尔版本，运行时与单测都方便）。</summary>
    public static bool ShouldRepair(
        int missingCount,
        bool handBusy,
        long nowMs,
        long missingSinceMs,
        long lastAttemptMs)
    {
        return Evaluate(missingCount, handBusy, nowMs, missingSinceMs, lastAttemptMs)
            == HandUiNodeRepairDecision.Repair;
    }
}
