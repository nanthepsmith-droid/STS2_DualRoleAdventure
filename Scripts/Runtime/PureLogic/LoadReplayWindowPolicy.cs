namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「读档窗口」的纯判定（r167 修 BUG-22 第二颗雷）。
///
/// 为什么需要：会话守卫（r158/r161）按「未进局 + 没有我们的大厅页」判断是否清会话。
/// 而**读档流程**恰好长这样：`RunManager.IsInProgress` 还是 false，载入界面又不是大厅白名单里的页面
/// ⇒ 约 1 秒后守卫就把会话 `Disable` 掉。后果（r166 日志实证，5 次读档 5 次复现）：
/// `GrantWakuuRelicsAsync` 首行 `if (!IsEnabled) return;` ⇒ 托管遗物不发 ⇒ 瓦库整局不出牌 / 不自动选事件；
/// 同时所有门控在 `IsEnabled` 上的归属守卫一起失效 ⇒ 事件卡牌奖励归属断档、点的人与奖励主人不匹配 ⇒ 软锁。
/// 修法：读档期间开一个**有超时的窗口**，守卫在此期间不下手。
///
/// 纯函数化只为可单测（窗口过期是"安全阀"：万一读档被取消，守卫照旧能在超时后收拾残留会话）。
/// </summary>
internal static class LoadReplayWindowPolicy
{
    /// <summary>窗口上限（毫秒）：超过就当作一般情形，让守卫恢复职责（防"读档取消后会话残留"回归 r158）。</summary>
    public const long DefaultTimeoutMs = 180_000;

    /// <summary>
    /// 窗口是否仍在有效期内。<paramref name="openedAtMs"/> ≤ 0 表示窗口从未开启。
    /// 时间用单调时钟（<c>Environment.TickCount64</c>），不受系统时间跳变影响。
    /// </summary>
    public static bool IsActive(long openedAtMs, long nowMs, long timeoutMs = DefaultTimeoutMs)
    {
        if (openedAtMs <= 0)
        {
            return false;
        }

        if (timeoutMs <= 0)
        {
            return false;
        }

        long elapsed = nowMs - openedAtMs;
        return elapsed >= 0 && elapsed < timeoutMs;
    }
}
