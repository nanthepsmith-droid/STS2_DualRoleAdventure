using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Models.Relics;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using Godot;

namespace LocalMultiControl.Scripts.Runtime;

internal static class LocalWakuuRelicRuntime
{
    private const int MaxCardsToPlay = 13;

    // 瓦库形态"打光所有手牌"模式的硬护栏上限：正常牌组远达不到，只为防御异常效果导致的死循环。
    private const int MaxCardsToPlayForm = 60;

    private const long WatchdogRestartCooldownMs = 300L;

    private static readonly Dictionary<string, long> _watchdogLastRunAt = new();
    private static readonly HashSet<string> _watchdogInFlight = new();
    // r83：托管遗物缺失兜底的去重集合（每个玩家每次运行只 WARN 一次 / 只调度补发一次）。
    private static readonly HashSet<ulong> _takeoverRelicMissingWarned = new();
    private static readonly HashSet<ulong> _takeoverRelicRestoreScheduled = new();
    private static readonly SemaphoreSlim SelectorScopeGate = new(1, 1);
    private static readonly FieldInfo? SelectorStackField =
        typeof(CardSelectCmd).GetField("_selectorStack", BindingFlags.NonPublic | BindingFlags.Static);
    private static int _selectorScopeInFlight;

    public readonly struct SelectorStackSnapshot
    {
        public SelectorStackSnapshot(int count, string topType, bool allVakuuSelectors)
        {
            Count = count;
            TopType = topType;
            AllVakuuSelectors = allVakuuSelectors;
        }

        public int Count { get; }

        public string TopType { get; }

        public bool AllVakuuSelectors { get; }
    }

    public static LocalWakuuStarterRelic? TryGetWakuuRelic(Player player)
    {
        return player.GetRelicById(ModelDb.GetId<LocalWakuuStarterRelic>()) as LocalWakuuStarterRelic;
    }

    public static LocalWakuuFormRelic? TryGetWakuuFormRelic(Player player)
    {
        return player.GetRelicById(ModelDb.GetId<LocalWakuuFormRelic>()) as LocalWakuuFormRelic;
    }

    /// <summary>瓦库接管遗物 = 旧的"永久低语耳环"或新的【瓦库形态】任一。</summary>
    public static RelicModel? TryGetTakeoverRelic(Player player)
    {
        return (RelicModel?)TryGetWakuuRelic(player) ?? TryGetWakuuFormRelic(player);
    }

    /// <summary>
    /// 该角色是否处于瓦库形态新模式（总开关开启 且 持有形态遗物）。
    /// 遗物缺失时按瓦库名单兜底（见 <see cref="IsTakeoverPlayerFallback"/>）。
    /// </summary>
    public static bool IsVakuuFormMode(Player player)
    {
        if (!LocalWakuuAutopilotConfig.UseVakuuForm)
        {
            return false;
        }

        return TryGetWakuuFormRelic(player) != null || IsTakeoverPlayerFallback(player);
    }

    /// <summary>
    /// 按 NetId 判断是否处于瓦库形态模式。用于选牌入口等只有 NetId 的场景；
    /// 找不到玩家模型时按 false 处理（宁可走正常 UI 也不误伤）。
    /// </summary>
    public static bool IsVakuuFormModeById(ulong netId)
    {
        if (!LocalWakuuAutopilotConfig.UseVakuuForm)
        {
            return false;
        }

        try
        {
            Player? player = RunManager.Instance.DebugOnlyGetState()?.GetPlayer(netId);
            return player != null && (TryGetWakuuFormRelic(player) != null || IsTakeoverPlayerFallback(player));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 视角策略判定（改进-2 / Phase 0）：是否抑制这次为瓦库形态角色切换前台的动作。
    /// 判定口径全部收敛到纯函数 <see cref="WakuuViewPolicy"/>（档位 × 触发场景），
    /// 调用方必须传入与自己语义一致的 <see cref="WakuuViewTrigger"/>。
    /// </summary>
    public static bool ShouldSuppressForegroundSwitch(
        Player? player,
        WakuuViewTrigger trigger,
        bool willAutoAnswerOverride = false)
    {
        if (player == null || !LocalSelfCoopContext.IsEnabled)
        {
            return false;
        }

        // 只有「作用域外真人交互选牌」这一条需要判断"会不会被自动作答"（其余场景该参数无意义）：
        // ① 调用方已判定会被自动作答（作用域外自动作答入口，见 CardSelectWakuuTurnStartAutoAnswerPatch）；
        // ② 栈上已有全局选择器（瓦库自动出牌作用域内）。
        bool willAutoAnswer = trigger == WakuuViewTrigger.HumanInteractionChoice
            && (willAutoAnswerOverride || CardSelectCmd.Selector != null);

        return WakuuViewPolicy.ShouldSuppressSwitch(
            LocalWakuuAutopilotConfig.ViewMode,
            LocalWakuuAutopilotConfig.BackgroundMode,
            trigger,
            IsVakuuFormMode(player),
            willAutoAnswer);
    }

    /// <summary>
    /// 兼容重载（旧布尔语义）：仅供「按有无全局选择器兜底」的老调用点使用。
    /// true = 作用域外真人交互选牌兜底（<see cref="WakuuViewTrigger.HumanInteractionChoice"/>）；
    /// false = 常规出牌路径（<see cref="WakuuViewTrigger.ReactivePlay"/>）。
    /// 新代码请直接用 <see cref="WakuuViewTrigger"/> 重载，语义更明确。
    /// </summary>
    public static bool ShouldSuppressForegroundSwitch(Player? player, bool onlyWhenSelectorActive)
    {
        return ShouldSuppressForegroundSwitch(
            player,
            onlyWhenSelectorActive ? WakuuViewTrigger.HumanInteractionChoice : WakuuViewTrigger.ReactivePlay);
    }

    /// <summary>
    /// 该角色是否「后台托管中的瓦库形态角色」——**与视角档位无关**的客观判定。
    /// 供安全网候选甄别、大脑 <c>isBackground</c> 提示等场合使用：
    /// 视角档位只决定「要不要切前台」，不改变「这个角色确实是后台托管的瓦库」这一事实。
    /// </summary>
    /// <summary>
    /// 运行是否已进入「清理 / 已结束」阶段 —— 此时自动出牌异步链里抛出的异常属**预期中止**
    /// （游戏状态已被 <c>RunManager.CleanUp</c> 拆掉），不该报 WARN 也不该上抛。
    /// 判定口径见纯函数 <see cref="WakuuTeardownPolicy"/>。
    /// </summary>
    private static bool IsRunTeardown()
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            return WakuuTeardownPolicy.ShouldTreatAsExpectedAbort(
                RunManager.Instance.IsInProgress,
                runState != null);
        }
        catch
        {
            // 连 RunManager 都读不动了 = 一定在清理/退出过程里。
            return true;
        }
    }

    public static bool IsBackgroundHostedWakuu(Player? player)
    {
        if (player == null || !LocalSelfCoopContext.IsEnabled)
        {
            return false;
        }

        return LocalWakuuAutopilotConfig.BackgroundMode && IsVakuuFormMode(player);
    }

    /// <summary>
    /// 该角色是否被瓦库托管（持有接管遗物；遗物缺失时按瓦库名单兜底）。
    /// </summary>
    public static bool HasWakuuRelic(Player player)
    {
        return TryGetTakeoverRelic(player) != null || IsTakeoverPlayerFallback(player);
    }

    /// <summary>
    /// 托管判据兜底（r83）：托管遗物被第三方效果移除后仍按瓦库名单维持托管，并补发一次遗物。
    ///
    /// 实证案例：TouhouAncients【无底之胃】"吞噬初始遗物与先古遗物以外的全部遗物"
    /// 会把【瓦库形态】吃掉，纯"持有遗物"判据会让瓦库当场停摆。
    /// 兜底后托管不中断，同时调度一次补发以恢复 +1 能量与遗物栏显示
    /// （补发走 <see cref="LocalMultiControlRuntime.GrantWakuuRelicsAsync"/>，已有则跳过）。
    /// </summary>
    private static bool IsTakeoverPlayerFallback(Player? player)
    {
        if (!LocalWakuuAutopilotConfig.KeepWakuuFormRelic)
        {
            return false;
        }

        if (player == null || !LocalSelfCoopContext.IsEnabled)
        {
            return false;
        }

        if (!LocalSelfCoopContext.IsWakuuEnabled(player.NetId))
        {
            return false;
        }

        if (_takeoverRelicMissingWarned.Add(player.NetId))
        {
            LocalMultiControlLogger.Warn(
                $"检测到瓦库托管遗物缺失（多半被第三方效果移除），按瓦库名单继续托管: player={player.NetId}");
        }

        if (_takeoverRelicRestoreScheduled.Add(player.NetId) && player.RunState is RunState runState)
        {
            TaskHelper.RunSafely(LocalMultiControlRuntime.GrantWakuuRelicsAsync(runState));
        }

        return true;
    }

    /// <summary>开局/读档时重置托管遗物兜底状态（重新武装 WARN 与补发调度）。</summary>
    public static void ResetTakeoverFallbackState()
    {
        _takeoverRelicMissingWarned.Clear();
        _takeoverRelicRestoreScheduled.Clear();
    }

    public static async Task ExecuteBeforePlayPhaseStartAsync(
        RelicModel relic,
        PlayerChoiceContext choiceContext,
        Player player)
    {
        if (!LocalSelfCoopContext.IsEnabled || player != relic.Owner)
        {
            return;
        }

        ICombatState? combatState = player.Creature.CombatState;
        if (combatState == null || CombatManager.Instance.IsOverOrEnding)
        {
            return;
        }

        // Phase 2.5：战斗内自动用药水（独立开关默认关；果汁另有"到手即喝"链路，见
        // PotionProcuredAutoDrinkPatch）。回合开始相位放在出牌循环之前、无牌可出的早退之前——
        // 没牌可出的回合同样可能需要喝药。消费即去重，看门狗重复进入为无害空转。
        if (LocalWakuuAutopilotConfig.AutoUsePotions && IsVakuuFormMode(player))
        {
            await LocalWakuuPotionAutoUse.UseEligiblePotionsInCombatAsync(
                relic, player, choiceContext, combatState, WakuuPotionPhase.StartOfTurn);
        }

        CardModel? firstPlayableCard = PileType.Hand.GetPile(relic.Owner).Cards.FirstOrDefault((candidate) => candidate.CanPlay());
        if (firstPlayableCard == null)
        {
            // 没牌可出：回合结束判定也要做（格挡/免伤等防御类药水的时机）
            if (LocalWakuuAutopilotConfig.AutoUsePotions && IsVakuuFormMode(player))
            {
                await LocalWakuuPotionAutoUse.UseEligiblePotionsInCombatAsync(
                    relic, player, choiceContext, combatState, WakuuPotionPhase.EndOfTurn);
            }

            return;
        }

        EnsureWakuuPerspective(player, "before-play-phase");
        relic.Flash();

        bool formFullPlay = IsVakuuFormMode(player) && LocalWakuuAutopilotConfig.PlayAllCards;
        int maxCardsThisTurn = formFullPlay ? MaxCardsToPlayForm : MaxCardsToPlay;
        if (formFullPlay)
        {
            LocalMultiControlLogger.Info(
                $"瓦库形态全量出牌模式: player={player.NetId}, round={combatState.RoundNumber}, cap={maxCardsThisTurn}");
        }

        bool reachedPlayLimit;
        int cardsPlayed;
        bool gateEntered = false;
        ulong enterTick = Time.GetTicksMsec();
        ulong gateWaitStartTick = Time.GetTicksMsec();
        LocalMultiControlLogger.Info(
            $"瓦库选择器闸门等待: player={player.NetId}, round={combatState.RoundNumber}, source={choiceContext.GetType().Name}, inFlight={Volatile.Read(ref _selectorScopeInFlight)}");
        await SelectorScopeGate.WaitAsync();
        gateEntered = true;
        int inFlight = Interlocked.Increment(ref _selectorScopeInFlight);
        ulong gateWaitMs = Time.GetTicksMsec() - gateWaitStartTick;
        SelectorStackSnapshot gateEnterSnapshot = SnapshotSelectorStack();
        LocalMultiControlLogger.Info(
            $"瓦库选择器闸门已进入: player={player.NetId}, round={combatState.RoundNumber}, waitMs={gateWaitMs}, inFlight={inFlight}, selectorStackCount={gateEnterSnapshot.Count}, selectorStackTop={gateEnterSnapshot.TopType}");
        try
        {
            using (CardSelectCmd.PushSelector(new LocalWakuuStrategySelector()))
            {
                SelectorStackSnapshot pushSnapshot = SnapshotSelectorStack();
                LocalMultiControlLogger.Info(
                    $"瓦库选择器作用域进入: player={player.NetId}, round={combatState.RoundNumber}, selectorStackCount={pushSnapshot.Count}, selectorStackTop={pushSnapshot.TopType}");
                // 任务 2.2：出牌循环改为向瓦库大脑要"下一步"（默认 HeuristicWakuuBrain =
                // 原「第一张可打牌 + ResolveTarget」逻辑原样搬移，行为零变化）。
                IWakuuCombatBrain brain = WakuuBrainFactory.Create();
                for (cardsPlayed = 0; cardsPlayed < maxCardsThisTurn; cardsPlayed++)
                {
                    if (TryGetAutoplayUnsafeReason(combatState, out string unsafeReason))
                    {
                        LocalMultiControlRuntime.RecordFlowBlockSignal(
                            "autoplay_skipped_due_to_phase",
                            unsafeReason,
                            player.NetId,
                            "wakuu-autoplay-loop",
                            combatState.RoundNumber,
                            dedupePerRoundPlayer: true);
                        LocalMultiControlLogger.Warn(
                            $"瓦库自动出牌已熔断跳过本次执行: player={player.NetId}, round={combatState.RoundNumber}, reason={unsafeReason}, played={cardsPlayed}");
                        break;
                    }

                    if (CombatManager.Instance.IsOverOrEnding)
                    {
                        break;
                    }

                    var ctx = new WakuuDecisionContext(
                        wakuu: relic.Owner,
                        combat: combatState,
                        hand: PileType.Hand.GetPile(relic.Owner).Cards,
                        energy: relic.Owner.PlayerCombatState?.Energy ?? 0,
                        turnNumber: combatState.RoundNumber,
                        playedThisTurn: cardsPlayed,
                        isBackground: IsBackgroundHostedWakuu(player));

                    if (!brain.TryDecideNext(ctx, out WakuuPlannedAction next)
                        || next.Kind != WakuuActionKind.PlayCard
                        || next.Card == null)
                    {
                        // 无牌可出（EndTurn）或大脑拒绝决策 → 结束出牌
                        break;
                    }

                    await next.Card.SpendResources();
                    await CardCmd.AutoPlay(choiceContext, next.Card, next.Target, AutoPlayType.Default, skipXCapture: true);
                }

                reachedPlayLimit = cardsPlayed >= maxCardsThisTurn;
                if (reachedPlayLimit && formFullPlay)
                {
                    // 全量模式触到护栏上限：大概率是异常效果反复生成可出牌，记录日志便于排查。
                    LocalMultiControlLogger.Warn(
                        $"瓦库形态全量出牌触及护栏上限: player={player.NetId}, round={combatState.RoundNumber}, cap={maxCardsThisTurn}");
                }

                SelectorStackSnapshot popSnapshot = SnapshotSelectorStack();
                LocalMultiControlLogger.Info(
                    $"瓦库选择器作用域退出: player={player.NetId}, round={combatState.RoundNumber}, cardsPlayed={cardsPlayed}, reachedLimit={reachedPlayLimit}, selectorStackCount={popSnapshot.Count}, selectorStackTop={popSnapshot.TopType}");
            }
        }
        catch (Exception exception)
        {
            // 运行清理期（退出这一局/回主菜单）自动出牌作用域仍在飞 → 游戏状态已被拆掉，异常属**预期中止**：
            // 降级为 INFO 且不上抛（上抛会让看门狗再报一条失败，把一次正常退出记成两个问题）。
            if (IsRunTeardown())
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自动出牌在运行清理期正常中止（预期）: player={player.NetId}, "
                    + $"round={combatState.RoundNumber}, error={exception.Message}");
                return;
            }

            LocalMultiControlLogger.Warn(
                $"瓦库选择器作用域异常退出: player={player.NetId}, round={combatState.RoundNumber}, error={exception.Message}");
            throw;
        }
        finally
        {
            if (gateEntered)
            {
                int remainInFlight = Interlocked.Decrement(ref _selectorScopeInFlight);
                SelectorScopeGate.Release();
                ulong elapsedMs = Time.GetTicksMsec() - enterTick;
                SelectorStackSnapshot releaseSnapshot = SnapshotSelectorStack();
                LocalMultiControlLogger.Info(
                    $"瓦库选择器闸门已释放: player={player.NetId}, round={combatState.RoundNumber}, elapsedMs={elapsedMs}, inFlight={remainInFlight}, selectorStackCount={releaseSnapshot.Count}, selectorStackTop={releaseSnapshot.TopType}");
                ProbeAndRecoverSelectorStack($"wakuu-selector-finally-{player.NetId}-{combatState.RoundNumber}", allowRecover: true);
            }
        }

        // 出牌结束后跑药水的"回合结束前"相位：覆盖出牌过程中获得的药水（炼药 Alchemize、
        // 混沌结算产物等）以及格挡/免伤/剩能量等以回合结束为时机的规则。
        // 此时能量与手牌状态即"回合结束前"语义。
        if (LocalWakuuAutopilotConfig.AutoUsePotions && IsVakuuFormMode(player) && !CombatManager.Instance.IsOverOrEnding)
        {
            await LocalWakuuPotionAutoUse.UseEligiblePotionsInCombatAsync(
                relic, player, choiceContext, combatState, WakuuPotionPhase.EndOfTurn);
        }

        if (cardsPlayed <= 0)
        {
            return;
        }

        LocString line = reachedPlayLimit
            ? new LocString("relics", "WHISPERING_EARRING.warning")
            : new LocString("relics", "WHISPERING_EARRING.approval");
        TalkCmd.Play(line, relic.Owner.Creature, VfxColor.Purple);
    }

    public static bool TryScheduleWatchdog(Player player, string source)
    {
        return TryScheduleWatchdog(player, source, out _);
    }

    public static bool TryScheduleWatchdog(Player player, string source, out string reason)
    {
        reason = "unknown";
        if (Volatile.Read(ref _selectorScopeInFlight) > 0)
        {
            reason = "selector-scope-busy";
            return false;
        }

        if (LocalManualPlayGuard.IsActive)
        {
            reason = "manual-play-active";
            return false;
        }

        ICombatState? combatState = player.Creature.CombatState;
        if (combatState == null || combatState.CurrentSide != CombatSide.Player || CombatManager.Instance.IsOverOrEnding)
        {
            reason = "invalid-combat-state";
            return false;
        }

        // 有弹层打开（含真人的牌堆/发现选牌）时不起看门狗，避免打断交互。
        if ((NOverlayStack.Instance?.ScreenCount ?? 0) > 0)
        {
            reason = "overlay-open";
            return false;
        }

        RelicModel? relic = TryGetTakeoverRelic(player);
        if (relic == null)
        {
            reason = "no-wakuu-relic";
            return false;
        }

        bool hasPlayableCards = PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay());
        if (!hasPlayableCards)
        {
            reason = "no-playable-cards";
            return false;
        }

        string key = $"{combatState.RoundNumber}:{player.NetId}";
        long nowMs = (long)Time.GetTicksMsec();
        if (_watchdogInFlight.Contains(key))
        {
            reason = "watchdog-in-flight";
            return false;
        }

        if (_watchdogLastRunAt.TryGetValue(key, out long lastRunMs)
            && nowMs - lastRunMs < WatchdogRestartCooldownMs)
        {
            reason = "watchdog-cooldown";
            return false;
        }

        _watchdogLastRunAt[key] = nowMs;
        _watchdogInFlight.Add(key);
        TaskHelper.RunSafely(RunWatchdogAsync(key, relic, player, combatState, source));
        reason = "scheduled";
        return true;
    }

    private static async Task RunWatchdogAsync(
        string key,
        RelicModel relic,
        Player player,
        ICombatState combatState,
        string source)
    {
        ulong? previousNetId = LocalContext.NetId;
        ulong previousSenderId = LocalSelfCoopContext.NetService?.NetId ?? 0UL;
        bool hasNetService = LocalSelfCoopContext.NetService != null;
        try
        {
            if (LocalManualPlayGuard.IsActive)
            {
                return;
            }

            if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress || CombatManager.Instance.IsOverOrEnding)
            {
                return;
            }

            if (combatState.CurrentSide != CombatSide.Player)
            {
                return;
            }

            if (RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
            {
                return;
            }

            if (player.Creature.CombatState != combatState || !HasWakuuRelic(player))
            {
                return;
            }
            if (!PileType.Hand.GetPile(player).Cards.Any((card) => card.CanPlay()))
            {
                return;
            }

            EnsureWakuuPerspective(player, source);
            LocalContext.NetId = player.NetId;
            LocalSelfCoopContext.NetService?.SetCurrentSenderId(player.NetId);

            HookPlayerChoiceContext choiceContext = new HookPlayerChoiceContext(
                relic,
                player.NetId,
                combatState,
                GameActionType.CombatPlayPhaseOnly);
            Task action = ExecuteBeforePlayPhaseStartAsync(relic, choiceContext, player);
            await choiceContext.AssignTaskAndWaitForPauseOrCompletion(action);
            await action;
            LocalMultiControlLogger.Info($"瓦库看门狗已重启自动出牌: player={player.NetId}, source={source}");
        }
        catch (Exception exception)
        {
            if (IsRunTeardown())
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自动出牌看门狗在运行清理期正常中止（预期）: player={player.NetId}, source={source}, error={exception.Message}");
            }
            else
            {
                LocalMultiControlLogger.Warn($"瓦库看门狗重启失败: player={player.NetId}, source={source}, error={exception.Message}");
            }
        }
        finally
        {
            _watchdogInFlight.Remove(key);
            LocalContext.NetId = previousNetId;
            if (hasNetService)
            {
                LocalSelfCoopContext.NetService?.SetCurrentSenderId(previousSenderId);
            }

            Callable.From(delegate
            {
                // 后台托管模式下前台从未切到瓦库（视角档位=不跟随/仅关键节点时），不需要也不应该再自动切走。
                if (ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.ReactivePlay))
                {
                    return;
                }

                LocalMultiControlRuntime.RequestAutoSwitchToNonWakuuOncePerRound($"wakuu-watchdog-{source}");
            }).CallDeferred();
            ProbeAndRecoverSelectorStack($"wakuu-watchdog-finally-{player.NetId}-{combatState.RoundNumber}-{source}", allowRecover: true);
        }
    }

    public static void ProbeAndRecoverSelectorStack(string source, bool allowRecover)
    {
        SelectorStackSnapshot snapshot = SnapshotSelectorStack();
        LocalMultiControlLogger.Info(
            $"瓦库选择器栈探针: source={source}, selectorStackCount={snapshot.Count}, selectorStackTop={snapshot.TopType}, allVakuu={snapshot.AllVakuuSelectors}, inFlight={Volatile.Read(ref _selectorScopeInFlight)}");

        if (!allowRecover || snapshot.Count <= 0)
        {
            return;
        }

        if (Volatile.Read(ref _selectorScopeInFlight) > 0)
        {
            return;
        }

        if (!snapshot.AllVakuuSelectors)
        {
            return;
        }

        if (TryClearSelectorStack(out int clearedCount))
        {
            LocalMultiControlLogger.Warn(
                $"检测到瓦库选择器栈残留，已执行自恢复清理: source={source}, clearedCount={clearedCount}, selectorStackTop={snapshot.TopType}");
        }
    }

    public static SelectorStackSnapshot SnapshotSelectorStack()
    {
        object? rawStack = SelectorStackField?.GetValue(null);
        if (rawStack == null)
        {
            return new SelectorStackSnapshot(0, "null", allVakuuSelectors: false);
        }

        Type stackType = rawStack.GetType();
        int count = (int?)stackType.GetProperty("Count")?.GetValue(rawStack) ?? 0;
        object? top = count > 0 ? stackType.GetMethod("Peek")?.Invoke(rawStack, null) : null;
        string topType = top?.GetType().Name ?? "none";

        bool allVakuuSelectors = count > 0;
        if (rawStack is IEnumerable enumerable)
        {
            foreach (object? selector in enumerable)
            {
                // 托管作用域可能压入游戏原生选择器或本 mod 的策略选择器，两者都视为瓦库选择器
                if (selector is not VakuuCardSelector and not LocalWakuuStrategySelector)
                {
                    allVakuuSelectors = false;
                    break;
                }
            }
        }
        else
        {
            allVakuuSelectors = false;
        }

        return new SelectorStackSnapshot(count, topType, allVakuuSelectors);
    }

    private static bool TryClearSelectorStack(out int clearedCount)
    {
        clearedCount = 0;
        object? rawStack = SelectorStackField?.GetValue(null);
        if (rawStack == null)
        {
            return false;
        }

        Type stackType = rawStack.GetType();
        int count = (int?)stackType.GetProperty("Count")?.GetValue(rawStack) ?? 0;
        if (count <= 0)
        {
            return false;
        }

        stackType.GetMethod("Clear")?.Invoke(rawStack, null);
        clearedCount = count;
        return true;
    }

    private static void EnsureWakuuPerspective(Player player, string source)
    {
        // 后台托管模式：瓦库形态角色不再切前台，直接以临时 owner 上下文出牌（视角档位=不跟随/仅关键节点时抑制）。
        if (ShouldSuppressForegroundSwitch(player, WakuuViewTrigger.ReactivePlay))
        {
            LocalMultiControlLogger.Info(
                $"瓦库形态后台模式，跳过自动切换视角: player={player.NetId}, source={source}");
            return;
        }

        ulong currentControlledPlayerId = LocalMultiControlRuntime.SessionState.CurrentControlledPlayerId
            ?? LocalContext.NetId
            ?? player.NetId;
        if (currentControlledPlayerId == player.NetId)
        {
            return;
        }

        LocalMultiControlLogger.Info($"瓦库自动操作前切换视角: {currentControlledPlayerId} -> {player.NetId}, source={source}");
        LocalMultiControlRuntime.SwitchControlledPlayerTo(player.NetId, $"wakuu-{source}");
    }

    private static bool TryGetAutoplayUnsafeReason(ICombatState combatState, out string reason)
    {
        reason = string.Empty;
        if (!RunManager.Instance.IsInProgress || !CombatManager.Instance.IsInProgress || CombatManager.Instance.IsOverOrEnding)
        {
            reason = "combat-not-in-progress";
            return true;
        }

        if (RunManager.Instance.ActionQueueSynchronizer.CombatState != ActionSynchronizerCombatState.PlayPhase)
        {
            reason = $"sync-{RunManager.Instance.ActionQueueSynchronizer.CombatState}";
            return true;
        }

        if (combatState.CurrentSide != CombatSide.Player)
        {
            reason = $"side-{combatState.CurrentSide}";
            return true;
        }

        NCombatUi? combatUi = NCombatRoom.Instance?.Ui;
        if (combatUi == null)
        {
            reason = "combat-ui-null";
            return true;
        }

        // 任何弹层（牌堆选牌/发现/确认框等）打开期间一律暂停自动出牌：
        // 既可能是真人正在交互（如酒狐合成选牌），也防止全局选择器被抢答。
        if ((NOverlayStack.Instance?.ScreenCount ?? 0) > 0)
        {
            reason = $"overlay-open({NOverlayStack.Instance?.ScreenCount})";
            return true;
        }

        NPlayerHand hand = combatUi.Hand;
        if (hand.InCardPlay)
        {
            reason = "hand-in-card-play";
            return true;
        }

        if (hand.IsInCardSelection)
        {
            reason = "hand-in-card-selection";
            return true;
        }

        if (NTargetManager.Instance?.IsInSelection ?? false)
        {
            reason = "target-selecting";
            return true;
        }

        return false;
    }
}
