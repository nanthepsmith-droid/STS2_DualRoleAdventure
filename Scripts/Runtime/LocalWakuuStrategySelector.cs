using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管专用的策略化选牌选择器（替代游戏自带的 VakuuCardSelector）：
/// - 效果选牌（GetSelectedCards，酒狐合成二选一、开局遗物二选一、各类"从手牌选 N 张"、
///   事件中的附魔/升级/变化选牌等）按 cardPickMode 配置取牌：
///   first=最前 / last=最后（默认）/ random=随机 / rare=稀有度最高，
///   解决"合成永远拿到排在最前的空手打击"这类问题；
/// - 卡牌奖励（GetSelectedCardReward）默认维持已拍板的"领最左"策略；
///   开启 skadaAssist 且查到有效社区统计时改用统计信号取牌，无数据一律回退最左（可行性分析 §8.2）。
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

    public LocalWakuuStrategySelector()
    {
    }

    /// <summary>带奖励归属者的实例：供 LocalWakuuRewardAutoClaim 结算卡牌奖励时使用。</summary>
    public LocalWakuuStrategySelector(Player rewardOwner)
    {
        _rewardOwner = rewardOwner;
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        string mode = LocalWakuuAutopilotConfig.CardPickMode;

        // 排序/取牌逻辑已抽为泛型纯函数（WakuuStrategyPicking），此处只负责随机源同步与稀有度权重
        List<CardModel> picked;
        lock (_randomLock)
        {
            picked = WakuuStrategyPicking.PickByStrategy(
                list, mode, maxSelect, _random,
                mode == WakuuChoiceModes.Rare ? (Func<CardModel, int>)RarityRank : null);
        }

        return Task.FromResult((IEnumerable<CardModel>)picked);
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
        if (!LocalWakuuAutopilotConfig.SkadaAssist || _rewardOwner == null || count == 1)
        {
            return leftmost;
        }

        try
        {
            string characterId = _rewardOwner.Character.Id.Entry.ToUpperInvariant();
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
                // 区分两种无数据：适配器不可用（启动探测已打日志，此处不重复刷屏）与
                // 适配器可用但查表 miss（mod 卡 / 该角色无数据），后者值得记录以便核对命中率。
                if (WakuuSkadaAdapter.IsReady())
                {
                    LocalMultiControlLogger.Info(
                        $"瓦库卡牌奖励社区统计查无有效数据（mod 卡或样本量不足），回退最左: "
                        + $"char={characterId}, 候选={count}");
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
            LocalMultiControlLogger.Warn($"瓦库卡牌奖励社区统计选取失败，回退最左: {exception.Message}");
            return leftmost;
        }
    }

    /// <summary>random 策略用的独立随机源：不动游戏 RunState RNG，避免污染局内随机序列。</summary>
    private static readonly System.Random _random = new();
    private static readonly object _randomLock = new();
}
