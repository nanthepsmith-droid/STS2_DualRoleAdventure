using System;
using System.Collections.Generic;
using System.Threading;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Logging;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// 被跟踪的预加载会话台账：记录每个会话的「首次观察 / 最近一次队列计数变化 / 有没有人真的在等它 /
/// 有没有真的被驱动过」，由 <see cref="PreloadStallWatchdog"/> 每
/// <see cref="PreloadStallPolicy.PollIntervalMs"/> 毫秒轮询一次。
///
/// 三个登记入口（覆盖不同第三方写法）：
/// <list type="number">
/// <item><c>AssetCache.CreateSession</c> —— 会话一出生就登记，并抓「谁创建的」栈；</item>
/// <item><c>NAssetLoader.LoadInTheBackground</c> —— 走了原版排队链路的；</item>
/// <item><c>AssetLoadingSession.WaitForCompletion</c> / <c>.Task</c> —— **真正有人在等它**时登记，
/// 这也是抓「谁在等」栈的地方（第三方可能自己 <c>Process()</c> 而不走 NAssetLoader，只靠 ② 会漏）。</item>
/// </list>
///
/// 为什么不由 <c>NAssetLoader._Process</c> 驱动轮询：本次要救的场景恰恰是「<c>_Process</c> 没跑」
/// （队列连一帧都没被处理）。挂在它上面等于和病人一起躺下，所以轮询放在我们自己的常驻节点里。
/// </summary>
internal static class PreloadStallRegistry
{
    private sealed class TrackedSession
    {
        internal long FirstSeenMs;
        internal long LastProgressSignature;
        internal long LastProgressChangedMs;
        internal long LastObservationLogMs;
        internal bool Reported;
        internal bool Driven;
        internal string? WaiterStack;
        internal string? CreateStack;
    }

    private static readonly object Gate = new object();
    private static readonly Dictionary<AssetLoadingSession, TrackedSession> Tracked = new Dictionary<AssetLoadingSession, TrackedSession>();
    private static int _trackedTotal;
    private static int _stalledTotal;

    /// <summary>跨线程安全的单调毫秒（与 <see cref="PreloadStallWatchdog"/> 同源）。</summary>
    internal static long NowMs => Environment.TickCount64;

    /// <summary>累计跟踪过的会话数（自检/日志用）。</summary>
    internal static int TrackedTotal => Volatile.Read(ref _trackedTotal);

    /// <summary>累计判定停滞并处理过的会话数（自检/日志用）。</summary>
    internal static int StalledTotal => Volatile.Read(ref _stalledTotal);

    /// <summary>当前仍在跟踪中的会话数（自检/日志用）。</summary>
    internal static int PendingCount
    {
        get
        {
            lock (Gate)
            {
                return Tracked.Count;
            }
        }
    }

    /// <summary>会话刚被创建（<c>AssetCache.CreateSession</c> 后缀）：登记 + 抓创建者栈。</summary>
    internal static void NoteCreated(AssetLoadingSession session)
    {
        if (session == null)
        {
            return;
        }

        lock (Gate)
        {
            TrackedSession entry = EnsureTrackedLocked(session);
            if (entry.CreateStack == null)
            {
                entry.CreateStack = PreloadStallInspector.DescribeCurrentStack();
                if (PreloadStallLog.DiagnosticsEnabled)
                {
                    Log.Info($"[PreloadStallGuard] 预加载会话创建: {PreloadStallInspector.Describe(session)}, 创建者={entry.CreateStack}");
                }
            }
        }

        PreloadStallWatchdog.EnsureAttached();
    }

    /// <summary>会话入队（<c>NAssetLoader.LoadInTheBackground</c> 后缀）：登记。</summary>
    internal static void Track(AssetLoadingSession session, long nowMs)
    {
        if (session == null)
        {
            return;
        }

        lock (Gate)
        {
            EnsureTrackedLocked(session);
        }

        PreloadStallWatchdog.EnsureAttached();
    }

    /// <summary>
    /// 有人在等这个会话（<c>WaitForCompletion</c> 前缀 / <c>.Task</c> 后缀）：登记 + **抓等待者栈**。
    /// 每个会话只抓一次，避免刷屏。
    /// </summary>
    internal static void NoteWaiter(AssetLoadingSession session, string how)
    {
        if (session == null)
        {
            return;
        }

        lock (Gate)
        {
            TrackedSession entry = EnsureTrackedLocked(session);
            if (entry.WaiterStack == null)
            {
                entry.WaiterStack = $"{how} <- {PreloadStallInspector.DescribeCurrentStack()}";
                Log.Info($"[PreloadStallGuard] 捕获等待者: {PreloadStallInspector.Describe(session)}, via={how}, 栈={entry.WaiterStack}");
            }
        }

        PreloadStallWatchdog.EnsureAttached();
    }

    /// <summary>会话被原版（或第三方）真的驱动过一帧（<c>AssetLoadingSession.Process</c> 前缀）。</summary>
    internal static void NoteDriven(AssetLoadingSession session)
    {
        if (session == null)
        {
            return;
        }

        lock (Gate)
        {
            if (Tracked.TryGetValue(session, out TrackedSession? entry))
            {
                entry.Driven = true;
            }
        }
    }

    /// <summary>轮询一次：更新进度、打「观察中」、摘掉已完成的、把停滞的处理掉。</summary>
    internal static void Poll(long nowMs)
    {
        List<(AssetLoadingSession Session, long StalledMs)> stalled = new();
        List<AssetLoadingSession> finished = new();

        lock (Gate)
        {
            if (Tracked.Count == 0)
            {
                return;
            }

            foreach (KeyValuePair<AssetLoadingSession, TrackedSession> pair in Tracked)
            {
                AssetLoadingSession session = pair.Key;
                TrackedSession entry = pair.Value;

                if (session.IsCompleted)
                {
                    finished.Add(session);
                    continue;
                }

                long signature = PreloadStallInspector.ProgressSignature(session);
                if (signature != entry.LastProgressSignature)
                {
                    entry.LastProgressSignature = signature;
                    entry.LastProgressChangedMs = nowMs;
                    continue;
                }

                if (entry.Reported)
                {
                    continue;
                }

                long pendingMs = nowMs - entry.FirstSeenMs;

                // 「观察中」：让日志能自证补丁在工作（也回答「用户到底等了多久」）。
                if (PreloadStallLog.DiagnosticsEnabled
                    && pendingMs >= PreloadStallPolicy.ObservationLogIntervalMs
                    && nowMs - entry.LastObservationLogMs >= PreloadStallPolicy.ObservationLogIntervalMs)
                {
                    entry.LastObservationLogMs = nowMs;
                    Log.Warn($"[PreloadStallGuard] 观察中: {PreloadStallInspector.Describe(session)}, 已 {pendingMs}ms 未完成, "
                        + $"driven={entry.Driven}, force@{PreloadStallPolicy.DefaultStallTimeoutMs}ms");
                }

                if (PreloadStallPolicy.IsStalled(
                        nowMs,
                        entry.FirstSeenMs,
                        entry.LastProgressChangedMs,
                        completed: false,
                        PreloadStallPolicy.DefaultStallTimeoutMs))
                {
                    entry.Reported = true;
                    stalled.Add((session, pendingMs));
                }
            }

            foreach (AssetLoadingSession session in finished)
            {
                Tracked.Remove(session);
            }
        }

        foreach ((AssetLoadingSession session, long stalledMs) in stalled)
        {
            Interlocked.Increment(ref _stalledTotal);
            Release(session, stalledMs);
        }
    }

    private static TrackedSession EnsureTrackedLocked(AssetLoadingSession session)
    {
        if (Tracked.TryGetValue(session, out TrackedSession? entry))
        {
            return entry;
        }

        entry = new TrackedSession
        {
            FirstSeenMs = NowMs,
            LastProgressSignature = PreloadStallInspector.ProgressSignature(session),
            LastProgressChangedMs = NowMs
        };
        Tracked[session] = entry;
        Interlocked.Increment(ref _trackedTotal);
        return entry;
    }

    private static void Release(AssetLoadingSession session, long stalledMs)
    {
        string detail = PreloadStallInspector.Describe(session);
        string waiter;
        string creator;
        bool driven;

        lock (Gate)
        {
            Tracked.TryGetValue(session, out TrackedSession? entry);
            waiter = entry?.WaiterStack ?? "未捕获";
            creator = entry?.CreateStack ?? "未捕获";
            driven = entry?.Driven ?? false;
        }

        if (!PreloadStallPolicy.ForceReleaseEnabled)
        {
            Log.Error($"[PreloadStallGuard] 资源预加载会话停滞 {stalledMs}ms（report 模式，未干预）: {detail}, driven={driven}; "
                + $"等待者={waiter}; 创建者={creator}。去掉环境变量 STS2_PRELOAD_STALL_GUARD=report 可启用强制放行。");
            return;
        }

        bool released = PreloadStallInspector.TryForceComplete(session);
        Log.Error($"[PreloadStallGuard] 资源预加载会话停滞 {stalledMs}ms，已强制放行以免流程永久挂起"
            + $"（该批资源可能未预加载，后续按原版未缓存同步加载兜底）: {detail}, driven={driven}, released={released}, "
            + $"累计停滞={StalledTotal}, 在跟踪={PendingCount}; 等待者={waiter}; 创建者={creator}");
    }
}
