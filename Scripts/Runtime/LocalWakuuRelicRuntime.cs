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
using MegaCrit.Sts2.Core.GameActions;
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

    /// <summary>
    /// 「模组自己收口后被放行」的日志去重（r124）：键 = `{回合}:{玩家}`。
    /// 这条行为是用户 2026-09-13 明确要求保留的（BUG-10 的 r123 收敛），且**很难自然复现**，
    /// 所以留一条 INFO 便于实机确认它真的放行了。
    /// </summary>
    private static readonly HashSet<string> _modEndExemptionLogged = new();

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

    /// <summary>
    /// 改进-2 / B1：回合开始 hook 登记「该瓦库本回合要出牌」的时间戳（key = `round:netId`）。
    /// 出牌实际由 tick 驱动的看门狗在 PlayPhase 内完成，这里用于量化
    /// 「回合开始 → 出牌启动」的交接延迟；同时在重置托管状态时清空。
    /// </summary>
    private static readonly Dictionary<string, ulong> _turnStartIntentAtMs = new();

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
        _turnStartIntentAtMs.Clear();
    }

    /// <summary>
    /// 回合开始触发（改进-2 / B1 修正版）：**只做回合开始相位该做的事**——用药、视角、遗物闪光、
    /// 登记"该瓦库本回合要出牌"的意图，然后立即返回；**出牌一律交给看门狗**
    /// （tick 驱动，经 <see cref="TryScheduleWatchdog"/> → <see cref="RunWatchdogAsync"/> →
    /// <see cref="ExecuteBeforePlayPhaseStartAsync"/>）。
    ///
    /// **为什么把出牌从 hook 里摘掉（实测前提修正）**：原实现在
    /// <c>RelicModel.AfterAutoPrePlayPhaseEnteredLate</c> 里直接 await 整串出牌，但该 hook 触发时
    /// <c>ActionQueueSynchronizer.CombatState</c> **还不是 PlayPhase** —— 原版要等**所有玩家**的
    /// AutoPrePlay 都跑完才 `SetCombatState(PlayPhase)`（sts2src `CombatManager.cs:814-835`），
    /// 而 <see cref="TryGetAutoplayUnsafeReason"/> 第一项就要求 PlayPhase。
    /// 所以那段出牌循环**必然在第一次判断就熔断**（`reason=sync-NotPlayPhase`、`played=0`）：
    /// 一张牌都出不了，却每回合每瓦库白占一次选择器闸门、压/弹一次选择器作用域、
    /// 并刷出「瓦库自动出牌已熔断跳过本次执行」的 WARN 噪声（历史日志里被当成"设计内噪声"）。
    /// 摘掉后出牌只剩一条路径（看门狗），日志干净、少一次闸门与反射探针往返，**行为不变**。
    /// </summary>
    public static async Task HandleTurnStartHookAsync(
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

        ulong hookStartTick = Time.GetTicksMsec();

        // Phase 2.5：战斗内自动用药水（独立开关默认关；果汁另有"到手即喝"链路，见 PotionProcuredAutoDrinkPatch）。
        // 保持原相位与顺序（回合开始 → 出牌前），只把它留在 hook 里（本方法唯一的 await 点）。
        if (LocalWakuuAutopilotConfig.AutoUsePotions && IsVakuuFormMode(player))
        {
            await LocalWakuuPotionAutoUse.UseEligiblePotionsInCombatAsync(
                relic, player, choiceContext, combatState, WakuuPotionPhase.StartOfTurn);
        }

        CardModel? firstPlayableCard = PileType.Hand.GetPile(relic.Owner).Cards.FirstOrDefault((candidate) => candidate.CanPlay());
        if (firstPlayableCard == null)
        {
            // 没牌可出：回合结束判定也要做（格挡/免伤等防御类药水的时机）。
            // 此时不会有看门狗（它要求"手上有可出牌"），所以这里是这类药水的唯一时机。
            if (LocalWakuuAutopilotConfig.AutoUsePotions && IsVakuuFormMode(player))
            {
                await LocalWakuuPotionAutoUse.UseEligiblePotionsInCombatAsync(
                    relic, player, choiceContext, combatState, WakuuPotionPhase.EndOfTurn);
            }

            return;
        }

        EnsureWakuuPerspective(player, "turn-start-hook");
        relic.Flash();

        string intentKey = $"{combatState.RoundNumber}:{player.NetId}";
        PruneTurnStartIntents(combatState.RoundNumber);
        _turnStartIntentAtMs[intentKey] = Time.GetTicksMsec();

        LocalMultiControlLogger.Info(
            $"瓦库回合开始已登记出牌意图（B1，出牌交由看门狗）: player={player.NetId}, "
            + $"round={combatState.RoundNumber}, hookMs={Time.GetTicksMsec() - hookStartTick}");
    }

    /// <summary>只保留当前回合的意图记录，避免跨回合残留。</summary>
    private static void PruneTurnStartIntents(int currentRound)
    {
        if (_turnStartIntentAtMs.Count == 0)
        {
            return;
        }

        string prefix = $"{currentRound}:";
        List<string> stale = _turnStartIntentAtMs.Keys.Where((key) => !key.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        foreach (string key in stale)
        {
            _turnStartIntentAtMs.Remove(key);
        }

        // 同口径清理放行日志的去重键，避免跨回合/跨战斗残留。
        foreach (string key in _modEndExemptionLogged.Where((key) => !key.StartsWith(prefix, StringComparison.Ordinal)).ToList())
        {
            _modEndExemptionLogged.Remove(key);
        }
    }

    /// <summary>
    /// 改进-2 / Phase 2 的**唯一出牌实现**（B1 起只由看门狗在 PlayPhase 内调用；
    /// 回合开始 hook 不再调用它，原因见 <see cref="HandleTurnStartHookAsync"/>）。
    /// </summary>
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
            using (WakuuSelectorRegistry.Open(player.NetId, new LocalWakuuStrategySelector()))
            {
                SelectorStackSnapshot pushSnapshot = SnapshotSelectorStack();
                LocalMultiControlLogger.Info(
                    $"瓦库选择器作用域进入: player={player.NetId}, round={combatState.RoundNumber}, selectorStackCount={pushSnapshot.Count}, selectorStackTop={pushSnapshot.TopType}");
                // 任务 2.2：出牌循环改为向瓦库大脑要"下一步"（默认 HeuristicWakuuBrain =
                // 原「第一张可打牌 + ResolveTarget」逻辑原样搬移，行为零变化）。
                IWakuuCombatBrain brain = WakuuBrainFactory.Create();
                for (cardsPlayed = 0; cardsPlayed < maxCardsThisTurn; cardsPlayed++)
                {
                    if (TryGetAutoplayUnsafeReason(combatState, player, out string unsafeReason))
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

                    // 闸门已放行 —— 此时若仍处于"模组收口被放行"状态，才真的算"收口后又拿到可出牌继续打"。
                    LogModIssuedEndExemptionOnce(player, combatState.RoundNumber);

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

                    // 改进-2 / 方案 D 实验档（默认关）：路径判定走纯函数（见 WakuuPlayQueuePolicy）。
                    WakuuPlayPath playPath = WakuuPlayQueuePolicy.DecidePath(
                        LocalWakuuAutopilotConfig.WakuuPlayQueue,
                        LocalSelfCoopContext.IsEnabled,
                        IsVakuuFormMode(player));

                    // 扣费归属：只有 inline AutoPlay 需要"外层先花"（它随后跳过 X 捕获）；
                    // 队列路径由 PlayCardAction 自己扣 —— 外层再花一次就是双重扣能量（方案 §12.2 ③）。
                    if (WakuuPlayQueuePolicy.NeedsExternalSpendResources(playPath))
                    {
                        await next.Card.SpendResources();
                    }

                    if (playPath == WakuuPlayPath.ActionQueue)
                    {
                        if (!await TryPlayCardViaActionQueueAsync(player, next, combatState.RoundNumber))
                        {
                            // 没能出牌（目标解析不到 / 入队后被取消）：牌留在手牌，收手交看门狗下一轮再判。
                            break;
                        }

                        continue;
                    }

                    // 改进-2 / r117：瓦库出牌加速 —— 跳过「牌飞向 Play 区 + 烟雾 VFX + 各牌堆补间」与两段固定等待
                    // （CardModel.OnPlayWrapper 内 CustomScaledWait 0.25~0.35s / 0.15~0.3s）。
                    // 多瓦库是串行的，单张牌的耗时会直接相加成整回合时长；实测每张牌 ~1.0~1.4s，跳过可省一大半。
                    // 判定见纯函数 WakuuPlaySpeedPolicy；关掉开关即恢复完整演出（观感与旧版一致）。
                    bool skipCardPileVisuals = WakuuPlaySpeedPolicy.ShouldSkipCardPileVisuals(
                        LocalWakuuAutopilotConfig.FastWakuuPlay,
                        LocalSelfCoopContext.IsEnabled,
                        IsVakuuFormMode(player));
                    await CardCmd.AutoPlay(
                        choiceContext, next.Card, next.Target, AutoPlayType.Default,
                        skipXCapture: true,
                        skipCardPileVisuals: skipCardPileVisuals);
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

    /// <summary>
    /// 改进-2 / 方案 D 实验档：把这张牌作为 <c>PlayCardAction</c> 入**该瓦库自己的**动作队列。
    ///
    /// 用的就是原版「代理玩家出牌」那条路 —— <c>CardModel.EnqueueManualPlay</c> 全文只有一行：
    /// <c>RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new PlayCardAction(this, target))</c>。
    /// <c>RequestEnqueue</c> 在本 mod 的场景（主机）会直接 `EnqueueWithoutSynchronizing` 并分配**全局递增
    /// action ID**，跨玩家顺序由 ID 决定（与真多人一致）；动作类型是 `CombatPlayPhaseOnly`，
    /// 非 PlayPhase 时会被它自己顺延（调用点已在看门狗里确认过 PlayPhase）。
    ///
    /// **逐张 await <c>action.CompletionTask</c>**（`GameAction` 公开的"等这个动作彻底跑完"），而不是
    /// "发一串再等"：每张牌执行完（含 `SpendResources` 与 `OnPlayWrapper`）才做下一次决策，
    /// 能量/手牌读数始终准确，也不会堆出一串注定被逐个 `Cancel` 的动作。
    ///
    /// 目标按**原版真人出牌口径**归一（见 <see cref="WakuuPlayQueuePolicy.ShouldUseResolvedTarget"/>）：
    /// 仅 `AnyEnemy` / `AnyAlly` 用大脑解析出来的目标，其余一律传 null —— `PlayCardAction` 会用
    /// <c>CardModel.IsValidTarget</c> 校验，"非 Any 的牌 + 非空目标"会被判非法而 `Cancel()`。
    /// 目标解析不到时**不构造动作**（避免必然被取消的入队），返回 false 让出牌循环收手。
    ///
    /// 返回 false = 本次没能出牌（牌仍在手牌），调用方应停止本轮出牌，交给看门狗下一轮重新判定。
    /// </summary>
    private static async Task<bool> TryPlayCardViaActionQueueAsync(
        Player player, WakuuPlannedAction next, int round)
    {
        CardModel card = next.Card!;
        bool isAnyTarget = card.TargetType is TargetType.AnyEnemy or TargetType.AnyAlly;
        Creature? target = WakuuPlayQueuePolicy.ShouldUseResolvedTarget(isAnyTarget) ? next.Target : null;

        if (!card.CanPlayTargeting(target))
        {
            LocalMultiControlLogger.Info(
                $"瓦库出牌入队前检查未通过（牌留在手牌，交看门狗下一轮）: player={player.NetId}, round={round}, "
                + $"card={card.Id.Entry}, targetType={card.TargetType}, target={target?.LogName ?? "无"}");
            return false;
        }

        ulong startTick = Time.GetTicksMsec();
        PlayCardAction action = new(card, target);
        try
        {
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(action);
            await action.CompletionTask;
        }
        catch (OperationCanceledException)
        {
            // 动作被 Cancel（牌已不在手牌 / 费用不够 / 目标失效 / 战斗收尾）→ 属正常收手，不上抛。
            LocalMultiControlLogger.Info(
                $"瓦库出牌入队后被取消（牌留在手牌，交看门狗下一轮）: player={player.NetId}, "
                + $"round={round}, card={card.Id.Entry}");
            return false;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库出牌入队执行异常: player={player.NetId}, round={round}, "
                + $"card={card.Id.Entry}, error={exception.Message}");
            return false;
        }

        LocalMultiControlLogger.Info(
            $"瓦库出牌走动作队列完成（方案 D 实验档）: player={player.NetId}, round={round}, "
            + $"card={card.Id.Entry}, actionId={action.Id?.ToString() ?? "none"}, ms={Time.GetTicksMsec() - startTick}");
        return true;
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

        // 该玩家自己的回合状态不适合再出牌 → 根本不调度看门狗（判定与出牌循环同一来源，见 WakuuTurnEndOrigin）：
        // 真正拦截在出牌循环里（`player-ready-to-end-turn`），这里只是避免每 300ms 空转一次
        // 并反复刷「瓦库自动出牌已熔断跳过本次执行」WARN。
        if (WakuuTurnEndOrigin.ShouldStopAutoplay(
                CombatManager.Instance.IsPlayerReadyToEndTurn(player),
                WakuuTurnEndOrigin.EndedExternally(player.NetId, combatState.RoundNumber),
                WakuuTurnEndOrigin.EndedByMod(player.NetId, combatState.RoundNumber),
                CombatManager.Instance.AllPlayersReadyToEndTurn()))
        {
            reason = "player-ready-to-end-turn";
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

            // 改进-2 / B1：量化「回合开始 hook → 出牌启动」的交接延迟（即 tick 调度延迟）。
            string intentKey = $"{combatState.RoundNumber}:{player.NetId}";
            if (_turnStartIntentAtMs.TryGetValue(intentKey, out ulong intentAtMs))
            {
                _turnStartIntentAtMs.Remove(intentKey);
                LocalMultiControlLogger.Info(
                    $"瓦库回合开始→出牌启动延迟: player={player.NetId}, round={combatState.RoundNumber}, "
                    + $"delayMs={Time.GetTicksMsec() - intentAtMs}, source={source}");
            }

            HookPlayerChoiceContext choiceContext = new HookPlayerChoiceContext(
                relic,
                player.NetId,
                combatState,
                GameActionType.CombatPlayPhaseOnly);
            ulong playStartTick = Time.GetTicksMsec();
            Task action = ExecuteBeforePlayPhaseStartAsync(relic, choiceContext, player);
            await choiceContext.AssignTaskAndWaitForPauseOrCompletion(action);
            await action;
            LocalMultiControlLogger.Info(
                $"瓦库出牌耗时: player={player.NetId}, round={combatState.RoundNumber}, "
                + $"ms={Time.GetTicksMsec() - playStartTick}, source={source}");
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

    /// <summary>
    /// 「模组自己收口后被放行」留痕（r124 首版有误 → r125 修正）：这一条是用户 2026-09-13 明确要求
    /// **保留**的行为（BUG-10 的 r123 收敛），但它依赖"收口后又拿到可出牌"这个不易自然出现的时序，
    /// 所以放行时打一条 INFO（每次「回合+玩家」只打一条），便于实机确认它真的生效了。
    ///
    /// **判据必须与闸门一致**：r124 首版只看 `IsPlayerReadyToEndTurn`，结果把**虚空形态**那种
    /// 外部强行结束也误报成"模组收口后继续出牌"（实机日志 8926 误报 / 8927 紧接着熔断）。
    /// 现在要求 `WakuuTurnEndOrigin.IsModIssuedExemption` 成立，并且**放在闸门之后调用** ——
    /// 即"闸门已经放行、这一次迭代会真的去打牌"才记录。
    /// </summary>
    private static void LogModIssuedEndExemptionOnce(Player player, int round)
    {
        if (!WakuuTurnEndOrigin.IsModIssuedExemption(
                CombatManager.Instance.IsPlayerReadyToEndTurn(player),
                WakuuTurnEndOrigin.EndedExternally(player.NetId, round),
                WakuuTurnEndOrigin.EndedByMod(player.NetId, round),
                CombatManager.Instance.AllPlayersReadyToEndTurn()))
        {
            return;
        }

        if (_modEndExemptionLogged.Add($"{round}:{player.NetId}"))
        {
            LocalMultiControlLogger.Info(
                $"瓦库在模组收口后继续出牌（本回合内又拿到可出牌，用户要求保留）: player={player.NetId}, round={round}");
        }
    }

    private static bool TryGetAutoplayUnsafeReason(ICombatState combatState, Player player, out string reason)
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

        // 该玩家**自己**的回合状态（逐玩家判定，不是全局），判据与理由见 WakuuTurnEndOrigin：
        //   ① 被**卡牌效果/原版强行结束**（虚空形态 `VoidForm.OnPlay` → `PlayerCmd.EndTurn(owner, false)`）
        //      或来源不明 → 停手（BUG-10；用户 2026-09-13 实机确认已修）；
        //   ② 被**模组自己收口**（无牌可出时 `TryEndAllPlayersWhenNoCards`）→ 只要还没"全员 ready"就放行，
        //      让它在**本回合内又因别人的效果拿到可出牌**时继续打（用户明确要求保留这条行为）；
        //   ③ 一旦 `AllPlayersReadyToEndTurn`（回合马上推进）→ 一律停手，不许打进攻城窗口。
        // 用**逐玩家**的 `IsPlayerReadyToEndTurn` 而不是全局 `PlayerActionsDisabled`：后者是单个布尔，
        // 且 mod 自己按**前台玩家**反复重算它（LocalMultiControlRuntime.cs:2457），代表不了每一个瓦库。
        if (WakuuTurnEndOrigin.ShouldStopAutoplay(
                CombatManager.Instance.IsPlayerReadyToEndTurn(player),
                WakuuTurnEndOrigin.EndedExternally(player.NetId, combatState.RoundNumber),
                WakuuTurnEndOrigin.EndedByMod(player.NetId, combatState.RoundNumber),
                CombatManager.Instance.AllPlayersReadyToEndTurn()))
        {
            reason = "player-ready-to-end-turn";
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
