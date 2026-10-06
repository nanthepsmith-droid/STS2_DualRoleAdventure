using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「我们联合」（瓦库四功能之三）的运行时编排：战斗界面那个按钮点了之后跑的两步联动。
///
/// 功能（提案《瓦库炼化净化联合地狱战神-功能提案与可行性分析》§4，2026-10-06 拍板两处口径）：
/// <list type="number">
/// <item>① **自己卡组 → 指定瓦库手牌**：从真人主卡组选 1 张，**复制**一份（owner 换成该瓦库）
///   塞进它的**战斗手牌**；</item>
/// <item>② **指定瓦库卡组 → 自己手牌**：从该瓦库主卡组选 1 张，**复制**一份（owner 换成本人）
///   塞进真人的**战斗手牌**。</item>
/// </list>
/// **两向都是复制**（2026-10-06 用户拍板）⇒ 主卡组一个字不动、零存档副作用；
/// 触发形态 = **战斗内 HUD 按钮**（不是开战自动弹窗），**每场战斗一次**（两步都取消则不消耗机会）。
///
/// 关键实现事实（核验报告 §4 + 2026-10-06 复勘）：
/// <list type="bullet">
/// <item>开战时 <c>SetUpCombat</c> 会把主卡组**克隆**进 DrawPile（主卡组与战斗牌堆从此互不影响）
///   ⇒ 「复制一份进战斗手牌」必须**建新卡**，不能搬原牌；</item>
/// <item>建卡走 <c>CombatState.CreateCard(主卡组牌, 目标)</c> ⇒ 新卡属于本场战斗（不会混进主卡组 / 存档）；</item>
/// <item>塞手牌走 <c>CardPileCmd.Add(新卡, PileType.Hand)</c> ⇒ 落到 <c>card.Owner</c> 的 Hand 牌堆
///   （跨玩家加手牌必须让 **owner = 目标**，否则被 <see cref="Patch.NPlayerHandAddOwnerGuardPatch"/> 拦下）；</item>
/// <item>选牌走 <c>CardSelectCmd.FromDeckGeneric(指定玩家, prefs)</c>：牌组来源由 player 参数决定，
///   「展示瓦库的卡组」开箱即用（净化已实机验证）；</item>
/// <item>两道防护缺一不可（r208~r209 教训）：<see cref="CardSelectForegroundSwitchPatch.PushChoiceOwner"/>
///   钉住归属者 + <see cref="CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice"/>
///   压制「瓦库作用域外自动作答」，否则 <c>FromDeckGeneric</c> 会被瓦库选择器替真人点掉。</item>
/// </list>
/// </summary>
internal static class LocalWakuuUniteRuntime
{
    /// <summary>流程进行中（防重入：自建弹层与选牌界面都是异步的，按钮可能被连点）。</summary>
    private static bool _flowInFlight;

    /// <summary>「本场战斗已发动过」的实现 = 记住当时的 <c>CombatId</c>（战斗换 id 自然复位，无需清理钩子）。</summary>
    private static CombatId? _usedCombatId;

    /// <summary>
    /// 按钮可见性的探测节流：<c>LocalCombatSwitchButtons.Refresh</c> 是**逐帧**调用的，
    /// 而完整判定要枚举玩家 / 读遗物 / 读卡组 —— 每帧做既浪费又产生小对象。
    /// 可见性对 250ms 的滞后不敏感（不是权威状态），所以这里做**短 TTL 缓存**；
    /// 流程结束 / 退局时显式失效（<see cref="InvalidateButtonProbe"/>），不留"该消失还显示"的窗口。
    /// </summary>
    private const long ButtonProbeIntervalMs = 250L;

    private static long _lastButtonProbeMs;
    private static bool _lastButtonProbeResult;

    /// <summary>战斗界面「我们联合」按钮是否显示（由 <c>LocalCombatSwitchButtons.Refresh</c> 逐帧求值）。</summary>
    internal static bool ShouldShowButton()
    {
        long now = Environment.TickCount64;
        if (_lastButtonProbeMs != 0L && now - _lastButtonProbeMs < ButtonProbeIntervalMs)
        {
            return _lastButtonProbeResult;
        }

        _lastButtonProbeMs = now;
        _lastButtonProbeResult = ProbeShouldShowButton();
        return _lastButtonProbeResult;
    }

    private static bool ProbeShouldShowButton()
    {
        bool coopSession = LocalSelfCoopContext.IsEnabled && LocalSelfCoopContext.UseSingleAdventureMode;
        bool combatInProgress = CombatManager.Instance.IsInProgress;
        bool alreadyUsed = HasUsedThisCombat() || _flowInFlight;

        int candidateCount = 0;
        int actorDeckCount = 0;
        // 昂贵的部分（枚举玩家 / 读遗物 / 读卡组）只在便宜的闸门全过时才做。
        if (LocalWakuuAutopilotConfig.UniteVakuu && coopSession && combatInProgress && !alreadyUsed)
        {
            Player? actor = TryResolveActor();
            if (actor != null)
            {
                candidateCount = CollectCandidates(actor).Count;
                actorDeckCount = CountDeckCards(actor);
            }
        }

        return WakuuUnitePolicy.ShouldShowButton(
            featureEnabled: LocalWakuuAutopilotConfig.UniteVakuu,
            coopSessionActive: coopSession,
            combatInProgress: combatInProgress,
            alreadyUsedThisCombat: alreadyUsed,
            candidateCount: candidateCount,
            actorDeckCount: actorDeckCount);
    }

    /// <summary>让下一次 <see cref="ShouldShowButton"/> 立刻重算（流程结束 / 退局）。</summary>
    private static void InvalidateButtonProbe()
    {
        _lastButtonProbeMs = 0L;
    }

    /// <summary>按钮被点（Godot 信号回调，同步入口）：校验后启动异步流程，自己不打穿信号链。</summary>
    internal static void OnButtonPressed()
    {
        if (_flowInFlight)
        {
            LocalMultiControlLogger.Info("我们联合忽略本次点击：上一次流程还在进行中。");
            return;
        }

        if (!LocalWakuuAutopilotConfig.UniteVakuu)
        {
            LocalMultiControlLogger.Info("我们联合无法发动：设置页「战斗联合」未开启。");
            return;
        }

        if (!CombatManager.Instance.IsInProgress)
        {
            LocalMultiControlLogger.Info("我们联合无法发动：不在战斗中。");
            return;
        }

        Player? actor = TryResolveActor();
        if (actor == null)
        {
            LocalMultiControlLogger.Warn("我们联合无法发动：找不到可发动的真人席位。");
            return;
        }

        if (HasUsedThisCombat())
        {
            LocalMultiControlLogger.Info($"我们联合无法发动：本场战斗已经发动过。actor={actor.NetId}");
            return;
        }

        _flowInFlight = true;
        // 显式丢弃：流程里有多个 await（弹层 + 选牌），异常由 TaskHelper 兜住并打日志，绝不打穿游戏信号链。
        _ = TaskHelper.RunSafely(RunAsync(actor));
    }

    /// <summary>退出对局时复位（R5 生命周期契约；正常路径下 <see cref="_usedCombatId"/> 靠 CombatId 比对也能自愈）。</summary>
    internal static void ResetForRun(string source)
    {
        _flowInFlight = false;
        _usedCombatId = null;
        InvalidateButtonProbe();
        LocalMultiControlLogger.Info($"我们联合状态已复位: {source}");
    }

    private static async Task RunAsync(Player actor)
    {
        try
        {
            List<Player> candidates = CollectCandidates(actor);
            if (candidates.Count == 0)
            {
                LocalMultiControlLogger.Info($"我们联合无法发动：没有可联合的瓦库。actor={actor.NetId}");
                return;
            }

            LocalMultiControlLogger.Info($"我们联合已发动: actor={actor.NetId}, 候选瓦库={candidates.Count}");

            bool gave = await GiveCardToVakuuAsync(actor, candidates);
            bool took = await TakeCardFromVakuuAsync(actor, candidates);

            if (WakuuUnitePolicy.ConsumesCombatChance(gave, took))
            {
                CombatId? combatId = CombatManager.Instance.CurrentCombatId;
                _usedCombatId = combatId;
                LocalMultiControlLogger.Info(
                    $"我们联合完成（本场战斗机会已用）: actor={actor.NetId}, 送去瓦库={gave}, 取回真人={took}");
            }
            else
            {
                LocalMultiControlLogger.Info($"我们联合未发动（两步都取消了，机会保留）: actor={actor.NetId}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"我们联合执行失败: {exception.Message}");
        }
        finally
        {
            _flowInFlight = false;
            // 立刻重算按钮可见性（"本场已用"要马上反映出来，不能等 TTL）。
            InvalidateButtonProbe();
        }
    }

    /// <summary>① 自己卡组选 1 张 → 复制给指定瓦库的战斗手牌。</summary>
    private static async Task<bool> GiveCardToVakuuAsync(Player actor, List<Player> candidates)
    {
        CardModel? picked = await PickOneDeckCardAsync(
            deckOwner: actor,
            chooserNetId: actor.NetId,
            promptKey: LocalWakuuUniteLocalization.GivePromptKey,
            fallbackPrompt: CardSelectorPrefs.UpgradeSelectionPrompt,
            stepLabel: "我们联合①");
        if (picked == null)
        {
            LocalMultiControlLogger.Info($"我们联合①取消：未选择卡牌。actor={actor.NetId}");
            return false;
        }

        ulong? targetId = await LocalWakuuPlayerPicker.PickAsync(
            LocalModText.UniteGivePickTitle, BuildEntries(candidates));
        if (!targetId.HasValue)
        {
            LocalMultiControlLogger.Info($"我们联合①取消：未选择目标瓦库。actor={actor.NetId}");
            return false;
        }

        Player? target = candidates.FirstOrDefault(candidate => candidate.NetId == targetId.Value);
        if (target == null)
        {
            LocalMultiControlLogger.Warn($"我们联合①中止：目标瓦库已不可用。actor={actor.NetId}, target={targetId.Value}");
            return false;
        }

        CardModel? copy = await CopyDeckCardIntoHandAsync(picked, target, "我们联合①");
        if (copy == null)
        {
            return false;
        }

        LocalMultiControlLogger.Info(
            $"我们联合①完成: actor={actor.NetId}, 目标瓦库={target.NetId}, "
            + $"复制并加入手牌的牌={copy.Id.Entry}, 已升级={copy.IsUpgraded}");
        return true;
    }

    /// <summary>② 选一个瓦库 → 从其卡组选 1 张 → 复制给自己的战斗手牌。</summary>
    private static async Task<bool> TakeCardFromVakuuAsync(Player actor, List<Player> candidates)
    {
        ulong? sourceId = await LocalWakuuPlayerPicker.PickAsync(
            LocalModText.UniteTakePickTitle, BuildEntries(candidates));
        if (!sourceId.HasValue)
        {
            LocalMultiControlLogger.Info($"我们联合②取消：未选择瓦库。actor={actor.NetId}");
            return false;
        }

        Player? source = candidates.FirstOrDefault(candidate => candidate.NetId == sourceId.Value);
        if (source == null)
        {
            LocalMultiControlLogger.Warn($"我们联合②中止：目标瓦库已不可用。actor={actor.NetId}, source={sourceId.Value}");
            return false;
        }

        CardModel? picked = await PickOneDeckCardAsync(
            deckOwner: source,
            chooserNetId: actor.NetId,
            promptKey: LocalWakuuUniteLocalization.TakePromptKey,
            fallbackPrompt: CardSelectorPrefs.UpgradeSelectionPrompt,
            stepLabel: "我们联合②");
        if (picked == null)
        {
            LocalMultiControlLogger.Info($"我们联合②取消：未选择卡牌。actor={actor.NetId}, 瓦库={source.NetId}");
            return false;
        }

        CardModel? copy = await CopyDeckCardIntoHandAsync(picked, actor, "我们联合②");
        if (copy == null)
        {
            return false;
        }

        LocalMultiControlLogger.Info(
            $"我们联合②完成: actor={actor.NetId}, 来源瓦库={source.NetId}, "
            + $"复制并加入手牌的牌={copy.Id.Entry}, 已升级={copy.IsUpgraded}");
        return true;
    }

    /// <summary>
    /// 弹原版卡组选牌界面，让**真人**选 1 张（可取消）。返回 null = 取消 / 无可选。
    ///
    /// 两道防护（见类注释）：归属者钉真人 + 压制「瓦库作用域外自动作答」。
    ///
    /// ⚠ 归属者（<paramref name="chooserNetId"/>）必须是**发起者（真人）**，不是卡组的主人 ——
    /// 步骤②是"从瓦库卡组里选牌"，但作答的是真人：若把归属者钉成瓦库，
    /// <c>CardSelectCmdSelectorGuardPatch</c> 会按"归属者是瓦库"改用**它自己的托管选择器**，
    /// 于是这次选牌会被瓦库自动作答（正好与我们要的相反）。
    /// </summary>
    private static async Task<CardModel?> PickOneDeckCardAsync(
        Player deckOwner,
        ulong chooserNetId,
        string promptKey,
        LocString fallbackPrompt,
        string stepLabel)
    {
        // Min=Max=1 ⇒ 构造里 RequireManualConfirmation 为 false，显式打开（否则可能被"直选"路径吃掉）。
        CardSelectorPrefs prefs = new(LocalWakuuUniteLocalization.ResolvePrompt(promptKey, fallbackPrompt), 1)
        {
            Cancelable = true,
            RequireManualConfirmation = true,
        };

        LocalMultiControlLogger.Info(
            $"{stepLabel}选牌交给真人（已压制瓦库自动作答与归属者抢答）: deckOwner={deckOwner.NetId}, "
            + $"chooser={chooserNetId}, 可选牌={CountDeckCards(deckOwner)}");

        List<CardModel> picked;
        using (CardSelectForegroundSwitchPatch.PushChoiceOwner(chooserNetId))
        using (CardSelectWakuuTurnStartAutoAnswerPatch.SuppressForHumanChoice())
        {
            IEnumerable<CardModel> result = await CardSelectCmd.FromDeckGeneric(deckOwner, prefs);
            picked = result?.ToList() ?? new List<CardModel>();
        }

        return picked.FirstOrDefault();
    }

    /// <summary>
    /// 把主卡组里的一张牌**复制**一份塞进 <paramref name="owner"/> 的战斗手牌。
    ///
    /// ⚠ 三条踩过 / 排掉的路（r213 实机 + 源码复核）：
    /// <list type="bullet">
    /// <item><c>CombatState.CreateCard(牌, 目标)</c>（以及 <c>RunState.CreateCard</c>）要的是**规范模型** ——
    ///   内部会 <c>ToMutable()</c> → <c>AssertCanonical()</c>，拿卡组里的**可变实例**去调必抛
    ///   <c>MutableModelException</c>（"Mutable model of type X used in incorrect place."，
    ///   r213 实机就是这样：选牌 / 选人都成功，最后一步建卡炸 ⇒ 看起来"复制了没效果"）；
    ///   而改喂 <c>ModelDb</c> 的 canonical 虽然能过，却会**丢掉升级 / 附魔 / 关键词**（canonical 是白板模板）；</item>
    /// <item><c>CardModel.CreateClone()</c> 只吃**战斗牌堆里**的牌，卡组牌会抛；</item>
    /// <item>跨玩家加手牌必须 **owner = 目标**，否则被 <see cref="Patch.NPlayerHandAddOwnerGuardPatch"/> 拦下。</item>
    /// </list>
    /// 正确姿势 = <c>CombatState.CloneCard(可变实例)</c>（= <c>ClonePreservingMutability()</c> + 注册进本场战斗，
    /// 保留升级 / 附魔等**当前状态**）→ <c>CardModel.GiveToAnotherPlayer(目标)</c> 改归属
    /// （不能走 <c>AddCard(card, owner)</c>：那条路的 owner setter 见"已有 owner"会抛异常，
    /// 游戏自己也是用 <c>GiveToAnotherPlayer</c> 直接改的）→ 再进 Hand 牌堆。
    /// </summary>
    private static async Task<CardModel?> CopyDeckCardIntoHandAsync(CardModel source, Player owner, string stepLabel)
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null)
        {
            LocalMultiControlLogger.Warn($"{stepLabel}中止：取不到战斗状态（战斗已结束？）。card={source.Id.Entry}");
            return null;
        }

        CardModel copy = combatState.CloneCard(source);
        copy.GiveToAnotherPlayer(owner);

        CardPileAddResult result = await CardPileCmd.Add(copy, PileType.Hand);
        if (!result.success)
        {
            LocalMultiControlLogger.Warn(
                $"{stepLabel}中止：加入战斗手牌失败。card={copy.Id.Entry}, owner={owner.NetId}");
            return null;
        }

        return copy;
    }

    /// <summary>是否已在本场战斗发动过（CombatId 变了自然复位）。</summary>
    private static bool HasUsedThisCombat()
    {
        CombatId? current = CombatManager.Instance.CurrentCombatId;
        return current.HasValue && _usedCombatId.HasValue && _usedCombatId.Value == current.Value;
    }

    /// <summary>
    /// 发起者 = **真人席位**（本地席位且不由瓦库 / 联机机器人驱动）：
    /// 优先当前前台（受控位 → 上下文），其次主席位，最后按本地席位顺序兜底。
    /// </summary>
    private static Player? TryResolveActor()
    {
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null)
            {
                return null;
            }

            SeatRegistry seats = LocalSeatSource.CurrentSeats();
            List<ulong> ordered = new();
            if (seats.ForegroundSeatId != 0UL)
            {
                ordered.Add(seats.ForegroundSeatId);
            }

            if (seats.PrimarySeatId != 0UL)
            {
                ordered.Add(seats.PrimarySeatId);
            }

            ordered.AddRange(seats.LocalSeatIds);

            foreach (ulong seatId in ordered)
            {
                // 身份口径问席位表（R0 规则 1）：真人 = 本地席位里驱动三态为 Human 的那个。
                if (seats.DriverOf(seatId) != SeatDriverMode.Human)
                {
                    continue;
                }

                Player? candidate = runState.Players.FirstOrDefault(player => player.NetId == seatId);
                if (candidate?.Creature != null && candidate.Creature.IsAlive)
                {
                    return candidate;
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"我们联合解析发起者失败: {exception.Message}");
        }

        return null;
    }

    /// <summary>
    /// 可联合的瓦库 = 处于瓦库形态、存活、**卡组非空**（步骤②要从它卡组里选牌）且不是发起者本人的其他玩家。
    /// 瓦库身份判定复用 <see cref="LocalWakuuRelicRuntime.IsVakuuFormMode(Player)"/>，不自己写判据。
    /// 任何异常一律吞掉并返回空表（宁可不给按钮，也不要让战斗界面炸掉）。
    /// </summary>
    internal static List<Player> CollectCandidates(Player actor)
    {
        List<Player> candidates = new();
        try
        {
            RunState? runState = RunManager.Instance.DebugOnlyGetState();
            if (runState?.Players == null || actor == null)
            {
                return candidates;
            }

            foreach (Player candidate in runState.Players)
            {
                if (candidate == null || candidate.NetId == actor.NetId)
                {
                    continue;
                }

                if (!LocalWakuuRelicRuntime.IsVakuuFormMode(candidate))
                {
                    continue;
                }

                if (candidate.Creature == null || candidate.Creature.IsDead)
                {
                    continue;
                }

                if (CountDeckCards(candidate) == 0)
                {
                    continue;
                }

                candidates.Add(candidate);
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"枚举可联合瓦库失败: {exception.Message}");
        }

        return candidates;
    }

    /// <summary>主卡组牌数（战斗牌堆与主卡组是两回事，这里要的就是主卡组）。</summary>
    private static int CountDeckCards(Player player)
    {
        return PileType.Deck.GetPile(player).Cards.Count;
    }

    private static List<LocalPlayerPickerEntry> BuildEntries(List<Player> candidates)
    {
        return candidates
            .Select(candidate => new LocalPlayerPickerEntry(candidate.NetId, DescribeCandidate(candidate)))
            .ToList();
    }

    /// <summary>选择器里的候选标签：角色席位 + 血量 + 卡组张数。</summary>
    private static string DescribeCandidate(Player candidate)
    {
        string slotLabel = LocalSelfCoopContext.GetSlotLabel(candidate.NetId);
        decimal currentHp = candidate.Creature?.CurrentHp ?? 0m;
        decimal maxHp = candidate.Creature?.MaxHp ?? 0m;
        return $"{LocalModText.RoleSlot(slotLabel)} · HP {currentHp:0}/{maxHp:0} · 卡组 {CountDeckCards(candidate)}";
    }
}
