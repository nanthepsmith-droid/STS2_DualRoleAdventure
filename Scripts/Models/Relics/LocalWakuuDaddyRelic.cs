#nullable enable

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using LocalMultiControl.Scripts.Models.Cards;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;

namespace LocalMultiControl.Scripts.Models.Relics;

/// <summary>
/// ④「**瓦库的爹**」—— 首版（遗物 + 三卡框架）的遗物本体。
///
/// 效果（提案 §5.1 的"效果①"）：**战斗开始时获得 3 张牌**（【我挡】/【你攻】/【合体】各 1 张，0 费技能）。
/// 三张牌的**效果已于 r228 实装**（我挡 = 转移承伤并减半 / 你攻 = 本回合瓦库集火 / 合体 = 收编手牌能量
/// 且本回合混抽混弃），见各卡自己的类注释。
///
/// 本遗物还兼**这些「本回合」登记的清理点**：它们是"本回合"语义，而"回合"= 玩家侧回合
/// （瓦库在同侧稍后行动 ⇒ 集火必须活到那一刻）⇒ 玩家侧回合结束清空；
/// 战斗结束也清一次（中途全灭时侧回合结束可能不再触发）。
///
/// 命名口径：<c>地狱战神</c> = 战灵召唤（另一件功能，排期队尾）；**本遗物 = 瓦库的爹**（用户拍板的占位名，
/// 正式名字用户另有安排）。发放规则见 <see cref="WakuuDaddyPolicy.ShouldGrantRelic"/>（**只发真人席位**）。
///
/// 给牌的 hook 选 <see cref="AfterSideTurnStart"/>（原版 <c>BigHat</c> 的同款写法）而不是
/// <c>BeforeHandDraw</c>：它发生在回合开始**抽牌之后**，塞进手牌的牌不会被随后的抽牌流程冲掉。
/// <c>TurnNumber == 1</c> 保证**每场战斗只发一次**（每个战斗的回合号从 1 重新数）。
/// </summary>
internal sealed class LocalWakuuDaddyRelic : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Event;

    // 一期借原版【低语耳环】图标（与瓦库托管遗物同一张图；正式美术待定，本项目没有 PCK）。
    protected override string IconBaseName => "whispering_earring";

    protected override IEnumerable<DynamicVar> CanonicalVars =>
        new DynamicVar[] { new CardsVar(WakuuDaddyPolicy.CardsPerCombat) };

    /// <summary>悬停时把要发的三张牌也显示出来（原版 BlessedAntler 的同款写法）。</summary>
    protected override IEnumerable<IHoverTip> ExtraHoverTips
    {
        get
        {
            List<IHoverTip> tips = new();
            tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<LocalWakuuDaddyShieldCard>());
            tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<LocalWakuuDaddyFocusCard>());
            tips.AddRange(HoverTipFactory.FromCardWithCardHoverTips<LocalWakuuDaddyMergeCard>());
            return tips;
        }
    }

    /// <summary>战斗开始（本回合抽牌之后）把 3 张牌塞进持牌人的手牌；每场战斗只发一次。</summary>
    public override async Task AfterSideTurnStart(
        CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (Owner.PlayerCombatState?.TurnNumber != 1 || !participants.Contains(Owner.Creature))
        {
            return;
        }

        Flash();
        List<CardModel> cards = new(WakuuDaddyPolicy.CardsPerCombat)
        {
            combatState.CreateCard<LocalWakuuDaddyShieldCard>(Owner),
            combatState.CreateCard<LocalWakuuDaddyFocusCard>(Owner),
            combatState.CreateCard<LocalWakuuDaddyMergeCard>(Owner),
        };

        CardCmd.PreviewCardPileAdd(await CardPileCmd.AddGeneratedCardsToCombat(cards, PileType.Hand, Owner));
        LocalMultiControlLogger.Info(
            $"[瓦库的爹] 战斗开始已发放三张牌: player={Owner.NetId}, count={cards.Count}, "
            + $"round={combatState.RoundNumber}");
    }

    /// <summary>玩家侧回合结束 ⇒ 清掉【你攻】的集火目标与【合体】的混抽/混弃链接（都是"本回合"语义）。</summary>
    public override Task AfterSideTurnEnd(
        PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == CombatSide.Player)
        {
            WakuuDaddyCombatState.ClearTurnScoped();
        }

        return Task.CompletedTask;
    }

    /// <summary>战斗结束兜底清一次（侧回合结束可能因"战斗中途全灭"不再触发）。</summary>
    public override Task AfterCombatEnd(CombatRoom room)
    {
        WakuuDaddyCombatState.ClearTurnScoped();
        return Task.CompletedTask;
    }
}
