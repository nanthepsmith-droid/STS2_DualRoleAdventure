using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 瓦库「作用域外」选牌自动作答（回合开始类遗物效果为主）。
///
/// 背景：酒狐初始遗物、非想天则、战斗开始给牌类遗物（如【工具箱】三选一）会在**瓦库自动出牌作用域
/// 之外**（全局选择器栈为空）弹出选牌：
/// - <c>CardSelectCmd.FromChooseACardScreen</c>：二选一（酒狐「应力/资源」等）；
/// - <c>CardSelectCmd.FromSimpleGrid</c>：网格多选一（工具箱给的无色牌三选一等）。
/// 对后台托管的瓦库角色而言这类界面无人点击，原实现只能切前台交真人处理 —— 与「瓦库托管」语义相悖
/// （实机 marker r107 日志：`combat-choice-FromSimpleGrid` 切前台后需真人替瓦库选牌）。
///
/// 本补丁在「瓦库形态 + 后台托管 + 栈上无选择器」时用策略选择器直接作答，真人无需接管。
/// 若栈上已有选择器（瓦库自动出牌作用域内，如攻击药水选牌），交回原选择器处理，不在此干预。
///
/// 与 <see cref="CardSelectForegroundSwitchPatch"/> 的关系：该补丁的切前台前缀会通过
/// <see cref="IsAutoAnswerEntry"/> + <see cref="ShouldAutoAnswer"/> 识别「这次会被自动作答」，
/// 从而跳过无谓的切前台（避免视角闪一下）。作用域外**不会**被自动作答的选牌仍切前台交真人
/// （防软锁兜底，历史教训 b949dfa：一律自动作答会导致进战斗黑屏）。
/// </summary>
[HarmonyPatch]
internal static class CardSelectWakuuTurnStartAutoAnswerPatch
{
    /// <summary>二选一入口（酒狐初始遗物等）。返回单张，作用于 <c>Task&lt;CardModel?&gt;</c>。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseACardScreen))]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromChooseACardScreenPrefix(
        IReadOnlyList<CardModel> cards,
        Player player,
        ref Task<CardModel?> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外选牌自动作答: player={player.NetId}, options={cards.Count}, "
            + $"mode={LocalWakuuAutopilotConfig.CardPickMode}, source=FromChooseACardScreen");

        __result = ComputeSingleAnswerAsync(cards);
        return false;
    }

    /// <summary>网格多选一入口（工具箱等战斗开始给牌类遗物）。返回多张，作用于 <c>Task&lt;IEnumerable&lt;CardModel&gt;&gt;</c>。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGrid))]
    [HarmonyPriority(Priority.High)]
    [HarmonyPrefix]
    private static bool FromSimpleGridPrefix(
        IReadOnlyList<CardModel> cardsIn,
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外选牌自动作答: player={player.NetId}, options={cardsIn.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
            + "source=FromSimpleGrid");

        __result = ComputeGridAnswerAsync(cardsIn, prefs);
        return false;
    }

    /// <summary>
    /// 奖励网格多选一入口（第三方遗物「从牌组随机展示 N 张，选一张获得它的原始版本复制」等多走这里）。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromSimpleGridForRewards))]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static bool FromSimpleGridForRewardsPrefix(
        List<CardCreationResult> cards,
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        List<CardModel> candidates = cards.Select((creation) => creation.Card).ToList();
        if (candidates.Count == 0)
        {
            return true;
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外选牌自动作答: player={player.NetId}, options={candidates.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
            + "source=FromSimpleGridForRewards");

        __result = ComputeGridAnswerAsync(candidates, prefs);
        return false;
    }

    /// <summary>该选牌入口是否由本补丁对瓦库做「作用域外自动作答」（供切前台前缀判断"要不要切"）。</summary>
    /// <remarks>
    /// <c>FromCombatPile</c> 自 r359 起在内（【前奏】PRELUDE_RUNE 的抽牌堆选牌）：不列进来的话，
    /// 切前台前缀会先把视角切到托管瓦库那一席，然后我们又把界面自动答掉了 ⇒ 真人视角无故被抢一次。
    /// </remarks>
    internal static bool IsAutoAnswerEntry(string source)
    {
        return source is "FromChooseACardScreen" or "FromSimpleGrid" or "FromSimpleGridForRewards"
            or "FromDeckGeneric" or "FromDeckForUpgrade" or "FromHand" or "FromCombatPile";
    }

    /// <summary>
    /// 是否满足「作用域外自动作答」条件（与入口无关的公共判据）。
    /// 供切前台前缀复用（<see cref="CardSelectForegroundSwitchPatch"/>）：命中时切前台是空操作。
    /// </summary>
    internal static bool ShouldAutoAnswer(Player player)
    {
        // 「本次选牌由真人亲自作答」作用域内一律不自动作答（见 SuppressForHumanChoice）。
        if (HumanChoiceScope.Value)
        {
            return false;
        }

        if (!LocalSelfCoopContext.IsEnabled || !LocalSelfCoopContext.UseSingleAdventureMode)
        {
            return false;
        }

        // 席位口径收敛在纯逻辑（BUG-23 方案 B 起与「自绘选牌」两处共用同一原语，避免三处口径漂移）：
        // 本地回环 + 本地席位 + 仅后台托管模式（瓦库不应占用前台、也不需要真人接手）+ 瓦库形态。
        if (!WakuuSelfDrawnChoicePolicy.IsManagedWakuuSeat(
                enabled: true,
                singleAdventureMode: true,
                loopbackSession: RunManager.Instance?.NetService is LocalLoopbackHostGameService,
                isLocalSeat: LocalSeatSource.IsLocalSeat(player.NetId),
                backgroundMode: LocalWakuuAutopilotConfig.BackgroundMode,
                vakuuFormMode: LocalWakuuRelicRuntime.IsVakuuFormMode(player)))
        {
            return false;
        }

        // 栈上是**我们的**策略选择器（瓦库自动出牌作用域内）→ 交回它按场景作答，避免重复作答。
        // ⚠ 这里**不能**写成"栈必须为空"：第三方 mod 也会自己 `PushSelector`（实例：猪猪 mod【重瞳】
        // 在调 `From*` 之前压它自己的选择器），那样我们会**永远**被挡在门外 —— r173 就是这么漏掉的。
        // 0.113：栈按玩家拆分 ⇒ 只看该玩家自己的栈顶。
        if (WakuuSelectorStackAccess.GetTop(player) is LocalWakuuStrategySelector)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 手牌选牌入口：作用域外的「从手牌选一张」（**复制类遗物效果最典型** —— 例：猪猪 mod 的【重瞳】
    /// 战斗开始时「选择一张牌复制」）。
    /// 优先级低于 <see cref="CardSelectHandScenarioPatch"/>：作用域内（瓦库出牌中）由它按场景优先级作答；
    /// 本前缀只在 <see cref="ShouldAutoAnswer"/> 成立（瓦库形态 + 后台托管 + 栈上无选择器）时接管。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHand), new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
        typeof(AbstractModel),
    })]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static bool FromHandPrefix(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        AbstractModel source,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        List<CardModel> candidates;
        try
        {
            candidates = PileType.Hand.GetPile(player).Cards
                .Where(filter ?? (_ => true))
                .ToList();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库作用域外手牌选牌取手牌失败，交回原流程: {exception.Message}");
            return true;
        }

        if (candidates.Count == 0)
        {
            return true;
        }

        WakuuPickScenario scenario = WakuuPriorityPicking.ClassifyHandScenario(
            source?.GetType().Name,
            CardSelectHandScenarioPatch.BuildPrefsLocKey(prefs.Prompt));

        LocalMultiControlLogger.Info(
            $"瓦库作用域外手牌选牌自动作答: player={player.NetId}, options={candidates.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, scenario={scenario}, source={source?.GetType().Name}, entry=FromHand");

        __result = scenario == WakuuPickScenario.Unknown
            ? new LocalWakuuStrategySelector().GetSelectedCards(candidates, prefs.MinSelect, prefs.MaxSelect)
            : new LocalWakuuStrategySelector(scenario).GetSelectedCards(candidates, prefs.MinSelect, prefs.MaxSelect);
        return false;
    }

    /// <summary>
    /// **战斗牌堆**选牌入口（r359，2026-11-15 用户实测「海克斯遗物【前奏】不能瓦库自动选择」）。
    ///
    /// <para>【前奏】(<c>PRELUDE_RUNE</c>，愚者专属) 的首回合效果是「不抽牌，改成从抽牌堆选 N 张加入手牌」
    /// ⇒ 走 <c>CardSelectCmd.FromCombatPile</c>（栈上无选择器 = 作用域外）。原先这里只有一条探针，
    /// 实机日志：<c>瓦库作用域外选牌入口未适配: entry=FromCombatPile, player=…329, selectorStackTop=none</c>
    /// ⇒ 弹 <c>NCombatPileCardSelectScreen</c> 等真人点（且前置的切前台前缀还把视角切到了瓦库那一席）。</para>
    ///
    /// <para>⚠ **只挂带 filter 的 5 参数重载**：4 参数重载的实现就是
    /// <c>return FromCombatPile(context, pile, player, prefs, _ =&gt; true)</c>（`sts2src` 同文件），
    /// 所以两条调用链都会落到这里；重载写错会触发 Harmony `AmbiguousMatchException`（skill 坑 4）。</para>
    ///
    /// <para>候选与游戏原方法**同口径**：<c>pile.Cards</c> + <c>filter</c>；抽牌堆再按稀有度排序
    /// （原版"有选择器"分支就是这样排的 ⇒ 与作用域内瓦库选择器的取牌顺序一致；本处只按稀有度做
    /// **稳定**排序，不做原版次级 key 的卡实例比较）。</para>
    ///
    /// <para>原版「候选数 ≤ MinSelect 且不要求手动确认」会**直接全选、不弹界面** ⇒ 那种情况不接管
    /// （否则等于把原版的"无选择"变成"我们选"，行为反而变了）。</para>
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromCombatPile), new[]
    {
        typeof(PlayerChoiceContext),
        typeof(CardPile),
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
    })]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static bool FromCombatPilePrefix(
        CardPile pile,
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        List<CardModel> candidates;
        try
        {
            candidates = pile.Cards.Where(filter ?? (_ => true)).ToList();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库作用域外战斗牌堆选牌取牌失败(FromCombatPile)，交回原流程: {exception.Message}");
            return true;
        }

        // 原版对这两种情况根本不弹界面（空候选 ⇒ 空结果；候选数 ≤ MinSelect ⇒ 直接全选）⇒ 交回原流程。
        if (candidates.Count == 0
            || (!prefs.RequireManualConfirmation && candidates.Count <= prefs.MinSelect))
        {
            return true;
        }

        if (pile.Type == PileType.Draw)
        {
            candidates = candidates.OrderBy(card => card.Rarity).ToList();
        }

        LocalMultiControlLogger.Info(
            $"瓦库作用域外战斗牌堆选牌自动作答: player={player.NetId}, options={candidates.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
            + $"pile={pile.Type}, source=FromCombatPile");

        __result = ComputeGridAnswerAsync(candidates, prefs);
        return false;
    }

    /// <summary>
    /// 未适配入口探针（**只记日志、不改行为**）：门控成立（本该自动作答）但还没适配的选牌入口，
    /// 复现日志里会直接点名 ⇒ 下次照它扩展，不用再猜第三方走了哪条路。
    /// </summary>

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromHandForUpgrade), new[]
    {
        typeof(PlayerChoiceContext),
        typeof(Player),
        typeof(AbstractModel),
    })]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static void FromHandForUpgradeProbe(Player player)
    {
        ProbeUncoveredEntry(player, "FromHandForUpgrade");
    }

    // ⚠ r389（2026-11-21 实机日志）：`FromHandForDiscard` **不再挂"未适配"探针**。
    //   原版实现（sts2src `CardSelectCmd.FromHandForDiscard`）整段就是
    //   `prefs.ShouldGlowGold = …; return await FromHand(context, player, prefs, filter, source);`
    //   ⇒ 一定会走到本类的 `FromHandPrefix`（已适配）自动作答。旧探针却在 `FromHand` 之前先打
    //   「入口未适配…本次不会自动作答」，紧跟着下一行就是 `瓦库作用域外手牌选牌自动作答 … entry=FromHand`
    //   —— 一句话把排查引向错误方向（实际那 5 次全部由 FromHand 链答掉了）。
    //   `FromHandForUpgrade` 保留探针：它自己走 `NPlayerHand.SelectCards` UI，**不**委派 FromHand，确实未适配。

    /// <summary>
    /// 牌组选牌入口 —— **r174 探针实测第三方遗物「从牌组随机展示 N 张，选一张获得它的原始版本复制」就走这里**
    /// （`entry=FromDeckGeneric`）；事件 / 商店 / 火堆的删牌与升级也会经过它。
    /// </summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckGeneric), new[]
    {
        typeof(Player),
        typeof(CardSelectorPrefs),
        typeof(Func<CardModel, bool>),
        typeof(Func<CardModel, int>),
    })]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static bool FromDeckGenericPrefix(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        Func<CardModel, int>? sortingOrder,
        ref Task<IEnumerable<CardModel>> __result)
    {
        return TryAutoAnswerDeckCards(player, prefs, filter, sortingOrder, "FromDeckGeneric", ref __result);
    }

    /// <summary>牌组「升级」选牌入口（火堆 smith / 事件里的升级类遗物）。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForUpgrade))]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static bool FromDeckForUpgradePrefix(
        Player player,
        CardSelectorPrefs prefs,
        ref Task<IEnumerable<CardModel>> __result)
    {
        return TryAutoAnswerDeckCards(player, prefs, (card) => card.IsUpgradable, null, "FromDeckForUpgrade", ref __result);
    }

    /// <summary>
    /// 牌组选牌的共用实现：候选与游戏原方法**同口径**（`PileType.Deck` + `filter` + `sortingOrder`），
    /// 再用我们的策略选择器作答（min/max 取自 prefs）—— 于是"谁被选中"由 `cardPickMode` / 场景表决定。
    /// </summary>
    private static bool TryAutoAnswerDeckCards(
        Player player,
        CardSelectorPrefs prefs,
        Func<CardModel, bool>? filter,
        Func<CardModel, int>? sortingOrder,
        string entry,
        ref Task<IEnumerable<CardModel>> __result)
    {
        if (!ShouldAutoAnswer(player))
        {
            return true;
        }

        List<CardModel> candidates;
        try
        {
            candidates = PileType.Deck.GetPile(player).Cards
                .Where(filter ?? (_ => true))
                .ToList();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库作用域外牌组选牌取牌失败({entry})，交回原流程: {exception.Message}");
            return true;
        }

        if (candidates.Count == 0)
        {
            return true;
        }

        if (sortingOrder != null)
        {
            candidates = candidates.OrderBy(sortingOrder).ToList();
        }

        // 🆕 r393：牌组通用入口也按 **prompt 标题** 识别「删牌」语义。
        // 实机（2026-11-23 Blitz 商店，`…322559__r391` 日志 L34606）：一次 `TO_REMOVE` 选牌走的是
        // `FromDeckGeneric`（不是 `FromDeckForRemoval`），本前缀按 `cardPickMode=last` **取列表最后一张**
        // ⇒ 先删掉了一张**有附魔的能力牌**，而牌组里还有打击/防御（用户当场发现）。
        // 只映射 `TO_REMOVE`（`ClassifyHandScenario` 里同一套键）：`TO_TRANSFORM` 在牌组通用入口的调用方
        // （木雕之类）语义尚未取证，保持 Unknown = 维持原策略，避免扩大改动面。
        WakuuPickScenario scenario = WakuuPriorityPicking.ClassifyHandScenario(
            sourceTypeName: null,
            CardSelectHandScenarioPatch.BuildPrefsLocKey(prefs.Prompt));

        LocalMultiControlLogger.Info(
            $"瓦库作用域外牌组选牌自动作答: player={player.NetId}, options={candidates.Count}, "
            + $"select={prefs.MinSelect}~{prefs.MaxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
            + $"scenario={scenario}, source={entry}");

        __result = LogDeckAnswerPickAsync(ComputeGridAnswerAsync(candidates, prefs, scenario), player, scenario, entry);
        return false;
    }

    /// <summary>
    /// 把「本屏最终选了哪几张」打出来（r393）。
    ///
    /// <para>为什么需要：这条链（`FromDeckGeneric` 自动作答）以前只打"options=N / 选几张"，**没有结果锚点** ——
    /// 2026-11-23 用户报「先删了有附魔的能力牌」，从日志里根本看不出删的是哪张，只能靠反推规则。</para>
    ///
    /// 日志失败绝不影响选牌链（包 try/catch，异常只打 WARN）。
    /// </summary>
    private static async Task<IEnumerable<CardModel>> LogDeckAnswerPickAsync(
        Task<IEnumerable<CardModel>> answerTask,
        Player player,
        WakuuPickScenario scenario,
        string entry)
    {
        IEnumerable<CardModel> picked;
        try
        {
            picked = await answerTask;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库作用域外牌组选牌作答失败({entry}): {exception.GetType().Name}: {exception.Message}");
            throw;   // 交回原版选择流程（原方法会对异常走它自己的兜底）
        }

        try
        {
            List<CardModel> list = picked?.ToList() ?? new List<CardModel>();
            LocalMultiControlLogger.Info(
                $"瓦库作用域外牌组选牌结果: player={player.NetId}, scenario={scenario}, "
                + $"选中={list.Count} 张 [{string.Join(",", list.Select((card) => card?.Id.Entry ?? "?"))}], source={entry}");
            return list;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库作用域外牌组选牌结果日志失败({entry}，忽略): {exception.Message}");
            return picked ?? Array.Empty<CardModel>();
        }
    }

    /// <summary>牌组「变化」选牌入口 —— 暂只探针（签名含 `CardTransformation`，真出现再适配）。</summary>
    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromDeckForTransformation))]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static void FromDeckForTransformationProbe(Player player)
    {
        ProbeUncoveredEntry(player, "FromDeckForTransformation");
    }

    [HarmonyPatch(typeof(CardSelectCmd), nameof(CardSelectCmd.FromChooseABundleScreen), new[]
    {
        typeof(Player),
        typeof(IReadOnlyList<IReadOnlyList<CardModel>>),
    })]
    [HarmonyPriority(Priority.Low)]
    [HarmonyPrefix]
    private static void FromChooseABundleProbe(Player player)
    {
        ProbeUncoveredEntry(player, "FromChooseABundleScreen");
    }

    internal static void ProbeUncoveredEntry(Player? player, string entry)
    {
        if (player == null || !ShouldAutoAnswer(player))
        {
            return;
        }

        LocalMultiControlLogger.Warn(
            $"瓦库作用域外选牌入口未适配: entry={entry}, player={player.NetId}, "
            + $"selectorStackTop={WakuuSelectorStackAccess.GetTop(player)?.GetType().Name ?? "none"} —— 本次不会自动作答"
            + "（若该角色是后台托管的瓦库，界面会等真人点）。请把这条日志报给维护者以扩展适配。");
    }

    private static async Task<CardModel?> ComputeSingleAnswerAsync(IReadOnlyList<CardModel> cards)
    {
        LocalWakuuStrategySelector selector = new();
        IEnumerable<CardModel> selected = await selector.GetSelectedCards(cards, 0, 1);
        return selected.FirstOrDefault();
    }

    private static async Task<IEnumerable<CardModel>> ComputeGridAnswerAsync(
        IReadOnlyList<CardModel> cards,
        CardSelectorPrefs prefs,
        WakuuPickScenario scenario = WakuuPickScenario.Unknown)
    {
        // 与 CardSelectCmd.FromSimpleGrid 走全局选择器时的口径一致：min/max 取自 prefs。
        // r393：带上场景 ⇒ 删牌屏会走删除优先级表（诅咒>状态>打击>基础防御>【愧疚】>过渡牌>其余>任务），
        //       Unknown 时行为与旧版完全一致（选择器内部只在 smartPick 开且场景明确时套表）。
        LocalWakuuStrategySelector selector = new(scenario);
        return await selector.GetSelectedCards(cards, prefs.MinSelect, prefs.MaxSelect);
    }

    /// <summary>
    /// 「本次选牌由**真人**亲自作答」作用域（AsyncLocal）：命中时 <see cref="ShouldAutoAnswer"/> 一律 false。
    ///
    /// 用途 = **模组自己发起、但要交给真人点选**的流程。首例：休息处净化 —— 真人替瓦库删牌，
    /// 走的是 <c>CardSelectCmd.FromDeckForRemoval</c> → 内部 <c>FromDeckGeneric</c>，而本补丁把
    /// `FromDeckGeneric` 也列为自动作答入口 ⇒ 不加这个的话目标瓦库会被按 `cardPickMode` 直接作答，
    /// **真人根本看不到选牌界面**（r208 实机：`瓦库作用域外牌组选牌自动作答 … source=FromDeckGeneric`，
    /// 一次删掉 5 张，用户报"不能自己选删什么牌"）。
    ///
    /// AsyncLocal 沿异步链流动 + 释放时恢复原值 ⇒ 不影响并发的其他选牌链。
    /// </summary>
    private static readonly System.Threading.AsyncLocal<bool> HumanChoiceScope = new();

    /// <summary>进入「真人亲自作答」作用域；释放时恢复原值（嵌套安全）。</summary>
    internal static IDisposable SuppressForHumanChoice()
    {
        bool previous = HumanChoiceScope.Value;
        HumanChoiceScope.Value = true;
        return new HumanChoiceScopeToken(previous);
    }

    /// <summary>
    /// ⚠ 本类必须声明在类体**末尾**（所有带 [HarmonyPatch] 的方法之后）—— 离线静态层 S4/S7 的解析器
    /// 按"最近出现过的 class 名"归属方法级 [HarmonyPatch]，插在方法前会被误判成"只有方法级补丁的类"。
    /// </summary>
    private sealed class HumanChoiceScopeToken : IDisposable
    {
        private readonly bool _previous;
        private bool _disposed;

        internal HumanChoiceScopeToken(bool previous)
        {
            _previous = previous;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            HumanChoiceScope.Value = _previous;
        }
    }
}
