using System;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 启发式评分大脑（<c>vakuuBrain=scored</c>）—— 可行性分析 §18.2「Phase 5：轻量启发式评分 + 目标选择升级」
/// 的落地，§21.6 已把 Phase 5 明确收缩为「接口 + 轻量启发式评分 + 目标选择升级」（不引入外部求解器）。
///
/// 与 <see cref="HeuristicWakuuBrain"/> 的关系：那个是「最左可打牌」的原逻辑，**默认档、行为零变化**；
/// 本实现只在配置里显式把 `vakuuBrain` 切到 `scored` 时生效（设置页「战斗决策大脑」行，默认启发式）。
///
/// 评分与排序规则全在纯逻辑 <see cref="WakuuCardScoring"/> 里（可单测）；目标选择在
/// <see cref="WakuuTargetPicking"/> 里（可单测）。本文件只做「游戏模型 → 纯逻辑标量」的映射。
///
/// ⚠ 已知取舍（每条都有出处，不是临场发明）：
/// 1. **致死线是硬门槛**（§21.4.2 #4）：不格挡会被打死且手上有格挡牌时，只在格挡牌里选，
///    不让"高基础分的攻击牌"把救命牌挤掉（线性总分的已知缺陷由这条兜住）；
/// 2. **伤害/格挡是粗估**（§18.2.4 的精度取舍）：卡面基础值（含附魔）+ 力量/敏捷，不含易伤/虚弱等
///    Hook 修正；只求区分"该打攻击还是该格挡"的粗粒度决策；
/// 3. **阵容中途变化 / 局内生成卡的入场联动**（§21.4.2 #2/#3）不在本期范围 —— 目标选择每轮现读
///    `HittableEnemies`，能跟上"敌人变多/变少"，但不会为新增单位重算计划（本期本来也没有跨轮计划）；
/// 4. **异常一律降级**：任何估算/评分异常都退回"最左可打牌"，保证评分出问题永远不会变成瓦库不出牌。
/// </summary>
internal sealed class ScoredWakuuBrain : IWakuuCombatBrain
{
    public string Id => "scored";

    public bool IsAvailable => true;

    public bool TryDecideNext(in WakuuDecisionContext ctx, out WakuuPlannedAction action)
    {
        try
        {
            // 注意：这里的循环刻意不用 LINQ —— ctx 是 `in` 参数，lambda 捕获 in 参数会被编译期拒绝。
            List<CardModel> playable = new();
            foreach (CardModel card in ctx.Hand)
            {
                if (card.CanPlay())
                {
                    playable.Add(card);
                }
            }

            if (playable.Count == 0)
            {
                action = EndTurn();
                return true;
            }

            // M1 知识层：对本回合手牌做**只读**抽样（不改决策、不写游戏状态），
            // 只打 `[瓦库评价]` 诊断日志；刻意只在评分档接入，保证默认（启发式）档日志逐字不变。
            WakuuKnowledgeSampler.ObserveHand(ctx.Hand, ctx.Wakuu, "scored-brain");

            List<WakuuScoreInput> inputs = new(playable.Count);
            foreach (CardModel card in playable)
            {
                inputs.Add(Describe(card, ctx));
            }

            WakuuScoreSituation situation = DescribeSituation(ctx);
            int index = WakuuCardScoring.PickBestIndex(inputs, situation);
            if (index >= 0 && index < playable.Count)
            {
                CardModel card = playable[index];
                int score = WakuuCardScoring.Score(inputs[index], situation);
                action = new WakuuPlannedAction(
                    WakuuActionKind.PlayCard,
                    card,
                    ResolveTarget(card, ctx),
                    null,
                    0,
                    $"scored:score={score}:{card.Id}",
                    confident: true);
                return true;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库评分大脑异常，本次降级为最左可打牌: {exception.Message}");
        }

        // 降级路径：与 HeuristicWakuuBrain 完全一致（最左可打牌 / 无牌可出即结束回合）。
        // ⚠ r204：**降级路径自身也必须兜住异常** —— 2026-10-03 实机就是这里栽的：
        // 上一段 catch 兜住了评分异常、打出"降级为最左可打牌"，但下面 ResolveTarget → EstimateDamage
        // 又抛了同一个 InvalidCastException（第三方卡 Repeat 类型不符），异常冒穿本方法
        // ⇒ 选择器作用域/出牌作用域以异常退出 ⇒ 看门狗重启失败 ⇒ 遗物反复闪 + 当回合不出牌。
        try
        {
            CardModel? fallback = ctx.Hand.FirstOrDefault(static card => card.CanPlay());
            if (fallback != null)
            {
                Creature? target = null;
                try
                {
                    target = ResolveTarget(fallback, ctx);
                }
                catch (Exception targetException)
                {
                    // 选目标失败不能连累出牌：按"无目标"出（与启发式档同形）。
                    LocalMultiControlLogger.Warn(
                        $"瓦库降级选目标异常，按无目标出牌: card={fallback.Id}, error={targetException.Message}");
                }

                action = new WakuuPlannedAction(
                    WakuuActionKind.PlayCard,
                    fallback,
                    target,
                    null,
                    0,
                    $"scored-fallback-first-playable:{fallback.Id}",
                    confident: true);
                return true;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库降级路径异常，本次结束回合: {exception.Message}");
        }

        action = EndTurn();
        return true;
    }

    public bool TryPlanTurn(in WakuuDecisionContext ctx, out IReadOnlyList<WakuuPlannedAction> plan, out string planFingerprint)
    {
        // 快路径模式：不提供整回合计划（将来异步求解器走这里，见 21.3）。
        plan = Array.Empty<WakuuPlannedAction>();
        planFingerprint = string.Empty;
        return false;
    }

    public bool TryAnswerCardChoice(in WakuuDecisionContext ctx, IReadOnlyList<CardModel> options, int minSelect, int maxSelect, out IReadOnlyList<CardModel> chosen)
    {
        // 出牌派生的选牌仍交既有选择器策略（LocalWakuuStrategySelector 等），本大脑不参与。
        chosen = Array.Empty<CardModel>();
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

    private static WakuuPlannedAction EndTurn()
    {
        return new WakuuPlannedAction(WakuuActionKind.EndTurn, null, null, null, 0, "scored-no-playable-card", confident: true);
    }

    // ------------------------------------------------------------------
    // 游戏模型 → 纯逻辑标量
    // ------------------------------------------------------------------

    private static WakuuScoreSituation DescribeSituation(in WakuuDecisionContext ctx)
    {
        List<Creature> enemies = ctx.Combat.HittableEnemies.ToList();

        int weakestEffectiveHp = -1;
        foreach (Creature enemy in enemies)
        {
            int effective = Math.Max(0, enemy.CurrentHp - enemy.Block);
            if (weakestEffectiveHp < 0 || effective < weakestEffectiveHp)
            {
                weakestEffectiveHp = effective;
            }
        }

        Creature self = ctx.Wakuu.Creature;
        int hp = self.CurrentHp;
        int incoming = LocalWakuuThreatEstimate.EstimateIncomingDamage(ctx.Combat);

        // 致死线：预估来袭伤害足以打穿「当前血 + 格挡」（0 血即死，所以用 >=）。
        bool lethal = incoming > 0 && incoming >= hp + self.Block;

        return new WakuuScoreSituation(
            playerHp: hp,
            playerMaxHp: self.MaxHp,
            energy: ctx.Energy,
            turnNumber: ctx.TurnNumber,
            enemyCount: enemies.Count,
            weakestEnemyEffectiveHp: weakestEffectiveHp,
            lethalDanger: lethal);
    }

    private static WakuuScoreInput Describe(CardModel card, in WakuuDecisionContext ctx)
    {
        WakuuScoreKeywords keywords = WakuuScoreKeywords.None;
        IReadOnlySet<CardKeyword> applied = card.Keywords;
        if (applied.Contains(CardKeyword.Exhaust))
        {
            keywords |= WakuuScoreKeywords.Exhaust;
        }

        if (applied.Contains(CardKeyword.Ethereal))
        {
            keywords |= WakuuScoreKeywords.Ethereal;
        }

        if (applied.Contains(CardKeyword.Retain))
        {
            keywords |= WakuuScoreKeywords.Retain;
        }

        return new WakuuScoreInput(
            kind: MapKind(card.Type),
            cost: card.EnergyCost.GetAmountToSpend(),
            isXCost: card.EnergyCost.CostsX,
            gainsBlock: card.GainsBlock,
            keywords: keywords,
            targetKind: MapTargetKind(card.TargetType),
            estimatedDamage: EstimateDamage(card, ctx.Wakuu),
            estimatedBlock: EstimateBlock(card, ctx.Wakuu));
    }

    private static WakuuScoreCardKind MapKind(CardType type)
    {
        return type switch
        {
            CardType.Attack => WakuuScoreCardKind.Attack,
            CardType.Skill => WakuuScoreCardKind.Skill,
            CardType.Power => WakuuScoreCardKind.Power,
            _ => WakuuScoreCardKind.Other,
        };
    }

    private static WakuuScoreTargetKind MapTargetKind(TargetType type)
    {
        return type switch
        {
            TargetType.Self => WakuuScoreTargetKind.Self,
            TargetType.AnyEnemy => WakuuScoreTargetKind.AnyEnemy,
            TargetType.AllEnemies => WakuuScoreTargetKind.AllEnemies,
            TargetType.AnyPlayer => WakuuScoreTargetKind.AnyPlayer,
            TargetType.AnyAlly => WakuuScoreTargetKind.AnyAlly,
            TargetType.AllAllies => WakuuScoreTargetKind.AllAllies,
            TargetType.None => WakuuScoreTargetKind.None,
            _ => WakuuScoreTargetKind.Other,
        };
    }

    /// <summary>
    /// 对单个目标的粗估伤害 = (卡面伤害含附魔 + 力量) × 段数（§18.2.7 连击按总伤害估值）。
    /// 没有 Damage 变量（纯功能牌 / 计算型伤害）按 0 处理 —— 计算型伤害（CalculatedDamage）本期不解析。
    /// </summary>
    private static int EstimateDamage(CardModel card, Player owner)
    {
        // ⚠ 一律走 WakuuCardVarReader：`DynamicVars.Damage` / `.Repeat` 是硬转型访问器
        // （游戏内部 `(DamageVar)_vars["Damage"]` / `(RepeatVar)_vars["Repeat"]`），
        // 第三方卡把同名变量声明成别的类型就会抛 InvalidCastException ——
        // 实测猪猪 mod `PIG_MULTI_SHOT` 的 "Repeat"，且本方法还会被"降级路径"调用，
        // 抛出去就是整轮自动出牌被打断（遗物反复闪 + 当回合不出牌）。
        if (!WakuuCardVarReader.TryReadEnchantedInt(card, "Damage", out int perHit))
        {
            return 0;
        }

        int strength = owner.Creature.GetPowerAmount<StrengthPower>();
        int hits = WakuuVarMath.RepeatOrDefault(
            WakuuCardVarReader.TryReadInt(card, "Repeat", out int repeat),
            repeat);
        return WakuuVarMath.EstimateAttackDamage(perHit, strength, hits);
    }

    /// <summary>粗估格挡 = 卡面格挡（含附魔）+ 敏捷。</summary>
    private static int EstimateBlock(CardModel card, Player owner)
    {
        if (!WakuuCardVarReader.TryReadEnchantedInt(card, "Block", out int baseBlock))
        {
            return 0;
        }

        int dexterity = owner.Creature.GetPowerAmount<DexterityPower>();
        return WakuuVarMath.EstimateBlockGain(baseBlock, dexterity);
    }

    // ------------------------------------------------------------------
    // 目标选择（§18.2.6）
    // ------------------------------------------------------------------

    private static Creature? ResolveTarget(CardModel card, in WakuuDecisionContext ctx)
    {
        return card.TargetType switch
        {
            TargetType.AnyEnemy => ResolveBestEnemyTarget(card, ctx),
            TargetType.AnyAlly => ResolveBestAllyTarget(ctx),
            TargetType.AnyPlayer => ctx.Wakuu.Creature,
            _ => null,
        };
    }

    /// <summary>可击杀优先 → 有效血量最低（§18.2.6）；不再像旧实现那样盲取"第一个可打敌人"。</summary>
    private static Creature? ResolveBestEnemyTarget(CardModel card, in WakuuDecisionContext ctx)
    {
        List<Creature> enemies = ctx.Combat.HittableEnemies.ToList();
        if (enemies.Count == 0)
        {
            return null;
        }

        // ④ 瓦库的爹【你攻】：本回合被点名的集火目标优先 —— 本人手上那张牌打不打得动它不管，先集火
        // （目标已经死掉/不可打时 TryGetFocus 会返回 null，自然回落到评分选择）。
        Creature? focus = WakuuDaddyCombatState.TryGetFocus(enemies);
        if (focus != null)
        {
            return focus;
        }

        List<int> effective = new(enemies.Count);
        foreach (Creature enemy in enemies)
        {
            effective.Add(Math.Max(0, enemy.CurrentHp - enemy.Block));
        }

        int index = WakuuTargetPicking.PickEnemyIndex(effective, EstimateDamage(card, ctx.Wakuu));
        return index >= 0 && index < enemies.Count ? enemies[index] : enemies[0];
    }

    /// <summary>队友增益优先给真人（瓦库是 AI 托管角色，增益给它不如给真人）。</summary>
    private static Creature? ResolveBestAllyTarget(in WakuuDecisionContext ctx)
    {
        Creature? firstAlive = null;
        Creature? firstHuman = null;
        foreach (Creature? ally in ctx.Combat.Allies)
        {
            if (ally == null || !ally.IsAlive || !ally.IsPlayer || ally.Player == null || ally == ctx.Wakuu.Creature)
            {
                continue;
            }

            firstAlive ??= ally;

            // 真人判据与全仓库同一来源（LocalSelfCoopContext.IsWakuuEnabled），不再用"随机队友"。
            if (!LocalSelfCoopContext.IsWakuuEnabled(ally.Player.NetId))
            {
                firstHuman ??= ally;
            }
        }

        return firstHuman ?? firstAlive;
    }
}
