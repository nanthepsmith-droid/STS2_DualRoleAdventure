using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;

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
        CardModel? card = ctx.Hand.FirstOrDefault((candidate) => candidate.CanPlay());
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
    /// 目标解析（原 LocalWakuuRelicRuntime.ResolveTarget 原样搬移）：
    /// AnyEnemy→第一个可打敌人；AnyAlly→存活真我队友中随机；AnyPlayer→自己；其余→null。
    /// </summary>
    private static Creature? ResolveTarget(CardModel card, ICombatState combatState, Player owner)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => combatState.HittableEnemies.FirstOrDefault(),
            TargetType.AnyAlly => owner.RunState.Rng.CombatTargets.NextItem(
                combatState.Allies.Where((creature) => creature != null && creature.IsAlive && creature.IsPlayer && creature != owner.Creature)),
            TargetType.AnyPlayer => owner.Creature,
            _ => null
        };
    }
}
