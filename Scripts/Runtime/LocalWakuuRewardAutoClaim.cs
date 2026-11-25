using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库角色战后奖励自动领取（可行性分析·需求三 a + 待确认 #1 拍板）：
/// 卡牌奖励领最左（复用游戏自带 VakuuCardSelector.GetSelectedCardReward 的 FirstOrDefault 语义），
/// 金币/遗物直接结算；药水按 2026-08-25 追加规则领取；非瓦库角色的奖励一律不动。
///
/// 药水领取规则：
/// - 药水栏有空位 → 直接领；
/// - 满栏且栏内有鲜血药水 → 先喝掉腾位再领；
/// - 满栏无鲜血 → 奖励稀有度高于栏内最低稀有度时丢弃栏内最低者再领，否则放弃。
///
/// 实现要点：
/// - 结算走 reward.SelectUnsynchronized()（绕过 RewardsSetSynchronizer；
///   本 mod 单进程回环全员本地，无远端分歧），其内部会从父 RewardsSet 移除该奖励。
/// - CardReward.OnSelect 在 LocalContext.IsMe(Player) 且无弹屏时走游戏原生
///   "Selector.GetSelectedCardReward" 自动作答分支（TestMode 的原生工作方式），
///   因此结算期间需要：①临时把上下文对齐到瓦库玩家；②压入选择器作用域；
///   ③用 NCardRewardSelectionScreenAutoClaimPatch 抑制弹屏（仅在本类标记置位时生效）。
/// - 已结算的奖励从展示列表移除，真人看到的奖励界面干净无干扰。
/// - RewardsCmdOfferCustomPatch 复用本类的单件结算入口处理事件/遗物自定义奖励。
/// </summary>
/// <summary>
/// 单个奖励的自动结算结果（r389）。
///
/// <para><b>为什么要分三态</b>：调用方（<c>RewardsCmdOfferCustomPatch</c>）以前只看 bool，于是把
/// 「按药水换栏规则**主动**放弃」也打成「结算未成功」WARN —— 2026-11-21 实机一局 4 条，全是设计如此，
/// 却让排查以为奖励系统坏了。现在"主动放弃"与"真失败"分流，日志各自如实。</para>
/// </summary>
internal enum WakuuRewardSettleOutcome
{
    /// <summary>领到了。</summary>
    Settled,

    /// <summary>按既有规则**主动**放弃（例：药水稀有度不高于栏内最低）。这是设计行为，不是故障。</summary>
    SkippedByRule,

    /// <summary>真的没结算成功（拿不到、异常、上游返回 false）。需要留 WARN。</summary>
    Failed,
}

internal static class LocalWakuuRewardAutoClaim
{
    /// <summary>喝鲜血腾位时等待动作队列处理的超时。</summary>
    private const int BloodDrinkTimeoutMs = 5000;

    private static int _suppressCardRewardDepth;

    /// <summary>
    /// 卡牌奖励自动领取作用域内的归属玩家（供个人记录器区分「瓦库自动」与「真人点选」）。
    /// 用 AsyncLocal 而非普通静态字段：OnSelect 是 await 链，普通字段在并发/嵌套下会读脏。
    /// </summary>
    private static readonly AsyncLocal<ulong?> _autoClaimCardOwner = new();

    /// <summary>NCardRewardSelectionScreenAutoClaimPatch 读取：true 时 ShowScreen 直接返回 null 不弹屏。</summary>
    internal static bool SuppressCardRewardScreen => _suppressCardRewardDepth > 0;

    /// <summary>
    /// 个人记录器读取：非 null 表示当前 CardReward.OnSelect 由瓦库自动领取驱动（Selector 自动作答、不弹屏），
    /// 不是真人决策，不应计入个人偏好样本。值为奖励归属玩家的 NetId。
    /// </summary>
    internal static ulong? AutoClaimCardOwnerId => _autoClaimCardOwner.Value;

    /// <summary>
    /// 「奖励自动领取」作用域内的归属玩家（本类 <see cref="TrySettleAsync"/> 期间置位）。
    ///
    /// 为什么需要它（r143 实机案例）：本作用域会把 `LocalContext` **对齐到瓦库**（<see cref="AlignLocalContext"/>），
    /// 于是作用域内任何代码看到的 `LocalContext.IsMe(瓦库玩家)` 都是 **true** —— 看起来像"真人正看着瓦库"，
    /// 实际是**我们的自动化**造成的。`RewardsCmdOfferCustomPatch` 恰好有一条"前台正是瓦库（IsMe）就不干预、
    /// 交真人点"的判据，于是在这个作用域里被误判：
    /// 瓦库拾取 YUI「赐福」遗物（`CardBlessingRelic.OfferBlessingReward`）→ 遗物在 RelicReward 结算
    /// 作用域内回调 `RewardsCmd.OfferCustom` → IsMe 为真 → **静默交真人** → 原版弹屏等人点。
    /// （与 r54 给"事件自动选择作用域"补 `IsAutoChoosingFor` 是同一个坑，只是换了一个作用域。）
    /// 用 AsyncLocal 与 <see cref="_autoClaimCardOwner"/> 同套理由：await 链上会嵌套/交错，普通字段读脏。
    /// </summary>
    private static readonly AsyncLocal<ulong?> _autoClaimScopeOwner = new();

    /// <summary>
    /// 当前是否处于「为这一位玩家自动领取奖励」的作用域内。供 RewardsCmdOfferCustomPatch 区分
    /// 「真人前台真的是瓦库」与「是我们的自动化把上下文对齐过去了」。
    /// </summary>
    internal static bool IsAutoClaimingFor(Player player)
    {
        // R3 B2 复核：这里比的是「本次操作是不是我为**这一位**开的自动化作用域」（AsyncLocal 归属），
        // 不是"这个 id 是谁" ⇒ 不走席位唯一入口（作用域各自持有自己的 AsyncLocal 是本设计的一部分）。
        return player != null && _autoClaimScopeOwner.Value == player.NetId;
    }

    /// <summary>
    /// 结算合并奖励列表中归属瓦库角色的可自动领取项，返回剩余需要展示给真人的奖励。
    /// 必须在 CombatRewardMergeContext.Enter() 生效期间调用（抑制镜像复制），
    /// 且在构建展示 RewardsSet 之前调用。
    /// </summary>
    public static async Task<List<Reward>> SettleAsync(List<Reward> mergedRewards)
    {
        List<Reward> remaining = new();
        foreach (Reward reward in mergedRewards)
        {
            Player? owner = reward.Player;
            if (owner == null)
            {
                remaining.Add(reward);
                continue;
            }

            WakuuRewardKind kind = ClassifyReward(reward);
            if (!ShouldAutoClaim(kind, owner))
            {
                remaining.Add(reward);
                continue;
            }

            // r389：结算结果三态化（"按规则主动放弃"与"真失败"都算没领到 ⇒ 仍留在展示集里给真人）
            WakuuRewardSettleOutcome outcome = await TrySettleAsync(reward, owner);
            if (outcome != WakuuRewardSettleOutcome.Settled)
            {
                remaining.Add(reward);
            }
        }

        return remaining;
    }

    private static bool ShouldAutoClaim(WakuuRewardKind kind, Player owner)
    {
        return WakuuRewardClaimPolicy.ShouldAutoClaim(
            kind,
            LocalWakuuRelicRuntime.IsVakuuFormMode(owner),
            LocalWakuuAutopilotConfig.AutoClaimCards,
            LocalWakuuAutopilotConfig.AutoClaimGoldRelics,
            LocalWakuuAutopilotConfig.AutoClaimPotions);
    }

    /// <summary>把具体奖励实例映射到策略层的种类（判定表见 <see cref="WakuuRewardClaimPolicy"/>）。</summary>
    private static WakuuRewardKind ClassifyReward(Reward reward)
    {
        return reward switch
        {
            CardReward => WakuuRewardKind.Card,
            GoldReward => WakuuRewardKind.Gold,
            RelicReward => WakuuRewardKind.Relic,
            PotionReward => WakuuRewardKind.Potion,
            // r150：取回被跳虫偷走的牌 / 事件给指定任务牌，都走这一个类型。
            SpecialCardReward => WakuuRewardKind.SpecialCard,
            _ => WakuuRewardKind.Other,
        };
    }

    /// <summary>供 RewardsCmdOfferCustomPatch 判定单个奖励是否可自动领取。</summary>
    internal static bool IsAutoClaimable(Reward reward, Player owner)
    {
        try
        {
            return ShouldAutoClaim(ClassifyReward(reward), owner);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 供 RewardsCmdOfferCustomPatch 结算单个奖励（内部含上下文对齐与异常兜底）。
    /// ⚠ 这条入口**没有真人能接**（后台瓦库原版不弹屏）⇒ "不抓"会退化成照常抓，见 <see cref="SettleCardRewardAsync"/>。
    /// 返回三态（r389）：调用方据此决定"主动放弃"打 INFO 还是"真失败"打 WARN。
    /// </summary>
    internal static Task<WakuuRewardSettleOutcome> TrySettleSingleRewardAsync(Reward reward, Player owner)
    {
        return TrySettleAsync(reward, owner, canLeaveForHuman: false);
    }

    /// <summary>这次卡牌奖励的评估输入（一次性算好，重掷后重算一次）。</summary>
    private readonly struct CardRewardEvaluation
    {
        public CardRewardEvaluation(
            int act, int deckSize, int ordinal, double bestPickRate,
            bool hasSignal, bool canSacrifice, bool canReroll)
        {
            Act = act;
            DeckSize = deckSize;
            Ordinal = ordinal;
            BestPickRate = bestPickRate;
            HasSignal = hasSignal;
            CanSacrifice = canSacrifice;
            CanReroll = canReroll;
        }

        public int Act { get; }

        public int DeckSize { get; }

        /// <summary>本局该席位第几次卡牌奖励（1 起；中卡组前三次保护用）。</summary>
        public int Ordinal { get; }

        /// <summary>候选里最高的融合抓取率。</summary>
        public double BestPickRate { get; }

        /// <summary>候选里有没有任何一张查得到数据（个人或社区）。</summary>
        public bool HasSignal { get; }

        /// <summary>这次奖励有没有献祭选项（佩尔之翼）。</summary>
        public bool CanSacrifice { get; }

        /// <summary>这张奖励还能不能重掷（浮木）。</summary>
        public bool CanReroll { get; }
    }

    /// <summary>
    /// 卡牌奖励结算（2026-11-19 抓牌档位）：
    /// 先按「档位 / 幕 / 牌组张数」判断这次该不该抓，再决定
    /// 照常抓 / 献祭（佩尔之翼）/ 重掷（浮木）/ 留给真人。
    ///
    /// <para>口径来源：用户 2026-11-19 —— 小卡组（上限 30）/ 中卡组（三张都烂不抓 + ≥40 抬门槛 +
    /// 前三次不许跳）/ 大卡组（永不跳）；二三层不单独识别过渡牌，而是**提高抓牌要求**；
    /// 有浮木时**先 roll 再看**，roll 完还是烂、且有佩尔之翼才献祭。</para>
    /// </summary>
    private static async Task<bool> SettleCardRewardAsync(CardReward reward, Player owner, bool canLeaveForHuman)
    {
        WakuuCardPickTier tier = LocalWakuuAutopilotConfig.CardPickTierValue;
        // 极速模式（Blitz）卡牌奖励机会少 ⇒ 门槛降低（2026-11-23 用户口径，见 WakuuCardPickPolicy）。
        bool isBlitz = owner.RunState?.GameMode == MegaCrit.Sts2.Core.Runs.GameMode.Blitz;
        CardRewardEvaluation eval = EvaluateCardReward(reward, owner, countOrdinal: true);
        double required = WakuuCardPickPolicy.RequiredPickRate(tier, eval.Act, eval.DeckSize, isBlitz);
        WakuuCardTakeDecision decision = WakuuCardPickPolicy.Decide(
            tier,
            eval.Act,
            eval.DeckSize,
            eval.Ordinal,
            eval.BestPickRate,
            eval.HasSignal,
            eval.CanSacrifice,
            eval.CanReroll,
            isBlitz);

        // 「不抓」在没有真人能接的入口（事件/遗物自定义奖励）会变成"白丢一张牌" ⇒ 退化成照常抓。
        if (decision == WakuuCardTakeDecision.Skip && !canLeaveForHuman)
        {
            LocalMultiControlLogger.Info(
                $"[瓦库抓牌] 三张都不达标，但该入口没有真人能接（事件/遗物自定义奖励）⇒ 照常抓: "
                + $"player={owner.NetId}, tier={LocalWakuuAutopilotConfig.CardPickTier}");
            decision = WakuuCardTakeDecision.Take;
        }

        if (decision == WakuuCardTakeDecision.Reroll)
        {
            try
            {
                LocalMultiControlLogger.Info(
                    $"[瓦库抓牌] 三张都不达标且有【浮木】，先重掷再看: player={owner.NetId}, "
                    + $"tier={LocalWakuuAutopilotConfig.CardPickTier}, act={eval.Act}, deck={eval.DeckSize}, "
                    + $"best={eval.BestPickRate:P0}, 要求={required:P0}");
                reward.Reroll(); // 重掷后 CanReroll 自动变 false ⇒ 不会循环
                eval = EvaluateCardReward(reward, owner, countOrdinal: false);
                required = WakuuCardPickPolicy.RequiredPickRate(tier, eval.Act, eval.DeckSize, isBlitz);
                decision = WakuuCardPickPolicy.Decide(
                    tier,
                    eval.Act,
                    eval.DeckSize,
                    eval.Ordinal,
                    eval.BestPickRate,
                    eval.HasSignal,
                    eval.CanSacrifice,
                    eval.CanReroll,
                    isBlitz);
                if (decision == WakuuCardTakeDecision.Skip && !canLeaveForHuman)
                {
                    decision = WakuuCardTakeDecision.Take;
                }
            }
            catch (Exception exception)
            {
                // 重掷本身炸了（第三方改过奖励选项 / 手动卡池不可重掷之类）：还有献祭选项就去献祭，
                // 否则按"不抓（留给真人）"处理；没有真人能接的入口退回照常抓。
                decision = eval.CanSacrifice
                    ? WakuuCardTakeDecision.Sacrifice
                    : (canLeaveForHuman ? WakuuCardTakeDecision.Skip : WakuuCardTakeDecision.Take);
                LocalMultiControlLogger.Warn(
                    $"[瓦库抓牌] 重掷失败，改按 {decision} 处理: {exception.Message}");
            }
        }

        LocalMultiControlLogger.Info(
            $"[瓦库抓牌] 决策={decision}: player={owner.NetId}, tier={LocalWakuuAutopilotConfig.CardPickTier}, "
            + $"act={eval.Act}, deck={eval.DeckSize}, 第{eval.Ordinal}次奖励, "
            + $"best={eval.BestPickRate:P0}{(eval.HasSignal ? string.Empty : "(无数据)")}, 要求={required:P0}, "
            + $"献祭可用={eval.CanSacrifice}, 可重掷={eval.CanReroll}");

        if (decision == WakuuCardTakeDecision.Skip)
        {
            return false; // 留给真人：由调用方把这条奖励留在展示集里
        }

        string? preferAlternative = decision == WakuuCardTakeDecision.Sacrifice ? "SACRIFICE" : null;

        // 用本 mod 的托管选择器（带奖励归属者，供社区统计按瓦库角色查表）；
        // 关闭 skadaAssist 时其取牌结果与游戏原生 VakuuCardSelector 完全一致（最左）。
        using (WakuuSelectorRegistry.Open(owner.NetId, new LocalWakuuStrategySelector(owner)
               {
                   PreferAlternativeOptionId = preferAlternative,
               }))
        {
            // 用**深度计数**而不是布尔：结算可以嵌套（r143 起，"遗物效果自建的奖励集"
            // 会在战后奖励结算的 await 链里再进一次本方法），布尔会被内层的 finally 提前清成 false，
            // 外层那次卡牌奖励就会漏抑制 → 又弹出选牌界面。
            _suppressCardRewardDepth++;
            _autoClaimCardOwner.Value = owner.NetId;

            // r134：**同步写入选牌归属者**（与 LocalWakuuEventAutoChoice / LocalWakuuRelicEffectAutoChoice 同一套做法）。
            //
            // 为什么这条链必须自己写：`CardReward.OnSelect` 读的是 `CardSelectCmd.Selector`，
            // 而它**不经过** `CardSelectCmd.From*` 入口 —— 那些入口才会由
            // CardSelectForegroundSwitchPatch 写入 `CurrentChoicePlayerId`。没人写，该 AsyncLocal
            // 就保留**沿异步链残留的旧值**（多半是真人）⇒ `CardSelectCmdSelectorGuardPatch` 判成
            // "真人选牌请求" → 把我们的选择器改成 null ⇒ `CardReward.OnSelect` 抛
            // `Card selector unset during test!` ⇒ **卡牌奖励自动领取失败**。
            // 金币/药水奖励不读 Selector，所以照常领到 —— 实机表现正是"只有卡牌奖励漏领"。
            // 日志实证（2026-09-14）：同一局的 5 个瓦库全是
            // `检测到真人选牌请求，本次跳过瓦库选择器改走正常UI: chooser=…326`
            // + `瓦库奖励自动领取失败，保留为人工领取: reward=CardReward, error=Card selector unset during test!`。
            ulong? savedChoicePlayerId = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
            if (savedChoicePlayerId.HasValue
                && !LocalWakuuRelicRuntime.IsVakuuFormModeById(savedChoicePlayerId.Value))
            {
                // 只在**确实命中了这个坑**时记一条（正常路径不刷屏）：便于日后一眼确认修复生效。
                LocalMultiControlLogger.Info(
                    $"卡牌奖励自动领取：选牌归属者在异步链上残留为其他角色，已改写为奖励归属者: "
                    + $"stale={savedChoicePlayerId.Value}, owner={owner.NetId}");
            }

            CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value = owner.NetId;
            try
            {
                await reward.SelectUnsynchronized();
                LocalMultiControlLogger.Info(
                    $"瓦库卡牌奖励已自动领取: player={owner.NetId}, reward={reward.GetType().Name}, "
                    + $"方式={decision}");
                return true;
            }
            finally
            {
                CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value = savedChoicePlayerId;
                _suppressCardRewardDepth = Math.Max(0, _suppressCardRewardDepth - 1);
                _autoClaimCardOwner.Value = null;
            }
        }
    }

    /// <summary>算一次卡牌奖励的评估输入（候选最高融合抓取率 / 有没有数据 / 献祭与重掷可用性）。</summary>
    private static CardRewardEvaluation EvaluateCardReward(CardReward reward, Player owner, bool countOrdinal)
    {
        double best = 0.0;
        bool hasSignal = false;

        try
        {
            string characterId = owner.Character.Id.Entry.ToUpperInvariant();
            bool isMulti = owner.RunState?.Players.Count > 1;
            PersonalStore store = LocalPersonalRecorder.Snapshot();

            foreach (CardModel card in reward.Cards)
            {
                PersonalCardSlice personal = WakuuPersonalQuery.CountCardOffers(
                    store, card.Id.Entry, isMulti: isMulti);
                WakuuCardSignal? community = WakuuSkadaAdapter.TryGetCardSignal(characterId, card.Id.Entry);
                bool cardHasSignal = personal.Offered > 0 || community.HasValue;
                hasSignal |= cardHasSignal;
                if (!cardHasSignal)
                {
                    continue;
                }

                double blended = WakuuStatBadgeQuery.BlendPickRate(
                    personal.Picked,
                    personal.Offered,
                    community?.PickRate,
                    community?.OfferCount ?? 0);
                if (blended > best)
                {
                    best = blended;
                }
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"[瓦库抓牌] 抓取率评估失败（按无数据处理）: {exception.Message}");
            hasSignal = false;
            best = 0.0;
        }

        int act = (owner.RunState?.CurrentActIndex ?? 0) + 1;
        int deckSize = owner.Deck?.Cards?.Count ?? 0;
        int ordinal = countOrdinal
            ? LocalWakuuCardRewardCounter.Next(owner.NetId)
            : Math.Max(1, LocalWakuuCardRewardCounter.Peek(owner.NetId));

        return new CardRewardEvaluation(
            act, deckSize, ordinal, best, hasSignal,
            canSacrifice: HasSacrificeAlternative(reward),
            canReroll: reward.CanReroll);
    }

    /// <summary>这次奖励有没有【佩尔之翼】的献祭选项（option id 固定 "SACRIFICE"）。</summary>
    private static bool HasSacrificeAlternative(CardReward reward)
    {
        try
        {
            foreach (CardRewardAlternative alternative in CardRewardAlternative.Generate(reward))
            {
                if (alternative != null
                    && string.Equals(alternative.OptionId, "SACRIFICE", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception exception)
        {
            // 生成选项本身失败（第三方改了 alternatives 之类）⇒ 当作没有献祭，别让它影响抓牌主链。
            LocalMultiControlLogger.Warn($"[瓦库抓牌] 读取奖励额外选项失败（按无献祭处理）: {exception.Message}");
        }

        return false;
    }

    /// <param name="canLeaveForHuman">
    /// 这个入口有没有真人能接这张卡牌奖励：合并战后奖励屏 = true（不抓就留在屏上给真人点）；
    /// 事件/遗物自定义奖励（后台瓦库原版不弹屏）= false（"不抓"会退化成照常抓，否则那张牌就白丢）。
    /// </param>
    private static async Task<WakuuRewardSettleOutcome> TrySettleAsync(Reward reward, Player owner, bool canLeaveForHuman = true)
    {
        // R3 B2：作用域进出时保存/还原的都是「回环上下文原值」（等价于旧写法 LocalContext.NetId）。
        ulong? previousNetId = LocalSeatSource.ContextSeatId();
        ulong? previousScopeOwner = _autoClaimScopeOwner.Value;
        try
        {
            // 与看门狗相同的上下文对齐模式：OnSelect 内部依赖 LocalContext.IsMe(Player)
            AlignLocalContext(owner.NetId);
            // 登记"这是自动化作用域"（见 IsAutoClaimingFor 的注释）：作用域内 IsMe(owner) 恒真，
            // 其它补丁不能把它误读成"真人正看着这一位"。
            _autoClaimScopeOwner.Value = owner.NetId;

            switch (reward)
            {
                case CardReward cardReward:
                    return await SettleCardRewardAsync(cardReward, owner, canLeaveForHuman)
                        ? WakuuRewardSettleOutcome.Settled
                        : WakuuRewardSettleOutcome.Failed;

                case GoldReward gold:
                    await reward.SelectUnsynchronized();
                    LocalMultiControlLogger.Info($"瓦库金币奖励已自动领取: player={owner.NetId}, amount={gold.Amount}");
                    return WakuuRewardSettleOutcome.Settled;

                case RelicReward relic:
                    // 拾遗物时若遗物效果触发卡牌选择（如 YUI「灵草丹」等"获得遗物时把一张卡变化"的
                    // 遗物会走 FromDeckForTransformation / FromDeckForUpgrade / FromDeckForRemoval），
                    // 由 LocalWakuuRelicEffectAutoChoice 在 RelicCmd.Obtain 期间统一自动作答（r94）。
                    // r81 曾在本处压栈，但只覆盖"奖励自动领取"一条路径——控制台/事件/商店/第三方授予
                    // 都不走 RelicReward，仍然弹屏；已下沉到公共入口，此处不再压栈（避免重复作答日志）。
                    await reward.SelectUnsynchronized();

                    LocalMultiControlLogger.Info(
                        $"瓦库遗物奖励已自动领取: player={owner.NetId}, relic={relic.Relic?.Id.Entry ?? "?"}");
                    return WakuuRewardSettleOutcome.Settled;

                case PotionReward potionReward:
                    return await TrySettlePotionRewardAsync(potionReward, owner);

                case SpecialCardReward specialCardReward:
                    // r150「取回被偷走的牌」：`SpecialCardReward.OnSelect` 只是把那张卡
                    // `CardPileCmd.Add(card, PileType.Deck)` 加回牌组，**不读** `CardSelectCmd.Selector`、
                    // 也不弹选牌界面 ⇒ 不需要压选择器作用域/抑制弹屏，上下文对齐到归属者即可（本方法已做）。
                    // 不领 = 牌永远拿不回来（`OnSkipped` 只记 wasPicked:false），所以必须自动领。
                    await reward.SelectUnsynchronized();
                    // 牌名不用我们抠：原版 `SpecialCardReward.OnSelect` 自己会打
                    // `Player <id> obtained CARD.X from special card reward`，与本行配对即可核对。
                    LocalMultiControlLogger.Info(
                        $"瓦库特殊卡牌奖励已自动领取（取回被偷走的牌/指定卡牌）: "
                        + $"player={owner.NetId}, reward={specialCardReward.GetType().Name}");
                    return WakuuRewardSettleOutcome.Settled;

                default:
                    return WakuuRewardSettleOutcome.Failed;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"瓦库奖励自动领取失败，保留为人工领取: reward={reward.GetType().Name}, error={exception.Message}");
            return WakuuRewardSettleOutcome.Failed;
        }
        finally
        {
            _autoClaimScopeOwner.Value = previousScopeOwner;
            AlignLocalContext(previousNetId);
        }
    }

    private static void AlignLocalContext(ulong? playerId)
    {
        // R3 B2：写前判定「上下文是不是已经对着这一位」改问席位快照（等价于旧写法 LocalContext.NetId == playerId）；
        // 写点（LocalContext.NetId = …）保持原样 —— 它写的就是这个来源，不能绕。
        if (playerId == null || LocalSeatSource.IsContextSeat(playerId.Value))
        {
            return;
        }

        LocalContext.NetId = playerId.Value;
        LocalSelfCoopContext.NetService?.SetCurrentSenderId(playerId.Value);
    }

    /// <summary>
    /// 药水奖励领取（2026-08-25 拍板规则）：
    /// 有空位直接领；满栏先喝鲜血药水腾位；仍满则比较稀有度——
    /// 奖励高于栏内最低才丢最低换领，等价或更低放弃（保留人工/跳过）。
    /// 稀有度直接按 PotionRarity 枚举序比较（Common&lt;Uncommon&lt;Rare&lt;Event&lt;Token）。
    /// </summary>
    private static async Task<WakuuRewardSettleOutcome> TrySettlePotionRewardAsync(PotionReward reward, Player owner)
    {
        PotionModel? rewardPotion = reward.Potion;
        if (rewardPotion == null)
        {
            return WakuuRewardSettleOutcome.Failed;
        }

        if (!owner.HasOpenPotionSlots)
        {
            List<PotionModel> slots = owner.Potions.ToList();

            // 特例：栏内有鲜血药水 → 先喝掉（回血 20% + 腾位），喝不掉再走稀有度比较
            BloodPotion? blood = slots.OfType<BloodPotion>().FirstOrDefault();
            if (blood != null)
            {
                LocalMultiControlLogger.Info(
                    $"瓦库药水栏已满，先喝鲜血药水腾位: player={owner.NetId}, 奖励={rewardPotion.Id.Entry}");
                // AnyPlayer 目标经 EnqueueManualUse 自动落到自己；NonCombat 动作战后随时可结算
                blood.EnqueueManualUse(null);
                await WaitForPotionRemovedAsync(owner, blood, BloodDrinkTimeoutMs);
            }

            if (!owner.HasOpenPotionSlots)
            {
                PotionModel? lowest = owner.Potions
                    .OrderBy((p) => (int)p.Rarity)
                    .FirstOrDefault();
                if (lowest == null)
                {
                    LocalMultiControlLogger.Warn($"瓦库药水栏状态异常（无药水但无空位），放弃领取: player={owner.NetId}");
                    return WakuuRewardSettleOutcome.Failed;
                }

                if ((int)rewardPotion.Rarity <= (int)lowest.Rarity)
                {
                    // 设计如此（2026-08-25 拍板规则）⇒ SkippedByRule：调用方打 INFO、不打"结算未成功"。
                    LocalMultiControlLogger.Info(
                        $"瓦库药水奖励稀有度不高于栏内最低（{rewardPotion.Rarity} <= {lowest.Rarity}），放弃领取: "
                        + $"player={owner.NetId}, 奖励={rewardPotion.Id.Entry}, 栏内最低={lowest.Id.Entry}");
                    return WakuuRewardSettleOutcome.SkippedByRule;
                }

                LocalMultiControlLogger.Info(
                    $"瓦库药水奖励更优，丢弃栏内最低再领取: player={owner.NetId}, 丢弃={lowest.Id.Entry}({lowest.Rarity}), "
                    + $"领取={rewardPotion.Id.Entry}({rewardPotion.Rarity})");
                await PotionCmd.Discard(lowest);
            }
        }

        bool claimed = await reward.SelectUnsynchronized();
        LocalMultiControlLogger.Info(
            $"瓦库药水奖励自动领取: player={owner.NetId}, potion={rewardPotion.Id.Entry}({rewardPotion.Rarity}), success={claimed}");
        return claimed ? WakuuRewardSettleOutcome.Settled : WakuuRewardSettleOutcome.Failed;
    }

    /// <summary>轮询等待某瓶药水离开药水栏（动作队列异步结算），超时返回 false。</summary>
    private static async Task WaitForPotionRemovedAsync(Player owner, PotionModel potion, int timeoutMs)
    {
        int waitedMs = 0;
        while (owner.Potions.Contains(potion) && waitedMs < timeoutMs)
        {
            await Task.Delay(150);
            waitedMs += 150;
        }

        if (owner.Potions.Contains(potion))
        {
            LocalMultiControlLogger.Warn(
                $"等待药水离开药水栏超时: player={owner.NetId}, potion={potion.Id.Entry}, waitedMs={waitedMs}");
        }
    }
}
