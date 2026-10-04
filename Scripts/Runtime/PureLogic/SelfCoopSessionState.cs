using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 本地多控**会话级**可变状态的收口（R5-3）。
///
/// **为什么**：会话 / 大厅线的状态（席位表 / 目标人数 / 受控位 / 读档窗口 / 事件自动切换挂起 /
/// 大厅编辑席位 / 页面级上限）原先全部以静态字段散在 `LocalSelfCoopContext` 里，复位只靠
/// `Enable` / `Disable` 两处"记得写" —— 与 R5-1 修的是同一类结构缺陷，只是作用域换成会话级。
/// 把状态收进一个**可单测**的单元后，「会话复位矩阵」（进 / 出大厅、进 / 退局、读档窗口三种时序）
/// 就能在测试里直接驱动并断言，而不必等实机出现"上一局的残留"。
///
/// **口径**：
/// ① 本类只碰纯值（id / 计数 / 时间戳 / 标志位），**不碰任何 Godot 类型** ——
///    `NetService` / `ActiveCharacterSelectScreen` / `ActiveSelfCoopLobbyScreen` 仍留在
///    `LocalSelfCoopContext`（节点 / 服务引用随节点生命周期走，不属复位矩阵）；
/// ② 取时间与打日志留在 Context（时间戳由参数传入 ⇒ 窗口判定可被单测驱动）；
/// ③ 复位判据只列"初值可判定"的项，刻意**不含** `IsSyncingCharacterHighlight`
///    （它在 `try/finally` 内保证复位，会话关闭时刻恰为 true 属瞬时态 ⇒ 拿它判会假阳性）。
/// </summary>
internal sealed class SelfCoopSessionState
{
    public const int MinLocalPlayerCount = 2;
    public const int MaxLocalPlayerCount = 12;

    /// <summary>本地席位表（初值 1..12）。集合内容可变（扩容 / 从存档恢复），引用不变。</summary>
    public List<ulong> LocalPlayerIds { get; } = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };

    /// <summary>瓦库托管席位（初值空）。</summary>
    public HashSet<ulong> WakuuPlayerIds { get; } = new();

    /// <summary>联机机器人（第三方 Co-op Bots）接管席位（初值空）。</summary>
    public HashSet<ulong> CoopBotsPlayerIds { get; } = new();

    /// <summary>目标本地玩家数（初值 <see cref="MinLocalPlayerCount"/>；会话关闭时刻意**不复位**）。</summary>
    public int DesiredLocalPlayerCount { get; set; } = MinLocalPlayerCount;

    /// <summary>当前大厅页面的本地席位上限（初值全局上限；会话关闭时必须复位）。</summary>
    public int LobbyLocalPlayerLimit { get; set; } = MaxLocalPlayerCount;

    /// <summary>主席位（初值 1，随席位表解析而变）。</summary>
    public ulong PrimaryPlayerId { get; set; } = 1;

    /// <summary>兼容字段：第二槽位（初值 2）。</summary>
    public ulong SecondaryPlayerId { get; set; } = 2;

    /// <summary>会话是否启用（初值 false）。</summary>
    public bool IsEnabled { get; set; }

    /// <summary>大厅当前正在编辑的席位（初值 = 主席位）。</summary>
    public ulong CurrentLobbyEditingPlayerId { get; set; } = 1;

    /// <summary>`SyncCharacterSelectHighlight` 的重入保护标志（`try/finally` 复位，不进复位判据）。</summary>
    public bool IsSyncingCharacterHighlight { get; set; }

    /// <summary>读档窗口开启时刻（`TickCount64`；0 = 未开启）。</summary>
    public long LoadReplayWindowOpenedAtMs { get; set; }

    /// <summary>待触发的事件自动切换来源席位（null = 无挂起请求）。</summary>
    public ulong? PendingEventAutoSwitchPlayerId { get; set; }

    /// <summary>已确认（事件结束、owner 匹配）等待消费的事件自动切换。</summary>
    public bool EventAutoSwitchPending { get; set; }

    // ── 时序①：进 / 出大厅（会话开关）──

    /// <summary>开启会话：启用 + 编辑席位回到主席位（页面 / 服务引用由 Context 自己接）。</summary>
    public void EnterSession()
    {
        IsEnabled = true;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
    }

    /// <summary>
    /// 关闭会话：把"必须回初值"的项一次清干净。
    ///
    /// **幂等** —— 无论从「离开大厅页（会话守卫判定）/ 退局（`OnRunCleanup`）/ 读档窗口被取消」
    /// 哪条路进来，结果一致；这样复位矩阵不依赖"从哪条路来"。
    /// </summary>
    public void LeaveSession()
    {
        IsEnabled = false;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        PendingEventAutoSwitchPlayerId = null;
        EventAutoSwitchPending = false;
        LobbyLocalPlayerLimit = MaxLocalPlayerCount;
        LoadReplayWindowOpenedAtMs = 0;
    }

    // ── 时序③：读档窗口（进局立刻关闭；超时安全阀）──

    /// <summary>读档窗口是否仍在有效期内（判据 <see cref="LoadReplayWindowPolicy.IsActive"/>）。</summary>
    public bool IsLoadReplayWindowActive(long nowMs)
    {
        return LoadReplayWindowPolicy.IsActive(LoadReplayWindowOpenedAtMs, nowMs);
    }

    /// <summary>打开读档窗口（记录开启时刻）。</summary>
    public void OpenLoadReplayWindow(long nowMs)
    {
        LoadReplayWindowOpenedAtMs = nowMs;
    }

    /// <summary>
    /// 关闭读档窗口。返回窗口存续时长（ms）；**未开窗时返回负值**（调用方据此跳过"已关闭"日志，
    /// 与旧实现"`_loadReplayWindowOpenedAtMs &lt;= 0` 直接 return"逐字等价）。
    /// </summary>
    public long CloseLoadReplayWindow(long nowMs)
    {
        if (LoadReplayWindowOpenedAtMs <= 0)
        {
            return -1;
        }

        long elapsedMs = nowMs - LoadReplayWindowOpenedAtMs;
        LoadReplayWindowOpenedAtMs = 0;
        return elapsedMs;
    }

    // ── 事件自动切换的三段式（请求 → 事件结束确认 → 消费 / 作废）──

    /// <summary>记录"事件选项选完后要自动切换"的请求来源席位。</summary>
    public void RequestEventAutoSwitch(ulong playerId)
    {
        PendingEventAutoSwitchPlayerId = playerId;
    }

    /// <summary>
    /// 事件结束且 owner 与挂起请求匹配 ⇒ 请求升级为"待消费"，返回 true。
    /// 判据（IsFinished + owner 相等）留在调用方（需要 `EventModel`）；本方法只做状态迁移。
    /// </summary>
    public bool ConfirmEventAutoSwitch(ulong ownerPlayerId)
    {
        if (PendingEventAutoSwitchPlayerId != ownerPlayerId)
        {
            return false;
        }

        PendingEventAutoSwitchPlayerId = null;
        EventAutoSwitchPending = true;
        return true;
    }

    /// <summary>消费"待自动切换"（返回 true 表示本次确实要切）。</summary>
    public bool TryConsumeEventAutoSwitch()
    {
        if (!EventAutoSwitchPending)
        {
            return false;
        }

        EventAutoSwitchPending = false;
        return true;
    }

    /// <summary>主动作废（含未消费的请求来源）；返回是否确实有东西被作废。</summary>
    public bool CancelEventAutoSwitch()
    {
        if (!EventAutoSwitchPending && !PendingEventAutoSwitchPlayerId.HasValue)
        {
            return false;
        }

        EventAutoSwitchPending = false;
        PendingEventAutoSwitchPlayerId = null;
        return true;
    }

    // ── 复位矩阵自检 ──

    /// <summary>
    /// 「会话关闭后应回初值」的残留判定（固定顺序，供日志逐字对比）。
    ///
    /// ⚠ 判据**只允许在会话确实关闭之后**使用（会话开着时 `is-enabled` 等必然命中，
    /// 那是正常的 —— 本类不做时间判断，由调用方保证）。
    /// </summary>
    public IReadOnlyList<string> FindResiduals()
    {
        List<string> residuals = new();

        if (IsEnabled)
        {
            residuals.Add("is-enabled=set");
        }

        if (CurrentLobbyEditingPlayerId != PrimaryPlayerId)
        {
            residuals.Add($"lobby-editing-player={CurrentLobbyEditingPlayerId}");
        }

        if (PendingEventAutoSwitchPlayerId.HasValue)
        {
            residuals.Add($"pending-event-auto-switch={PendingEventAutoSwitchPlayerId.Value}");
        }

        if (EventAutoSwitchPending)
        {
            residuals.Add("event-auto-switch-pending=set");
        }

        if (LobbyLocalPlayerLimit != MaxLocalPlayerCount)
        {
            residuals.Add($"lobby-local-player-limit={LobbyLocalPlayerLimit}");
        }

        if (LoadReplayWindowOpenedAtMs != 0)
        {
            residuals.Add("load-replay-window=set");
        }

        return residuals;
    }

    /// <summary>把 <see cref="FindResiduals"/> 的结果渲染成日志文案（`无残留` / `键=值, …`）。</summary>
    public static string Describe(IReadOnlyList<string> residuals)
    {
        return residuals.Count == 0 ? "无残留" : string.Join(", ", residuals);
    }
}
