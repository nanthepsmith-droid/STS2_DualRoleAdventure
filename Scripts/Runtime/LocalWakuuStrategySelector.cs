using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.TestSupport;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库托管专用的策略化选牌选择器（替代游戏自带的 VakuuCardSelector）：
/// - 效果选牌（GetSelectedCards，酒狐合成二选一、开局遗物二选一、各类"从手牌选 N 张"等）
///   按 cardPickMode 配置取牌：first=最前 / last=最后（默认）/ random=随机，
///   解决"合成永远拿到排在最前的空手打击"这类问题；
/// - 卡牌奖励（GetSelectedCardReward）维持已拍板的"领最左"策略不变。
/// </summary>
internal sealed class LocalWakuuStrategySelector : ICardSelector
{
    /// <summary>共享实例：作用域外的瓦库选牌兜底（Selector getter 返回）也用它。</summary>
    internal static readonly LocalWakuuStrategySelector Shared = new();

    public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
    {
        List<CardModel> list = options.ToList();
        string mode = LocalWakuuAutopilotConfig.CardPickMode;

        // 排序/取牌逻辑已抽为泛型纯函数（WakuuStrategyPicking），此处只负责随机源同步
        List<CardModel> picked;
        lock (_randomLock)
        {
            picked = WakuuStrategyPicking.PickByStrategy(list, mode, maxSelect, _random);
        }

        return Task.FromResult((IEnumerable<CardModel>)picked);
    }

    /// <summary>卡牌奖励保持"最左"（拍板 #5：与瓦库行为一致，不受选牌策略影响）。</summary>
    public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> options, IReadOnlyList<CardRewardAlternative> alternatives)
    {
        return new CardRewardSelection
        {
            card = options.FirstOrDefault()?.Card,
        };
    }

    /// <summary>random 策略用的独立随机源：不动游戏 RunState RNG，避免污染局内随机序列。</summary>
    private static readonly System.Random _random = new();
    private static readonly object _randomLock = new();
}
