#nullable enable

using System;
using System.Diagnostics;
using System.Text;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「战斗内逐帧开销」探针（r282，2026-10-17 用户报「战神一多就会很卡」时的**先量后猜**工具）。
///
/// 为什么需要它：逐帧回调里有好几段"兜底/自愈"逻辑（切人按钮刷新、结束回合门、按钮对账、手牌对账、
/// 立绘探针），它们在**战神多开**时都要按席位/手牌规模放大，但帧率掉下来时**光看日志分辨不出是谁**。
/// 之前的做法只能靠读代码猜"哪个像慢的"，得出的是估计而不是测量。
///
/// 做法：在每个逐帧调用点外面套一对 <see cref="Begin"/> / <see cref="End"/>（只读时间戳，零分配），
/// 每 <see cref="ReportIntervalMs"/> 打**一条** INFO，把"每秒/每帧花在各个槽位多少毫秒 + 最大单帧"列出来，
/// 帧率取 <c>Engine.GetFramesPerSecond</c> 的窗口均值。
///
/// ⚠ 只在战斗内逐帧跟踪器里驱动（<c>LocalCombatSwitchTracker</c>），非战斗期不积累、不打日志；
/// 正常战斗每 5s 一条，判读时按窗口对比"战神 0 个 vs 10 个"即可定位。
/// </summary>
internal static class LocalPerfProbe
{
    /// <summary>探针槽位（新增槽位时记得同步 <see cref="SlotNames"/> 与数组尺寸）。</summary>
    internal enum Slot
    {
        /// <summary>切人按钮容器刷新（含"找结束回合按钮"这一步）。</summary>
        SwitchRefresh = 0,

        /// <summary>结束回合门 + 托管调度 + 战神收口（<c>TryAutoEndTurnForRelicControlledPlayer</c>）。</summary>
        EndTurnGate = 1,

        /// <summary>立绘站位位移探针（自身 250ms 节流）。</summary>
        PositionProbe = 2,

        /// <summary>结束回合按钮状态自愈对账。</summary>
        EndTurnReconcile = 3,

        /// <summary>手牌 UI 顺序对账（自身 250ms 节流）。</summary>
        HandOrderReconcile = 4,

        // ---- r284：把"结束回合门"那道 100ms 重活拆成子槽（2026-10-19 实测单次最大 117ms，>20ms 的窗口 8/27）----

        /// <summary>门的前置：R8 联坐检查 / 战神死亡锚点 / 战斗场次跟踪 / 待处理切人。</summary>
        GatePrelude = 5,

        /// <summary>战神空闲收口（<c>TryEndIdleWarGodTurns</c>）。</summary>
        GateIdleWarGods = 6,

        /// <summary>"瓦库全都无牌可出"自动切人判定。</summary>
        GateAutoSwitch = 7,

        /// <summary>逐席位扫手牌判"托管还能打吗"（<c>HasHostedPlayableCards</c> → <c>CanPlay</c> → 钩子遍历）。</summary>
        GateHandScan = 8,

        /// <summary>逐席位调度看门狗（<c>TryScheduleWatchdog</c>）。</summary>
        GateWatchdog = 9,

        /// <summary>
        /// 出牌循环里的**大脑决策**（`brain.TryDecideNext`；scored 档会逐张评分 + 知识层抽样）。
        /// 它是 <see cref="GateWatchdog"/> 尖峰的头号嫌疑：`TryScheduleWatchdog` 会**同步**启动出牌循环，
        /// 首个决策就发生在调度调用内部（2026-10-19 实测该槽单次最大 124.5ms）。
        /// </summary>
        PlayLoopDecide = 10,

        Count = 11,
    }

    private const long ReportIntervalMs = 5000L;

    private static readonly string[] SlotNames =
    {
        "切人按钮刷新",
        "结束回合门",
        "立绘探针",
        "结束按钮对账",
        "手牌顺序对账",
        "门-前置",
        "门-战神收口",
        "门-自动切人",
        "门-手牌扫描",
        "门-看门狗调度",
        "出牌循环-大脑决策",
    };

    private static readonly long[] TotalTicks = new long[(int)Slot.Count];
    private static readonly long[] MaxTicks = new long[(int)Slot.Count];
    private static readonly int[] CallCounts = new int[(int)Slot.Count];

    private static long _windowStartMs;
    private static int _frames;
    private static double _fpsSum;
    private static int _fpsSamples;

    /// <summary>窗口起点（毫秒）：由第一次 <see cref="TickFrames"/> 懒开窗。</summary>
    internal static long Begin() => Stopwatch.GetTimestamp();

    /// <summary>记一次调用的耗时（tick 单位，换算在 <see cref="Report"/> 里做）。</summary>
    internal static void End(Slot slot, long startTimestamp)
    {
        long delta = Stopwatch.GetTimestamp() - startTimestamp;
        int index = (int)slot;
        TotalTicks[index] += delta;
        CallCounts[index]++;
        if (delta > MaxTicks[index])
        {
            MaxTicks[index] = delta;
        }
    }

    /// <summary>每帧调一次（只做累加；到点才打日志）。</summary>
    internal static void TickFrames()
    {
        _frames++;
        double fps = Engine.GetFramesPerSecond();
        if (fps > 0)
        {
            _fpsSum += fps;
            _fpsSamples++;
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (_windowStartMs == 0L)
        {
            _windowStartMs = nowMs;
            return;
        }

        if (nowMs - _windowStartMs < ReportIntervalMs)
        {
            return;
        }

        Report(nowMs);
    }

    private static void Report(long nowMs)
    {
        try
        {
            long elapsedMs = Math.Max(1L, nowMs - _windowStartMs);
            double msPerTick = 1000.0 / Stopwatch.Frequency;
            double fpsAverage = _fpsSamples > 0 ? (double)_fpsSum / _fpsSamples : 0.0;
            double frames = Math.Max(1, _frames);

            StringBuilder parts = new();
            for (int i = 0; i < (int)Slot.Count; i++)
            {
                if (CallCounts[i] == 0)
                {
                    continue;
                }

                double totalMs = TotalTicks[i] * msPerTick;
                double maxMs = MaxTicks[i] * msPerTick;
                parts.Append(parts.Length > 0 ? ", " : string.Empty);
                parts.Append(SlotNames[i])
                    .Append("=").Append(totalMs.ToString("0.0")).Append("ms/")
                    .Append(elapsedMs).Append("ms窗口(每帧").Append((totalMs / frames).ToString("0.000"))
                    .Append("ms, 最大").Append(maxMs.ToString("0.00")).Append("ms, 调用").Append(CallCounts[i])
                    .Append(')');
            }

            // 引擎侧同窗口读数：单看"我们的逐帧开销"不够 —— 若它很小而 process 很大，
            // 说明卡顿不在我们的逐帧回调里（渲染 / 第三方 / 节点规模），判读时能直接排除一大片。
            double engineProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            double nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            double objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);

            LocalMultiControlLogger.Info(
                $"[性能探针] 窗口={elapsedMs}ms, fps均={fpsAverage:0.0}, 引擎process={engineProcessMs:0.00}ms/帧, "
                + $"节点={nodeCount:0}, 对象={objectCount:0}, 帧={_frames}"
                + (parts.Length > 0 ? $", {parts}" : ", 无逐帧开销记录"));
        }
        catch (Exception exception)
        {
            // 探针绝不影响战斗：出问题只记一条，然后照常清窗口。
            LocalMultiControlLogger.Warn($"[性能探针] 汇总失败（已忽略）: {exception.Message}");
        }
        finally
        {
            Array.Clear(TotalTicks);
            Array.Clear(MaxTicks);
            Array.Clear(CallCounts);
            _frames = 0;
            _fpsSum = 0;
            _fpsSamples = 0;
            _windowStartMs = nowMs;
        }
    }

    /// <summary>退局 / 换战斗复位（由运行时清理链调用，避免把上一场的窗口带过去）。</summary>
    internal static void Reset(string source)
    {
        Array.Clear(TotalTicks);
        Array.Clear(MaxTicks);
        Array.Clear(CallCounts);
        _frames = 0;
        _fpsSum = 0;
        _fpsSamples = 0;
        _windowStartMs = 0L;
        _ = source;
    }
}
