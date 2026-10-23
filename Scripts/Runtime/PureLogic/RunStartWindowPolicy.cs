namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「本地回环大厅已经发起开局」的窗口判定（r317）。
///
/// 为什么需要：会话守卫（r158/r161）r317 起对角色选择页改用严格判据 ——
/// 「页面可见 **且** 页面上挂的大厅还是我们建的回环服务」才算"仍在流程中"。
/// 但开局那一刻页面会 `FadeOut(0.8s)`、随后可能被隐藏，而 `RunManager.IsInProgress` 还要等到
/// `RunManager.Launch` 之后才成立；这中间的窗口里守卫会把会话清掉 ⇒ 进局后 `IsEnabled` 为 false
/// （托管遗物 / 归属守卫全失效，等价 r166 那次事故）。
///
/// 修法同 <see cref="LoadReplayWindowPolicy"/>：由 `StartRunLobby.BeginRunForAllPlayers`（**只在
/// `NetService is LocalLoopbackHostGameService` 时**）开一个带超时的窗口，如实机日志里的
/// 「检测到本地回环 Lobby 开始开局」那一刻。窗口只在**我们自己的**开局上打开，所以官方单人页
/// 永远不会被它挡（那正是 r317 要修的 bug）。
///
/// 纯函数化只为可单测（超时是安全阀：开局被取消时守卫仍能收拾残留会话）。
/// </summary>
internal static class RunStartWindowPolicy
{
    /// <summary>窗口上限（毫秒）。开局正常 1~3 秒内就 `RunManager.Launch` 了，留足余量即可。</summary>
    public const long DefaultTimeoutMs = 120_000;

    /// <summary>
    /// 窗口是否仍在有效期内。<paramref name="openedAtMs"/> ≤ 0 表示窗口从未开启。
    /// 时间用单调时钟（<c>Environment.TickCount64</c>），不受系统时间跳变影响。
    /// </summary>
    public static bool IsActive(long openedAtMs, long nowMs, long timeoutMs = DefaultTimeoutMs)
    {
        if (openedAtMs <= 0 || timeoutMs <= 0)
        {
            return false;
        }

        long elapsed = nowMs - openedAtMs;
        return elapsed >= 0 && elapsed < timeoutMs;
    }
}
