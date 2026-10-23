using System;
using System.Threading;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// r313：启动心跳 —— 日志**没有行时间戳**，导致「两个阶段之间到底花了多久」只能靠猜。
///
/// 做法：mod 初始化时装一个 5 秒一次的定时器，每拍打一行
/// <c>[StartupProf] heartbeat n=… since_process_start_ms=…</c>；
/// 日志顺序 = 时间顺序 ⇒ 只要看某段日志前后夹着哪几拍，就知道那段花了多久。
///
/// 边界（刻意保守）：
/// - 只打 <see cref="MaxTicks"/> 拍（默认 36 拍 ≈ 3 分钟）后自动停，不长期污染日志；
/// - 回调里任何异常都吞掉（诊断绝不允许影响游戏）；
/// - 线程：回调在计时器线程上打日志 —— <c>Log.Info</c> 是引擎的 print，可跨线程，且整段 try/catch。
/// </summary>
internal static class StartupHeartbeat
{
    private const int DefaultIntervalMs = 5000;
    private const int DefaultMaxTicks = 36;

    private static Timer? _timer;
    private static int _ticks;
    private static bool _started;

    internal static void Start(int intervalMs = DefaultIntervalMs, int maxTicks = DefaultMaxTicks)
    {
        if (_started)
        {
            return;
        }

        try
        {
            _started = true;
            _timer = new Timer(_ => Tick(maxTicks), null, intervalMs, intervalMs);
            LocalMultiControlLogger.Info(
                $"[StartupProf] 心跳已启动（每 {intervalMs / 1000}s 一拍，最多 {maxTicks} 拍）");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[StartupProf] 心跳启动失败（不影响加载）: {exception.Message}");
        }
    }

    private static void Tick(int maxTicks)
    {
        try
        {
            int n = Interlocked.Increment(ref _ticks);
            if (n > maxTicks)
            {
                _timer?.Dispose();
                _timer = null;
                return;
            }

            LocalMultiControlLogger.Info(
                $"[StartupProf] heartbeat n={n} since_process_start_ms={StartupProfileProbe.SinceProcessStartMs():F0}");
        }
        catch
        {
            // 心跳失败一律吞掉
        }
    }
}
