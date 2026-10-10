#nullable enable

using System.Threading.Tasks;
using LocalMultiControl.Scripts.Models.Powers;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Runtime.PureLogic;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace LocalMultiControl.Scripts.Models.Cards;

/// <summary>
/// ④「瓦库的爹」三张牌之一：**【我挡】**（提案 §5.1 = 0 费技能，升级加「保留」）。
///
/// 效果（r228 实装）：选择一个队友 ⇒ 它本回合受到的**攻击**伤害改由你承受、且**只有一半**，抽 1 张牌。
/// 落点：把 <see cref="LocalWakuuDaddyShieldPower"/> 挂在**被挡的队友**身上
/// （重定向 + 减半两条都在那个状态里，见它的类注释），本回合（含接下来的敌方回合）有效。
///
/// <see cref="TargetType"/> 取 <see cref="TargetType.AnyAlly"/>（**选队友**）；
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

    /// <summary>没有 PCK ⇒ 借原版铁甲战士【防御】的卡图（三张牌各借一张不同的原版图，免得手牌里认不出来）。</summary>
    public override string PortraitPath => ModelDb.Card<DefendIronclad>().PortraitPath;

    /// <summary>升级 = 加「保留」（提案 §5.1：三张牌升级都加保留）。</summary>
    protected override void OnUpgrade()
    {
        AddKeyword(CardKeyword.Retain);
    }

    /// <summary>选一个队友 ⇒ 给它挂「伤害由我承受、只担一半」的状态；随后抽 1 张牌。</summary>
    protected override async Task OnPlay(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        Creature? target = cardPlay.Target;
        if (target != null && WakuuDaddyCardPolicy.CanShieldTarget(ReferenceEquals(target, Owner.Creature)))
        {
            await PowerCmd.Apply<LocalWakuuDaddyShieldPower>(
                choiceContext, target, 1m, Owner.Creature, this);

            // R13（战灵召唤 Phase 5）：这张牌的**目标没有席位限制**（`TargetType.AnyAlly` 的池子 =
            // `CombatState.PlayerCreatures`，战神在里面）⇒ 战神天然可选。日志把席位种类打出来，
            // 让实机复测能一眼确认"挡的是战神"。
            // ⚠ 边界（有意为之、非 bug）：若目标正是本人的战神，而本人身上又挂着 R10 的【战灵守卫】，
            //   两条重定向（本状态 → 打牌者 / 守卫 → 战神）在 `Hook.ModifyUnblockedDamageTarget` 里是
            //   **链式**结算、谁先跑取决于监听器顺序 ⇒ 最终落在谁身上按原版顺序定，不会死循环。
            bool targetIsWarGod = target.Player != null && LocalSelfCoopContext.IsTemporarySeat(target.Player.NetId);
            LocalMultiControlLogger.Info(
                $"[瓦库的爹] 我挡：{target.Player?.NetId.ToString() ?? "?"}{(targetIsWarGod ? "(战神)" : string.Empty)} "
                + $"本回合受击伤害转由 {Owner.NetId} 承受（一半）"
                + (targetIsWarGod ? "；⚠ 它与 R10【战灵守卫】同源，若该战神的主人也有守卫则按原版监听器顺序结算一条" : string.Empty));
        }

        await CardPileCmd.Draw(choiceContext, 1m, Owner);
    }
}
