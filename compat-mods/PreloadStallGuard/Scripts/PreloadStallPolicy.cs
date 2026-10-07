using System;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// 「预加载会话是否已停滞」的纯判定（不碰 Godot 与游戏类型，便于阅读与单测）。
///
/// 为什么用「无进展」而不是单纯「超时」：原版大会话是正常的慢工 —— 实测 'Common'
/// （810 资源 + 94 个 vfx 场景）耗时 1831~5623ms，'Combat Room' 100~200ms。
/// 单纯卡一个固定阈值容易误伤；真正的病灶是「未完成且队列计数长时间一点不动」：
///   · NAssetLoader._Process 没被驱动（会话连一帧都没跑）；
///   · 或某个资源在 Godot 线程加载器里永久停在 InProgress（_loading 计数不再变化）。
/// 两种情况下 <c>_toLoad + _loading + _finalizing + _vfxScenes</c> 都是恒定的。
/// </summary>
internal static class PreloadStallPolicy
{
    /// <summary>
    /// 默认停滞判定阈值（毫秒）。取值口径：必须显著大于原版最慢的**正常**会话
    /// （实测约 5.6s，且那类会话的队列计数一直在变、不会被判停滞），
    /// 同时短到用户还能忍受等它自愈。
    /// </summary>
    internal const long DefaultStallTimeoutMs = 5000L;

    /// <summary>轮询间隔（毫秒）。</summary>
    internal const long PollIntervalMs = 500L;

    /// <summary>「观察中」日志的最小间隔（毫秒）——让日志自证补丁在工作，又不至于刷屏。</summary>
    internal const long ObservationLogIntervalMs = 2000L;

    /// <summary>
    /// 判定一次：会话「未完成」且「距首次观察」与「距上次队列计数变化」都超过阈值时算停滞。
    /// </summary>
    internal static bool IsStalled(
        long nowMs,
        long firstSeenMs,
        long lastProgressMs,
        bool completed,
        long timeoutMs)
    {
        if (completed)
        {
            return false;
        }

        if (timeoutMs <= 0L)
        {
            timeoutMs = DefaultStallTimeoutMs;
        }

        return nowMs - firstSeenMs >= timeoutMs && nowMs - lastProgressMs >= timeoutMs;
    }

    /// <summary>
    /// 是否强制放行：默认是；环境变量 <c>STS2_PRELOAD_STALL_GUARD=report</c> 时只报告不干预。
    /// </summary>
    internal static bool ForceReleaseEnabled => !string.Equals(PreloadStallLog.Mode, "report", StringComparison.OrdinalIgnoreCase);
}
