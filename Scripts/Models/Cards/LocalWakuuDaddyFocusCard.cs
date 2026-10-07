#nullable enable

using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张牌之二：**【你攻】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 效果（r228 实装）：选择一个敌人 ⇒ **本回合所有瓦库出牌时优先攻击它**，抽 1 张牌。
/// 只影响**瓦库席位**（托管大脑）：真人自己选目标，不受这里影响。
///
/// 落点 = <see cref="WakuuDaddyCombatState.SetFocus"/> 登记，两个瓦库大脑
/// （<c>HeuristicWakuuBrain</c> / <c>ScoredWakuuBrain</c>）在解析 <c>TargetType.AnyEnemy</c> 时先问它。
/// 为什么不做成原版机制：r228 把 <c>sts2src</c> 翻了一遍 —— 敌方/玩家侧的"选谁当攻击目标"**没有任何钩子**
/// （唯一相关的是 `DieForYouPower` 那种伤害落地后重定向，与"选目标"无关），
/// 而瓦库的目标选择本来就在我们自己手里 ⇒ 直接在决策侧优先即可（纯逻辑可单测）。
///
/// <see cref="TargetType"/> 取 <see cref="TargetType.AnyEnemy"/>（**选敌人**）。
/// 池子 / 卡图口径见 <see cref="LocalWakuuDaddyShieldCard"/>。
/// </summary>
internal sealed class LocalWakuuDaddyFocusCard : CardModel
{
    public LocalWakuuDaddyFocusCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版铁甲战士【打击】的卡图（与另两张牌区分开）。</summary>
    public override string PortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    /// <summary>选一个敌人 ⇒ 登记为本回合瓦库集火目标；随后抽 1 张牌。</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target != null)
        {
            WakuuDaddyCombatState.SetFocus(target);
        }

        await CardPileCmd.Draw(choiceContext, 1m, Owner);
    }
}
