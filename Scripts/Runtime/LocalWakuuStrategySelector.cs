using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Patch;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管专用的策略化选牌选择器（替代游戏自带的 VakuuCardSelector）：
/// - 效果选牌（GetSelectedCards，酒狐合成二选一、开局遗物二选一、各类"从手牌选 N 张"、
///   事件中的附魔/升级/变化选牌等）按 cardPickMode 配置取牌：
///   first=最前 / last=最后（默认）/ random=随机 / rare=稀有度最高，
///   解决"合成永远拿到排在最前的空手打击"这类问题；
/// - 卡牌奖励（GetSelectedCardReward）默认维持已拍板的"领最左"策略；
///   开启 skadaAssist 且查到有效社区统计时改用统计信号取牌，无数据一律回退最左（可行性分析 §8.2）；
/// - 智能选牌优先级（可行性分析 §9.1/9.2）：带场景的实例（删除/变化/手牌消耗/复制/弃牌）在
///   smartPick 开启时按数据驱动优先级表选牌，否则维持既有 cardPickMode 策略。
///   弃牌场景（Discard）奇巧牌（IsSlyThisTurn，丢弃后自动打出）优先级最高。
/// </summary>
internal sealed class LocalWakuuStrategySelector : ICardSelector
{
    /// <summary>共享实例：作用域外的瓦库选牌兜底（Selector getter 返回）也用它。</summary>
    internal static readonly LocalWakuuStrategySelector Shared = new();

    /// <summary>
    /// 卡牌奖励归属者（仅奖励场景传入）：社区统计须按"瓦库玩家自己的角色"查表，
    /// 不能走前台角色（多控下语义漂移）。为 null 时不查表，直接最左。
    /// </summary>
    private readonly Player? _rewardOwner;

    /// <summary>选牌场景（智能选牌优先级）：Unknown = 维持既有策略。</summary>
    private readonly WakuuPickScenario _scenario;

    /// <summary>
    /// 选牌日志标记（可选）：非空时每次作答都打一条日志，便于实机核对"瓦库到底选了什么"。
    /// 默认不打——战斗自动出牌的选牌很频繁，不能刷屏。
    /// </summary>
    internal string? LogLabel { get; set; }

    /// <summary>
    /// 动态场景提供器（可选）：非空且返回非 null 时，优先于构造时传入的场景。
    /// 用于「同一个选择器实例要按实际入口决定优先级表」的场合（r94）：遗物获取期间既可能是
    /// "变化一张卡"（Transform 表）也可能是"删除一张卡"（Remove 表），压栈时还不知道会走哪个入口。
    /// 返回 <see cref="WakuuPickScenario.Unknown"/> 表示退回既有 cardPickMode 策略。
    /// </summary>
    internal Func<WakuuPickScenario?>? ScenarioProvider { get; set; }

    /// <summary>
    /// 卡牌奖励里**优先选这个额外选项**（option id，如佩尔之翼的 <c>SACRIFICE</c>）。
    /// 非空且本次候选里确实有这个选项时，<see cref="GetSelectedCardReward"/> 直接返回它而不是取牌。
    /// 2026-11-19：抓牌档位决定"这张奖励不值得抓、但有献祭选项"时用。
    /// </summary>
    internal string? PreferAlternativeOptionId { get; set; }

    /// <summary>实际生效的选牌场景：动态提供器优先，否则用构造时传入的场景。</summary>
    private WakuuPickScenario ResolveScenario()
    {
        return ScenarioProvider?.Invoke() ?? _scenario;
    }

    public LocalWakuuStrategySelector()
    {
    }

    /// <summary>带奖励归属者的实例：供 LocalWakuuRewardAutoClaim 结算卡牌奖励时使用。</summary>
    public LocalWakuuStrategySelector(Player rewardOwner)
    {
        _rewardOwner = rewardOwner;
    }

    /// <summary>带选牌场景的实例：供事件自动作答入口（删除/变化等）使用。</summary>
    public LocalWakuuStrategySelector(WakuuPickScenario scenario)
    {
        _scenario = scenario;
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        WakuuPickScenario scenario = ResolveScenario();

        // 智能选牌优先级（§9.1/9.2）：开关开启且场景明确时套优先级表，否则维持既有策略
        List<CardModel> picked;
        if (LocalWakuuAutopilotConfig.SmartPick && scenario != WakuuPickScenario.Unknown)
        {
            picked = PickByScenario(list, maxSelect, scenario);
        }
        else
        {
            string mode = LocalWakuuAutopilotConfig.CardPickMode;

            // 排序/取牌逻辑已抽为泛型纯函数（WakuuStrategyPicking），此处只负责随机源同步与稀有度权重
            lock (_randomLock)
            {
                picked = WakuuStrategyPicking.PickByStrategy(
                    list, mode, maxSelect, _random,
                    mode == WakuuChoiceModes.Rare ? (Func<CardModel, int>)RarityRank : null);
            }
        }

        if (LogLabel != null)
        {
            LocalMultiControlLogger.Info(
                $"瓦库自动选牌作答: source={LogLabel}, scenario={scenario}, options={list.Count}, "
                + $"select={maxSelect}, mode={LocalWakuuAutopilotConfig.CardPickMode}, "
                + $"picked={string.Join(",", picked.Select((c) => c?.Id?.Entry ?? "?"))}");
        }

        return Task.FromResult((IEnumerable<CardModel>)picked);
    }

    /// <summary>
    /// 按场景优先级表选牌（§9.2）：候选归类 → 规则表排序 → 取前 maxSelect 张。
    /// 任一步异常回退既有策略——智能选牌不能因为自身失败而卡住瓦库。
    /// </summary>
    private List<CardModel> PickByScenario(List<CardModel> options, int maxSelect, WakuuPickScenario scenario)
    {
        try
        {
            // 大卡组档（2026-11-19 用户口径）：删牌**只删 诅咒/状态/打击/防御**，别的牌一概不动。
            // 做法 = 先把候选过滤成垃圾牌；过滤空了就回退全量（否则这条删牌流程（商店已付钱 /
            // 营地净化已选）会没有候选可答）。
            List<CardModel> candidates = options;
            if (scenario == WakuuPickScenario.Remove
                && WakuuCardPickPolicy.IsRemovalJunkOnly(LocalWakuuAutopilotConfig.CardPickTierValue))
            {
                candidates = options
                    .Where((c) => c != null && WakuuCardPickPolicy.IsRemovableInJunkOnly(
                        WakuuPriorityPicking.ClassifyCard(c.Id.Entry, (int)c.Type, c.IsSlyThisTurn)))
                    .ToList();
                if (candidates.Count == 0)
                {
                    LocalMultiControlLogger.Warn(
                        "大卡组档删牌：牌组里没有 诅咒/状态/打击/防御 可删，回退原规则表（避免这条删牌流程没有候选）");
                    candidates = options;
                }
            }

            List<WakuuCardKind> kinds = candidates
                .Select((c) => WakuuPriorityPicking.ClassifyCard(c.Id.Entry, (int)c.Type, c.IsSlyThisTurn))
                .ToList();

            // 删除 / 变化两个场景追加「过渡牌」次级分值：
            // - 删除（用户 2026-11-18）：一层抓取率高、二/三层低 ⇒ 按落差先删；
            // - 变化（用户 2026-11-20）：打击 → 防御 → **过渡牌** → 其余 ⇒ 没有打击/防御可变时才轮到过渡牌。
            // 只在同一优先级档内的「其余」之间比较，诅咒/状态/打击/防御/任务牌的硬规则不变。
            IReadOnlyList<double>? transitionScores =
                scenario == WakuuPickScenario.Remove || scenario == WakuuPickScenario.Transform
                    ? BuildTransitionScores(candidates, scenario)
                    : null;

            List<int> ranked = WakuuPriorityPicking.RankIndicesByScenario(scenario, kinds, transitionScores);
            List<CardModel> picked = ranked
                .Take(maxSelect)
                .Select((i) => candidates[i])
                .ToList();

            LocalMultiControlLogger.Info(
                $"瓦库智能选牌: scenario={scenario}, options={options.Count}"
                + (candidates.Count != options.Count ? $"（大卡组档过滤后 {candidates.Count}）" : string.Empty)
                + $", select={maxSelect}, picked={string.Join(",", picked.Select((c) => c.Id.Entry))}");
            return picked;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库智能选牌异常，回退既有策略: {exception.Message}");
            string mode = LocalWakuuAutopilotConfig.CardPickMode;
            lock (_randomLock)
            {
                return WakuuStrategyPicking.PickByStrategy(
                    options, mode, maxSelect, _random,
                    mode == WakuuChoiceModes.Rare ? (Func<CardModel, int>)RarityRank : null);
            }
        }
    }

    /// <summary>
    /// 删除 / 变化场景的「过渡牌」次级分值（用户 2026-11-18 / 2026-11-20 口径）：按个人记录器的
    /// **分幕抓取率**算「一层 − 二/三层」的落差。这些牌是撑过前期的过渡牌 ⇒
    /// 删除时先删、变化时先变（都只在同档「其余」之内比较）。
    ///
    /// 没有个人数据 / 样本不足（一层 &lt; 3 次、后两层合计 &lt; 2 次）⇒ 全 0，退化成原规则表。
    /// 切片优先用本次选牌的归属者（<see cref="CardSelectForegroundSwitchPatch.CurrentChoicePlayerId"/>），
    /// 拿不到就不按角色过滤 —— 宁可聚合也不要拿错角色的数据。
    /// </summary>
    private double[] BuildTransitionScores(List<CardModel> options, WakuuPickScenario scenario)
    {
        double[] scores = new double[options?.Count ?? 0];
        string sceneName = scenario == WakuuPickScenario.Transform ? "变化" : "删牌";
        try
        {
            if (options == null || options.Count == 0)
            {
                return scores;
            }

            PersonalStore store = LocalPersonalRecorder.Snapshot();
            if (store == null || store.cardOffers.Count == 0)
            {
                return scores;
            }

            Player? owner = ResolveChoiceOwner();
            bool? isMulti = owner?.RunState == null ? (bool?)null : owner.RunState.Players.Count > 1;
            string? character = null;
            if (owner?.Character != null)
            {
                character = owner.Character.Id.Entry.ToUpperInvariant();
            }

            StringBuilder? log = null;
            for (int i = 0; i < options.Count; i++)
            {
                string? id = options[i]?.Id.Entry;
                double score = WakuuTransitionCardTier.ScoreOf(store, id, isMulti, character);
                scores[i] = score;
                if (score > 0 && id != null)
                {
                    log ??= new StringBuilder();
                    if (log.Length > 0)
                    {
                        log.Append(", ");
                    }

                    log.Append(id).Append("(落差=").Append(score.ToString("P0")).Append(')');
                }
            }

            if (log != null)
            {
                LocalMultiControlLogger.Info(
                    $"瓦库{sceneName}过渡牌优先（一层抓取率高、二/三层低）: char={character ?? "全部"}, "
                    + $"isMulti={(isMulti?.ToString() ?? "全部")}, 命中=[{log}]");
            }
            else
            {
                // 未命中也要留一行（2026-11-19）：否则"没有过渡牌"和"这段代码没走到/没数据"在日志里长得一样。
                LocalMultiControlLogger.Info(
                    $"瓦库{sceneName}过渡牌优先: 本屏未命中（无过渡牌或样本不足）: options={options.Count}, "
                    + $"char={character ?? "全部"}, isMulti={(isMulti?.ToString() ?? "全部")}, "
                    + $"个人 offer 记录={store.cardOffers.Count}");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库{sceneName}过渡牌评分失败，回退规则表: {exception.Message}");
        }

        return scores;
    }

    /// <summary>本次选牌的归属者：奖励归属者优先，其次本次异步链登记的 chooser，最后 null。</summary>
    private Player? ResolveChoiceOwner()
    {
        if (_rewardOwner != null)
        {
            return _rewardOwner;
        }

        try
        {
            ulong? chooser = CardSelectForegroundSwitchPatch.CurrentChoicePlayerId.Value;
            if (!chooser.HasValue)
            {
                return null;
            }

            return RunManager.Instance?.DebugOnlyGetState()?.Players
                .FirstOrDefault((p) => p != null && p.NetId == chooser.Value);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 稀有度排序权重（用于 rare=稀有度最高）：Ancient &gt; Rare &gt; Uncommon &gt; Common &gt; Basic &gt; 其他。
    /// 附魔候选牌经 EnchantmentModel.CanEnchant 过滤（排除 Status/Curse/Quest），
    /// 这里的枚举权重只关心"值得优先选"的稀有度，其余一律垫底。
    /// </summary>
    private static int RarityRank(CardModel card)
    {
        return card?.Rarity switch
        {
            CardRarity.Ancient => 5,
            CardRarity.Rare => 4,
            CardRarity.Uncommon => 3,
            CardRarity.Common => 2,
            CardRarity.Basic => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// 卡牌奖励取牌：默认保持"最左"（拍板 #5：与瓦库行为一致，不受选牌策略影响）；
    /// 开启 skadaAssist 时先试社区统计信号，无有效数据回退最左。
    /// </summary>
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        if (PreferAlternativeOptionId != null && alternatives != null)
        {
            foreach (CardRewardAlternative alternative in alternatives)
            {
                if (alternative != null
                    && string.Equals(alternative.OptionId, PreferAlternativeOptionId, StringComparison.OrdinalIgnoreCase))
                {
                    return new CardRewardSelection { alternative = alternative };
                }
            }
        }

        return new CardRewardSelection
        {
            card = PickRewardCard(options),
        };
    }

    /// <summary>
    /// 卡牌奖励选牌（可行性分析 §8.2 的第②③级：社区统计 → 最左兜底）。
    /// 任一步无数据（未安装 SkadaHelper / 查表 miss / 样本量不足）都返回最左，与关闭辅助时完全一致。
    /// </summary>
    private CardModel? PickRewardCard(IReadOnlyList<CardCreationResult> options)
    {
        int count = options.Count;
        if (count == 0)
        {
            return null;
        }

        CardModel? leftmost = options[0].Card;
        if (_rewardOwner == null || count == 1)
        {
            return leftmost;
        }

        try
        {
            string characterId = _rewardOwner.Character.Id.Entry.ToUpperInvariant();

            // 三级决策链第①级：个人偏好记录器（personalAssist 开启时优先，多人局优先多人切片）
            if (LocalWakuuAutopilotConfig.PersonalAssist)
            {
                int personalBest = PickBestCardByPersonalStats(options, characterId);
                if (personalBest >= 0)
                {
                    WakuuCardSignal personalSignal = BuildPersonalSignals(options, characterId)[personalBest]!.Value;
                    LocalMultiControlLogger.Info(
                        $"瓦库卡牌奖励按个人统计选取: card={options[personalBest].Card!.Id.Entry}, "
                        + $"index={personalBest}/{count}, char={characterId}, "
                        + $"pickRate={personalSignal.PickRate:F3}, gain={personalSignal.WinRateGain:F3}, "
                        + $"offered={personalSignal.OfferCount}");
                    return options[personalBest].Card;
                }
            }

            // 第②级：社区统计（skadaAssist 开启时参考 SkadaHelper 大数据）
            if (!LocalWakuuAutopilotConfig.SkadaAssist)
            {
                return leftmost;
            }

            WakuuCardSignal?[] signals = new WakuuCardSignal?[count];
            for (int i = 0; i < count; i++)
            {
                CardModel? card = options[i].Card;
                if (card == null)
                {
                    continue;
                }

                signals[i] = WakuuSkadaAdapter.TryGetCardSignal(characterId, card.Id.Entry);
            }

            int bestIndex = WakuuSignalPicking.PickBestCardIndex(signals);
            if (bestIndex < 0 || options[bestIndex].Card == null)
            {
                // 区分两种不适用：适配器不可用（启动探测已打日志，此处不重复刷屏）与
                // 适配器可用但统计未采用（无数据 or 信号为负），后者值得记录以便核对命中率与决策质量。
                if (WakuuSkadaAdapter.IsReady())
                {
                    int withData = signals.Count((s) => s != null);
                    LocalMultiControlLogger.Info(
                        $"瓦库卡牌奖励社区统计未采用，回退最左: char={characterId}, 候选={count}, 查到数据={withData}"
                        + (withData > 0
                            ? $"（样本量门槛 {WakuuSignalPicking.DefaultMinOfferCount}，或最佳候选因果增益为负）"
                            : "（mod 卡或样本量不足）"));
                }

                return leftmost;
            }

            WakuuCardSignal best = signals[bestIndex]!.Value;
            LocalMultiControlLogger.Info(
                $"瓦库卡牌奖励按社区统计选取: card={options[bestIndex].Card!.Id.Entry}, index={bestIndex}/{count}, "
                + $"char={characterId}, pickRate={best.PickRate:F3}, gain={best.WinRateGain:F3}, offerCount={best.OfferCount}");
            return options[bestIndex].Card;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库卡牌奖励统计选取失败，回退最左: {exception.Message}");
            return leftmost;
        }
    }

    /// <summary>
    /// 个人统计选牌（三级决策链第①级）：逐候选查个人信号，样本足够且非负面者参与竞选；
    /// 全部无倾向/负面时返回 -1（回退社区统计/最左）。
    /// </summary>
    private int PickBestCardByPersonalStats(IReadOnlyList<CardCreationResult> options, string characterId)
    {
        WakuuCardSignal?[] signals = BuildPersonalSignals(options, characterId);
        int withData = signals.Count((s) => s != null);
        int bestIndex = WakuuSignalPicking.PickBestCardIndex(
            signals, minOfferCount: WakuuPersonalQuery.DefaultMinPersonalCount);
        if (bestIndex >= 0)
        {
            return bestIndex;
        }

        if (withData > 0)
        {
            LocalMultiControlLogger.Info(
                $"瓦库卡牌奖励个人统计未采用，回退社区/最左: char={characterId}, 候选={options.Count}, "
                + $"查到个人数据={withData}（样本低于 {WakuuPersonalQuery.DefaultMinPersonalCount} 或最佳候选负面）");
        }

        return -1;
    }

    /// <summary>逐候选查个人信号（个人记录器快照，非空才查询）。</summary>
    private WakuuCardSignal?[] BuildPersonalSignals(IReadOnlyList<CardCreationResult> options, string characterId)
    {
        WakuuCardSignal?[] signals = new WakuuCardSignal?[options.Count];
        PersonalStore store = LocalPersonalRecorder.Snapshot();
        if (store.cardOffers.Count == 0)
        {
            return signals;
        }

        bool isMulti = _rewardOwner?.RunState?.Players.Count > 1;
        for (int i = 0; i < options.Count; i++)
        {
            CardModel? card = options[i].Card;
            if (card == null)
            {
                continue;
            }

            signals[i] = WakuuPersonalQuery.TryGetCardDecisionSignal(
                store, card.Id.Entry, isMulti, characterId,
                tierPreference: LocalWakuuAutopilotConfig.PersonalTier);
        }

        return signals;
    }

    /// <summary>random 策略用的独立随机源：不动游戏 RunState RNG，避免污染局内随机序列。</summary>
    private static readonly System.Random _random = new();
    private static readonly object _randomLock = new();
}
