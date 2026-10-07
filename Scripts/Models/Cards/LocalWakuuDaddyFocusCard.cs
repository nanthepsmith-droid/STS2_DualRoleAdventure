#nullable enable

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张占位牌之二：**【你攻】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 设计（效果**留空**）：选择一个敌人，让**所有瓦库本回合优先攻击它**，抽 1 张牌。
/// <see cref="TargetType"/> 取 <see cref="TargetType.AnyEnemy"/>（**选敌人**）；打出当前只消耗 0 费。
/// 池子 / 卡图口径见 <see cref="LocalWakuuDaddyShieldCard"/>。
/// </summary>
internal sealed class LocalWakuuDaddyFocusCard : CardModel
{
    public LocalWakuuDaddyFocusCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyEnemy)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版铁甲战士【打击】的卡图（与另两张占位牌区分开）。</summary>
    public override string PortraitPath => ModelDb.Card<StrikeIronclad>().PortraitPath;

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
