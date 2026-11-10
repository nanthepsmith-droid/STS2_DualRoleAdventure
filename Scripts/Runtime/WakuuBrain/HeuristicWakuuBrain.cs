using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 启发式大脑（默认实现）：把 LocalWakuuRelicRuntime 出牌循环的决策逻辑原样搬入，
/// 行为零变化——取第一张可打牌（最左）+ ResolveTarget。
/// 其余派生能力（计划路径/选牌作答）当前不提供，交既有逻辑。
/// </summary>
internal sealed class HeuristicWakuuBrain : IWakuuCombatBrain
{
    public string Id => "heuristic";

    public bool IsAvailable => true;

    public bool TryDecideNext(in WakuuDecisionContext ctx, out WakuuPlannedAction action)
    {
        // 原逻辑：PileType.Hand.GetPile(owner).Cards.FirstOrDefault(candidate => candidate.CanPlay())
        // 2026-11-10：敌方全部【无实体】（伤害被压成 1/段）时，攻击牌几乎只值 1 点
        // ⇒ 先找非攻击牌（格挡/铺能力/抽牌），没有才回到"最左可打牌"。除此之外行为零变化。
        CardModel? card = AllHittableEnemiesIntangible(ctx.Combat)
            ? ctx.Hand.FirstOrDefault((candidate) => candidate.CanPlay() && candidate.Type != CardType.Attack)
              ?? ctx.Hand.FirstOrDefault((candidate) => candidate.CanPlay())
            : ctx.Hand.FirstOrDefault((candidate) => candidate.CanPlay());
        if (card != null)
        {
            Creature? target = ResolveTarget(card, ctx.Combat, ctx.Wakuu);
            action = new WakuuPlannedAction(
                WakuuActionKind.PlayCard, card, target, null, 0,
                $"heuristic-first-playable:{card.Id}", confident: true);
            return true;
        }

        action = new WakuuPlannedAction(
            WakuuActionKind.EndTurn, null, null, null, 0,
            "heuristic-no-playable-card", confident: true);
        return true;
    }

    public bool TryPlanTurn(in WakuuDecisionContext ctx, out IReadOnlyList<WakuuPlannedAction> plan, out string planFingerprint)
    {
        // 快路径模式：不提供整回合计划（将来异步求解器走这里）。
        plan = System.Array.Empty<WakuuPlannedAction>();
        planFingerprint = string.Empty;
        return false;
    }

    public bool TryAnswerCardChoice(in WakuuDecisionContext ctx, IReadOnlyList<CardModel> options, int minSelect, int maxSelect, out IReadOnlyList<CardModel> chosen)
    {
        // 交既有选择器策略（LocalWakuuStrategySelector 等），本大脑不参与选牌。
        chosen = System.Array.Empty<CardModel>();
        return false;
    }

    public void OnCombatBegin(Player wakuu)
    {
    }

    public void OnTurnBegin(in WakuuDecisionContext ctx)
    {
    }

    public void OnCombatEnd()
    {
    }

    /// <summary>
    /// 所有可打击敌人是否都处于【无实体】（`IntangiblePower`，受到的伤害压成 1）。
    /// 空场返回 false（空场不靠这条判断，敌方目标牌另有"没有敌人"口径）。
    /// </summary>
    private static bool AllHittableEnemiesIntangible(ICombatState combatState)
    {
        try
        {
            bool any = false;
            foreach (Creature? enemy in combatState.HittableEnemies)
            {
                if (enemy == null || !enemy.IsAlive)
                {
                    continue;
                }

                any = true;
                if (enemy.GetPowerAmount<IntangiblePower>() <= 0)
                {
                    return false;
                }
            }

            return any;
        }
        catch
        {
            return false; // 读不到就当"没有无实体"，保持既有行为
        }
    }

    /// <summary>
    /// 目标解析（原 LocalWakuuRelicRuntime.ResolveTarget 原样搬移）：
    /// AnyEnemy→**本回合被【你攻】点名的敌人**（还活着且还能打时）→ 否则第一个可打敌人；
    /// AnyAlly→存活真我队友中随机；AnyPlayer→自己；其余→null。
    /// </summary>
    private static Creature? ResolveTarget(CardModel card, ICombatState combatState, Player owner)
    {
        return card.TargetType switch
        {
            // consumer=owner：临时席位（战神）命中集火时由登记处打一条 R13 自证锚点（见 TryGetFocus）。
            TargetType.AnyEnemy => WakuuDaddyCombatState.TryGetFocus(combatState.HittableEnemies, owner)
                ?? combatState.HittableEnemies.FirstOrDefault(),
            TargetType.AnyAlly => owner.RunState.Rng.CombatTargets.NextItem(
                combatState.Allies.Where((creature) => creature != null && creature.IsAlive && creature.IsPlayer && creature != owner.Creature)),
            TargetType.AnyPlayer => owner.Creature,
            _ => null
        };
    }
}
