using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库定向作答选择器：按调用方给的挑选函数在候选牌中选牌，
/// 用于药水效果内部的定向选牌（灰水消耗状态/诅咒、赌徒特酿只丢打击/防御/状态/诅咒、
/// 癫狂之触选高费牌、预知之滴选能力/稀有牌、液态记忆选能力牌等）。
/// 挑选结果会钳制到 [minSelect, maxSelect]：不足时按"倒序补齐"，仍不足则全部交出。
/// 卡牌奖励分支维持最左兜底（本选择器不用于奖励场景）。
/// </summary>
internal sealed class LocalWakuuTargetedCardSelector : ICardSelector
{
    private readonly Func<IReadOnlyList<CardModel>, int, int, List<CardModel>> _picker;

    public LocalWakuuTargetedCardSelector(Func<IReadOnlyList<CardModel>, int, int, List<CardModel>> picker)
    {
        _picker = picker;
    }

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        List<CardModel> picked = _picker(list, minSelect, maxSelect);

        if (picked.Count > maxSelect)
        {
            picked = picked.Take(maxSelect).ToList();
        }

        if (picked.Count < minSelect)
        {
            foreach (CardModel candidate in list.AsEnumerable().Reverse())
            {
                if (picked.Count >= Math.Min(minSelect, list.Count))
                {
                    break;
                }

                if (!picked.Contains(candidate))
                {
                    picked.Add(candidate);
                }
            }

            if (picked.Count < minSelect)
            {
                picked = list.ToList();
            }
        }

        return Task.FromResult((IEnumerable<CardModel>)picked);
    }

    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        return new CardRewardSelection
        {
            card = options.FirstOrDefault()?.Card,
        };
    }
}
