using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalSelfCoopContext
{
    private const int MinLocalPlayerCount = 2;
    public const int MaxLocalPlayerCount = 12;
    private const int MaxLocalAscensionLevel = 10;

    private static readonly List<ulong> _localPlayerIds = new() { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 };
    private static readonly HashSet<ulong> _wakuuPlayerIds = new();

    /// <summary>
    /// 「读档窗口」开启时刻（单调时钟毫秒，0 = 未开启）。见 <see cref="LoadReplayWindowPolicy"/>：
    /// 读档期间会话守卫**不得**清会话 —— 否则 `GrantWakuuRelicsAsync` 等门控在 `IsEnabled` 上的逻辑全部失效
    /// ⇒ 瓦库不出牌 / 不自动选事件，且事件卡牌奖励归属断档导致点的人与奖励主人不匹配（软锁）。
    /// r166 日志实证：5 次读档 5 次复现（`本地多控模式已关闭，原因: no-local-lobby-screen` 紧跟读档就绪）。
    /// </summary>
    private static long _loadReplayWindowOpenedAtMs;

    /// <summary>
    /// 由第三方 mod「Co-op Bots」接管的本地席位（POC）。与 <see cref="_wakuuPlayerIds"/> **互斥**：
    /// 同一席位同时只能有一种驱动（真人 / 瓦库 / 联机机器人），见 <see cref="CoopBotsSeatPlan"/>。
    /// </summary>
    private static readonly HashSet<ulong> _coopBotsPlayerIds = new();

    private static int _desiredLocalPlayerCount = 2;

    private static bool _isSyncingCharacterHighlight;

    private static ulong? _pendingEventAutoSwitchPlayerId;

    private static bool _eventAutoSwitchPending;
    public static bool UseSingleAdventureMode => true;

    public static bool UseSingleEventFlow => false;

    public static int DesiredLocalPlayerCount => _desiredLocalPlayerCount;

    /// <summary>
    /// 当前大厅页面的本地席位上限（默认 <see cref="MaxLocalPlayerCount"/>）。
    /// 每日挑战页固定为 4：游戏 `NDailyRunScreen` 建厅时写死 `new StartRunLobby(..., 4)`，
    /// 且 `StartRunLobby._maxPlayers` 是 readonly ⇒ 超过 4 的本地席位根本加不进大厅。
    /// 进 Daily 页时设置、离开时恢复。
    /// </summary>
    public static int LobbyLocalPlayerLimit { get; private set; } = MaxLocalPlayerCount;

    public static IReadOnlyList<ulong> LocalPlayerIds => _localPlayerIds;
    public static IReadOnlyCollection<ulong> WakuuPlayerIds => _wakuuPlayerIds;
    public static IReadOnlyCollection<ulong> CoopBotsPlayerIds => _coopBotsPlayerIds;

    public static ulong PrimaryPlayerId { get; private set; } = 1;
    // 保留兼容字段，旧代码仍可读取第二槽位。
    public static ulong SecondaryPlayerId { get; private set; } = 2;

    public static bool IsEnabled { get; private set; }

    public static LocalLoopbackHostGameService? NetService { get; private set; }

    public static ulong CurrentLobbyEditingPlayerId { get; private set; } = 1;

    public static NCharacterSelectScreen? ActiveCharacterSelectScreen { get; set; }

    /// <summary>
    /// 本轮会话「我们自己 push 的那个大厅页」（自定义 / 每日；角色选择页另有 <see cref="ActiveCharacterSelectScreen"/>）。
    ///
    /// 谁在用：会话守卫判断"是否还停在自建大厅页上"。为什么不能只看大厅的 `NetService`（r161）：
    /// **每日页的大厅是异步建的**（先 await 时间服务器，`_lobby` 会长时间为 null）——
    /// 断网时 DNS 失败还要重试两回，此时页面开着但 `_lobby` 一直是 null ⇒ 守卫误判"没有大厅页"，
    /// 约 1 秒就把会话清掉（实机表现 = 断网进「本地·每日挑战」只有单人、连加人按钮都没有）。
    /// 记页面本身即可：`IsInsideTree() && Visible` 就说明玩家正停在这一页（`NSubmenuStack.Pop` 只把页面
    /// `Visible = false`，并不出树，所以必须带上 `Visible` 判据，否则退回主菜单后会残留会话）。
    /// </summary>
    public static CanvasItem? ActiveSelfCoopLobbyScreen { get; set; }

    public static ulong ResolvePrimaryPlayerId()
    {
        ulong localPlatformPlayerId = PlatformUtil.GetLocalPlayerId(PlatformUtil.PrimaryPlatform);
        if (localPlatformPlayerId == 0)
        {
            localPlatformPlayerId = 1;
        }

        List<ulong> ids = BuildSequentialPlayerIds(localPlatformPlayerId, _desiredLocalPlayerCount);
        ApplyLocalPlayerIds(ids);
        LocalMultiControlLogger.Info($"本地多控玩家ID已解析: {string.Join(",", _localPlayerIds)}");
        return PrimaryPlayerId;
    }

    public static void UseSavedPlayerIds(ulong primaryPlayerId, ulong secondaryPlayerId)
    {
        UseSavedPlayerIds(new List<ulong> { primaryPlayerId, secondaryPlayerId });
    }

    public static void UseSavedPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        List<ulong> normalized = NormalizePlayerIds(playerIds, fallbackPrimaryId: PrimaryPlayerId);
        if (normalized.Count < MinLocalPlayerCount)
        {
            LocalMultiControlLogger.Warn($"忽略无效存档玩家ID列表: {string.Join(",", playerIds)}");
            return;
        }

        _desiredLocalPlayerCount = Math.Clamp(normalized.Count, MinLocalPlayerCount, MaxLocalPlayerCount);
        ApplyLocalPlayerIds(normalized);
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        LocalMultiControlLogger.Info($"已从存档恢复本地多控玩家ID: {string.Join(",", _localPlayerIds)}");
    }

    public static void UseSavedWakuuPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        _wakuuPlayerIds.Clear();
        // 过滤规则抽成纯函数（r166）：只认本地席位表里的 id、丢占位 0、去重 —— 可单测。
        foreach (ulong playerId in WakuuSeatRestorePolicy.FilterToLocalSeats(
                     _localPlayerIds.ToList(),
                     playerIds))
        {
            _wakuuPlayerIds.Add(playerId);
        }

        LocalMultiControlLogger.Info($"已恢复瓦库勾选玩家: {string.Join(",", _wakuuPlayerIds)}");
    }

    /// <summary>
    /// 从配置层恢复联机机器人席位（不落盘、不接管，只是把「谁归联机机器人」记进来）。
    /// 席位过滤口径与瓦库一致：只认本地席位表里存在的 id。
    /// </summary>
    public static void UseSavedCoopBotsPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        _coopBotsPlayerIds.Clear();
        foreach (ulong playerId in playerIds.Where((id) => id != 0))
        {
            if (_localPlayerIds.Contains(playerId))
            {
                _coopBotsPlayerIds.Add(playerId);
            }
        }

        LocalMultiControlLogger.Info($"联机机器人席位已登记: {string.Join(",", GetCoopBotsPlayerIdsSnapshot())}");
    }

    /// <summary>
    /// 该席位是否由瓦库托管。
    ///
    /// R3 B1c（2026-09-30）**已评估：不改成问 <see cref="SeatRegistry.DriverOf"/>** ——
    /// 理由：驱动事实源本来就只有这一处（`_wakuuPlayerIds` / `_coopBotsPlayerIds`，
    /// 且 <see cref="SetCoopBotsDriven"/> / <see cref="SetWakuuEnabled"/> 在写入侧强制互斥），
    /// 消费点全是调本方法，换一层包装只增间接；而三态互斥口径在"两处同时命中"时会改变结果，
    /// 那不是"行为零变化"。**新代码若要"互斥的唯一答案"就显式问
    /// `LocalSeatSource.CurrentSeats().IsWakuuDriven(id)`**（并在需要时先确认不变量）。
    /// 本方法与 <see cref="IsCoopBotsDriven"/> 保留为**驱动事实源的源级原语**。
    /// </summary>
    public static bool IsWakuuEnabled(ulong playerId)
    {
        return _wakuuPlayerIds.Contains(playerId);
    }

    /// <summary>该席位是否交给第三方 mod「Co-op Bots」作答（源级原语，见 <see cref="IsWakuuEnabled"/> 的 B1c 说明）。</summary>
    public static bool IsCoopBotsDriven(ulong playerId)
    {
        return _coopBotsPlayerIds.Contains(playerId);
    }

    /// <summary>
    /// 该席位是否属于**本地多控会话**（= 位于本地席位表里）。
    ///
    /// 第三方往局里塞进来的席位（典型：Co-op Bots 的**合成 Bot**，netId 形如 `0xB07B…`）**不是**我们的席位：
    /// 一律不该由我们代它领奖励 / 镜像遗物金币药水 / 切前台 / 结束回合 / 报就绪。
    /// 判据只用本地席位表（**不**查 `IsCoopBotsDriven`）—— 因为「被 CB 接管的本地席位」仍然是我们自己的席位，
    /// 只是作答方换成了第三方（见《Co-op_Bots联机队友兼容可行性分析》§2.1 的三态模型）。
    ///
    /// R3（2026-09-28 B1b）：**消费方一律走唯一入口 <see cref="LocalSeatSource.IsLocalSeat"/>**
    /// （薄适配 + 席位快照）。本方法保留为**席位表源级原语**（`LocalSelfCoopContext` 内部判据与将来的源级逻辑用）；
    /// B1b 之后已无外部调用点 —— 不要再拿它发散判定逻辑，否则又回到"多路各自判身份"。
    /// </summary>
    public static bool IsLocalSessionSeat(ulong playerId)
    {
        return playerId != 0 && _localPlayerIds.Contains(playerId);
    }

    /// <summary>
    /// 席位驱动三态互斥（POC 版入口，Phase 1 由选人屏循环钮调用）：
    /// 勾选联机机器人即把该席位从瓦库名单移除（反向由 <see cref="SetWakuuEnabled"/> 调用点保证）。
    /// 返回 false = 该席位不在本地席位表内、或状态本就如此（未变更）。
    /// </summary>
    public static bool SetCoopBotsDriven(ulong playerId, bool enabled, string source)
    {
        if (!_localPlayerIds.Contains(playerId))
        {
            return false;
        }

        bool changed = enabled
            ? _coopBotsPlayerIds.Add(playerId)
            : _coopBotsPlayerIds.Remove(playerId);

        if (enabled && _wakuuPlayerIds.Remove(playerId))
        {
            changed = true;
            LocalMultiControlLogger.Info($"席位驱动互斥: seat={playerId} 由瓦库改为联机机器人, source={source}");
        }

        if (!changed)
        {
            return false;
        }

        LocalMultiControlLogger.Info($"席位驱动变更: seat={playerId}, coopBots={enabled}, source={source}");
        MarkCurrentProfileTag();
        return true;
    }

    /// <summary>当前被标记为联机机器人的席位（按本地席位表顺序，日志/接管顺序稳定）。</summary>
    public static List<ulong> GetCoopBotsPlayerIdsSnapshot()
    {
        return _coopBotsPlayerIds
            .Where((playerId) => _localPlayerIds.Contains(playerId))
            .OrderBy((playerId) => _localPlayerIds.IndexOf(playerId))
            .ToList();
    }

    public static bool SetWakuuEnabled(ulong playerId, bool enabled, string source)
    {
        if (!_localPlayerIds.Contains(playerId))
        {
            return false;
        }

        bool changed = enabled
            ? _wakuuPlayerIds.Add(playerId)
            : _wakuuPlayerIds.Remove(playerId);

        // 三态互斥（POC）：勾选瓦库即取消该席位的「联机机器人」驱动，避免两侧同时作答（分析 §4.3）。
        if (enabled && _coopBotsPlayerIds.Remove(playerId))
        {
            LocalMultiControlLogger.Info(
                $"席位驱动互斥: player={playerId} 由联机机器人改为瓦库, source={source}");
        }

        if (!changed)
        {
            return false;
        }

        LocalMultiControlLogger.Info($"瓦库勾选状态变更: player={playerId}, enabled={enabled}, source={source}");
        string slotLabel = GetSlotLabel(playerId);
        string tip = enabled
            ? LocalModText.VakuuControlsPlayer(slotLabel)
            : LocalModText.VakuuReleasedPlayer(slotLabel);
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(tip));
        MarkCurrentProfileTag();
        return true;
    }

    public static bool SetAllWakuuEnabled(bool enabled, string source)
    {
        List<ulong> targetPlayerIds = _localPlayerIds
            .Take(_desiredLocalPlayerCount)
            .Where((id) => id != 0)
            .ToList();
        if (targetPlayerIds.Count == 0)
        {
            return false;
        }

        bool changed = false;
        foreach (ulong playerId in targetPlayerIds)
        {
            changed |= enabled
                ? _wakuuPlayerIds.Add(playerId)
                : _wakuuPlayerIds.Remove(playerId);
        }

        if (!changed)
        {
            return false;
        }

        LocalMultiControlLogger.Info(
            $"全体瓦库开关变更: enabled={enabled}, count={targetPlayerIds.Count}, source={source}");
        string tip = enabled
            ? LocalModText.VakuuControlsAllPlayers()
            : LocalModText.VakuuReleasedAllPlayers();
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(tip));
        MarkCurrentProfileTag();
        return true;
    }

    public static List<ulong> GetWakuuPlayerIdsSnapshot()
    {
        return _wakuuPlayerIds
            .Where((playerId) => _localPlayerIds.Contains(playerId))
            .OrderBy((playerId) => _localPlayerIds.IndexOf(playerId))
            .ToList();
    }

    public static bool IsSaveOwnedByLocalSelfCoop(SerializableRun run)
    {
        if (run.Players.Count < MinLocalPlayerCount)
        {
            return false;
        }

        // R3 B5 复核：这是席位表的**源级**谓词（"存档里的玩家是否就是这张席位表"），在来源类内部
        // 直接用 `_localPlayerIds` 属预期；消费方的身份判定一律走 `LocalSeatSource`（B1b 已收编）。
        return _localPlayerIds
            .Take(_desiredLocalPlayerCount)
            .All((playerId) => run.Players.Any((player) => player.NetId == playerId));
    }

    public static void Enable(LocalLoopbackHostGameService netService)
    {
        IsEnabled = true;
        NetService = netService;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        ActiveCharacterSelectScreen = null;
        ActiveSelfCoopLobbyScreen = null;
        netService.SetCurrentSenderId(CurrentLobbyEditingPlayerId);
        LocalContext.NetId = CurrentLobbyEditingPlayerId;
        // 会话守卫：玩家从大厅页直接退回主菜单时，兜底把残留会话清掉（r158）
        LocalSelfCoopSessionGuard.EnsureAttached();
        LocalMultiControlLogger.Info($"本地多控模式已启用，目标玩家数: {_desiredLocalPlayerCount}");
    }

    /// <summary>读档窗口是否仍在有效期内（守卫据此不下手）。详见 <see cref="LoadReplayWindowPolicy"/>。</summary>
    public static bool IsLoadReplayWindowActive =>
        LoadReplayWindowPolicy.IsActive(_loadReplayWindowOpenedAtMs, System.Environment.TickCount64);

    /// <summary>
    /// 打开「读档窗口」（继续游戏 / ESC 快速重启 的读档入口调用）。
    /// 有超时安全阀：万一读档被取消，守卫仍会在超时后收拾残留会话（不退化成 r158 的老问题）。
    /// </summary>
    public static void OpenLoadReplayWindow(string source)
    {
        _loadReplayWindowOpenedAtMs = System.Environment.TickCount64;
        LocalMultiControlLogger.Info($"读档窗口已开启: source={source}（窗口内会话守卫不下手，最长 180 秒）");
    }

    /// <summary>关闭「读档窗口」（进局 / 清理 / 会话关闭时调用）。</summary>
    public static void CloseLoadReplayWindow(string source)
    {
        if (_loadReplayWindowOpenedAtMs <= 0)
        {
            return;
        }

        long elapsedMs = System.Environment.TickCount64 - _loadReplayWindowOpenedAtMs;
        _loadReplayWindowOpenedAtMs = 0;
        LocalMultiControlLogger.Info($"读档窗口已关闭: source={source}, 时长={elapsedMs}ms");
    }

    public static void Disable(string reason)
    {
        // 会话都关了就不该再留着窗口（否则会遮住守卫后续的清理职责）。
        CloseLoadReplayWindow($"disable:{reason}");

        if (!IsEnabled && NetService == null)
        {
            return;
        }

        IsEnabled = false;
        NetService = null;
        CurrentLobbyEditingPlayerId = PrimaryPlayerId;
        ActiveCharacterSelectScreen = null;
        ActiveSelfCoopLobbyScreen = null;
        _pendingEventAutoSwitchPlayerId = null;
        _eventAutoSwitchPending = false;
        // 会话结束：把页面级席位上限复位（否则从每日页直接退出会把上限留在 4，
        // 之后再开 Standard/Custom 就只能选到 2~4 人）。
        LobbyLocalPlayerLimit = MaxLocalPlayerCount;
        LocalMultiControlLogger.Info($"本地多控模式已关闭，原因: {reason}");
    }

    public static bool SwitchLobbyEditingPlayer(bool next)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        List<ulong> activePlayerIds = GetActiveLobbyLocalPlayerIds();
        if (activePlayerIds.Count < MinLocalPlayerCount)
        {
            return false;
        }

        int currentIndex = activePlayerIds.IndexOf(CurrentLobbyEditingPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int delta = next ? 1 : -1;
        int targetIndex = (currentIndex + delta + activePlayerIds.Count) % activePlayerIds.Count;
        ulong previousPlayerId = CurrentLobbyEditingPlayerId;
        CurrentLobbyEditingPlayerId = activePlayerIds[targetIndex];

        EnsureLobbySenderContext("switch-lobby-editing-player");
        SyncCharacterSelectHighlight();
        TrimWakuuPlayerIdsToConfiguredPlayers();

        string slotLabel = GetSlotLabel(CurrentLobbyEditingPlayerId);
        LocalMultiControlLogger.Info($"大厅编辑角色切换: {previousPlayerId} -> {CurrentLobbyEditingPlayerId} (槽位{slotLabel})");
        NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.LobbyEditingSlot(slotLabel)));
        return true;
    }

    public static bool SetLobbyEditingPlayer(ulong playerId, string source)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        List<ulong> activePlayerIds = GetActiveLobbyLocalPlayerIds();
        if (activePlayerIds.Count < MinLocalPlayerCount || !activePlayerIds.Contains(playerId))
        {
            return false;
        }

        ulong previousPlayerId = CurrentLobbyEditingPlayerId;
        CurrentLobbyEditingPlayerId = playerId;
        EnsureLobbySenderContext(source);
        SyncCharacterSelectHighlight();
        TrimWakuuPlayerIdsToConfiguredPlayers();

        if (previousPlayerId != CurrentLobbyEditingPlayerId)
        {
            string slotLabel = GetSlotLabel(CurrentLobbyEditingPlayerId);
            LocalMultiControlLogger.Info(
                $"大厅编辑角色定向切换: {previousPlayerId} -> {CurrentLobbyEditingPlayerId} (槽位{slotLabel})");
            NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.LobbyEditingSlot(slotLabel)));
        }

        return true;
    }

    public static bool AdjustDesiredLocalPlayerCount(int delta, string source)
    {
        int oldCount = _desiredLocalPlayerCount;
        // 上限取「全局上限」与「当前页面上限」的较小值（Daily 页 = 4）
        int maximum = Math.Min(MaxLocalPlayerCount, LobbyLocalPlayerLimit);
        int targetCount = Math.Clamp(oldCount + delta, MinLocalPlayerCount, maximum);
        if (targetCount == oldCount)
        {
            return false;
        }

        _desiredLocalPlayerCount = targetCount;
        EnsureLocalPlayerIdCapacity(targetCount);
        TrimWakuuPlayerIdsToConfiguredPlayers();

        bool reconciled = ReconcileStartRunLobbyPlayerCount(source);
        if (!reconciled)
        {
            LocalMultiControlLogger.Info($"已更新目标本地玩家数: {oldCount} -> {targetCount}");
        }

        MarkCurrentProfileTag();
        return true;
    }

    /// <summary>
    /// 设置当前大厅页面的本地席位上限（每日挑战页 = 4）。若已选人数超过新上限，一并收下来并同步大厅。
    /// </summary>
    public static void SetLobbyLocalPlayerLimit(int limit, string source)
    {
        int clamped = Math.Clamp(limit, MinLocalPlayerCount, MaxLocalPlayerCount);
        if (LobbyLocalPlayerLimit == clamped)
        {
            return;
        }

        LobbyLocalPlayerLimit = clamped;
        LocalMultiControlLogger.Info($"大厅本地席位上限已设置: {clamped}, source={source}");

        if (_desiredLocalPlayerCount <= clamped)
        {
            return;
        }

        int oldCount = _desiredLocalPlayerCount;
        _desiredLocalPlayerCount = clamped;
        EnsureLocalPlayerIdCapacity(clamped);
        TrimWakuuPlayerIdsToConfiguredPlayers();
        ReconcileStartRunLobbyPlayerCount($"{source}:limit-clamp");
        LocalMultiControlLogger.Info($"本地人数超过当前大厅上限，已下调: {oldCount} -> {clamped}, source={source}");
    }

    /// <summary>恢复默认席位上限（离开受限页面时调用；不会把已选人数改回去）。</summary>
    public static void ResetLobbyLocalPlayerLimit(string source)
    {
        SetLobbyLocalPlayerLimit(MaxLocalPlayerCount, source);
    }

    public static bool TryGetSlotIndex(ulong playerId, out int slotIndex)
    {
        slotIndex = _localPlayerIds.IndexOf(playerId);
        return slotIndex >= 0;
    }

    public static string GetSlotLabel(ulong playerId)
    {
        return TryGetSlotIndex(playerId, out int slotIndex)
            ? (slotIndex + 1).ToString()
            : "?";
    }

    public static bool EnsureLobbySenderContext(string source)
    {
        if (!IsEnabled || NetService == null)
        {
            return false;
        }

        EnsureLobbyEditingPlayerIsValid();
        NetService.SetCurrentSenderId(CurrentLobbyEditingPlayerId);
        LocalContext.NetId = CurrentLobbyEditingPlayerId;
        LocalMultiControlLogger.Info($"大厅控制上下文同步: player={CurrentLobbyEditingPlayerId}, source={source}");
        return true;
    }

    public static void NotifyCharacterSelectPlayerChanged(ulong playerId)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (playerId == CurrentLobbyEditingPlayerId)
        {
            SyncCharacterSelectHighlight();
            TrimWakuuPlayerIdsToConfiguredPlayers();
        }
    }

    public static void RequestEventAutoSwitchAfterChoice(ulong playerId)
    {
        if (!IsEnabled)
        {
            return;
        }

        _pendingEventAutoSwitchPlayerId = playerId;
        LocalMultiControlLogger.Info($"记录事件自动切换请求: player={playerId}");
    }

    public static bool ShouldQueueEventAutoSwitchAfterEventState(EventModel eventModel)
    {
        if (!IsEnabled || !_pendingEventAutoSwitchPlayerId.HasValue || eventModel.Owner == null)
        {
            return false;
        }

        // R3 B5 复核：意图缓存比较（事件 owner ↔ 刚记录的待切换席位），不是身份判定 ⇒ 不走 `LocalSeatSource`。
        if (!eventModel.IsFinished || eventModel.Owner.NetId != _pendingEventAutoSwitchPlayerId.Value)
        {
            return false;
        }

        _pendingEventAutoSwitchPlayerId = null;
        _eventAutoSwitchPending = true;
        return true;
    }

    public static bool TryConsumePendingEventAutoSwitch()
    {
        if (!_eventAutoSwitchPending)
        {
            return false;
        }

        _eventAutoSwitchPending = false;
        return true;
    }

    /// <summary>
    /// 主动作废待触发的事件自动切换（含未消费的请求来源角色）。
    /// 用于调用方自行切换角色的场景，避免弹层移除后的自动切换链再次切人造成来回跳。
    /// </summary>
    public static void CancelPendingEventAutoSwitch()
    {
        if (_eventAutoSwitchPending || _pendingEventAutoSwitchPlayerId.HasValue)
        {
            _eventAutoSwitchPending = false;
            _pendingEventAutoSwitchPlayerId = null;
            LocalMultiControlLogger.Info("已作废待触发的事件自动切换请求。");
        }
    }

    public static bool BootstrapSecondPlayer(NCharacterSelectScreen characterSelectScreen)
    {
        // 保留旧方法名，兼容已有调用。
        return BootstrapLocalPlayers(characterSelectScreen);
    }

    public static bool BootstrapLocalPlayers(NCharacterSelectScreen characterSelectScreen)
    {
        ActiveCharacterSelectScreen = characterSelectScreen;
        return ReconcileStartRunLobbyPlayerCount("bootstrap-local-players");
    }

    public static void EnsureLocalAscensionOptionsUnlocked(NCharacterSelectScreen screen, string source)
    {
        if (!IsEnabled)
        {
            return;
        }

        StartRunLobby? lobby = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby")?.GetValue(screen) as StartRunLobby;
        if (lobby == null)
        {
            return;
        }

        EnsureLobbyAscensionCapacity(lobby, source);
    }

    private static bool ReconcileStartRunLobbyPlayerCount(string source)
    {
        if (!IsEnabled || NetService == null || ActiveCharacterSelectScreen == null)
        {
            return false;
        }

        NCharacterSelectScreen screen = ActiveCharacterSelectScreen;
        StartRunLobby? lobby = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby")?.GetValue(screen) as StartRunLobby;
        if (lobby == null)
        {
            LocalMultiControlLogger.Warn($"大厅玩家同步跳过：Lobby尚未初始化，source={source}");
            return false;
        }

        EnsureLobbyMaxCapacity(lobby);

        int targetCount = Math.Clamp(_desiredLocalPlayerCount, MinLocalPlayerCount, MaxLocalPlayerCount);
        EnsureLocalPlayerIdCapacity(targetCount);

        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();
        int maxAscension = MaxLocalAscensionLevel;

        List<ulong> targetPlayerIds = _localPlayerIds.Take(targetCount).ToList();

        foreach (ulong playerId in targetPlayerIds)
        {
            bool exists = lobby.Players.Any((player) => player.id == playerId);
            if (exists)
            {
                continue;
            }

            NetService.SetCurrentSenderId(playerId);
            _ = lobby.AddLocalHostPlayerInternal(serializableUnlockState, maxAscension);
        }

        List<ulong> removablePlayerIds = _localPlayerIds
            .Skip(targetCount)
            .Where((playerId) => playerId != PrimaryPlayerId)
            .ToList();
        foreach (ulong removableId in removablePlayerIds)
        {
            int playerIndex = lobby.Players.FindIndex((player) => player.id == removableId);
            if (playerIndex < 0)
            {
                continue;
            }

            StartRunLobbyPlayer removedPlayer = lobby.Players[playerIndex];
            lobby.Players.RemoveAt(playerIndex);
            lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
            screen.RemotePlayerDisconnected(removedPlayer);
        }

        foreach (ulong playerId in targetPlayerIds)
        {
            if (playerId == PrimaryPlayerId)
            {
                continue;
            }

            int playerIndex = lobby.Players.FindIndex((player) => player.id == playerId);
            if (playerIndex < 0)
            {
                continue;
            }

            StartRunLobbyPlayer lobbyPlayer = lobby.Players[playerIndex];
            if (lobbyPlayer.isReady)
            {
                continue;
            }

            lobbyPlayer.isReady = true;
            lobby.Players[playerIndex] = lobbyPlayer;
            screen.PlayerChanged(lobbyPlayer, false);
        }

        EnsureLobbyEditingPlayerIsValid();
        EnsureLobbySenderContext(source);
        SyncCharacterSelectHighlight();
        TrimWakuuPlayerIdsToConfiguredPlayers();
        EnsureLobbyAscensionCapacity(lobby, source);

        LocalMultiControlLogger.Info(
            $"大厅本地玩家数已同步: target={targetCount}, actual={GetActiveLobbyLocalPlayerIds().Count}, source={source}");
        return true;
    }

    private static void EnsureLobbyAscensionCapacity(StartRunLobby lobby, string source)
    {
        bool changed = false;

        for (int i = 0; i < lobby.Players.Count; i++)
        {
            StartRunLobbyPlayer player = lobby.Players[i];
            if (player.maxMultiplayerAscensionUnlocked >= MaxLocalAscensionLevel)
            {
                continue;
            }

            player.maxMultiplayerAscensionUnlocked = MaxLocalAscensionLevel;
            lobby.Players[i] = player;
            lobby.LobbyListener.PlayerChanged(player, false);
            changed = true;
        }

        int currentMaxAscension = lobby.MaxAscension;
        if (currentMaxAscension < MaxLocalAscensionLevel)
        {
            AccessTools.Field(typeof(StartRunLobby), "<MaxAscension>k__BackingField")
                ?.SetValue(lobby, MaxLocalAscensionLevel);
            lobby.LobbyListener.MaxAscensionChanged();
            changed = true;
        }

        int clampedAscension = Math.Clamp(lobby.Ascension, 0, MaxLocalAscensionLevel);
        if (lobby.Ascension != clampedAscension)
        {
            lobby.SyncAscensionChange(clampedAscension);
            changed = true;
        }

        if (changed)
        {
            LocalMultiControlLogger.Info(
                $"已为本地多控开放完整进阶难度(0-{MaxLocalAscensionLevel})：players={lobby.Players.Count}, source={source}");
        }
    }

    private static void EnsureLobbyMaxCapacity(StartRunLobby lobby)
    {
        // beta111 起 StartRunLobby 的 _maxPlayers 为构造函数中一次性传入的 readonly
        // 字段，已无法通过反射调整。本地多控大厅在创建时统一以 MaxLocalPlayerCount 初始化，
        // 因此这里只需确认目标人数不超过已设定的容量即可。
        if (_desiredLocalPlayerCount > MaxLocalPlayerCount)
        {
            LocalMultiControlLogger.Warn($"本地多控目标人数超过大厅容量上限: {_desiredLocalPlayerCount} > {MaxLocalPlayerCount}");
        }
    }

    private static void EnsureLobbyEditingPlayerIsValid()
    {
        List<ulong> activePlayerIds = GetActiveLobbyLocalPlayerIds();
        if (activePlayerIds.Count == 0)
        {
            activePlayerIds = _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        }

        if (activePlayerIds.Count == 0)
        {
            CurrentLobbyEditingPlayerId = PrimaryPlayerId;
            return;
        }

        if (!activePlayerIds.Contains(CurrentLobbyEditingPlayerId))
        {
            CurrentLobbyEditingPlayerId = activePlayerIds[0];
        }
    }

    private static List<ulong> GetActiveLobbyLocalPlayerIds()
    {
        if (ActiveCharacterSelectScreen == null || !GodotObject.IsInstanceValid(ActiveCharacterSelectScreen))
        {
            return _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        }

        StartRunLobby? lobby = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby")?.GetValue(ActiveCharacterSelectScreen) as StartRunLobby;
        if (lobby == null)
        {
            return _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        }

        return lobby.Players
            .Where((player) => _localPlayerIds.Contains(player.id))
            .Select((player) => player.id)
            .Distinct()
            .OrderBy((playerId) => _localPlayerIds.IndexOf(playerId))
            .ToList();
    }

    private static void SyncCharacterSelectHighlight()
    {
        if (ActiveCharacterSelectScreen == null || _isSyncingCharacterHighlight)
        {
            return;
        }

        try
        {
            _isSyncingCharacterHighlight = true;

            EnsureLobbyEditingPlayerIsValid();
            StartRunLobby? lobby = AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby")?.GetValue(ActiveCharacterSelectScreen) as StartRunLobby;
            if (lobby == null)
            {
                return;
            }

            int localPlayerIndex = lobby.Players
                .FindIndex((player) => player.id == CurrentLobbyEditingPlayerId);
            if (localPlayerIndex < 0)
            {
                return;
            }

            StartRunLobbyPlayer localPlayer = lobby.Players[localPlayerIndex];
            Control? charButtonContainer = AccessTools.Field(typeof(NCharacterSelectScreen), "_charButtonContainer")
                ?.GetValue(ActiveCharacterSelectScreen) as Control;
            if (charButtonContainer == null)
            {
                return;
            }

            List<NCharacterSelectButton> buttons = charButtonContainer.GetChildren().OfType<NCharacterSelectButton>().ToList();
            foreach (NCharacterSelectButton button in buttons)
            {
                foreach (StartRunLobbyPlayer player in lobby.Players)
                {
                    button.OnRemotePlayerDeselected(player.id);
                }
            }

            NCharacterSelectButton? selectedButton = null;
            foreach (NCharacterSelectButton button in buttons)
            {
                bool isSelected = button.Character == localPlayer.character;
                AccessTools.Field(typeof(NCharacterSelectButton), "_isSelected")?.SetValue(button, isSelected);
                if (isSelected)
                {
                    selectedButton = button;
                }
            }

            foreach (StartRunLobbyPlayer player in lobby.Players)
            {
                if (player.id == localPlayer.id)
                {
                    continue;
                }

                NCharacterSelectButton? targetButton = buttons.FirstOrDefault((button) => button.Character == player.character);
                targetButton?.OnRemotePlayerSelected(player.id);
            }

            foreach (NCharacterSelectButton button in buttons)
            {
                AccessTools.Method(typeof(NCharacterSelectButton), "RefreshState")?.Invoke(button, Array.Empty<object>());
            }

            AccessTools.Field(typeof(NCharacterSelectScreen), "_selectedButton")?.SetValue(ActiveCharacterSelectScreen, selectedButton);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"同步角色选择高亮失败: {exception.Message}");
        }
        finally
        {
            _isSyncingCharacterHighlight = false;
        }
    }

    private static void ApplyLocalPlayerIds(IReadOnlyList<ulong> playerIds)
    {
        _localPlayerIds.Clear();
        _localPlayerIds.AddRange(playerIds.Distinct().Take(MaxLocalPlayerCount));
        if (_localPlayerIds.Count < MinLocalPlayerCount)
        {
            _localPlayerIds.Clear();
            _localPlayerIds.Add(1);
            _localPlayerIds.Add(2);
        }

        PrimaryPlayerId = _localPlayerIds[0];
        SecondaryPlayerId = _localPlayerIds.Count > 1 ? _localPlayerIds[1] : _localPlayerIds[0];
        TrimWakuuPlayerIdsToConfiguredPlayers();
    }

    private static void EnsureLocalPlayerIdCapacity(int targetCount)
    {
        int clampedTargetCount = Math.Clamp(targetCount, MinLocalPlayerCount, MaxLocalPlayerCount);
        if (_localPlayerIds.Count >= clampedTargetCount)
        {
            return;
        }

        List<ulong> expanded = BuildSequentialPlayerIds(PrimaryPlayerId, clampedTargetCount);
        ApplyLocalPlayerIds(expanded);
    }

    private static List<ulong> NormalizePlayerIds(IReadOnlyList<ulong> playerIds, ulong fallbackPrimaryId)
    {
        List<ulong> normalized = playerIds
            .Where((id) => id != 0)
            .Distinct()
            .Take(MaxLocalPlayerCount)
            .ToList();
        if (normalized.Count >= MinLocalPlayerCount)
        {
            return normalized;
        }

        ulong primary = fallbackPrimaryId == 0 ? 1UL : fallbackPrimaryId;
        return BuildSequentialPlayerIds(primary, MinLocalPlayerCount);
    }

    private static List<ulong> BuildSequentialPlayerIds(ulong primaryPlayerId, int count)
    {
        int targetCount = Math.Clamp(count, MinLocalPlayerCount, MaxLocalPlayerCount);
        List<ulong> ids = new(targetCount) { primaryPlayerId == 0 ? 1UL : primaryPlayerId };
        while (ids.Count < targetCount)
        {
            ulong nextId = ids[^1] == ulong.MaxValue ? 1UL : ids[^1] + 1UL;
            while (nextId == 0 || ids.Contains(nextId))
            {
                nextId = nextId == ulong.MaxValue ? 1UL : nextId + 1UL;
            }

            ids.Add(nextId);
        }

        return ids;
    }

    private static void TrimWakuuPlayerIdsToConfiguredPlayers()
    {
        HashSet<ulong> activeSet = _localPlayerIds.Take(_desiredLocalPlayerCount).ToHashSet();
        int removedWakuu = _wakuuPlayerIds.RemoveWhere((playerId) => !activeSet.Contains(playerId));
        // 联机机器人席位同口径收敛（本地玩家数下调后，超出的席位不再接管）。
        _coopBotsPlayerIds.RemoveWhere((playerId) => !activeSet.Contains(playerId));

        // r166：真丢席位才留痕。读档后「瓦库不出牌/不自动选事件」这类问题
        // 全是"席位被悄悄清空"造成的，有此锚点下次一眼可辨（此前完全静默）。
        if (removedWakuu > 0)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库席位被收敛剔除 {removedWakuu} 个: active={string.Join(",", activeSet)}, "
                + $"剩余wakuu={string.Join(",", _wakuuPlayerIds)}");
        }
    }

    private static void MarkCurrentProfileTag()
    {
        List<ulong> saveIds = _localPlayerIds.Take(_desiredLocalPlayerCount).ToList();
        LocalSelfCoopSaveTag.MarkCurrentProfile(saveIds, GetWakuuPlayerIdsSnapshot());
    }
}
