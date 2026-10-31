#nullable enable

using System;
using System.Diagnostics;
using System.Text;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Runs;

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
///
/// r327（2026-10-30）补三块**只能测不能猜**的读数（用户报"越到后面越卡 + 开加速反而更卡"）：
///  ① <c>帧时 / process 均 / process 占比</c>：原来只记**窗口最后一帧**的 <c>TimeProcess</c>，
///     最后一帧恰好是空闲帧时会把"帧慢"误判成"渲染慢"（r326 局实测有的窗 process=33ms 而 fps=1.4）；
///     现在逐帧累加 ⇒ 能直接回答"这一秒的帧时间有多少在 process 步里"。
///  ② <c>战斗 / 回合 / 楼层 / 席位</c>：判断"越到后面越卡"是**同局越打越慢**还是**深入楼层本来就更慢**
///     （r326 局读档重开 floor 7 后立刻还是 1fps ⇒ 必须带上楼层才能定性）。
///  ③ <c>资源快照</c>：照抄游戏自带 <c>AutoSlay/MemoryProfiler</c> 的口径（VRAM / 静态内存 / 托管堆 /
///     资源数 / 孤儿节点 / 根视口信号 / 绘制调用 / 图元 / GC 三代计数）。节点数与对象数全程是平的，
///     只能证伪"对象泄漏"；显存与信号连接泄漏、GC 抖动这三种它一个都看不出。
///  ④ <c>加速档</c> + <see cref="NotifyConfigChanged"/> 锚点：下一次复测可以直接在同一局里做干净 A/B。
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

        // ---- r324：把「门-看门狗调度」单次 456ms 的尖峰再拆三段（2026-10-27 12 席局实测）----
        // 该槽的 456ms 里大脑决策只占 134ms（整窗），剩下 445ms 不知道在哪 ⇒ 拆开才能决定砍谁。

        /// <summary>看门狗调度的前置判定（上下文/阶段/回合状态/弹层/遗物/冷却）。</summary>
        WdChecks = 11,

        /// <summary>看门狗内部的**重复**手牌扫描（<c>RunWatchdogAsync</c> 会再算一遍，门里已算过）。</summary>
        WdHandRescan = 12,

        /// <summary>启动出牌循环（<c>TaskHelper.RunSafely(RunWatchdogAsync)</c> 的同步段：决策 + 首张入队）。</summary>
        WdStartLoop = 13,

        /// <summary>
        /// r329：第三方 `LocalContext.IsMe` 放行判定的栈回溯（<c>LocalContextThirdPartyIsMePatch</c>）。
        ///
        /// 它**不在**任何 `_Process` 里，所以 2026-10-30 之前那套"我方只占 1.9%"的读数把它漏掉了；
        /// `dotnet-trace` 采样才把它点出来：主线程 **63.9%** 自耗时在 `StackTrace.CaptureStackTrace`，
        /// 调用方就是这条（figure_Saya 的 `GeneralizedResistancePower`、Koishi 的 `SealAllRelicPatch`
        /// 每次查询都触发）。加这个槽位是为了以后能**直接看它有没有复发**，不必再采样一次。
        /// </summary>
        ThirdPartyIsMe = 14,

        Count = 15,
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
        "门-看门狗-前置判定",
        "门-看门狗-重复手牌扫描",
        "门-看门狗-启动出牌循环",
        "第三方IsMe放行",
    };

    private static readonly long[] TotalTicks = new long[(int)Slot.Count];
    private static readonly long[] MaxTicks = new long[(int)Slot.Count];
    private static readonly int[] CallCounts = new int[(int)Slot.Count];

    // ---- r324：托管出牌的「等泵 / 执行」两段耗时（按张聚合，随窗口一起打一条）----
    // 目的：回答"一回合几十秒到底是排队等动作泵，还是每张牌本身要跑很多帧"。
    // 前者 ⇒ 并发/调度问题；后者 ⇒ 帧率问题（每张牌要 N 帧 × 每帧 ms）。
    // 两条路径分开记：排队（`ActionQueue`，瓦库）与就地（inline `AutoPlay`，战神）——
    // 混在一起平均会让"就地"那些 0 等泵把均值拉低，判读时看不出排队到底占多少。
    private static int _queuedPlayCount;
    private static long _queuedWaitSumMs;
    private static long _queuedWaitMaxMs;
    private static long _queuedExecSumMs;
    private static long _queuedExecMaxMs;
    private static int _inlinePlayCount;
    private static long _inlineExecSumMs;
    private static long _inlineExecMaxMs;

    private static long _windowStartMs;
    private static int _frames;
    private static double _fpsSum;
    private static int _fpsSamples;

    // ---- r328：自测帧间隔分布（不依赖 Godot 监视器；见 TickFrames 注释） ----
    /// <summary>超过这个毫秒数的帧记为"长帧"（30fps 的一帧是 33ms，200ms 已经是明显卡顿）。</summary>
    private const long LongFrameMs = 200L;

    private static long _lastTickMs;
    private static long _frameSumMs;
    private static long _frameMaxMs;
    private static int _longFrames;

    // ---- 托管堆侧读数（每窗口取一次差值：GC 暂停总时长 / 分配总量） ----
    private static double _lastGcPauseMs = -1.0;
    private static long _lastGcAllocBytes = -1L;

    /// <summary>
    /// r327 资源快照（照抄游戏 `AutoSlay/MemoryProfiler` 的口径，只留能定性"越打越慢"的那几项）。
    /// **基线随进程保留**（不随窗口/换战斗清），这样"相对首窗的增量"就是整场会话的累积量。
    /// </summary>
    private struct ResourceSnapshot
    {
        internal double VramMb;
        internal double StaticMb;
        internal double GcMb;
        internal int Resources;
        internal int Orphans;
        internal int RootSignals;
        internal int DrawCalls;
        internal int Primitives;
        internal int Gc0;
        internal int Gc1;
        internal int Gc2;
    }

    private static ResourceSnapshot? _baselineResources;

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

    /// <summary>
    /// 记一张**托管出牌**的两段耗时（毫秒）。
    ///
    /// <paramref name="waitMs"/> = 入队 → 真正开始执行（在动作队列里排队，排在前面的牌越慢这里越大）；
    /// <paramref name="execMs"/> = 开始执行 → 出牌完成（这段就是"这张牌要跑多少帧"的真实成本）。
    /// <paramref name="queued"/> = 走动作队列（瓦库）还是就地 inline（战神）；两条分开统计。
    ///
    /// 只在窗口末尾汇总成一条，不逐张打日志（逐张那条已经带 `等泵/执行`，见
    /// <c>瓦库出牌走动作队列完成</c> 与 <c>瓦库出牌完成（就地）</c>）。
    /// </summary>
    internal static void RecordHostedPlay(long waitMs, long execMs, bool queued)
    {
        if (waitMs < 0)
        {
            waitMs = 0;
        }

        if (execMs < 0)
        {
            execMs = 0;
        }

        if (queued)
        {
            _queuedPlayCount++;
            _queuedWaitSumMs += waitMs;
            _queuedExecSumMs += execMs;
            if (waitMs > _queuedWaitMaxMs)
            {
                _queuedWaitMaxMs = waitMs;
            }

            if (execMs > _queuedExecMaxMs)
            {
                _queuedExecMaxMs = execMs;
            }

            return;
        }

        _inlinePlayCount++;
        _inlineExecSumMs += execMs;
        if (execMs > _inlineExecMaxMs)
        {
            _inlineExecMaxMs = execMs;
        }
    }

    /// <summary>
    /// 分段计时器：一个方法里有多个早退分支（每分支都要 <see cref="End"/> 一次）时用它。
    /// 用法 <c>using var phase = new PhaseScope(Slot.A); ... phase.Switch(Slot.B); ...</c>，
    /// 出作用域自动结算最后一段 —— 早退分支不会漏记，也不用在每个 <c>return</c> 前手写一次。
    ///
    /// ⚠ 必须是 <c>struct</c> + 局部变量（每次 <c>Switch</c> 改的是自己那份状态），
    /// 别存成字段/装箱 ⇒ 那样每次调用都分配。
    /// </summary>
    internal struct PhaseScope : IDisposable
    {
        private Slot _slot;
        private long _start;

        internal PhaseScope(Slot slot)
        {
            _slot = slot;
            _start = Begin();
        }

        /// <summary>结束当前段、开始下一段（两段各自累计，不重叠）。</summary>
        internal void Switch(Slot slot)
        {
            if (_start == 0L)
            {
                return;
            }

            End(_slot, _start);
            _slot = slot;
            _start = Begin();
        }

        public void Dispose()
        {
            if (_start == 0L)
            {
                return;
            }

            End(_slot, _start);
            _start = 0L;
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

        // r328：**自测**帧间隔（毫秒）—— 原来用 `Performance.Monitor.TimeProcess` 折算占比，
        // 2026-10-30 实测该读数会给出"process 均值 > 帧时"（占比 119%/317%）这种物理上不可能的值
        // ⇒ 这个 build 里它不可信，占比结论一律作废。改成用我们自己的调用间隔量帧时长分布。
        long nowMs = (long)Time.GetTicksMsec();
        if (_lastTickMs != 0L)
        {
            long frameDelta = nowMs - _lastTickMs;
            _frameSumMs += frameDelta;
            if (frameDelta > _frameMaxMs)
            {
                _frameMaxMs = frameDelta;
            }

            if (frameDelta >= LongFrameMs && _longFrames < int.MaxValue)
            {
                _longFrames++;
            }
        }

        _lastTickMs = nowMs;

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

            if (_queuedPlayCount > 0)
            {
                parts.Append(parts.Length > 0 ? ", " : string.Empty);
                parts.Append("托管出牌=排队").Append(_queuedPlayCount).Append("张(等泵均=")
                    .Append((_queuedWaitSumMs / (double)_queuedPlayCount).ToString("0"))
                    .Append("ms/最大=").Append(_queuedWaitMaxMs)
                    .Append("ms, 执行均=")
                    .Append((_queuedExecSumMs / (double)_queuedPlayCount).ToString("0"))
                    .Append("ms/最大=").Append(_queuedExecMaxMs)
                    .Append("ms)");
            }

            if (_inlinePlayCount > 0)
            {
                parts.Append(parts.Length > 0 ? ", " : string.Empty);
                parts.Append("就地出牌=").Append(_inlinePlayCount).Append("张(执行均=")
                    .Append((_inlineExecSumMs / (double)_inlinePlayCount).ToString("0"))
                    .Append("ms/最大=").Append(_inlineExecMaxMs)
                    .Append("ms)");
            }

            // 引擎侧同窗口读数：单看"我们的逐帧开销"不够 —— 若它很小而 process 很大，
            // 说明卡顿不在我们的逐帧回调里（渲染 / 第三方 / 节点规模），判读时能直接排除一大片。
            double engineProcessMs = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
            double nodeCount = Performance.GetMonitor(Performance.Monitor.ObjectNodeCount);
            double objectCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);

            // r328：帧时长分布（自测）+ 托管堆侧读数；不再用不可信的 `TimeProcess` 折算占比。
            double frameMs = _frames > 0 ? (double)_frameSumMs / _frames : 0.0;

            LocalMultiControlLogger.Info(
                $"[性能探针] 窗口={elapsedMs}ms, fps均={fpsAverage:0.0}, 引擎process={engineProcessMs:0.00}ms/帧, "
                + $"节点={nodeCount:0}, 对象={objectCount:0}, 帧={_frames}"
                + DescribeFrameTiming(elapsedMs, frameMs)
                + DescribeProgress()
                + DescribeResources()
                + (parts.Length > 0 ? $", {parts}" : ", 无逐帧开销记录"));
        }
        catch (Exception exception)
        {
            // 探针绝不影响战斗：出问题只记一条，然后照常清窗口。
            LocalMultiControlLogger.Warn($"[性能探针] 汇总失败（已忽略）: {exception.Message}");
        }
        finally
        {
            ClearWindow();
            _windowStartMs = nowMs;
        }
    }

    /// <summary>
    /// r328：帧时长分布（自测调用间隔）+ 托管堆侧读数（GC 暂停总时长 / 分配速率）。
    ///
    /// 为什么这两件事必须放一起：2026-10-30 实机（12 席、floor 8）测到"有托管出牌在飞"时
    /// fps 从 **33.8 掉到 1.4~3.3**，而我方逐帧槽位只占 1%~2% ⇒ 剩下 98% 只能在这几类里：
    /// ① GC 抖动（分配速率高 ⇒ 每次 gen0 都打断主线程）；② 引擎/第三方逻辑；③ 渲染。
    /// 有 `GC暂停` 与 `分配` 两个数就能先把 ① 一次问死，不必再猜。
    /// </summary>
    private static string DescribeFrameTiming(long elapsedMs, double frameAvgMs)
    {
        string text = $", 帧时=均{frameAvgMs:0}ms/最坏{_frameMaxMs}ms(>200ms:{_longFrames}帧)";

        try
        {
            double pauseMs = GC.GetTotalPauseDuration().TotalMilliseconds;
            long allocBytes = GC.GetTotalAllocatedBytes(precise: false);
            if (_lastGcPauseMs >= 0.0 && _lastGcAllocBytes >= 0L)
            {
                double pauseDelta = Math.Max(0.0, pauseMs - _lastGcPauseMs);
                double allocMb = Math.Max(0L, allocBytes - _lastGcAllocBytes) / 1048576.0;
                double seconds = Math.Max(0.001, elapsedMs / 1000.0);
                text += $", GC暂停={pauseDelta:0}ms(占{pauseDelta / Math.Max(1L, elapsedMs) * 100.0:0}%)"
                        + $", 分配={allocMb:0.0}MB(={allocMb / seconds:0.0}MB/s)";
            }

            _lastGcPauseMs = pauseMs;
            _lastGcAllocBytes = allocBytes;
        }
        catch (Exception)
        {
            // 运行时没有这两个 API 时静默跳过（本仓 net9.0 有，兜底防未来降级）。
        }

        return text;
    }

    /// <summary>
    /// r327：本窗口处在**哪一局/哪一层/哪一场/第几回合**。
    /// 没有这一组读数就没法区分"同一局越打越慢"和"深入楼层本来就更慢"（r326 局读档重开 floor 7 后立刻仍是 1fps）。
    /// </summary>
    private static string DescribeProgress()
    {
        int combatId = -1;
        int round = -1;
        int floor = -1;
        int seats = -1;
        try
        {
            combatId = CombatManager.Instance?.CurrentCombatId?.Value ?? -1;
            round = CombatManager.Instance?.DebugOnlyGetState()?.RoundNumber ?? -1;
            RunState? state = RunManager.Instance?.DebugOnlyGetState();
            if (state != null)
            {
                floor = state.TotalFloor;
                seats = state.Players.Count;
            }
        }
        catch (Exception)
        {
            // 读数失败按未知处理，绝不影响战斗。
        }

        return $", 战斗={combatId}, 回合={round}, 楼层={floor}, 席位={seats}"
            + $", 加速档={(LocalWakuuAutopilotConfig.FastWakuuPlay ? "开" : "关")}";
    }

    /// <summary>
    /// r327：泄漏面读数。节点/对象数全程是平的，只能证伪"对象泄漏"；
    /// 显存（贴图材质越攒越多 ⇒ 渲染变慢）、托管堆（GC 抖动）、根视口信号连接（每帧回调越挂越多）
    /// 这三种必须单独看。基线取**本进程第一个窗口**，括号里是相对基线的增量。
    /// </summary>
    private static string DescribeResources()
    {
        ResourceSnapshot current = CaptureResources();
        _baselineResources ??= current;
        ResourceSnapshot baseline = _baselineResources.Value;

        return $", 资源[VRAM={current.VramMb:0}MB({DiffInt(current.VramMb, baseline.VramMb)}), "
            + $"静态={current.StaticMb:0}MB({DiffInt(current.StaticMb, baseline.StaticMb)}), "
            + $"托管堆={current.GcMb:0}MB({DiffInt(current.GcMb, baseline.GcMb)}), "
            + $"资源数={current.Resources}({DiffInt(current.Resources, baseline.Resources)}), "
            + $"孤儿={current.Orphans}({DiffInt(current.Orphans, baseline.Orphans)}), "
            + $"根信号={current.RootSignals}, 绘制={current.DrawCalls}, 图元={current.Primitives}, "
            + $"GC={current.Gc0}/{current.Gc1}/{current.Gc2}]";
    }

    private static ResourceSnapshot CaptureResources()
    {
        int rootSignals = -1;
        try
        {
            if (Engine.GetMainLoop() is SceneTree tree && tree.Root != null)
            {
                rootSignals = tree.Root.GetSignalConnectionList(Viewport.SignalName.SizeChanged).Count;
            }
        }
        catch (Exception)
        {
            rootSignals = -1;
        }

        ResourceSnapshot snapshot = new()
        {
            RootSignals = rootSignals,
            Resources = (int)Performance.GetMonitor(Performance.Monitor.ObjectResourceCount),
            Orphans = (int)Performance.GetMonitor(Performance.Monitor.ObjectOrphanNodeCount),
            DrawCalls = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame),
            Primitives = (int)Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame),
            Gc0 = GC.CollectionCount(0),
            Gc1 = GC.CollectionCount(1),
            Gc2 = GC.CollectionCount(2),
        };

        try
        {
            snapshot.VramMb = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0;
            snapshot.StaticMb = OS.GetStaticMemoryUsage() / 1048576.0;
            snapshot.GcMb = GC.GetTotalMemory(forceFullCollection: false) / 1048576.0;
        }
        catch (Exception)
        {
            // 个别读数在某平台上缺失时留 0，不影响其它项。
        }

        return snapshot;
    }

    private static string DiffInt(double current, double baseline)
    {
        double delta = current - baseline;
        return delta >= 0 ? $"+{delta:0.#}" : $"{delta:0.#}";
    }

    /// <summary>
    /// r327：配置档（尤其「瓦库出牌加速」）切换时打一条锚点并**复位窗口**，
    /// 让切换后第一个窗口是干净的同局 A/B 数据（否则切换点落在窗口中间，两边都掺着）。
    /// </summary>
    internal static void NotifyConfigChanged(string detail)
    {
        LocalMultiControlLogger.Info($"[性能探针] 配置档切换（性能窗口复位，下一窗口为切换后数据）: {detail}");
        Reset("config-changed");
    }

    /// <summary>退局 / 换战斗复位（由运行时清理链调用，避免把上一场的窗口带过去）。</summary>
    internal static void Reset(string source)
    {
        ClearWindow();
        _windowStartMs = 0L;
        _ = source;
    }

    private static void ClearWindow()
    {
        Array.Clear(TotalTicks);
        Array.Clear(MaxTicks);
        Array.Clear(CallCounts);
        _queuedPlayCount = 0;
        _queuedWaitSumMs = 0L;
        _queuedWaitMaxMs = 0L;
        _queuedExecSumMs = 0L;
        _queuedExecMaxMs = 0L;
        _inlinePlayCount = 0;
        _inlineExecSumMs = 0L;
        _inlineExecMaxMs = 0L;
        _frames = 0;
        _fpsSum = 0;
        _fpsSamples = 0;
        _lastTickMs = 0L;
        _frameSumMs = 0L;
        _frameMaxMs = 0L;
        _longFrames = 0;
    }
}
