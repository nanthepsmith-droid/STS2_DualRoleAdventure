using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「诊断 / 节流」窗口台账（R5-4：把原先散在 <c>LocalMultiControlRuntime</c> 字段区的 9 个静态状态收成一处）。
///
/// **收的是什么**：两个**按 2 秒时间片翻滚**的计数窗口 ——
/// <list type="number">
/// <item><b>瓦库看门狗调度窗口</b>：窗口内"调度成功次数" + 按 `reason` 的"被拒次数"，外加最近一次的
/// 玩家 / 回合 / 来源（只用于日志归因，**翻滚时不清**，只有退局复位才回初值）；</item>
/// <item><b>流程阻塞信号窗口</b>：按 `信号:原因` 的计数 + 可选的"按回合 + 玩家去重"集合
/// （去重集**翻滚时也不清** —— 同一回合同一玩家只该报一次，这是原实现刻意的语义）。</item>
/// </list>
///
/// **为什么值得收**：这两个窗口的规则（懒开窗、2 秒翻滚、翻滚时清什么 / 不清什么、退局才复位）
/// 原来只能靠读那四十多行调用点才看得出来；收成台账后**可单测**，且退局复位只剩一次 <see cref="Reset"/>。
///
/// **不是纯数据搬运**：窗口翻滚判据（<see cref="IsWatchdogWindowDue"/> / <see cref="IsFlowWindowDue"/>）
/// 与键格式（`reason` 兜底 `unknown`、`signal:reason`、去重键 `signal:round:player`）都留在本类里，
/// **日志文案与键格式逐字保持**（`log_scan` 的既有锚点依赖它）。
///
/// **与「纯逻辑」边界的关系**（AGENTS §1 硬规则 2）：本类只碰字符串 / 整数 / 时间戳，不引用 Godot 与游戏类型；
/// 取时间（`Time.GetTicksMsec()`）与打日志仍留在调用方（Runtime），因此窗口时间从参数传入、可被单测驱动。
/// </summary>
internal static class RuntimeDiagnosticsLedger
{
    /// <summary>窗口长度（两张窗口共用；沿原实现的 2000ms 字面量）。</summary>
    internal const long WindowMs = 2000L;

    private static readonly Dictionary<string, int> WatchdogRejects = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> FlowSignals = new Dictionary<string, int>();
    private static readonly HashSet<string> FlowDedupeKeys = new HashSet<string>();

    private static long _watchdogWindowStartMs;
    private static int _watchdogSuccessCount;
    private static ulong _watchdogLastPlayerId;
    private static int _watchdogLastRound = -1;
    private static string _watchdogLastSource = "none";

    private static long _flowWindowStartMs;

    // ── 读（日志与复位自检用）──

    /// <summary>看门狗被拒计数的**条目数**（自检用；键 = reason）。</summary>
    internal static int WatchdogRejectCount => WatchdogRejects.Count;

    /// <summary>看门狗窗口是否已开（初值 0 = 未开）。</summary>
    internal static bool WatchdogWindowStarted => _watchdogWindowStartMs > 0L;

    internal static int WatchdogSuccessCount => _watchdogSuccessCount;

    /// <summary>最近一次调度是否已有具体身份（初值 0 / -1；退局复位后回到初值）。</summary>
    internal static bool WatchdogLastTargetSet => _watchdogLastPlayerId != 0UL || _watchdogLastRound != -1;

    internal static ulong WatchdogLastPlayerId => _watchdogLastPlayerId;

    internal static int WatchdogLastRound => _watchdogLastRound;

    internal static string WatchdogLastSource => _watchdogLastSource;

    /// <summary>流程阻塞信号计数的条目数（自检用；键 = `signal:reason`）。</summary>
    internal static int FlowSignalCount => FlowSignals.Count;

    /// <summary>流程阻塞去重集的条目数（自检用；键 = `signal:round:player`）。</summary>
    internal static int FlowDedupeCount => FlowDedupeKeys.Count;

    /// <summary>流程阻塞窗口是否已开（初值 0 = 未开）。</summary>
    internal static bool FlowWindowStarted => _flowWindowStartMs > 0L;

    // ── 瓦库看门狗调度窗口 ──

    /// <summary>
    /// 记一次看门狗调度结果：懒开窗 → 更新最近身份 → 成功计数或按原因计入被拒表。
    /// （翻滚判据与日志由调用方负责，见 <see cref="IsWatchdogWindowDue"/>。）
    /// </summary>
    internal static void NoteWatchdogSchedule(
        bool scheduled,
        string reason,
        ulong playerId,
        int roundNumber,
        string source,
        long nowMs)
    {
        if (_watchdogWindowStartMs <= 0L)
        {
            _watchdogWindowStartMs = nowMs;
        }

        _watchdogLastPlayerId = playerId;
        _watchdogLastRound = roundNumber;
        _watchdogLastSource = source;

        if (scheduled)
        {
            _watchdogSuccessCount++;
            return;
        }

        string key = string.IsNullOrEmpty(reason) ? "unknown" : reason;
        WatchdogRejects[key] = (WatchdogRejects.TryGetValue(key, out int count) ? count : 0) + 1;
    }

    /// <summary>窗口是否到点（`>= WindowMs`；刚从本次调用开窗时为 false）。</summary>
    internal static bool IsWatchdogWindowDue(long nowMs)
    {
        return nowMs - _watchdogWindowStartMs >= WindowMs;
    }

    /// <summary>本次窗口已经过的毫秒数（**翻滚前**取，供日志里的 `windowMs=`）。</summary>
    internal static long WatchdogWindowElapsedMs(long nowMs)
    {
        return nowMs - _watchdogWindowStartMs;
    }

    /// <summary>`reason:次数,reason:次数`（无数据 `none`；顺序 = 插入顺序，与原实现一致）。</summary>
    internal static string DescribeWatchdogRejects()
    {
        return Describe(WatchdogRejects);
    }

    /// <summary>翻滚窗口：起点前移、清成功计数与被拒表（**最近身份不清** —— 原实现如此）。</summary>
    internal static void RollWatchdogWindow(long nowMs)
    {
        _watchdogWindowStartMs = nowMs;
        _watchdogSuccessCount = 0;
        WatchdogRejects.Clear();
    }

    // ── 流程阻塞信号窗口 ──

    /// <summary>
    /// 「同一回合同一玩家只记一次」的受理判据（返回 false = 本次应当直接丢弃）。
    /// `dedupePerRoundPlayer` 关闭或 `round &lt; 0` 时一律受理（原实现的早退条件）。
    /// </summary>
    internal static bool TryAcceptFlowSignal(string signal, int round, ulong playerId, bool dedupePerRoundPlayer)
    {
        if (!dedupePerRoundPlayer || round < 0)
        {
            return true;
        }

        return FlowDedupeKeys.Add($"{signal}:{round}:{playerId}");
    }

    /// <summary>记一次流程阻塞信号：懒开窗 + 按 `signal:reason` 计数。</summary>
    internal static void NoteFlowSignal(string signal, string reason, long nowMs)
    {
        if (_flowWindowStartMs <= 0L)
        {
            _flowWindowStartMs = nowMs;
        }

        string key = $"{signal}:{reason}";
        FlowSignals[key] = (FlowSignals.TryGetValue(key, out int count) ? count : 0) + 1;
    }

    /// <summary>窗口是否到点（`>= WindowMs`；刚从本次调用开窗时为 false）。</summary>
    internal static bool IsFlowWindowDue(long nowMs)
    {
        return nowMs - _flowWindowStartMs >= WindowMs;
    }

    /// <summary>本次窗口已经过的毫秒数（**翻滚前**取，供日志里的 `windowMs=`）。</summary>
    internal static long FlowWindowElapsedMs(long nowMs)
    {
        return nowMs - _flowWindowStartMs;
    }

    /// <summary>`signal:reason:次数,…`（无数据 `none`）。</summary>
    internal static string DescribeFlowSignals()
    {
        return Describe(FlowSignals);
    }

    /// <summary>翻滚窗口：起点前移、清计数表（**去重集不清** —— 原实现如此，同一回合同一玩家不再重复报）。</summary>
    internal static void RollFlowWindow(long nowMs)
    {
        _flowWindowStartMs = nowMs;
        FlowSignals.Clear();
    }

    // ── 退局复位 ──

    /// <summary>
    /// 退局复位（R5-1 复位契约的一部分）：两张窗口都回到"未开窗"，计数清空，
    /// 看门狗最近身份回初值，`lastSource` 写成传入的哨兵（沿用原 `OnRunCleanup` 的 `run-cleanup`）。
    /// </summary>
    internal static void Reset(string source)
    {
        _watchdogWindowStartMs = 0L;
        _watchdogSuccessCount = 0;
        _watchdogLastPlayerId = 0UL;
        _watchdogLastRound = -1;
        _watchdogLastSource = source;
        WatchdogRejects.Clear();

        _flowWindowStartMs = 0L;
        FlowSignals.Clear();
        FlowDedupeKeys.Clear();
    }

    private static string Describe(Dictionary<string, int> counts)
    {
        return counts.Count == 0
            ? "none"
            : string.Join(",", counts.Select((entry) => $"{entry.Key}:{entry.Value}"));
    }
}
