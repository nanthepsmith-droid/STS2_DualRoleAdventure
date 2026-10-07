#nullable enable

using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张占位牌之三：**【合体】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 设计（效果**留空**）：选一个瓦库队友 —— 它的手牌与能量全给你，本回合你抽/弃牌随机走双方的抽弃堆，抽 1 张牌。
/// <see cref="TargetType"/> 取 <see cref="TargetType.AnyAlly"/>（**选瓦库队友**）；打出当前只消耗 0 费。
/// 池子 / 卡图口径见 <see cref="LocalWakuuDaddyShieldCard"/>。
/// </summary>
internal sealed class LocalWakuuDaddyMergeCard : CardModel
{
    public LocalWakuuDaddyMergeCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyAlly)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版事件牌【Stack】的卡图（与另两张占位牌区分开）。</summary>
    public override string PortraitPath => ModelDb.Card<Stack>().PortraitPath;

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }
}
