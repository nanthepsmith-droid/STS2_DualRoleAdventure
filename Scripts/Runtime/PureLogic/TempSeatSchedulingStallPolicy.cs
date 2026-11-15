namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 临时席位（战神）「托管调度一直被挡」的兜底判定（r358，纯逻辑）。
///
/// **实机事故（2026-11-15）**：一场战斗打到第 2 回合，真人点「结束回合」毫无反应。日志：
/// <code>玩家点结束回合 → 检测到全员无牌可出，已自动结束全部角色回合 round=2（只结束了 2 个真人席位）
/// 瓦库看门狗调度统计: rejected=selector-scope-busy:19, lastPlayer=4096, selectorStackCount=0</code>
/// 真凶两层：
/// <list type="number">
///   <item>选择器作用域计数泄漏（见 <see cref="SelectorScopeLeaseBook"/>）⇒ 战神这一档（非并发档）
///   的看门狗**每次都被挡回**；</item>
///   <item>战神手上有"托管能打"的牌 ⇒ 空闲收口（`TryEndIdleWarGodTurns`）不敢替它 ready ⇒
///   它这一席永远不 ready ⇒ `PlayersReadyToEndTurn.Count` 永远少一个 ⇒ 回合链停住。</item>
/// </list>
///
/// 本类只回答两个问题：**哪种拒绝算"卡住"**、**卡了多久该强制收口**。
/// 「卡住」只认"别人占着、等下去也不会自己好"的两种拒绝：
/// <list type="bullet">
///   <item>`selector-scope-busy`：有作用域在飞（并发档/串行档的闸门）；</item>
///   <item>`watchdog-in-flight`：这一席自己的出牌循环还挂着。</item>
/// </list>
/// 其它拒绝（`no-playable-cards` / `player-ready-to-end-turn` / `overlay-open` …）都有明确的别的收口路径，
/// 计进来只会在正常回合里误杀战神的出牌权。
/// </summary>
internal static class TempSeatSchedulingStallPolicy
{
    /// <summary>该拒绝原因是否表示"等下去也不会自己好"。</summary>
    public static bool IsBlockingRejection(string? reason)
    {
        return reason switch
        {
            "selector-scope-busy" => true,
            "watchdog-in-flight" => true,
            _ => false,
        };
    }

    /// <summary>
    /// 这一席是否已经被挡太久（该兜底收口）。<paramref name="blockedSinceMs"/> ≤ 0 表示还没开始计时。
    /// </summary>
    public static bool ShouldForceEnd(long blockedSinceMs, long nowMs, long graceMs)
    {
        if (blockedSinceMs <= 0L || graceMs <= 0L || nowMs < blockedSinceMs)
        {
            return false;
        }

        return nowMs - blockedSinceMs >= graceMs;
    }
}
