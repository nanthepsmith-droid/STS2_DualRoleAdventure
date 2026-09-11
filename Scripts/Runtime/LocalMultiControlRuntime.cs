using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Capstones;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.TopBar;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalMultiControlRuntime
{
    private static readonly LocalMultiSessionState Session = new LocalMultiSessionState();

    private static readonly HashSet<string> _fieldSyncFailures = new HashSet<string>();
    private static readonly HashSet<string> _wakuuAutoEndIssued = new HashSet<string>();
    private static readonly HashSet<int> _allPlayersAutoEndedRounds = new HashSet<int>();
    private static readonly HashSet<string> _wakuuToNonWakuuSwitchedRounds = new HashSet<string>();
    private static readonly Dictionary<string, int> _watchdogScheduleRejectCounts = new Dictionary<string, int>();
    private static readonly Dictionary<string, int> _flowBlockSignalCounts = new Dictionary<string, int>();
    private static readonly HashSet<string> _flowBlockSignalDedupeRoundPlayer = new HashSet<string>();
    private static int _lastAutoEndCombatIdentity = -1;
    private static Vector2? _combatEnergyContainerDefaultPosition;

    /// <summary>战斗能量归属诊断日志去重（每场战斗入战时清一次，避免每回合刷屏）。</summary>
    private static readonly HashSet<string> _combatEnergyDiagKeys = new HashSet<string>();
    private static long _flowBlockSignalWindowStartMs;
    private static long _watchdogScheduleWindowStartMs;
    private static int _watchdogScheduleSuccessCount;
    private static ulong _watchdogScheduleLastPlayerId;
    private static int _watchdogScheduleLastRound = -1;
    private static string _watchdogScheduleLastSource = "none";
    private static string? _pendingWakuuAutoSwitchRoundKey;
    private static string? _pendingWakuuAutoSwitchSource;
    private static ulong? _pendingManualEndTurnPlayerId;
    private static int _pendingManualEndTurnRound = -1;

    /// <summary>结束回合按钮自愈（r104）节流：同一场战斗最多尝试间隔 500ms，失败日志每场最多 5 条。</summary>
    private static long _lastEndTurnReconcileAttemptMs;
    private static int _endTurnReconcileLogCount;

    /// <summary>改进-1 跳过回合开始抽牌演出的日志去重（战斗场次变化时清空）。</summary>
    private static readonly HashSet<string> _skipDrawAnimLogged = new HashSet<string>();
    private static int _skipDrawAnimCombatIdentity = int.MinValue;

    public static LocalMultiSessionState SessionState => Session;

    public static void OnRunLaunched(RunState runState)
    {
        LocalWakuuRelicLocalization.Initialize();
        try
        {
            LocalWakuuAutopilotConfig.Reload("run-launched");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库托管配置加载异常(已忽略): {exception.Message}");
        }

        try
        {
            LocalWakuuSafetyNet.EnsureTicker();
        }
        catch (Exception exception)
        {
            // 安全网挂载失败不能影响开局/读档主流程。
            LocalMultiControlLogger.Warn($"安全网挂载异常(已忽略): {exception.Message}");
        }

        LocalMultiControlLogger.Info(
            $"检测到 RunManager.Launch，开始初始化本地多控会话。 players={runState.Players.Count}, floor={runState.TotalFloor}");
        if (LocalSelfCoopContext.IsEnabled)
        {
            RunManager.Instance.CombatStateSynchronizer.IsDisabled = true;
            LocalMultiControlLogger.Info("本地双人模式已禁用战斗同步等待，避免单进程回环阻塞。");
        }

        Session.InitializeFromRunState(runState);
        if (Session.CurrentControlledPlayerId.HasValue)
        {
            ApplyControlContext("run-launched");
        }
        else
        {
            LocalMultiControlLogger.Info("当前运行未启用本地多控会话。");
        }

        // r83：重置托管遗物兜底状态；若上一局/读档前遗物被第三方效果移除，本次会重新补发。
        LocalWakuuRelicRuntime.ResetTakeoverFallbackState();
        TaskHelper.RunSafely(GrantWakuuRelicsAsync(runState));
    }

    public static void OnRunCleanup()
    {
        Session.Reset("RunManager.CleanUp");
        _wakuuAutoEndIssued.Clear();
        _allPlayersAutoEndedRounds.Clear();
        _wakuuToNonWakuuSwitchedRounds.Clear();
        _lastAutoEndCombatIdentity = -1;
        _pendingWakuuAutoSwitchRoundKey = null;
        _pendingWakuuAutoSwitchSource = null;
        _pendingManualEndTurnPlayerId = null;
        _pendingManualEndTurnRound = -1;
        _watchdogScheduleRejectCounts.Clear();
        _flowBlockSignalCounts.Clear();
        _flowBlockSignalDedupeRoundPlayer.Clear();
        _flowBlockSignalWindowStartMs = 0L;
        _watchdogScheduleWindowStartMs = 0L;
        _watchdogScheduleSuccessCount = 0;
        _watchdogScheduleLastPlayerId = 0UL;
        _watchdogScheduleLastRound = -1;
        _watchdogScheduleLastSource = "run-cleanup";
        LocalMerchantInventoryRuntime.Clear();
        LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack("run-cleanup", allowRecover: true);
        LocalSelfCoopContext.Disable("RunManager.CleanUp");
        LocalMultiControlLogger.Info("RunManager.CleanUp 后已完成本地多控会话清理。");
    }

    public static void SwitchNextControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        // 占卜进行中禁止切换：切走后完成回调会因 owner 与本地玩家不符而失败，事件卡死（软锁）
        if (CrystalSphereMirrorRuntime.HasActiveDivinationOverlay())
        {
            LocalMultiControlLogger.Info($"占卜小游戏进行中，已忽略切换请求: source={source}");
            return;
        }

        // 假商人药水投掷结算进行中禁止切换：界面节点正在播台词动画，重建房间会丢失战斗就绪信号
        if (FoulPotionOnUsePatch.IsThrowInProgress)
        {
            LocalMultiControlLogger.Info($"假商人药水投掷结算进行中，已忽略切换请求: source={source}");
            return;
        }

        if (CombatManager.Instance.IsInProgress)
        {
            // 风险点：战斗中如果按“会话顺序”盲切，可能切到不在当前 CombatState 的角色，
            // 进而触发手牌UI与动作队列 owner 不一致，表现为“无法出牌/切到空角色”。
            if (!CanSwitchDuringCombat(source))
            {
                return;
            }

            if (TrySwitchCombatPlayer(next: true, source))
            {
                NoteManualSwitchToWakuu(source);
                return;
            }
        }

        if (Session.SwitchNextPlayer())
        {
            ApplyControlContext(source);
            NoteManualSwitchToWakuu(source);
        }
    }

    /// <summary>
    /// 手动切**到**瓦库角色时，把本回合登记为「已处理过」，避免自动化立刻以
    /// 「瓦库角色无牌可出」把玩家弹回自己（r103）。
    ///
    /// 实机现象：战斗开始前停在瓦库视角 → 进战斗后第一次切角色会被立刻弹回自己
    /// （只看到一次刷新动画），第二次才停住——因为弹回本身是按「每回合一次」登记的，
    /// 第一次弹回把名额用掉了，第二次才没人再弹。这里把「用户手动选择了瓦库」
    /// 也视为该名额已用掉，从第一次起就不再弹。
    /// 只在「该瓦库当前确实没有可出的牌」时登记：有牌可出的情况下自动化本来也不会切走，
    /// 不该抢掉本回合的名额。
    /// </summary>
    private static void NoteManualSwitchToWakuu(string source)
    {
        if (!source.StartsWith("hotkey", StringComparison.Ordinal)
            && !source.StartsWith("player-state", StringComparison.Ordinal))
        {
            return;
        }

        if (!LocalSelfCoopContext.IsEnabled || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        ulong playerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        if (playerId == 0UL || !LocalSelfCoopContext.IsWakuuEnabled(playerId))
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatState == null)
        {
            return;
        }

        Player? player = combatState.GetPlayer(playerId);
        if (player == null || PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay()))
        {
            return;
        }

        RefreshAutoEndTrackingForCombat(combatState);
        if (_wakuuToNonWakuuSwitchedRounds.Add(BuildWakuuSwitchRoundKey(combatState.RoundNumber)))
        {
            LocalMultiControlLogger.Info(
                $"手动切到瓦库角色，本轮不再因「无牌可出」自动切走: player={playerId}, "
                + $"round={combatState.RoundNumber}, source={source}");
        }
    }

    public static void SwitchPreviousControlledPlayer(string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        // 占卜进行中禁止切换（风险同 SwitchNextControlledPlayer）
        if (CrystalSphereMirrorRuntime.HasActiveDivinationOverlay())
        {
            LocalMultiControlLogger.Info($"占卜小游戏进行中，已忽略切换请求: source={source}");
            return;
        }

        // 假商人药水投掷结算进行中禁止切换（风险同 SwitchNextControlledPlayer）
        if (FoulPotionOnUsePatch.IsThrowInProgress)
        {
            LocalMultiControlLogger.Info($"假商人药水投掷结算进行中，已忽略切换请求: source={source}");
            return;
        }

        if (CombatManager.Instance.IsInProgress)
        {
            // 风险点同上；战斗内必须按当前 CombatState 的双角色互切。
            if (!CanSwitchDuringCombat(source))
            {
                return;
            }

            if (TrySwitchCombatPlayer(next: false, source))
            {
                NoteManualSwitchToWakuu(source);
                return;
            }
        }

        if (Session.SwitchPreviousPlayer())
        {
            ApplyControlContext(source);
            NoteManualSwitchToWakuu(source);
        }
    }

    public static void SwitchControlledPlayerTo(ulong playerId, string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (Session.TrySetCurrentPlayer(playerId))
        {
            ApplyControlContext(source);
            NoteManualSwitchToWakuu(source);
        }
    }

    public static void TryRunPendingEventAutoSwitch(string source)
    {
        if (!LocalSelfCoopContext.TryConsumePendingEventAutoSwitch())
        {
            return;
        }

        SwitchNextControlledPlayer(source);
    }

    public static void TryAutoEndTurnForRelicControlledPlayer()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            return;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        if (RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            return;
        }

        RefreshAutoEndTrackingForCombat(combatState);
        TryConsumePendingWakuuAutoSwitch(combatState);
        if (LocalManualPlayGuard.IsActive)
        {
            return;
        }

        TryAutoSwitchFromWakuuWhenAllWakuuNoPlayableCards(combatState, "wakuu-no-playable-cards-tick");

        bool anyWakuuPlayerHasPlayableCards = false;
        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive)
            {
                continue;
            }

            bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());

            if (LocalWakuuRelicRuntime.HasWakuuRelic(player) && hasPlayableCards)
            {
                anyWakuuPlayerHasPlayableCards = true;
                bool scheduled = LocalWakuuRelicRuntime.TryScheduleWatchdog(player, "combat-watchdog", out string reason);
                RecordWatchdogScheduleResult(scheduled, reason, player.NetId, combatState.RoundNumber, "combat-watchdog");
            }
        }

        if (anyWakuuPlayerHasPlayableCards)
        {
            _allPlayersAutoEndedRounds.Remove(combatState.RoundNumber);
        }
    }

    public static bool TryManualEndTurnAutoCloseAllPlayers()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            return false;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return false;
        }

        if (RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            return false;
        }

        RefreshAutoEndTrackingForCombat(combatState);

        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive)
            {
                continue;
            }

            bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
            if (hasPlayableCards)
            {
                LocalMultiControlLogger.Info($"玩家主动结束回合时检测到仍有可出牌角色，不触发全员结束: round={combatState.RoundNumber}");
                return false;
            }
        }

        return TryEndAllPlayersWhenNoCards(combatState, "manual-end-turn");
    }

    private static bool TryEndAllPlayersWhenNoCards(CombatState combatState, string source)
    {
        if (_allPlayersAutoEndedRounds.Contains(combatState.RoundNumber))
        {
            return false;
        }

        bool endedAnyPlayer = false;
        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive)
            {
                continue;
            }

            if (CombatManager.Instance.IsPlayerReadyToEndTurn(player))
            {
                continue;
            }

            string key = $"{_lastAutoEndCombatIdentity}:{combatState.RoundNumber}:{player.NetId}";
            if (!_wakuuAutoEndIssued.Add(key))
            {
                continue;
            }

            MegaCrit.Sts2.Core.Commands.PlayerCmd.EndTurn(player, canBackOut: false);
            endedAnyPlayer = true;
        }

        if (endedAnyPlayer)
        {
            _allPlayersAutoEndedRounds.Add(combatState.RoundNumber);
            LocalMultiControlLogger.Info($"检测到全员无牌可出，已自动结束全部角色回合: round={combatState.RoundNumber}, source={source}");
        }

        return endedAnyPlayer;
    }

    private static void RefreshAutoEndTrackingForCombat(CombatState combatState)
    {
        int combatIdentity = RuntimeHelpers.GetHashCode(combatState);
        if (_lastAutoEndCombatIdentity == combatIdentity)
        {
            return;
        }

        _lastAutoEndCombatIdentity = combatIdentity;
        _wakuuAutoEndIssued.Clear();
        _allPlayersAutoEndedRounds.Clear();
        _wakuuToNonWakuuSwitchedRounds.Clear();
        _pendingWakuuAutoSwitchRoundKey = null;
        _pendingWakuuAutoSwitchSource = null;
        _pendingManualEndTurnPlayerId = null;
        _pendingManualEndTurnRound = -1;
        _lastEndTurnReconcileAttemptMs = 0L;
        _endTurnReconcileLogCount = 0;
        LocalMultiControlLogger.Info($"检测到战斗场次切换，重置瓦库自动结束回合状态: combat={combatIdentity}");
        LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack($"combat-switch-{combatIdentity}", allowRecover: true);
    }

    public static void RecordManualEndTurnIntent(ulong playerId, string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        _pendingManualEndTurnPlayerId = playerId;
        _pendingManualEndTurnRound = combatState?.RoundNumber ?? -1;
        LocalMultiControlLogger.Info($"已记录手动结束回合意图: player={playerId}, round={_pendingManualEndTurnRound}, source={source}");
    }

    /// <summary>
    /// 为瓦库名单内的玩家发放托管遗物（已持有则跳过，可重复调用）。
    /// r83 起同时作为"遗物被第三方效果移除后的补发入口"。
    /// </summary>
    public static async Task GrantWakuuRelicsAsync(RunState runState)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        List<ulong> wakuuPlayerIds = LocalSelfCoopContext.GetWakuuPlayerIdsSnapshot();
        if (wakuuPlayerIds.Count == 0)
        {
            return;
        }

        foreach (ulong playerId in wakuuPlayerIds)
        {
            Player? player = runState.GetPlayer(playerId);
            if (player == null)
            {
                continue;
            }

            bool useForm = LocalWakuuAutopilotConfig.UseVakuuForm;
            RelicModel? existing = useForm
                ? (RelicModel?)LocalWakuuRelicRuntime.TryGetWakuuFormRelic(player)
                : LocalWakuuRelicRuntime.TryGetWakuuRelic(player);
            if (existing != null)
            {
                continue;
            }

            // 跨开关切换旧存档时可能两件遗物并存（+1 能量叠加），打日志说明即可，不做自动拆除。
            RelicModel? other = useForm
                ? (RelicModel?)LocalWakuuRelicRuntime.TryGetWakuuRelic(player)
                : (RelicModel?)LocalWakuuRelicRuntime.TryGetWakuuFormRelic(player);
            if (other != null)
            {
                LocalMultiControlLogger.Warn(
                    $"检测到玩家同时持有两种瓦库遗物（跨开关切换存档导致），能量将叠加: player={playerId}, "
                    + $"mode={(useForm ? "瓦库形态" : "永久低语耳环")}");
            }

            RelicModel granted = useForm
                ? ModelDb.Relic<LocalWakuuFormRelic>().ToMutable()
                : ModelDb.Relic<LocalWakuuStarterRelic>().ToMutable();
            granted.FloorAddedToDeck = Math.Max(1, runState.TotalFloor);
            player.AddRelicInternal(granted);
            SaveManager.Instance.MarkRelicAsSeen(granted);
            await granted.AfterObtained();
            LocalMultiControlLogger.Info(
                $"已为瓦库角色自动发放托管遗物: player={playerId}, relic={granted.Id.Entry}, mode={(useForm ? "瓦库形态" : "永久低语耳环")}");
        }
    }

    private static void ApplyControlContext(string source)
    {
        ulong? currentControlledPlayerId = Session.CurrentControlledPlayerId;
        if (!currentControlledPlayerId.HasValue)
        {
            return;
        }

        LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack($"apply-control-before-{source}", allowRecover: true);

        if (CombatManager.Instance.IsInProgress)
        {
            NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
            CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
            if (combatState != null && combatState.GetPlayer(currentControlledPlayerId.Value) == null)
            {
                LocalMultiControlLogger.Warn($"检测到无效战斗角色ID，回退到1号位: {currentControlledPlayerId.Value}");
                ulong fallbackPlayerId = Session.OrderedPlayerIds.FirstOrDefault();
                if (fallbackPlayerId != 0 && Session.TrySetCurrentPlayer(fallbackPlayerId))
                {
                    currentControlledPlayerId = Session.CurrentControlledPlayerId;
                }

                if (!currentControlledPlayerId.HasValue || combatState.GetPlayer(currentControlledPlayerId.Value) == null)
                {
                    return;
                }
            }
        }

        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = currentControlledPlayerId.Value;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(currentControlledPlayerId.Value);
        SyncRunSynchronizerLocalPlayerId(currentControlledPlayerId.Value);

        bool combatUiRefreshSucceeded = RefreshCombatUiForControlledPlayer(currentControlledPlayerId.Value);
        if (CombatManager.Instance.IsInProgress && !combatUiRefreshSucceeded)
        {
            // 进行中的出牌/选牌流程只是瞬时占用，战斗UI稍后可安全重建。此时不再回滚上下文，
            // 保持已切换的 LocalContext/NetId，并延后到流程结束后补刷战斗UI与回合结束按钮状态，
            // 否则切人后回合结束按钮会消失/回滚且选牌弹窗展示错误角色的手牌。
            if (IsCombatUiInPickFlow())
            {
                ScheduleDeferredCombatUiRefresh(currentControlledPlayerId.Value);
            }
            else
            {
                // 非瞬时失败：若 LocalContext 已切换但战斗UI刷新失败，会导致“逻辑 owner 与显示 owner”分离，
                // 该状态会把后续出牌入队到错误玩家队列，因此这里必须立即回滚上下文。
                LocalMultiControlLogger.Warn($"控制上下文切换回滚：战斗UI刷新失败，target={currentControlledPlayerId.Value}");
                LocalContext.NetId = previousNetId;
                if (previousNetId.HasValue)
                {
                    LocalSelfCoopContext.NetService?.SetCurrentSenderId(previousNetId.Value);
                    SyncRunSynchronizerLocalPlayerId(previousNetId.Value);
                }

                return;
            }
        }

        RefreshTopBarForControlledPlayer(currentControlledPlayerId.Value);
        RefreshDeckViewForControlledPlayer(currentControlledPlayerId.Value);
        RefreshRestSiteForControlledPlayer(currentControlledPlayerId.Value);
        // 切人前先收掉其他角色遗留的"已完成占卜"水晶球弹层，避免挡住当前角色的事件界面；
        // 同时吃掉待触发的事件自动切换标记，防止与本次手动/流程切换来回跳。
        CrystalSphereMirrorRuntime.CloseFinishedMinigameOverlays($"apply-control-{source}", consumePendingSwitch: true);
        RefreshEventRoomForControlledPlayer(currentControlledPlayerId.Value);
        LocalMerchantInventoryRuntime.RefreshShopRoomForPlayer(currentControlledPlayerId.Value);
        EnsureTreasureCursorVisibleAfterSwitch(source);
        LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack($"apply-control-after-{source}", allowRecover: true);
        LocalMultiControlLogger.Info($"控制上下文已更新: {previousNetId?.ToString() ?? "null"} -> {currentControlledPlayerId.Value}, source={source}");
        if (source != "run-launched" && !source.StartsWith("wakuu-", StringComparison.Ordinal))
        {
            string slotLabel = LocalSelfCoopContext.GetSlotLabel(currentControlledPlayerId.Value);
            NGame.Instance?.AddChildSafely(NFullscreenTextVfx.Create(LocalModText.ControlledSlot(slotLabel)));
        }
    }

    private static void SyncRunSynchronizerLocalPlayerId(ulong playerId)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        ulong eventOwnerPlayerId = LocalSelfCoopContext.UseSingleEventFlow
            ? LocalSelfCoopContext.PrimaryPlayerId
            : playerId;
        TrySetLocalPlayerId(RunManager.Instance.EventSynchronizer, eventOwnerPlayerId, nameof(RunManager.EventSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RewardsSetSynchronizer, playerId, nameof(RunManager.RewardsSetSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RewardSynchronizer, playerId, nameof(RunManager.RewardSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.RestSiteSynchronizer, playerId, nameof(RunManager.RestSiteSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.OneOffSynchronizer, playerId, nameof(RunManager.OneOffSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.TreasureRoomRelicSynchronizer, playerId, nameof(RunManager.TreasureRoomRelicSynchronizer));
        TrySetLocalPlayerId(RunManager.Instance.FlavorSynchronizer, playerId, nameof(RunManager.FlavorSynchronizer));
    }

    public static void AlignContextForActionOwner(ulong playerId, string source)
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return;
        }

        if (LocalContext.NetId == playerId)
        {
            SyncRunSynchronizerLocalPlayerId(playerId);
            return;
        }

        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = playerId;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId);
        SyncRunSynchronizerLocalPlayerId(playerId);
        LocalMultiControlLogger.Warn(
            $"检测到手动出牌上下文漂移，已强制校正: {previousNetId?.ToString() ?? "null"} -> {playerId}, source={source}");
    }

    /// <summary>
    /// 改进-1：是否跳过该玩家在**回合开始**的前台切换（等价于跳过其抽牌演出）。
    ///
    /// 本地多控下回合开始会依次把前台切到每个真人玩家、逐个播完自动抽牌动画再切下一个；
    /// 满员时太慢。开启配置后只保留「当前正在看的那位」的演出，其他人不切前台 ——
    /// 原版对非本地玩家（<c>LocalContext.IsMe == false</c>）的 Draw→Hand 本来就不建卡牌节点、
    /// 不做补间（CardPileCmd.GetTweenForCardsChangingPiles 里 <c>IsMe</c> 为假且不涉及 Play 堆
    /// 就直接 continue），所以其抽牌瞬时生效、数据完全照常；之后切到该角色时
    /// <c>RefreshCombatUiForControlledPlayer</c> 会按手牌区重建 UI，手牌完整可见。
    ///
    /// 只在**回合开始**（<c>SetupPlayerTurn</c>）这一条路径上调用：回合结束 / 弃牌
    /// （<c>DoTurnEnd</c> / <c>FlushPlayerHand</c>）不适用本开关，保持既有观感。
    /// 判定口径见 <see cref="TurnStartDrawAnimPolicy"/>。
    /// </summary>
    internal static bool ShouldSkipTurnStartDrawAnimationFor(Player player, string source)
    {
        if (player?.Creature?.CombatState == null)
        {
            return false;
        }

        // 改进-2：后台托管的瓦库形态角色**不由本开关管辖** —— 它的回合开始是否切前台由
        // 「瓦库托管视角」档位统一决定（不跟随 = 已被 WakuuViewPolicy 抑制在前；仅关键节点 = peek；
        // 全程跟随 = 正常切）。不加这条的话 keyNodes 的回合开始跟随会被本开关静默吃掉
        // （实机 marker r107 日志实证：整段 keyNodes 只有本开关的跳过日志、没有任何 turn-start 切前台）。
        if (LocalWakuuRelicRuntime.IsBackgroundHostedWakuu(player))
        {
            return false;
        }

        ulong foregroundPlayerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        if (!TurnStartDrawAnimPolicy.ShouldSkipSwitch(
                toggleEnabled: LocalWakuuAutopilotConfig.SkipTurnStartDrawAnim,
                localMultiControlEnabled: LocalSelfCoopContext.IsEnabled,
                foregroundPlayerId: foregroundPlayerId,
                playerId: player.NetId))
        {
            return false;
        }

        // 日志：每场战斗每人每回合一条，便于核对「谁被跳过了、当时前台是谁」
        int combatIdentity = RuntimeHelpers.GetHashCode(player.Creature.CombatState);
        if (combatIdentity != _skipDrawAnimCombatIdentity)
        {
            _skipDrawAnimCombatIdentity = combatIdentity;
            _skipDrawAnimLogged.Clear();
        }

        int round = player.PlayerCombatState?.TurnNumber ?? -1;
        string key = $"{combatIdentity}:{round}:{player.NetId}";
        if (_skipDrawAnimLogged.Add(key))
        {
            LocalMultiControlLogger.Info(
                $"已跳过回合开始抽牌演出（非前台玩家，改进-1）: player={player.NetId}, "
                + $"foreground={foregroundPlayerId}, round={round}, source={source}");
        }

        return true;
    }

    /// <summary>「仅关键节点」peek 后自动切回原视角的延时（秒）。</summary>
    private const double PeekReturnDelaySeconds = 1.2;

    /// <summary>
    /// 改进-2「仅关键节点」peek：瓦库回合开始切过去看一眼后，延时自动切回**原先的真人视角**。
    /// 只在「前台仍是那个瓦库」（说明用户没手动切走）、原前台是本地玩家、且当前无进行中的
    /// 出牌/选牌/瞄准流程时才切回；任一条件不满足就放弃（宁可不切，也不打断用户操作）。
    /// </summary>
    public static void ScheduleReturnToForegroundAfterPeek(
        ulong peekPlayerId,
        ulong? previousPlayerId,
        string source)
    {
        if (!previousPlayerId.HasValue || previousPlayerId.Value == peekPlayerId)
        {
            return;
        }

        SceneTree? sceneTree = NGame.Instance?.GetTree();
        if (sceneTree == null)
        {
            return;
        }

        ulong returnPlayerId = previousPlayerId.Value;
        sceneTree.CreateTimer(PeekReturnDelaySeconds).Timeout += () =>
        {
            try
            {
                if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
                {
                    return;
                }

                if (!LocalSelfCoopContext.LocalPlayerIds.Contains(returnPlayerId))
                {
                    return;
                }

                // 用户已经手动切走（或又切回瓦库自己操作）→ 不抢视角。
                if (Session.CurrentControlledPlayerId != peekPlayerId)
                {
                    return;
                }

                NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
                if (combatUi != null)
                {
                    NPlayerHand hand = combatUi.Hand;
                    if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
                    {
                        return;
                    }
                }

                LocalMultiControlLogger.Info(
                    $"仅关键节点：瓦库回合开始已看过，自动切回原视角: {peekPlayerId} -> {returnPlayerId}, source={source}");
                SwitchControlledPlayerTo(returnPlayerId, $"wakuu-peek-return-{source}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn($"仅关键节点自动切回原视角失败: {exception.Message}");
            }
        };
    }

    public static bool TryEnsureForegroundForPlayer(Player player, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        if (player?.Creature == null || player.Creature.CombatState == null)
        {
            return false;
        }

        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(player.NetId))
        {
            return false;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            LocalMultiControlLogger.Info(
                $"自动切前台延后（当前有进行中的出牌/选牌/瞄准流程）: target={player.NetId}, source={source}");
            return false;
        }

        ulong previousPlayerId = Session.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? LocalSelfCoopContext.PrimaryPlayerId;
        if (previousPlayerId == player.NetId)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"检测到后台角色触发战斗效果/选牌，自动切换前台: from={previousPlayerId}, to={player.NetId}, source={source}");

        if (!Session.TrySetCurrentPlayer(player.NetId))
        {
            return false;
        }

        ApplyControlContext($"auto-foreground-{source}");

        bool switched = Session.CurrentControlledPlayerId == player.NetId && LocalContext.NetId == player.NetId;
        if (!switched)
        {
            LocalMultiControlLogger.Warn(
                $"自动切前台失败，已回滚会话控制索引: target={player.NetId}, source={source}");
            Session.TrySetCurrentPlayer(previousPlayerId);
        }

        return switched;
    }

    /// <summary>
    /// 按玩家ID解析当前战斗中的 Player（仅战斗进行中有效）。供 hook 入队等仅有 NetId 的挂点使用。
    /// </summary>
    /// <summary>
    /// 确保奖励界面弹出时没有被地图/角色面板遮挡。
    /// NOverlayStack.Push 在 StackIsCovered（NMapScreen 打开或 NCapstoneContainer 占用）时
    /// 会立即调用 screen.AfterOverlayHidden() 把弹层 Visible=false——读档重放路径会先恢复
    /// 地图/界面状态，导致战后奖励界面"黑屏"（弹层在栈上但不可见，仅模组自有按钮可见）。
    /// </summary>
    public static void EnsureOverlayNotCoveredForRewards(string source)
    {
        try
        {
            NMapScreen? map = NMapScreen.Instance;
            bool mapOpen = map != null && map.IsOpen;
            bool capstoneInUse = NCapstoneContainer.Instance?.InUse ?? false;
            LocalMultiControlLogger.Info(
                $"奖励遮挡检查({source}): mapOpen={mapOpen}, capstoneInUse={capstoneInUse}");
            if (mapOpen && map != null)
            {
                map.Close(animateOut: false);
                LocalMultiControlLogger.Info($"奖励弹出前关闭处于打开状态的地图界面: source={source}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"奖励遮挡检查异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 当前是否处于读档重放的转场黑幕遮盖期。
    /// 原版读档链路：StartRun 先 FadeOut 变黑，随后 LoadRun 内部进入 PreFinishedRoom
    /// 并重放战后奖励，全部完成后才 FadeIn 亮屏。若此期间任何代码同步等待玩家
    /// 操作（如我们的合并奖励界面 await），FadeIn 将永远无法执行，表现为永久黑屏。
    /// 原版自身对此的处理是 reward.Offer() fire-and-forget 不等待。
    /// </summary>
    public static bool IsLoadReplayTransitionCovering()
    {
        try
        {
            NTransition? transition = NGame.Instance?.Transition;
            return transition != null && transition.Visible && transition.InTransition;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 打印某个控件从自身到场景根的可见性链（Visible/透明度/位置/尺寸），
    /// 用于定位"逻辑存在但渲染不可见"的黑屏类问题。
    /// </summary>
    public static void DumpControlVisibilityChain(Godot.Control? control, string source)
    {
        try
        {
            if (control == null)
            {
                LocalMultiControlLogger.Info($"可见性诊断({source}): 控件为 null");
                return;
            }

            List<string> chain = new();
            Godot.Node node = control;
            int depth = 0;
            while (node != null && depth < 24)
            {
                string entry = node switch
                {
                    Godot.Control c => $"{node.GetType().Name}[{node.Name}] Visible={c.Visible} ModulateA={c.Modulate.A:F2} Scale={c.Scale} Pos={c.Position} Size={c.Size}",
                    Godot.CanvasLayer l => $"{node.GetType().Name}[{node.Name}] Layer={l.Layer} Visible={l.Visible}",
                    _ => $"{node.GetType().Name}[{node.Name}]"
                };
                chain.Add(entry);
                node = node.GetParent();
                depth++;
            }

            LocalMultiControlLogger.Info($"可见性诊断({source}): {string.Join(" <- ", chain)}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"可见性诊断异常(已忽略): {exception.Message}");
        }
    }

    /// <summary>
    /// 扫描整棵场景树里的转场黑幕（NTransition）与加载遮罩（NLoadingOverlay）节点，
    /// 输出其可见状态——用于诊断"逻辑正常但画面全黑"的读档问题：
    /// 若 FadeOut 后配对的 FadeIn 未执行，NTransition 这个全屏 ColorRect 会永久盖住画面。
    /// </summary>
    public static void DumpTransitionOverlayState(string source)
    {
        try
        {
            Godot.Node? game = NGame.Instance;
            if (game == null)
            {
                LocalMultiControlLogger.Info($"转场扫描({source}): NGame 不存在");
                return;
            }

            List<string> found = new();
            CollectTransitionNodes(game, found, source);
            if (found.Count == 0)
            {
                LocalMultiControlLogger.Info($"转场扫描({source}): 场景中无 NTransition/NLoadingOverlay 节点");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"转场扫描异常(已忽略): {exception.Message}");
        }
    }

    private static void CollectTransitionNodes(Godot.Node node, List<string> found, string source)
    {
        string typeName = node.GetType().Name;
        if (typeName == "NTransition" && node is Godot.ColorRect rect)
        {
            float simpleAlpha = -1f;
            float gradientAlpha = -1f;
            foreach (Godot.Node child in node.GetChildren())
            {
                if (child is Godot.Control cc && cc.Name == "SimpleTransition")
                {
                    simpleAlpha = cc.Modulate.A;
                }
                else if (child is Godot.Control gc && gc.Name == "GradientTransition")
                {
                    gradientAlpha = gc.Modulate.A;
                }
            }

            found.Add("hit");
            LocalMultiControlLogger.Info(
                $"转场扫描({source}): NTransition Visible={rect.Visible} ModulateA={rect.Modulate.A:F2} "
                + $"SimpleA={simpleAlpha:F2} GradientA={gradientAlpha:F2} Size={rect.Size} InTree={node.IsInsideTree()}");
        }
        else if (typeName == "NLoadingOverlay" && node is Godot.Control loading)
        {
            found.Add("hit");
            LocalMultiControlLogger.Info(
                $"转场扫描({source}): NLoadingOverlay Visible={loading.Visible} ModulateA={loading.Modulate.A:F2} Size={loading.Size}");
        }

        foreach (Godot.Node child in node.GetChildren())
        {
            CollectTransitionNodes(child, found, source);
        }
    }

    /// <summary>
    /// 当前前台（受控）玩家——即屏幕上正在显示的那位。
    /// 注意与 <see cref="LocalContext"/> 区分：LocalContext 会为「瓦库后台出牌的动作归属」临时漂移，
    /// 前台归属只认本 mod 的会话状态（第三方 UI 归属用它，见 SecondaryResourceCombatUiOwnerPatch）。
    /// </summary>
    internal static Player? TryGetForegroundPlayer()
    {
        ulong playerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        return playerId == 0UL ? null : TryGetCombatPlayer(playerId);
    }

    internal static Player? TryGetCombatPlayer(ulong playerId)
    {
        if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return null;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return null;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        return combatState?.GetPlayer(playerId);
    }

    /// <summary>
    /// 按玩家ID把前台/控制上下文切到指定角色，适用于只有 NetId、没有现成 Player 引用的挂点
    /// （如 ActionQueueSynchronizer.EnqueueHookAction 入队瞬间，仅有 GenericHookGameAction.OwnerId）。
    /// </summary>
    internal static bool TryEnsureForegroundForPlayerId(ulong playerId, string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        if (!LocalSelfCoopContext.LocalPlayerIds.Contains(playerId))
        {
            return false;
        }

        Player? player = TryGetCombatPlayer(playerId);
        if (player == null)
        {
            return false;
        }

        return TryEnsureForegroundForPlayer(player, source);
    }

    private static void TrySetLocalPlayerId(object? target, ulong playerId, string componentName)
    {
        if (target == null)
        {
            return;
        }

        try
        {
            AccessTools.Field(target.GetType(), "_localPlayerId")?.SetValue(target, playerId);
        }
        catch (Exception exception)
        {
            string key = $"{componentName}:{target.GetType().Name}";
            if (_fieldSyncFailures.Add(key))
            {
                LocalMultiControlLogger.Warn($"同步 {key} 的 _localPlayerId 失败: {exception.Message}");
            }
        }
    }

    public static void TryAutoSwitchAfterEndTurn(ulong endedPlayerId)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        bool matchedManualEndTurn = TryConsumeManualEndTurnIntent(endedPlayerId);
        if (!matchedManualEndTurn && Session.CurrentControlledPlayerId != endedPlayerId)
        {
            LocalMultiControlLogger.Info(
                $"跳过结束回合后自动切人：ended={endedPlayerId}, controlled={Session.CurrentControlledPlayerId?.ToString() ?? "null"}, manualMatched={matchedManualEndTurn}");
            return;
        }

        if (CombatManager.Instance.AllPlayersReadyToEndTurn())
        {
            LocalMultiControlLogger.Info("所有角色均已结束回合，跳过自动切换，等待敌方回合推进。");
            return;
        }

        LocalMultiControlLogger.Info($"检测到角色 {endedPlayerId} 结束回合，自动切换到下一位。");
        Callable.From(delegate
        {
            if (TrySwitchToNextOperableNonWakuuPlayerWhenAllWakuuNoPlayableCards(endedPlayerId, "auto-end-turn-all-vakuu-no-cards"))
            {
                return;
            }

            if (TrySwitchToNextPlayablePlayer(endedPlayerId, "auto-end-turn-next-playable"))
            {
                return;
            }

            NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
            CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
            if (combatState != null)
            {
                TryEndAllPlayersWhenNoCards(combatState, "auto-end-turn-fallback");
            }
        }).CallDeferred();
    }

    private static bool TrySwitchToNextOperableNonWakuuPlayerWhenAllWakuuNoPlayableCards(ulong currentPlayerId, string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatState == null)
        {
            return false;
        }

        bool hasWakuuPlayer = false;
        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive)
            {
                continue;
            }

            if (!LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
            {
                continue;
            }

            hasWakuuPlayer = true;
            bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
            if (hasPlayableCards)
            {
                return false;
            }
        }

        if (!hasWakuuPlayer)
        {
            return false;
        }

        return TrySwitchToNextOperableNonWakuuPlayer(currentPlayerId, source);
    }

    private static bool TryAutoSwitchFromWakuuWhenAllWakuuNoPlayableCards(CombatState combatState, string source)
    {
        string roundKey = BuildWakuuSwitchRoundKey(combatState.RoundNumber);
        if (_wakuuToNonWakuuSwitchedRounds.Contains(roundKey))
        {
            return false;
        }

        ulong currentPlayerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        if (currentPlayerId == 0 || !LocalSelfCoopContext.IsWakuuEnabled(currentPlayerId))
        {
            return false;
        }

        bool hasAliveWakuu = false;
        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive || !LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
            {
                continue;
            }

            hasAliveWakuu = true;
            bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
            if (hasPlayableCards)
            {
                return false;
            }
        }

        if (!hasAliveWakuu)
        {
            return false;
        }

        bool switched = TrySwitchToNextOperableNonWakuuPlayer(currentPlayerId, source);
        if (switched)
        {
            _wakuuToNonWakuuSwitchedRounds.Add(roundKey);
            LocalMultiControlLogger.Info($"检测到所有瓦库角色无牌可出，已自动切换到非瓦库角色: from={currentPlayerId}");
        }

        return switched;
    }

    public static bool TryAutoSwitchToNonWakuuOncePerRound(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        if (!CanSwitchDuringCombat(source))
        {
            return false;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatState == null)
        {
            return false;
        }

        RefreshAutoEndTrackingForCombat(combatState);
        string roundKey = BuildWakuuSwitchRoundKey(combatState.RoundNumber);
        if (_wakuuToNonWakuuSwitchedRounds.Contains(roundKey))
        {
            return false;
        }

        ulong currentPlayerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        if (currentPlayerId == 0 || !LocalSelfCoopContext.IsWakuuEnabled(currentPlayerId))
        {
            return false;
        }

        bool hasAliveWakuu = false;
        foreach (Player player in combatState.Players)
        {
            if (player?.Creature == null || !player.Creature.IsAlive || !LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
            {
                continue;
            }

            hasAliveWakuu = true;
            if (PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay()))
            {
                return false;
            }
        }

        if (!hasAliveWakuu)
        {
            return false;
        }

        bool switched = TrySwitchToNextOperableNonWakuuPlayer(currentPlayerId, $"{source}-once-per-round");
        if (!switched)
        {
            return false;
        }

        _wakuuToNonWakuuSwitchedRounds.Add(roundKey);
        LocalMultiControlLogger.Info($"瓦库自动切非瓦库（每回合一次）已触发: round={combatState.RoundNumber}, from={currentPlayerId}, source={source}");
        return true;
    }

    public static void RequestAutoSwitchToNonWakuuOncePerRound(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        RefreshAutoEndTrackingForCombat(combatState);
        string roundKey = BuildWakuuSwitchRoundKey(combatState.RoundNumber);
        if (_wakuuToNonWakuuSwitchedRounds.Contains(roundKey))
        {
            return;
        }

        _pendingWakuuAutoSwitchRoundKey = roundKey;
        _pendingWakuuAutoSwitchSource = source;
        LocalMultiControlLogger.Info($"已登记瓦库自动切非瓦库请求: round={combatState.RoundNumber}, source={source}");
    }

    private static bool CanSwitchDuringCombat(string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            LocalMultiControlLogger.Info($"忽略切换请求({source})：战斗UI未就绪。");
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false))
        {
            // 风险点：在拖牌、目标选择、选牌UI过程中切换，会把 NCardPlay/选择上下文中途打断，
            // 容易触发 NMouseCardPlay._ExitTree 空引用，以及动作队列进入 cancel-all 状态。
            LocalMultiControlLogger.Info($"忽略切换请求({source})：当前存在进行中的出牌/选牌操作。");
            return false;
        }

        ActionSynchronizerCombatState combatSyncState = RunManager.Instance.ActionQueueSynchronizer.CombatState;
        if (combatSyncState != ActionSynchronizerCombatState.PlayPhase)
        {
            // 风险点：非 PlayPhase 期间切换 owner，动作会被延迟/拒绝入队，造成“按牌无反应”。
            LocalMultiControlLogger.Info($"忽略切换请求({source})：战斗同步阶段={combatSyncState}。");
            return false;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            LocalMultiControlLogger.Info($"忽略切换请求({source})：当前不在玩家出牌阶段。");
            return false;
        }

        return true;
    }

    private static bool TrySwitchCombatPlayer(bool next, string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null)
        {
            return false;
        }

        List<ulong> combatPlayerIds = combatState.Players.Select((player) => player.NetId).Distinct().ToList();
        if (combatPlayerIds.Count < 2)
        {
            // 风险点：当前实现只保证“双角色本地多控”，人数异常时继续切换会引入不可预期 owner 绑定。
            LocalMultiControlLogger.Warn($"战斗角色切换需要至少2名玩家，当前数量={combatPlayerIds.Count}");
            return false;
        }

        ulong currentPlayerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? combatPlayerIds[0];
        int currentIndex = combatPlayerIds.IndexOf(currentPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        int targetIndex = (currentIndex + (next ? 1 : combatPlayerIds.Count - 1)) % combatPlayerIds.Count;
        ulong targetPlayerId = combatPlayerIds[targetIndex];
        if (targetPlayerId == currentPlayerId)
        {
            return false;
        }

        if (!Session.TrySetCurrentPlayer(targetPlayerId))
        {
            return false;
        }

        ApplyControlContext(source);
        return true;
    }

    private static bool TrySwitchToNextOperableNonWakuuPlayer(ulong currentPlayerId, string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null)
        {
            return false;
        }

        List<ulong> combatPlayerIds = combatState.Players.Select((player) => player.NetId).Distinct().ToList();
        if (combatPlayerIds.Count < 2)
        {
            return false;
        }

        int currentIndex = combatPlayerIds.IndexOf(currentPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        for (int offset = 1; offset < combatPlayerIds.Count; offset++)
        {
            int targetIndex = (currentIndex + offset) % combatPlayerIds.Count;
            ulong targetPlayerId = combatPlayerIds[targetIndex];
            Player? targetPlayer = combatState.GetPlayer(targetPlayerId);
            if (targetPlayer?.Creature == null || !targetPlayer.Creature.IsAlive)
            {
                continue;
            }

            if (CombatManager.Instance.IsPlayerReadyToEndTurn(targetPlayer))
            {
                continue;
            }

            if (LocalSelfCoopContext.IsWakuuEnabled(targetPlayerId))
            {
                continue;
            }

            if (!Session.TrySetCurrentPlayer(targetPlayerId))
            {
                return false;
            }

            ApplyControlContext(source);
            LocalMultiControlLogger.Info($"结束回合后优先切换到可操作非瓦库角色: {currentPlayerId} -> {targetPlayerId}");
            return true;
        }

        return false;
    }

    private static bool TrySwitchToNextPlayablePlayer(ulong currentPlayerId, string source)
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null)
        {
            return false;
        }

        List<ulong> combatPlayerIds = combatState.Players.Select((player) => player.NetId).Distinct().ToList();
        if (combatPlayerIds.Count < 2)
        {
            return false;
        }

        int currentIndex = combatPlayerIds.IndexOf(currentPlayerId);
        if (currentIndex < 0)
        {
            currentIndex = 0;
        }

        for (int offset = 1; offset < combatPlayerIds.Count; offset++)
        {
            int targetIndex = (currentIndex + offset) % combatPlayerIds.Count;
            ulong targetPlayerId = combatPlayerIds[targetIndex];
            Player? targetPlayer = combatState.GetPlayer(targetPlayerId);
            if (targetPlayer?.Creature == null || !targetPlayer.Creature.IsAlive)
            {
                continue;
            }

            if (CombatManager.Instance.IsPlayerReadyToEndTurn(targetPlayer))
            {
                continue;
            }

            bool hasPlayableCards = PileType.Hand.GetPile(targetPlayer).Cards.Any((card) => card.CanPlay());
            if (!hasPlayableCards)
            {
                continue;
            }

            if (!Session.TrySetCurrentPlayer(targetPlayerId))
            {
                return false;
            }

            ApplyControlContext(source);
            LocalMultiControlLogger.Info($"结束回合后已切换到下一个可出牌角色: {currentPlayerId} -> {targetPlayerId}");
            return true;
        }

        return false;
    }

    private static string BuildWakuuSwitchRoundKey(int roundNumber)
    {
        return $"{_lastAutoEndCombatIdentity}:{roundNumber}";
    }

    private static bool TryConsumeManualEndTurnIntent(ulong endedPlayerId)
    {
        if (!_pendingManualEndTurnPlayerId.HasValue)
        {
            return false;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        int round = combatState?.RoundNumber ?? -1;
        bool roundMatches = _pendingManualEndTurnRound < 0 || _pendingManualEndTurnRound == round;
        bool matched = roundMatches && _pendingManualEndTurnPlayerId.Value == endedPlayerId;

        if (!matched && round != _pendingManualEndTurnRound)
        {
            _pendingManualEndTurnPlayerId = null;
            _pendingManualEndTurnRound = -1;
            return false;
        }

        if (!matched)
        {
            return false;
        }

        _pendingManualEndTurnPlayerId = null;
        _pendingManualEndTurnRound = -1;
        return true;
    }

    private static void TryConsumePendingWakuuAutoSwitch(CombatState combatState)
    {
        string currentRoundKey = BuildWakuuSwitchRoundKey(combatState.RoundNumber);
        if (_pendingWakuuAutoSwitchRoundKey == null)
        {
            return;
        }

        if (_pendingWakuuAutoSwitchRoundKey != currentRoundKey)
        {
            _pendingWakuuAutoSwitchRoundKey = null;
            _pendingWakuuAutoSwitchSource = null;
            return;
        }

        if (_wakuuToNonWakuuSwitchedRounds.Contains(currentRoundKey))
        {
            _pendingWakuuAutoSwitchRoundKey = null;
            _pendingWakuuAutoSwitchSource = null;
            return;
        }

        if (LocalManualPlayGuard.IsActive)
        {
            return;
        }

        string source = _pendingWakuuAutoSwitchSource ?? "wakuu-pending";
        bool switched = TryAutoSwitchToNonWakuuOncePerRound($"{source}-retry");
        if (!switched)
        {
            return;
        }

        _pendingWakuuAutoSwitchRoundKey = null;
        _pendingWakuuAutoSwitchSource = null;
    }

    private static CombatState? TryGetCombatState(NCombatUi combatUi)
    {
        return AccessTools.Field(typeof(NEndTurnButton), "_combatState")?.GetValue(combatUi.EndTurnButton) as CombatState;
    }

    private static bool IsCombatUiInPickFlow()
    {
        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return false;
        }

        NPlayerHand hand = combatUi.Hand;
        return hand.InCardPlay || hand.IsInCardSelection || (NTargetManager.Instance?.IsInSelection ?? false);
    }

    private const int MaxCombatUiRefreshDeferTries = 40;
    private static ulong? _pendingCombatUiRefreshTarget;
    private static int _pendingCombatUiRefreshTries;
    private static long _lastPendingCombatUiRefreshLogMs;
    private const long PendingCombatUiRefreshLogIntervalMs = 1500L;

    /// <summary>
    /// 控制上下文已切换但战斗UI因进行中的出牌/选牌流程暂时无法重建时，把刷新延后到流程结束后执行，
    /// 不再回滚上下文。延后补刷会顺带重跑 ReevaluateEndTurnButtonState，避免切人后回合结束按钮消失/状态陈旧。
    /// </summary>
    private static void ScheduleDeferredCombatUiRefresh(ulong targetPlayerId)
    {
        if (_pendingCombatUiRefreshTarget == targetPlayerId)
        {
            return;
        }

        _pendingCombatUiRefreshTarget = targetPlayerId;
        _pendingCombatUiRefreshTries = 0;
        Callable.From(RunDeferredCombatUiRefresh).CallDeferred();
        LocalMultiControlLogger.Info($"控制上下文已切换，战斗UI刷新顺延到选牌/出牌流程结束后: player={targetPlayerId}");
    }

    private static void RunDeferredCombatUiRefresh()
    {
        ulong? target = _pendingCombatUiRefreshTarget;
        _pendingCombatUiRefreshTarget = null;
        if (!target.HasValue)
        {
            return;
        }

        if (_pendingCombatUiRefreshTries >= MaxCombatUiRefreshDeferTries)
        {
            LocalMultiControlLogger.Warn($"战斗UI延后补刷已达上限，放弃: player={target.Value}, tries={_pendingCombatUiRefreshTries}");
            return;
        }

        if (IsCombatUiInPickFlow())
        {
            _pendingCombatUiRefreshTries++;
            long nowMs = (long)Time.GetTicksMsec();
            if (nowMs - _lastPendingCombatUiRefreshLogMs >= PendingCombatUiRefreshLogIntervalMs)
            {
                _lastPendingCombatUiRefreshLogMs = nowMs;
                LocalMultiControlLogger.Info($"战斗UI延后补刷等待中，流程仍占用: player={target.Value}, tries={_pendingCombatUiRefreshTries}");
            }

            _pendingCombatUiRefreshTarget = target;
            Callable.From(RunDeferredCombatUiRefresh).CallDeferred();
            return;
        }

        if (!RefreshCombatUiForControlledPlayer(target.Value) && IsCombatUiInPickFlow())
        {
            _pendingCombatUiRefreshTries++;
            _pendingCombatUiRefreshTarget = target;
            _lastPendingCombatUiRefreshLogMs = (long)Time.GetTicksMsec();
            Callable.From(RunDeferredCombatUiRefresh).CallDeferred();
        }
    }

    private static bool RefreshCombatUiForControlledPlayer(ulong playerId)
    {
        if (!CombatManager.Instance.IsInProgress)
        {
            return true;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return true;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null)
        {
            return true;
        }

        Player? player = combatState.GetPlayer(playerId);
        if (player == null)
        {
            LocalMultiControlLogger.Warn($"刷新战斗UI失败：未找到玩家 {playerId}");
            return false;
        }

        try
        {
            if (IsCombatUiInPickFlow())
            {
                LocalMultiControlLogger.Info($"战斗UI刷新延后：当前有进行中的出牌/选牌操作，player={playerId}");
                return false;
            }

            NPlayerHand hand = combatUi.Hand;
            CardPile handPile = PileType.Hand.GetPile(player);
            AccessTools.Field(typeof(NEndTurnButton), "_playerHand")?.SetValue(combatUi.EndTurnButton, handPile);
            combatUi.DrawPile.Initialize(player);
            combatUi.DiscardPile.Initialize(player);
            combatUi.ExhaustPile.Initialize(player);

            hand.CancelAllCardPlay();
            foreach (Node child in hand.CardHolderContainer.GetChildren().ToList())
            {
                if (child is not NCardHolder holder || !GodotObject.IsInstanceValid(holder))
                {
                    continue;
                }

                try
                {
                    hand.RemoveCardHolder(holder);
                }
                catch
                {
                    // 防御性兜底：历史日志中该处出现过节点生命周期竞争（已释放对象被二次访问）。
                    // 这里保留最小破坏的强制移除路径，后续请谨慎改动该分支。
                    holder.GetParent()?.RemoveChild(holder);
                    holder.QueueFreeSafely();
                }
            }

            foreach (CardModel card in handPile.Cards)
            {
                NCard? cardNode = NCard.Create(card);
                if (cardNode != null)
                {
                    hand.Add(cardNode);
                }
            }

            hand.ForceRefreshCardIndices();
            RefreshCombatEnergyUi(combatUi, player);
            ReevaluateEndTurnButtonState(combatUi, combatState, player);
            LocalMultiControlLogger.Info($"战斗UI已刷新到当前角色 {playerId}，手牌数量={handPile.Cards.Count}");
            return true;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新战斗UI失败: {exception.Message}");
            return false;
        }
    }

    private static void RefreshCombatEnergyUi(NCombatUi combatUi, Player player)
    {
        NEnergyCounter? oldEnergyCounter = AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.GetValue(combatUi) as NEnergyCounter;
        PlayerCombatState? playerCombatState = player.PlayerCombatState;
        if (!_combatEnergyContainerDefaultPosition.HasValue)
        {
            _combatEnergyContainerDefaultPosition = combatUi.EnergyCounterContainer.Position;
        }

        if (oldEnergyCounter != null)
        {
            oldEnergyCounter.QueueFreeSafely();
        }

        NEnergyCounter? newEnergyCounter = NEnergyCounter.Create(player);
        if (newEnergyCounter != null)
        {
            Vector2 targetPosition = player.Character.ShouldAlwaysShowStarCounter
                ? new Vector2(100f, 806f)
                : _combatEnergyContainerDefaultPosition ?? combatUi.EnergyCounterContainer.Position;
            combatUi.EnergyCounterContainer.SetPosition(targetPosition, keepOffsets: true);
            combatUi.EnergyCounterContainer.AddChildSafely(newEnergyCounter);
            AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.SetValue(combatUi, newEnergyCounter);
        }

        // 辉星计数器在能量球之前处理：它现在常驻战斗UI，与「随时会被重建的能量球」解耦（r99）。
        EnsureStarCounterDisplay(combatUi, player, recreate: true);

        // 第三方次级资源计数器（RitsuLib 框架，如 LexNinja2 的蕾克拉）只会跟着 CombatStateChanged 刷新，
        // 我们切前台不走那个事件 → 切到别的角色后它仍显示上一个角色的资源，这里主动补一次刷新（r100）。
        LocalThirdPartySecondaryResourceBridge.RefreshCombatUiForPlayer(combatUi, player);
    }

    private static void RefreshTopBarForControlledPlayer(ulong playerId)
    {
        NTopBar? topBar = NRun.Instance?.GlobalUi?.TopBar;
        NRun? runNode = NRun.Instance;
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (topBar == null || runState == null || runNode?.GlobalUi == null)
        {
            return;
        }

        Player? player = runState.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        try
        {
            RefreshTopBarDeck(topBar.Deck, player);
            topBar.Gold.Initialize(player);
            topBar.Hp.Initialize(player);
            foreach (Node child in topBar.Portrait.GetChildren())
            {
                child.QueueFreeSafely();
            }

            topBar.Portrait.Initialize(player);

            NPotionContainerPatch.TryBindPotionContainerToPlayer(runNode.GlobalUi.TopBar.PotionContainer, runState, playerId);

            // 注意：遗物刷新必须先清理旧节点再重建，避免切人后叠层。
            NRelicInventoryPatch.TryRebuildRelicInventoryToPlayer(runNode.GlobalUi.RelicInventory, runState, playerId);
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory), "UpdateNavigation")
                ?.Invoke(runNode.GlobalUi.RelicInventory, Array.Empty<object>());
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新顶部栏失败: {exception.Message}");
        }
    }

    public static void RefreshCombatEnergyForCurrentPlayer(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        // r97：入战瞬间 CombatManager.IsInProgress 还是 false（CombatSetUp 事件早于战斗真正开始，
        // 见日志 7838「Combat started」在 OnCombatSetUp 之后），原门禁会让这三次刷新**全部空转**
        // （r96 日志实证：一条「入战能量显示已刷新」都没有）。此时原版 NCombatUi.Activate 已经建好
        // 能量球容器、PlayerCombatState 也已就绪，放宽到「战斗房已进入 ActiveCombat」即可安全刷新。
        CombatRoomMode? roomMode = NCombatRoom.Instance?.Mode;
        if (!CombatManager.Instance.IsInProgress && roomMode != CombatRoomMode.ActiveCombat)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatUi == null || combatState == null)
        {
            return;
        }

        // 入战刷新目标 = 当前受控玩家（此时手牌还没发出来，读不到真实手牌归属；
        // 手牌随后按同一受控玩家抽出，两者一致。真出错由回合开始的归属核对兜底）。
        ulong playerId = Session.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? LocalSelfCoopContext.PrimaryPlayerId;
        Player? player = combatState.GetPlayer(playerId);
        if (player == null)
        {
            return;
        }

        try
        {
            RefreshCombatEnergyUi(combatUi, player);
            LocalMultiControlLogger.Info($"入战能量显示已刷新: player={playerId}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"入战能量显示刷新失败: player={playerId}, source={source}, error={exception.Message}");
        }
    }

    /// <summary>每场战斗入战时清一次能量归属诊断去重，避免同一 key 在后续战斗中不再打日志。</summary>
    public static void ResetCombatUiDiagnostics(string source)
    {
        _combatEnergyDiagKeys.Clear();
        // r111/r112（BUG-7）：手牌顺序诊断的跟踪状态也每场清一次，避免跨战斗被历史签名压掉。
        _handOrderDiagSignature = string.Empty;
        _handOrderDiagFirstSeenMs = 0L;
        _handOrderDiagLoggedSignature = string.Empty;
        LocalMultiControlLogger.Info($"战斗UI归属诊断已重置: source={source}");
    }

    /// <summary>
    /// 延迟到下一帧再核对一次能量归属（回合开始瞬间手牌可能还没发出来，帧末才有真实手牌可判）。
    /// </summary>
    public static void ScheduleEnsureCombatEnergyMatchesHand(string source)
    {
        try
        {
            Callable.From(() => EnsureCombatEnergyMatchesHand(source)).CallDeferred();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"排程能量归属核对失败: source={source}, error={exception.Message}");
        }
    }

    /// <summary>
    /// 保证能量球与当前展示的手牌同属一个玩家（BUG-1：战斗第一回合能量不同步）。
    /// 不变量：能量球所属玩家 == 手牌所属玩家；不一致时按「手牌归属 &gt; 受控玩家」重建能量球。
    /// 手牌归属**直接读手牌区里实际卡牌的持有者**（r97 修正：不能用入战瞬间的 LocalContext 当基准——
    /// 原版 NCombatUi.Activate 按入战瞬间的 me 建能量球，而手牌是开战后按「当前受控玩家」抽出来的，
    /// 入战前停在瓦库角色、入战后切回真人时两者必然分家；r96 正是基准取错所以一次都没校正）。
    /// 只在检测到不一致时动手；取不到归属信息时一律不改（不做无依据的重建）。
    /// </summary>
    public static void EnsureCombatEnergyMatchesHand(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatUi == null || combatState == null)
        {
            return;
        }

        ulong? handPlayerId = TryGetDisplayedHandPlayerId(combatUi, out string handSource);
        ulong? energyPlayerId = TryGetCombatEnergyPlayerId(combatUi);
        ulong? controlledPlayerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId;

        NStarCounter? starCounter = TryGetStarCounter(combatUi);
        ulong? starPlayerId = starCounter != null ? TryGetStarCounterPlayerId(starCounter) : null;

        if (_combatEnergyDiagKeys.Add($"{source}:round{combatState.RoundNumber}"))
        {
            LocalMultiControlLogger.Info(
                $"战斗能量归属核对: 能量={energyPlayerId?.ToString() ?? "null"}, "
                + $"辉星={starPlayerId?.ToString() ?? "null"}, "
                + $"手牌={handPlayerId?.ToString() ?? "null"}({handSource}), "
                + $"受控={controlledPlayerId?.ToString() ?? "null"}, round={combatState.RoundNumber}, source={source}");
        }

        if (CombatEnergyOwnership.TryResolveMismatch(energyPlayerId, handPlayerId, controlledPlayerId, out ulong targetPlayerId))
        {
            Player? target = combatState.GetPlayer(targetPlayerId);
            if (target == null)
            {
                return;
            }

            try
            {
                // 能量球重建会顺带把辉星计数器一起重绑（同一个函数里处理）。
                RefreshCombatEnergyUi(combatUi, target);
                LocalMultiControlLogger.Warn(
                    $"战斗能量归属不一致已校正: 能量={energyPlayerId?.ToString() ?? "null"}, "
                    + $"辉星={starPlayerId?.ToString() ?? "null"}, "
                    + $"手牌={handPlayerId?.ToString() ?? "null"}({handSource}), 受控={controlledPlayerId?.ToString() ?? "null"} "
                    + $"→ 重建为 {targetPlayerId}, source={source}");
            }
            catch (Exception exception)
            {
                LocalMultiControlLogger.Warn(
                    $"战斗能量归属校正失败: target={targetPlayerId}, source={source}, error={exception.Message}");
            }

            return;
        }

        // 能量一致时仍要单独核对辉星：辉星是另一个节点，可以单独分家（储君/Regent 的第二资源）。
        ulong? starTarget = handPlayerId ?? controlledPlayerId;
        if (starCounter == null || !starTarget.HasValue || starPlayerId == starTarget)
        {
            return;
        }

        Player? starPlayer = combatState.GetPlayer(starTarget.Value);
        if (starPlayer == null)
        {
            return;
        }

        EnsureStarCounterDisplay(combatUi, starPlayer, recreate: false);
        LocalMultiControlLogger.Warn(
            $"辉星归属不一致已校正: 辉星={starPlayerId?.ToString() ?? "null"} → {starTarget.Value}, "
            + $"手牌={handPlayerId?.ToString() ?? "null"}({handSource}), 受控={controlledPlayerId?.ToString() ?? "null"}, source={source}");
    }

    /// <summary>
    /// 读「当前手牌区里真实展示的是谁的牌」——遍历手牌 holder 取第一张牌的持有者。
    /// 这是唯一可靠的基准（比任何上下文/追踪变量都准）；手牌为空时返回 null（交给受控玩家兜底）。
    /// </summary>
    private static ulong? TryGetDisplayedHandPlayerId(NCombatUi combatUi, out string source)
    {
        source = "none";
        try
        {
            foreach (Node child in combatUi.Hand.CardHolderContainer.GetChildren())
            {
                if (child is not NCardHolder holder || !GodotObject.IsInstanceValid(holder))
                {
                    continue;
                }

                Player? owner = holder.CardNode?.Model?.Owner;
                if (owner != null)
                {
                    source = "cardOwner";
                    return owner.NetId;
                }
            }

            source = "emptyHand";
            return null;
        }
        catch (Exception exception)
        {
            source = "error";
            LocalMultiControlLogger.Warn($"读取手牌归属失败: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 保证辉星计数器（储君/Regent 第二资源）存在、挂在**战斗UI**下、绑定到目标玩家并正确显示（r99）。
    ///
    /// 为什么不塞进能量球：能量球每次切角色都会重建并 QueueFree 旧的，辉星计数器作为它的子节点
    /// 极易被一并带走（实机表现：单人储君正常，本地多控下辉星**完全不显示**——正是我们接管 UI 之后）。
    /// 这里改为常驻战斗UI（= 场景里的原始父节点，锚点相对全屏，位置就是 star_counter.tscn 里设计的位置），
    /// 生命周期与能量球彻底解耦；重建时直接从场景实例化，避免继承旧节点被反复 Reparent 后的排布。
    /// </summary>
    private static void EnsureStarCounterDisplay(NCombatUi combatUi, Player player, bool recreate)
    {
        try
        {
            NStarCounter? starCounter = TryGetStarCounter(combatUi);
            bool invalid = starCounter == null
                || !GodotObject.IsInstanceValid(starCounter)
                || starCounter.GetParent() != combatUi;
            if (recreate || invalid)
            {
                NStarCounter? fresh = CreateStarCounter();
                if (fresh == null)
                {
                    LocalMultiControlLogger.Warn("辉星计数器重建失败：场景实例化返回 null（改动已跳过，不影响战斗）。");
                    return;
                }

                if (starCounter != null && GodotObject.IsInstanceValid(starCounter) && starCounter.GetParent() == combatUi)
                {
                    starCounter.QueueFreeSafely();
                }

                combatUi.AddChildSafely(fresh);
                AccessTools.Field(typeof(NCombatUi), "_starCounter")?.SetValue(combatUi, fresh);
                starCounter = fresh;
            }

            RefreshStarCounterForPlayer(starCounter!, player);
            bool shouldShow = player.Character.ShouldAlwaysShowStarCounter
                || (player.PlayerCombatState?.Stars ?? 0) > 0;
            starCounter!.Visible = shouldShow;

            if (_combatEnergyDiagKeys.Add($"star:{player.NetId}"))
            {
                LocalMultiControlLogger.Info(
                    $"辉星计数器就绪: player={player.NetId}, alwaysShow={player.Character.ShouldAlwaysShowStarCounter}, "
                    + $"stars={player.PlayerCombatState?.Stars ?? -1}, visible={starCounter.Visible}, "
                    + $"parent={starCounter.GetParent()?.Name.ToString() ?? "null"}, "
                    + $"pos={starCounter.GlobalPosition}, size={starCounter.Size}, scale={starCounter.Scale}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"辉星计数器准备失败: player={player.NetId}, error={exception.Message}");
        }
    }

    /// <summary>按原版场景新建一个辉星计数器（布局取场景默认值，不沿用旧节点的偏移）。</summary>
    private static NStarCounter? CreateStarCounter()
    {
        try
        {
            string path = SceneHelper.GetScenePath("combat/energy_counters/star_counter");
            PackedScene? scene = PreloadManager.Cache.GetScene(path);
            return scene?.Instantiate<NStarCounter>(PackedScene.GenEditState.Disabled);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"辉星计数器场景实例化失败: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 把辉星计数器（NStarCounter，储君/Regent 的第二资源）重新绑定到目标玩家。
    /// 原版坑：Initialize 里只有 !_isListeningToCombatState 才会订阅 StarsChanged，
    /// 而这个标志一旦置起就没人复位 → 必须「先退订旧玩家 + 复位标志」再 Initialize，
    /// 否则重绑后辉星再也不会响应 StarsChanged（数值只能靠 _Process 轮询勉强跟上）。
    /// </summary>
    private static void RefreshStarCounterForPlayer(NStarCounter starCounter, Player player)
    {
        try
        {
            if (AccessTools.Field(typeof(NStarCounter), "_player")?.GetValue(starCounter) is Player previous
                && previous.PlayerCombatState != null)
            {
                MethodInfo? onStarsChangedMethod = AccessTools.Method(typeof(NStarCounter), "OnStarsChanged");
                if (onStarsChangedMethod != null)
                {
                    Action<int, int> onStarsChanged = (Action<int, int>)onStarsChangedMethod.CreateDelegate(typeof(Action<int, int>), starCounter);
                    previous.PlayerCombatState.StarsChanged -= onStarsChanged;
                }
            }

            AccessTools.Field(typeof(NStarCounter), "_isListeningToCombatState")?.SetValue(starCounter, false);
            starCounter.Initialize(player);
            AccessTools.Method(typeof(NStarCounter), "RefreshVisibility")?.Invoke(starCounter, Array.Empty<object>());
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"辉星计数器重绑失败: player={player.NetId}, error={exception.Message}");
        }
    }

    private static NStarCounter? TryGetStarCounter(NCombatUi combatUi)
    {
        try
        {
            return AccessTools.Field(typeof(NCombatUi), "_starCounter")?.GetValue(combatUi) as NStarCounter;
        }
        catch
        {
            return null;
        }
    }

    private static ulong? TryGetStarCounterPlayerId(NStarCounter starCounter)
    {
        try
        {
            return (AccessTools.Field(typeof(NStarCounter), "_player")?.GetValue(starCounter) as Player)?.NetId;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取当前能量球绑定的玩家（取不到返回 null，不抛异常）。</summary>
    private static ulong? TryGetCombatEnergyPlayerId(NCombatUi combatUi)
    {
        try
        {
            NEnergyCounter? counter = AccessTools.Field(typeof(NCombatUi), "_energyCounter")?.GetValue(combatUi) as NEnergyCounter;
            if (counter == null)
            {
                return null;
            }

            Player? player = AccessTools.Field(typeof(NEnergyCounter), "_player")?.GetValue(counter) as Player;
            return player?.NetId;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"读取能量球归属失败: {exception.Message}");
            return null;
        }
    }

    public static void RefreshSharedTopBarForCombat(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        NRun? runNode = NRun.Instance;
        if (runState == null || runNode?.GlobalUi == null)
        {
            return;
        }

        bool potionRefreshed = false;
        bool relicRefreshed = false;

        try
        {
            ulong playerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? LocalSelfCoopContext.PrimaryPlayerId;
            potionRefreshed = NPotionContainerPatch.TryBindPotionContainerToPlayer(runNode.GlobalUi.TopBar.PotionContainer, runState, playerId);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"战斗前药水栏刷新失败: {exception.Message}");
        }

        try
        {
            relicRefreshed = NRelicInventoryPatch.TryRebuildRelicInventoryToPrimaryPlayer(runNode.GlobalUi.RelicInventory, runState);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"战斗前遗物栏刷新失败: {exception.Message}");
        }

        if (!potionRefreshed && !relicRefreshed)
        {
            LocalMultiControlLogger.Warn($"战斗前顶部栏刷新未生效: source={source}");
            return;
        }

        AccessTools.Method(typeof(NTopBar), "UpdateNavigation")?.Invoke(runNode.GlobalUi.TopBar, Array.Empty<object>());
        AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Relics.NRelicInventory), "UpdateNavigation")?.Invoke(runNode.GlobalUi.RelicInventory, Array.Empty<object>());
        LocalMultiControlLogger.Info($"战斗前顶部栏刷新完成: source={source}, potion={potionRefreshed}, relic={relicRefreshed}");
    }

    private static void RefreshTopBarDeck(NTopBarDeckButton deckButton, Player player)
    {
        CardPile? oldPile = AccessTools.Field(typeof(NTopBarDeckButton), "_pile")?.GetValue(deckButton) as CardPile;
        MethodInfo? updateMethod = AccessTools.Method(typeof(NTopBarDeckButton), "OnPileContentsChanged");
        if (oldPile != null && updateMethod != null)
        {
            Action updateHandler = (Action)Delegate.CreateDelegate(typeof(Action), deckButton, updateMethod);
            oldPile.CardAddFinished -= updateHandler;
            oldPile.CardRemoveFinished -= updateHandler;
        }

        deckButton.Initialize(player);
    }

    private static void RefreshDeckViewForControlledPlayer(ulong playerId)
    {
        NDeckViewScreen? deckView = NCapstoneContainer.Instance?.CurrentCapstoneScreen as NDeckViewScreen;
        if (deckView == null)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? player = runState?.GetPlayer(playerId);
        if (runState == null || player == null)
        {
            return;
        }

        try
        {
            CardPile? oldPile = AccessTools.Field(typeof(NDeckViewScreen), "_pile")?.GetValue(deckView) as CardPile;
            MethodInfo? onPileContentsChangedMethod = AccessTools.Method(typeof(NDeckViewScreen), "OnPileContentsChanged");
            if (oldPile != null && onPileContentsChangedMethod != null)
            {
                Action handler = (Action)Delegate.CreateDelegate(typeof(Action), deckView, onPileContentsChangedMethod);
                oldPile.ContentsChanged -= handler;
            }

            CardPile newPile = PileType.Deck.GetPile(player);
            AccessTools.Field(typeof(NDeckViewScreen), "_player")?.SetValue(deckView, player);
            AccessTools.Field(typeof(NDeckViewScreen), "_pile")?.SetValue(deckView, newPile);

            if (onPileContentsChangedMethod != null)
            {
                Action handler = (Action)Delegate.CreateDelegate(typeof(Action), deckView, onPileContentsChangedMethod);
                newPile.ContentsChanged += handler;
                onPileContentsChangedMethod.Invoke(deckView, Array.Empty<object>());
            }
            else
            {
                AccessTools.Method(typeof(NDeckViewScreen), "DisplayCards")?.Invoke(deckView, Array.Empty<object>());
            }

            LocalMultiControlLogger.Info($"卡组界面已切换到当前角色: {playerId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新卡组界面失败: {exception.Message}");
        }
    }

    private static void RefreshRestSiteForControlledPlayer(ulong playerId)
    {
        NRestSiteRoom? restSiteRoom = NRestSiteRoom.Instance;
        if (restSiteRoom == null)
        {
            return;
        }

        try
        {
            RunManager.Instance.RestSiteSynchronizer.LocalOptionHovered(null);
            AccessTools.Field(typeof(NRestSiteRoom), "_lastFocused")?.SetValue(restSiteRoom, null);
            AccessTools.Method(typeof(NRestSiteRoom), "UpdateRestSiteOptions")?.Invoke(restSiteRoom, null);
            RestSiteUiRefreshUtil.EnsureChoicesVisibleForLocalPlayer(restSiteRoom, $"runtime-switch-{playerId}");
            LocalMultiControlLogger.Info($"休息区UI已刷新到当前角色: {playerId}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新休息区UI失败: {exception.Message}");
        }
    }

    private static void RefreshEventRoomForControlledPlayer(ulong playerId)
    {
        if (LocalSelfCoopContext.UseSingleEventFlow)
        {
            return;
        }

        LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack($"event-refresh-before-{playerId}", allowRecover: true);

        NEventRoom? eventRoom = NEventRoom.Instance;
        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        if (eventRoom == null)
        {
            return;
        }

        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        Player? player = runState?.GetPlayer(playerId);
        if (runState == null || player == null)
        {
            return;
        }

        EventModel targetEvent = synchronizer.GetEventForPlayer(player);

        // 共享事件默认所有角色共用同一界面；但自定义布局的共享事件（如假商人）把商店库存
        // 绑定在“进房时前台角色”的事件实例上，不按角色重建的话，购买会扣错金币、遗物进错兜。
        // 因此共享事件里只有 Custom 布局需要重建，且仅当当前确实处于事件房间时执行。
        if (synchronizer.IsShared)
        {
            if (targetEvent.LayoutType != EventLayoutType.Custom || runState.CurrentRoom is not EventRoom)
            {
                return;
            }
        }

        EventModel? currentEvent = AccessTools.Field(typeof(NEventRoom), "_event")?.GetValue(eventRoom) as EventModel;
        if (currentEvent == null || currentEvent.Owner == null || currentEvent == targetEvent)
        {
            return;
        }

        try
        {
            ResetEventNodeReference(currentEvent);
            ResetEventNodeReference(targetEvent);

            if (targetEvent.LayoutType == EventLayoutType.Combat && targetEvent.Node == null)
            {
                RunManager.Instance.EventSynchronizer.GenerateInternalCombatStateIfNecessary(targetEvent);
            }

            bool isPreFinished = runState.CurrentRoom is EventRoom currentEventRoom && currentEventRoom.IsPreFinished;
            NEventRoom? refreshedRoom = NEventRoom.Create(targetEvent, runState, isPreFinished);
            if (refreshedRoom == null)
            {
                LocalMultiControlLogger.Warn($"重建事件房间失败：Create 返回 null，player={playerId}");
                return;
            }

            NRun.Instance?.SetCurrentRoom(refreshedRoom);
            LocalWakuuRelicRuntime.ProbeAndRecoverSelectorStack($"event-refresh-after-{playerId}", allowRecover: true);
            string scopeLabel = synchronizer.IsShared ? "共享自定义事件界面" : "非共享事件房间";
            LocalMultiControlLogger.Info($"{scopeLabel}已按当前角色重建: player={playerId}, event={targetEvent.Id.Entry}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"切换事件视图失败: {exception.Message}");
        }
    }

    private static void ResetEventNodeReference(EventModel eventModel)
    {
        AccessTools.PropertySetter(typeof(EventModel), nameof(EventModel.Node))
            ?.Invoke(eventModel, new object?[] { null });
    }

    private static void RecordWatchdogScheduleResult(bool scheduled, string reason, ulong playerId, int roundNumber, string source)
    {
        long nowMs = (long)Time.GetTicksMsec();
        if (_watchdogScheduleWindowStartMs <= 0L)
        {
            _watchdogScheduleWindowStartMs = nowMs;
        }

        _watchdogScheduleLastPlayerId = playerId;
        _watchdogScheduleLastRound = roundNumber;
        _watchdogScheduleLastSource = source;

        if (scheduled)
        {
            _watchdogScheduleSuccessCount++;
        }
        else
        {
            string key = string.IsNullOrEmpty(reason) ? "unknown" : reason;
            _watchdogScheduleRejectCounts[key] = (_watchdogScheduleRejectCounts.TryGetValue(key, out int count) ? count : 0) + 1;
        }

        if (nowMs - _watchdogScheduleWindowStartMs < 2000L)
        {
            return;
        }

        string rejectSummary = _watchdogScheduleRejectCounts.Count == 0
            ? "none"
            : string.Join(",", _watchdogScheduleRejectCounts.Select((entry) => $"{entry.Key}:{entry.Value}"));
        LocalWakuuRelicRuntime.SelectorStackSnapshot snapshot = LocalWakuuRelicRuntime.SnapshotSelectorStack();
        LocalMultiControlLogger.Info(
            $"瓦库看门狗调度统计: windowMs={nowMs - _watchdogScheduleWindowStartMs}, scheduled={_watchdogScheduleSuccessCount}, rejected={rejectSummary}, lastPlayer={_watchdogScheduleLastPlayerId}, lastRound={_watchdogScheduleLastRound}, lastSource={_watchdogScheduleLastSource}, selectorStackCount={snapshot.Count}, selectorStackTop={snapshot.TopType}");

        _watchdogScheduleWindowStartMs = nowMs;
        _watchdogScheduleSuccessCount = 0;
        _watchdogScheduleRejectCounts.Clear();
    }

    public static void RecordFlowBlockSignal(
        string signal,
        string reason,
        ulong playerId,
        string source,
        int round,
        bool dedupePerRoundPlayer = false)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return;
        }

        if (dedupePerRoundPlayer && round >= 0)
        {
            string dedupeKey = $"{signal}:{round}:{playerId}";
            if (!_flowBlockSignalDedupeRoundPlayer.Add(dedupeKey))
            {
                return;
            }
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (_flowBlockSignalWindowStartMs <= 0L)
        {
            _flowBlockSignalWindowStartMs = nowMs;
        }

        string key = $"{signal}:{reason}";
        _flowBlockSignalCounts[key] = (_flowBlockSignalCounts.TryGetValue(key, out int count) ? count : 0) + 1;

        if (nowMs - _flowBlockSignalWindowStartMs < 2000L)
        {
            return;
        }

        string signalSummary = _flowBlockSignalCounts.Count == 0
            ? "none"
            : string.Join(",", _flowBlockSignalCounts.Select((entry) => $"{entry.Key}:{entry.Value}"));
        LocalWakuuRelicRuntime.SelectorStackSnapshot snapshot = LocalWakuuRelicRuntime.SnapshotSelectorStack();
        LocalMultiControlLogger.Warn(
            $"流程阻塞看门狗统计: windowMs={nowMs - _flowBlockSignalWindowStartMs}, signals={signalSummary}, player={playerId}, round={round}, source={source}, selectorStackCount={snapshot.Count}, selectorStackTop={snapshot.TopType}");

        _flowBlockSignalWindowStartMs = nowMs;
        _flowBlockSignalCounts.Clear();
    }

    private static void ReevaluateEndTurnButtonState(NCombatUi combatUi, CombatState combatState, Player currentPlayer)
    {
        if (combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        try
        {
            NEndTurnButton button = combatUi.EndTurnButton;
            Player? me = LocalContext.GetMe(combatState);
            bool shouldDisable = CombatManager.Instance.IsPlayerReadyToEndTurn(currentPlayer);
            bool canTakeAction = me != null && me.Creature != null && me.Creature.IsAlive && !shouldDisable;

            AccessTools.PropertySetter(typeof(CombatManager), "PlayerActionsDisabled")?.Invoke(CombatManager.Instance, new object[] { shouldDisable });

            Type? stateType = AccessTools.Inner(typeof(NEndTurnButton), "State");
            FieldInfo? stateField = AccessTools.Field(typeof(NEndTurnButton), "_state");
            MethodInfo? setStateMethod = AccessTools.Method(typeof(NEndTurnButton), "SetState");
            int oldState = -1;
            if (stateField != null && stateField.GetValue(button) != null)
            {
                oldState = Convert.ToInt32(stateField.GetValue(button));
            }

            if (stateType != null && setStateMethod != null)
            {
                object stateValue = Enum.ToObject(stateType, canTakeAction ? 0 : 1);
                setStateMethod.Invoke(button, new object[] { stateValue });
            }

            int newState = -1;
            if (stateField != null && stateField.GetValue(button) != null)
            {
                newState = Convert.ToInt32(stateField.GetValue(button));
            }

            if (canTakeAction && newState == 0)
            {
                TryRepairEndTurnButtonOffscreenPosition(button, stateType != null && oldState != 0);
            }

            button.RefreshEnabled();
            // r104：文字也跟目标玩家对齐（否则切到瓦库后可能仍写着「撤销结束回合」）
            TrySyncEndTurnButtonLabel(button, currentPlayer);

            string handMode = "?";
            try
            {
                handMode = combatUi.Hand.CurrentMode.ToString();
            }
            catch
            {
                // ignore
            }

            LocalMultiControlLogger.Info(
                $"回合结束按钮重评: player={currentPlayer.NetId}, me={me?.NetId.ToString() ?? "null"}, ready={shouldDisable}, actionsDisabled={CombatManager.Instance.PlayerActionsDisabled}, state={oldState}->{newState}, handMode={handMode}, inCardSel={combatUi.Hand.IsInCardSelection}, y={button.Position.Y:0.0}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"刷新回合结束按钮状态失败: {exception.Message}");
        }
    }

    /// <summary>
    /// 对「当前控制角色」重新评估回合结束按钮状态（供回合开始等相位兜底调用）。
    /// 修复：一玩家死亡后，另一存活玩家回合开始时按钮被误判为隐藏/禁用（死亡玩家干扰了
    /// 原版 OnTurnStarted 的 GetMe 判定）。此处按「当前控制角色是否存活且未 ready」强制重评，
    /// 存活玩家回合开始必然能拿到 Enabled 按钮；已结算/非玩家回合则跳过。
    /// </summary>
    public static void ReevaluateEndTurnButtonForControlledPlayer(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null || combatState.CurrentSide != CombatSide.Player)
        {
            return;
        }

        ulong? controlledId = Session.CurrentControlledPlayerId ?? LocalContext.NetId;
        if (!controlledId.HasValue)
        {
            return;
        }

        Player? player = combatState.GetPlayer(controlledId.Value);
        if (player == null)
        {
            return;
        }

        // 只对「存活且未 ready 可行动」的角色做兜底重评，避免覆盖正常 Ready 状态。
        if (player.Creature == null || !player.Creature.IsAlive || CombatManager.Instance.IsPlayerReadyToEndTurn(player))
        {
            return;
        }

        ReevaluateEndTurnButtonState(combatUi, combatState, player);
        LocalMultiControlLogger.Info($"回合开始兜底重评结束回合按钮: player={player.NetId}, source={source}");
    }

    /// <summary>
    /// 结束回合按钮点击前的归属校正（r104，BUG-2）。
    ///
    /// 原版 <c>NEndTurnButton.CallReleaseLogic</c> 用 <c>LocalContext.GetMe(...)</c> 决定「这次点击是
    /// 结束谁的回合」；而本 mod 的 <c>LocalContext</c> 会为**瓦库后台出牌的动作归属**临时漂移。
    /// 一旦漂移到「已经结束回合的角色」身上，点击就会被当成「撤销结束回合」处理，
    /// 表现为**点结束回合完全没反应**；切回自己再切到瓦库（重新对齐上下文）才恢复。
    ///
    /// 这里在点击瞬间把上下文校正到**前台玩家**（<see cref="Session"/>.CurrentControlledPlayerId，
    /// 不受漂移影响），保证后续原版逻辑结算的是玩家正在看的那个角色。
    /// 返回校正后的前台玩家 id；无战斗中前台角色时返回 null（交回原版自行处理）。
    /// </summary>
    internal static ulong? AlignLocalContextToForegroundForEndTurn()
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return null;
        }

        Player? foreground = TryGetForegroundPlayer();
        if (foreground == null)
        {
            return null;
        }

        ulong playerId = foreground.NetId;
        if (LocalContext.NetId == playerId)
        {
            return playerId;
        }

        ulong? previousNetId = LocalContext.NetId;
        LocalContext.NetId = playerId;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId);
        SyncRunSynchronizerLocalPlayerId(playerId);
        LocalMultiControlLogger.Info(
            $"结束回合点击：上下文已校正到前台玩家 {previousNetId?.ToString() ?? "null"} -> {playerId}");
        return playerId;
    }

    /// <summary>
    /// 结束回合按钮自愈（r104，BUG-2）：前台角色可操作（存活且未结束回合）时，若按钮仍处于
    /// 禁用/隐藏状态就重评一次。
    ///
    /// 修复「真人先结束回合 → 自动切到瓦库 → 点结束回合无效，切回自己再切到瓦库才生效」：
    /// 按钮状态机绑定前台角色，而自动切人 / 瓦库自动结束回合会绕开原版按钮事件，
    /// 于是按钮可能被留在旧状态（被 <c>Disable()</c> 时点击事件根本不会派发）。
    /// 判定口径见 <see cref="EndTurnButtonReconcilePolicy"/>；只在真需要时动手并节流，
    /// 避免与游戏状态机打架、避免日志刷屏。
    /// </summary>
    public static void ReconcileEndTurnButtonForForeground(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            return;
        }

        CombatState? combatState = TryGetCombatState(combatUi);
        if (combatState == null)
        {
            return;
        }

        ulong playerId = Session.CurrentControlledPlayerId ?? LocalContext.NetId ?? 0UL;
        if (playerId == 0UL)
        {
            return;
        }

        Player? player = combatState.GetPlayer(playerId);
        bool foregroundAlive = player?.Creature != null && player.Creature.IsAlive;
        bool foregroundReady = player != null && CombatManager.Instance.IsPlayerReadyToEndTurn(player);

        NEndTurnButton button = combatUi.EndTurnButton;
        if (button == null)
        {
            return;
        }

        FieldInfo? stateField = AccessTools.Field(typeof(NEndTurnButton), "_state");
        bool buttonStateEnabled = stateField?.GetValue(button) != null
            && Convert.ToInt32(stateField.GetValue(button)) == 0;

        if (!EndTurnButtonReconcilePolicy.ShouldReconcile(
                playerSideActive: combatState.CurrentSide == CombatSide.Player,
                combatInProgress: CombatManager.Instance.IsInProgress,
                foregroundAlive: foregroundAlive,
                foregroundReady: foregroundReady,
                inPickFlow: IsCombatUiInPickFlow(),
                buttonStateEnabled: buttonStateEnabled,
                buttonInputEnabled: button.IsEnabled))
        {
            return;
        }

        // 节流：同一场战斗最多 500ms 尝试一次（自愈失败时别每帧硬刷）
        long nowMs = (long)Time.GetTicksMsec();
        if (nowMs - _lastEndTurnReconcileAttemptMs < 500L)
        {
            return;
        }

        _lastEndTurnReconcileAttemptMs = nowMs;

        try
        {
            int stateBefore = stateField?.GetValue(button) != null ? Convert.ToInt32(stateField.GetValue(button)) : -1;
            bool inputBefore = button.IsEnabled;
            ReevaluateEndTurnButtonState(combatUi, combatState, player!);

            if (_endTurnReconcileLogCount < 5)
            {
                _endTurnReconcileLogCount++;
                int stateAfter = stateField?.GetValue(button) != null ? Convert.ToInt32(stateField.GetValue(button)) : -1;
                LocalMultiControlLogger.Info(
                    $"结束回合按钮自愈: player={playerId}, state={stateBefore}->{stateAfter}, " +
                    $"input={inputBefore}->{button.IsEnabled}, source={source}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"结束回合按钮自愈失败: {exception.Message}");
        }
    }

    /// <summary>手牌 UI 顺序自愈的逐帧节流（r111）。</summary>
    private const long HandOrderCheckIntervalMs = 250L;

    /// <summary>
    /// 诊断节流：同一处「我们**不处理**的差异」必须持续这么久才记一条 WARN。
    /// 取 1500ms 是因为原版手牌变换的视觉更新**故意延迟约 0.9s**
    /// （<c>NCardTransformShineVfx.PlayUntilCardUpdate</c>：先等 <c>0.75 + 0.125</c> 秒才 <c>UpdateCard</c>），
    /// 那段窗口里「数据已是新牌、UI 还是旧牌」是完全正常的动画态。
    /// </summary>
    private const long HandOrderDiagnosticDelayMs = 1500L;

    private static long _lastHandOrderCheckMs;
    private static string _handOrderDiagSignature = string.Empty;
    private static long _handOrderDiagFirstSeenMs;
    private static string _handOrderDiagLoggedSignature = string.Empty;

    /// <summary>
    /// 手牌 UI 顺序自愈（r111，BUG-7）：把**当前显示的手牌**节点顺序拉回与「前台角色的手牌堆」一致。
    ///
    /// 背景：本地多控下「显示的手牌」只有一份（前台角色的那份），而每个角色各自有一份手牌堆数据。
    /// 手牌变换（<c>CardCmd.Transform</c>）发生在**后台角色**身上时会**故意跳过视觉**（r109，否则原版会到
    /// 前台手牌里找原卡节点并抛 <c>Couldn't get hand node for original card</c>）——若此刻屏幕上显示的正好是
    /// 那个角色的手牌（切人后「延后重建」窗口、或显示层尚未跟上），节点顺序/内容就不会被更新，
    /// 表现为「牌都对但顺序不对，切一次角色（重建手牌 UI）就恢复」。
    ///
    /// 收口为一条不变量：**显示的手牌 UI 顺序必须等于前台角色手牌堆顺序**。判定见
    /// <see cref="HandUiOrderPolicy"/>（纯函数，可单测）；这里只负责安全地落到 Godot 节点：
    /// ① 显示的手牌整个属于别的玩家 → **只记诊断**（重建由切人链路自己负责，这里不抢）；
    /// ② 选牌 / 拖牌 / 出牌 / 有牌等待打出时顺序不可比 → 一律不干预；
    /// ③ 牌是同一批、只是顺序不同 → 按数据顺序重排（**只动次序，不建节点、不删节点**）；
    /// ④ 「多余 / 缺节点」这类差异 **一律不处理**，只在持续 ≥1.5s 时记一条 WARN 便于日后核对。
    ///
    /// ⚠ r112：④ 原本会「清多余节点 / 触发整表重建」，实机立刻回归（打出的「数据链」停在屏幕中间不消耗）——
    /// 原版手牌变换的视觉更新是**故意延迟约 0.9s** 的，这段窗口天然「数据新、UI 旧」，
    /// 据此重建会在出牌结算途中把出牌链打断。所以现在**只允许重排**这一种动作。
    /// 逐帧调用点由 <see cref="HandOrderCheckIntervalMs"/> 节流，只在真动手时打 INFO。
    /// </summary>
    public static void ReconcileDisplayedHandOrder(string source)
    {
        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return;
        }

        if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress)
        {
            return;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        CombatState? combatState = combatUi != null ? TryGetCombatState(combatUi) : null;
        if (combatUi == null || combatState == null)
        {
            return;
        }

        ulong playerId = Session.CurrentControlledPlayerId ?? 0UL;
        Player? player = playerId != 0UL ? combatState.GetPlayer(playerId) : null;
        if (player?.PlayerCombatState == null)
        {
            return;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand?.CardHolderContainer == null)
        {
            return;
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (nowMs - _lastHandOrderCheckMs < HandOrderCheckIntervalMs)
        {
            return;
        }

        _lastHandOrderCheckMs = nowMs;

        try
        {
            List<NCardHolder> holders = new();
            foreach (Node child in hand.CardHolderContainer.GetChildren())
            {
                if (child is NCardHolder holder && GodotObject.IsInstanceValid(holder) && holder.CardNode?.Model != null)
                {
                    holders.Add(holder);
                }
            }

            // ① 显示的手牌整个属于别的玩家 → 只记诊断：这是切人后「延后重建」窗口的正常中间态，
            //    重建由切人链路自己负责（ApplyControlContext → ScheduleDeferredCombatUiRefresh），
            //    这里再排一次只会在出牌途中整表重建、把出牌链打断（r111 实机回归教训）。
            ulong? displayedOwner = ResolveSingleHandOwner(holders);
            if (displayedOwner.HasValue && displayedOwner.Value != playerId)
            {
                TrackHandOrderDiagnostic(
                    $"stale|{playerId}|{displayedOwner.Value}",
                    "手牌显示归属与前台不一致（仅记录，重建由切人链路负责）: "
                    + $"foreground={playerId}, displayed={displayedOwner.Value}, source={source}");
                return;
            }

            // ② 选牌 / 拖牌 / 出牌流程中 holder 会被搬去别的容器，顺序不可比 → 不干预
            if (hand.InCardPlay || hand.IsInCardSelection || hand.CurrentMode != NPlayerHand.Mode.Play)
            {
                return;
            }

            CardPile handPile = PileType.Hand.GetPile(player);
            List<CardModel> pileCards = handPile.Cards.ToList();

            // ③ 有任何一张手牌堆里的牌，其 holder 不在手牌容器里（拖拽中 / 等待打出 / 已选中）→ 顺序不可比
            foreach (CardModel card in pileCards)
            {
                NCardHolder? existing = hand.GetCardHolder(card);
                if (existing != null && GodotObject.IsInstanceValid(existing)
                    && !hand.CardHolderContainer.IsAncestorOf(existing))
                {
                    return;
                }
            }

            List<string> pileKeys = new(pileCards.Count);
            List<string> pileLabels = new(pileCards.Count);
            foreach (CardModel card in pileCards)
            {
                pileKeys.Add(BuildHandCardKey(card));
                pileLabels.Add(card.Id.Entry);
            }

            List<string> uiKeys = new(holders.Count);
            List<string> uiLabels = new(holders.Count);
            Dictionary<string, NCardHolder> holderByKey = new();
            foreach (NCardHolder holder in holders)
            {
                CardModel model = holder.CardNode!.Model!;
                string key = BuildHandCardKey(model);
                uiKeys.Add(key);
                uiLabels.Add(model.Id.Entry);
                holderByKey[key] = holder;
            }

            HandUiOrderAction action = HandUiOrderPolicy.Decide(pileKeys, uiKeys);
            if (action == HandUiOrderAction.None)
            {
                // 「多余 / 缺失」在变换动画窗口里天然存在，只在持续 ≥1.5s 时记一条诊断；一致时清空诊断状态。
                List<string> extras = HandUiOrderPolicy.ListUiExtras(pileKeys, uiKeys);
                List<string> missing = HandUiOrderPolicy.ListMissingInUi(pileKeys, uiKeys);
                if (extras.Count == 0 && missing.Count == 0)
                {
                    TrackHandOrderDiagnostic(string.Empty, string.Empty);
                }
                else
                {
                    TrackHandOrderDiagnostic(
                        $"gap|{playerId}|{string.Join(",", extras)}|{string.Join(",", missing)}",
                        "手牌UI与数据存在差异但未处理（仅记录）: "
                        + $"player={playerId}, 多余=[{string.Join(",", extras.Select(LabelOfKey))}], "
                        + $"缺失=[{string.Join(",", missing.Select(LabelOfKey))}], "
                        + $"数据={pileKeys.Count}张, UI={uiKeys.Count}张, source={source}");
                }

                return;
            }

            // 走到这里只可能是 Reorder：牌是同一批、仅顺序不同 → 只调整节点次序（零节点增删）。
            TrackHandOrderDiagnostic(string.Empty, string.Empty);

            string before = string.Join(",", uiLabels);
            int moved = 0;
            for (int i = 0; i < pileCards.Count; i++)
            {
                if (!holderByKey.TryGetValue(pileKeys[i], out NCardHolder? holder) || !GodotObject.IsInstanceValid(holder))
                {
                    continue;
                }

                if (holder.GetIndex() != i)
                {
                    hand.CardHolderContainer.MoveChildSafely(holder, i);
                    moved++;
                }
            }

            hand.ForceRefreshCardIndices();

            LocalMultiControlLogger.Info(
                $"手牌UI顺序已按数据自愈: player={playerId}, 重排={moved}, "
                + $"前=[{before}], 后=[{string.Join(",", pileLabels)}], source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"手牌UI顺序自愈失败: {exception.Message}");
        }
    }

    /// <summary>手牌变换结束后排一次顺序自愈（下一帧执行，等原版视觉分支先落定）。</summary>
    public static void ScheduleReconcileDisplayedHandOrder(string source)
    {
        try
        {
            Callable.From(() => ReconcileDisplayedHandOrder(source)).CallDeferred();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"排程手牌UI顺序自愈失败: source={source}, error={exception.Message}");
        }
    }

    /// <summary>手牌比较键：牌名 + 实例标识（同名重复牌如多张防御也能区分）。</summary>
    private static string BuildHandCardKey(CardModel card)
    {
        return $"{card.Id.Entry}#{RuntimeHelpers.GetHashCode(card)}";
    }

    /// <summary>手牌区节点模型若**全部**属于同一个玩家，返回该玩家 id；信息不足（跨玩家/取不到）返回 null。</summary>
    private static ulong? ResolveSingleHandOwner(List<NCardHolder> holders)
    {
        ulong? owner = null;
        foreach (NCardHolder holder in holders)
        {
            ulong? netId = holder.CardNode?.Model?.Owner?.NetId;
            if (netId == null)
            {
                return null;
            }

            if (owner == null)
            {
                owner = netId;
            }
            else if (owner.Value != netId.Value)
            {
                return null;
            }
        }

        return owner;
    }

    /// <summary>把比较键（牌名#实例标识）还原成可读牌名。</summary>
    private static string LabelOfKey(string key)
    {
        int index = key.IndexOf('#');
        return index > 0 ? key.Substring(0, index) : key;
    }

    /// <summary>
    /// 诊断节流（r111/r112）：对「我们**不处理**的差异」只在**同一个差异持续 ≥
    /// <see cref="HandOrderDiagnosticDelayMs"/>** 时记一条 WARN；<paramref name="signature"/> 传空串表示
    /// 当前已一致（重置跟踪状态）。
    ///
    /// 取 1.5s 是因为手牌变换的视觉更新故意延迟约 0.9s（`NCardTransformShineVfx.PlayUntilCardUpdate`），
    /// 那个窗口里「数据是新牌、UI 还是旧牌」属正常，不该报。同一签名只报一次（若之后差异消失又重现，会重新计时）。
    /// </summary>
    private static void TrackHandOrderDiagnostic(string signature, string message)
    {
        if (signature.Length == 0)
        {
            _handOrderDiagSignature = string.Empty;
            _handOrderDiagFirstSeenMs = 0L;
            return;
        }

        long nowMs = (long)Time.GetTicksMsec();
        if (!string.Equals(signature, _handOrderDiagSignature, StringComparison.Ordinal))
        {
            _handOrderDiagSignature = signature;
            _handOrderDiagFirstSeenMs = nowMs;
            return;
        }

        if (nowMs - _handOrderDiagFirstSeenMs < HandOrderDiagnosticDelayMs
            || string.Equals(signature, _handOrderDiagLoggedSignature, StringComparison.Ordinal))
        {
            return;
        }

        _handOrderDiagLoggedSignature = signature;
        LocalMultiControlLogger.Warn(message);
    }

    /// <summary>
    /// 结束回合按钮点击门禁诊断（r104，BUG-2）：一次性打出「点击为什么没反应」需要的全部条件。
    /// 只在真的点了按钮（走到 <c>CallReleaseLogic</c>）时调用，不会刷屏。
    /// </summary>
    internal static void LogEndTurnButtonClickGate(Player? clickTarget, NEndTurnButton button, string source)
    {
        try
        {
            NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
            string handMode = "?";
            bool inPickFlow = false;
            if (combatUi != null)
            {
                try
                {
                    handMode = combatUi.Hand.CurrentMode.ToString();
                }
                catch
                {
                    // 忽略：诊断用
                }

                inPickFlow = IsCombatUiInPickFlow();
            }

            FieldInfo? stateField = AccessTools.Field(typeof(NEndTurnButton), "_state");
            int state = button != null && stateField?.GetValue(button) != null
                ? Convert.ToInt32(stateField.GetValue(button))
                : -1;

            bool focused = false;
            try
            {
                PropertyInfo? focusedProperty = AccessTools.Property(
                    typeof(MegaCrit.Sts2.Core.Nodes.GodotExtensions.NClickableControl), "IsFocused");
                focused = focusedProperty?.GetValue(button) is bool value && value;
            }
            catch
            {
                // 忽略：诊断用
            }

            Player? foreground = TryGetForegroundPlayer();
            LocalMultiControlLogger.Info(
                $"结束回合点击门禁: target={clickTarget?.NetId.ToString() ?? "null"}, " +
                $"foreground={foreground?.NetId.ToString() ?? "null"}, context={LocalContext.NetId?.ToString() ?? "null"}, " +
                $"state={state}(0=Enabled), inputEnabled={button?.IsEnabled.ToString() ?? "null"}, focused={focused}, " +
                $"handMode={handMode}, inPickFlow={inPickFlow}, source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"结束回合点击门禁诊断失败: {exception.Message}");
        }
    }

    /// <summary>
    /// 让结束回合按钮的文字与**目标玩家**的状态一致（r104）。
    ///
    /// 原版只在 <c>OnTurnStarted</c> / <c>AfterPlayerEndedTurn</c>（且 <c>LocalContext.IsMe(player)</c>）时写按钮文字，
    /// 本地多控下切换前台不走这些事件 → 切到瓦库后按钮可能还写着「撤销结束回合」，
    /// 与按钮此刻的真实行为（结束瓦库回合）不符。这里按目标玩家是否已 ready 同步成
    /// END_TURN / UNDO_END_TURN，视觉与行为对齐。
    /// </summary>
    private static void TrySyncEndTurnButtonLabel(NEndTurnButton button, Player player)
    {
        if (button == null || player?.PlayerCombatState == null)
        {
            return;
        }

        try
        {
            object? label = AccessTools.Field(typeof(NEndTurnButton), "_label")?.GetValue(button);
            if (label == null)
            {
                return;
            }

            bool ready = CombatManager.Instance.IsPlayerReadyToEndTurn(player);
            LocString text = new("gameplay_ui", ready ? "UNDO_END_TURN_BUTTON" : "END_TURN_BUTTON");
            text.Add("turnNumber", player.PlayerCombatState.TurnNumber);
            AccessTools.Method(label.GetType(), "SetTextAutoSize", new[] { typeof(string) })
                ?.Invoke(label, new object[] { text.GetFormattedText() });
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"同步结束回合按钮文字失败: {exception.Message}");
        }
    }

    private static void TryRepairEndTurnButtonOffscreenPosition(NEndTurnButton button, bool forceAnimateIn)
    {
        try
        {
            PropertyInfo? showPosProperty = AccessTools.Property(typeof(NEndTurnButton), "ShowPos");
            PropertyInfo? hidePosProperty = AccessTools.Property(typeof(NEndTurnButton), "HidePos");
            if (showPosProperty == null || hidePosProperty == null)
            {
                return;
            }

            Vector2 showPos = (Vector2)(showPosProperty.GetValue(button) ?? default(Vector2));
            Vector2 hidePos = (Vector2)(hidePosProperty.GetValue(button) ?? default(Vector2));
            if (showPos == default(Vector2) && hidePos == default(Vector2))
            {
                return;
            }

            // 状态已是 Enabled 但节点仍停留在屏幕下方（AnimOut 残留/AnimIn 曾被跳过）时，强制补一次 AnimIn。
            if (forceAnimateIn && button.Position.Y >= hidePos.Y - 2f && button.Position.Y > showPos.Y)
            {
                AccessTools.Method(typeof(NEndTurnButton), "AnimIn")?.Invoke(button, null);
                LocalMultiControlLogger.Info($"回合结束按钮离屏自修复：重新 AnimIn y={button.Position.Y:0.0}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"回合结束按钮离屏自修复失败: {exception.Message}");
        }
    }

    private static void EnsureTreasureCursorVisibleAfterSwitch(string source)
    {
        if (!IsTreasurePickingActive())
        {
            return;
        }

        try
        {
            Input.MouseMode = Input.MouseModeEnum.Visible;
            Callable.From(delegate
            {
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }).CallDeferred();
            LocalMultiControlLogger.Info($"宝箱切人后已强制恢复鼠标可见: source={source}");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"宝箱切人后恢复鼠标失败: source={source}, error={exception.Message}");
        }
    }

    private static bool IsTreasurePickingActive()
    {
        if (!RunManager.Instance.IsInProgress)
        {
            return false;
        }

        TreasureRoomRelicSynchronizer? synchronizer = RunManager.Instance.TreasureRoomRelicSynchronizer;
        if (synchronizer?.CurrentRelics == null || synchronizer.CurrentRelics.Count == 0)
        {
            return false;
        }

        object? votesObject = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_votes")?.GetValue(synchronizer);
        if (votesObject is not System.Collections.IEnumerable votesEnumerable)
        {
            return false;
        }

        foreach (object? vote in votesEnumerable)
        {
            if (vote == null)
            {
                return true;
            }

            bool? voteReceived = AccessTools.Field(vote.GetType(), "voteReceived")?.GetValue(vote) as bool?;
            if (voteReceived == false)
            {
                return true;
            }
        }

        return false;
    }

}
