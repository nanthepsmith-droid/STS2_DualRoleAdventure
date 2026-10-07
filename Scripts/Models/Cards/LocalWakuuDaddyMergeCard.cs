#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张牌之三：**【合体】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 效果（r228 实装）：选择一个**瓦库队友** ⇒
/// <list type="number">
/// <item>它的手牌（战斗牌，都是卡组克隆）**改归属**后进你的手牌；</item>
/// <item>它的能量全给你（<c>PlayerCmd.LoseEnergy</c> + <c>GainEnergy</c>，会走原版的能量增益钩子）；</item>
/// <item>**本回合**你抽牌时随机从「自己 / 它」的抽牌堆取、弃牌时随机进其中一方的弃牌堆
///   （登记在 <see cref="WakuuDaddyCombatState"/>，搬运见 <see cref="Patch.WakuuDaddyMergePilePatch"/>）；</item>
/// <item>随后抽 1 张牌（这一抽本身就吃上面的混抽规则）。</item>
/// </list>
///
/// 目标必须是瓦库托管席位（判定 <see cref="WakuuDaddyCardPolicy.CanMergeTarget"/>）：
/// <see cref="TargetType.AnyAlly"/> 没法把"队友"筛成"瓦库" ⇒ 点在真人队友上时**不发动效果**（只抽 1 张）。
///
/// 池子 / 卡图口径见 <see cref="LocalWakuuDaddyShieldCard"/>。
/// </summary>
internal sealed class LocalWakuuDaddyMergeCard : CardModel
{
    public LocalWakuuDaddyMergeCard()
        : base(0, CardType.Skill, CardRarity.Event, TargetType.AnyAlly)
    {
    }

    /// <summary>没有 PCK ⇒ 借原版事件牌【Stack】的卡图（与另两张牌区分开）。</summary>
    public override string PortraitPath => ModelDb.Card<Stack>().PortraitPath;

    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    /// <summary>选一个瓦库队友 ⇒ 手牌与能量归你 + 本回合混抽混弃；随后抽 1 张牌。</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Player? wakuu = cardPlay.Target?.Player;
        if (wakuu != null)
        {
            bool isWakuuSeat = LocalSelfCoopContext.IsWakuuEnabled(wakuu.NetId);
            if (WakuuDaddyCardPolicy.CanMergeTarget(isWakuuSeat, ReferenceEquals(wakuu, Owner)))
            {
                await MergeAsync(wakuu);
            }
            else
            {
                LocalMultiControlLogger.Info(
                    $"[瓦库的爹] 合体目标不是瓦库托管席位，效果未发动（只抽 1 张）: target={wakuu.NetId}, isWakuuSeat={isWakuuSeat}");
            }
        }

        await CardPileCmd.Draw(choiceContext, 1m, Owner);
    }

    /// <summary>手牌改归属转移 + 能量转移 + 登记本回合混抽混弃。</summary>
    private async Task MergeAsync(Player wakuu)
    {
        CardPile? hand = PileType.Hand.GetPile(wakuu);
        List<CardModel> cards = hand?.Cards.ToList() ?? new List<CardModel>();
        foreach (CardModel card in cards)
        {
            // 🔴 **顺序铁律**（r229 实机事故，务必照抄）：
            //   `CardModel.Pile` 不是自己的字段，而是**按 Owner 反查**出来的 ——
            //   `Pile => _owner?.Piles.FirstOrDefault(p => p.Cards.Contains(this))`。
            //   所以「先改归属、再 CardPileCmd.Add」会让 `card.Pile` 在改归属那一刻变成 **null**
            //   （牌还没进任何新牌堆），`Add` 于是以为"这牌不在任何牌堆里"、**跳过摘除** ⇒
            //   同一张牌**同时留在瓦库手里 + 进你手里**（一个实例挂在两个牌堆上）。
            //   后果（实机）：瓦库把这张"自己的牌"每回合重打 60 次（撞 `cap=60` 护栏上限）、
            //   卡牌节点暴涨（那局泄漏 6.1 万 CanvasItem）⇒ 严重卡顿。
            //   ✅ 正确顺序 = **先摘（此时 Owner 还是瓦库，Pile 正确指向瓦库手牌）→ 再改归属 → 再 Add**
            //   （= 原版 `CardPileCmd.GiveToAnotherPlayer` 内部的三步，只是它随后用"桌上已有节点"做动画、
            //   对瓦库手牌取不到节点所以什么都不画，见下）；`Add` 此时 `oldPile == null`，
            //   仍会建节点并挂进手牌（`GetTweenForCardsChangingPiles` 的 `pileType == Hand` 分支）。
            //   手牌已满时原版会把它落进接收方的弃牌堆（`CardPileCmd.Add` 的 isFullHandAdd 分支）。
            card.RemoveFromCurrentPile(silent: true);
            card.GiveToAnotherPlayer(Owner);
            await CardPileCmd.Add(card, PileType.Hand);
        }

        int energy = wakuu.PlayerCombatState?.Energy ?? 0;
        if (energy > 0)
        {
            await PlayerCmd.LoseEnergy(energy, wakuu);
            await PlayerCmd.GainEnergy(energy, Owner);
        }

        WakuuDaddyCombatState.RegisterMerge(Owner, wakuu);
        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 合体：收编手牌={cards.Count} 张、能量={energy}，施牌者 {Owner.NetId} ← 瓦库 {wakuu.NetId}");
    }
}
