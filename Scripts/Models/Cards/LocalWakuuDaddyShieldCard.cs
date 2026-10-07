#nullable enable

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张占位牌之一：**【我挡】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 设计（效果**留空**，首版只做框架）：选择一个队友，它在本回合受到的伤害换成由你承受、但只有 1/2，抽 1 张牌。
/// 所以 <see cref="TargetType"/> 取 <see cref="TargetType.AnyAlly"/>（**选队友**），
/// <see cref="MegaCrit.Sts2.Core.Models.CardModel.OnPlay"/> 走基类空实现 —— 打出去只消耗 0 费、什么也不做。
///
/// 池子口径与卡图借用见 <c>LocalWakuuDaddyPolicy</c> / 三张卡共用的说明：
/// **必须进 <c>EventCardPool</c>（稀有度 <see cref="CardRarity.Event"/>）**，绝不放无色池；
/// 本项目无 PCK ⇒ 覆写 <see cref="PortraitPath"/> 借原版卡立绘。
/// </summary>
internal sealed class LocalWakuuDaddyShieldCard : CardModel
{
    public LocalWakuuDaddyShieldCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyAlly)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版铁甲战士【防御】的卡图（三张占位牌各借一张不同的原版图，免得手牌里认不出来）。</summary>
    public override string PortraitPath => ModelDb.Card<DefendIronclad>().PortraitPath;

    /// <summary>升级 = 加「保留」（提案 §5.1：三张牌升级都加保留）。</summary>
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
