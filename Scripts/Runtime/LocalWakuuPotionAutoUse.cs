using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库战斗中自动用药水：数据驱动的规则表版（2026-08-26，按用户逐药拍板的
/// 「原版药水一览表.md」实现；独立开关 autoUsePotions，默认关）。
///
/// 结构：
/// - 每种原版药水一条 PotionRule（评估时机/战斗范围/附加条件/目标策略/定向选牌）；
/// - 两个评估相位：StartOfTurn=回合开始出牌前、EndOfTurn=回合结束前（自动出牌结束后，
///   此时能量/手牌状态即"回合结束前"语义）；无牌可出的回合同样会跑 EndOfTurn 相位；
/// - 消费即去重（OnUseWrapper 开头移除药水），看门狗重复进入为无害空转；
/// - 污浊药水绝不自动使用（全场伤害含自己），只在商人处自动投掷（LocalWakuuMerchantFoulThrow）；
/// - 果汁另有"到手立刻喝"链路（PotionProcuredAutoDrinkPatch），战斗中由规则表随时补喝；
/// - mod 药水（非游戏命名空间）：普通战斗随机回合消耗（预掷 1~3 回合）；
/// - 未收录的原版药水保守跳过。
/// </summary>
internal static class LocalWakuuPotionAutoUse
{
    /// <summary>mod 药水随机消耗的候选回合范围 [1, ModPotionRoundMax]。</summary>
    private const int ModPotionRoundMax = 3;

    [Flags]
    internal enum WakuuPotionPhase
    {
        StartOfTurn = 1,
        EndOfTurn = 2,
        Both = StartOfTurn | EndOfTurn,
    }

    private enum FightScope
    {
        AnyCombat,
        HardFight,   // 精英或 Boss
        BossFight,   // 仅 Boss
    }

    private enum TargetKind
    {
        Default,          // AnyEnemy→第一个敌人；AllEnemies→null；其余→自己
        HumanFirst,       // 优先给存活的真人队友，没有则自用
        AllyCharacter,    // 自己就是该职业则自用，否则给该职业的存活队友，再没有则自用
    }

    /// <summary>单次评估的上下文快照（手牌/牌堆/意图伤害/能量等一次算好供条件与选择器复用）。</summary>
    private sealed class PotionRuleContext
    {
        public required Player Owner;
        public required ICombatState CombatState;
        public required IReadOnlyList<PotionModel> FullBar;
        public required IReadOnlyList<CardModel> Hand;
        public required IReadOnlyList<CardModel> DrawPile;
        public required IReadOnlyList<CardModel> DiscardPile;
        public required bool HardFight;
        public required bool BossFight;
        public required bool AnyEnemyIntendsAttack;
        public required int TotalIncomingDamage;

        public int Round => CombatState.RoundNumber;
        public decimal CurrentHp => Owner.Creature?.CurrentHp ?? 0m;
        public decimal MaxHp => Owner.Creature?.MaxHp ?? 1m;
        public decimal Block => Owner.Creature?.Block ?? 0m;
        public int Energy => Owner.PlayerCombatState?.Energy ?? 0;
        public bool LowHp => CurrentHp * 2m < MaxHp;
        public bool LethalThreat => TotalIncomingDamage >= CurrentHp + Block && TotalIncomingDamage > 0;
        public bool HasOpenSlots => Owner.HasOpenPotionSlots;
    }

    private sealed class PotionRule
    {
        public required string Name;
        public required Func<PotionModel, bool> Match;
        public WakuuPotionPhase Phases = WakuuPotionPhase.Both;
        public FightScope Scope = FightScope.AnyCombat;
        public bool FirstRoundOnly;
        public Func<PotionRuleContext, bool>? Condition;
        public TargetKind Target = TargetKind.Default;
        public Type? AllyCharacterClass;
        public Func<PotionRuleContext, IReadOnlyList<CardModel>, int, int, List<CardModel>>? CardPicker;
        public bool DiscardInsteadOfUse;
    }

    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

    /// <summary>mod 药水的随机回合计划：药水实例 → (战斗状态引用, 预定回合)。</summary>
    private static readonly Dictionary<object, (object Combat, int Round)> _modPotionPlan = new();

    // ------------------------------------------------------------------
    // 规则表（2026-08-26 用户逐药拍板，见 pain\原版药水一览表.md）
    // ------------------------------------------------------------------
    private static readonly List<PotionRule> Rules = new()
    {
        // —— 特殊处理 ——
        new() { Name = "废弃药水丢弃", Match = (p) => p is DeprecatedPotion, DiscardInsteadOfUse = true },
        new() { Name = "果汁随时喝", Match = (p) => p is FruitJuice },

        // —— 治疗类 ——
        new() { Name = "血液药水低血自用", Match = (p) => p is BloodPotion, Condition = (c) => c.LowHp },
        new() { Name = "再生药水低血自用", Match = (p) => p is RegenPotion, Condition = (c) => c.LowHp },
        new()
        {
            Name = "龙涎香Boss残血", Match = (p) => p is Ambergris, Scope = FightScope.BossFight,
            Phases = WakuuPotionPhase.EndOfTurn, Condition = (c) => c.LowHp,
        },
        new()
        {
            Name = "混沌药水填栏", Match = (p) => p is EntropicBrew,
            Condition = (c) => c.HasOpenSlots && c.FullBar.All((x) => x is EntropicBrew),
        },

        // —— 精英/Boss 首回合增益（自用）——
        new() { Name = "力量首回合", Match = (p) => p is StrengthPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "敏捷首回合", Match = (p) => p is DexterityPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "集中首回合", Match = (p) => p is FocusPotion, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Defect) },
        new() { Name = "异鱼之油首回合", Match = (p) => p is FyshOil, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "流动铜液首回合", Match = (p) => p is LiquidBronze, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "马萨雷斯赠礼首回合", Match = (p) => p is MazalethsGift, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "明耀酊剂首回合", Match = (p) => p is RadiantTincture, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "宇宙药剂首回合", Match = (p) => p is CosmicConcoction, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "精炼混沌首回合", Match = (p) => p is DistilledChaos, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "明晰提取物首回合", Match = (p) => p is Clarity, Scope = FightScope.HardFight, FirstRoundOnly = true },

        // —— 精英/Boss 首回合攻击/减益（对敌）——
        new() { Name = "火焰首回合对敌", Match = (p) => p is FirePotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "毒素首回合对敌", Match = (p) => p is PoisonPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "灾厄首回合对敌", Match = (p) => p is PotionOfDoom, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "易伤首回合对敌", Match = (p) => p is VulnerablePotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "虚弱首回合对敌", Match = (p) => p is WeakPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "消亡粉末首回合对敌", Match = (p) => p is PowderedDemise, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new()
        {
            Name = "爆炸安瓿多敌或首回合", Match = (p) => p is ExplosiveAmpoule, Scope = FightScope.HardFight,
            FirstRoundOnly = true, Condition = (c) => EnemyCount(c) >= 3,
        },

        // —— 意图触发的攻击/减益 ——
        new() { Name = "甲虫汁敌人攻击", Match = (p) => p is BeetleJuice, Scope = FightScope.HardFight, Condition = (c) => c.AnyEnemyIntendsAttack },
        new() { Name = "镣铐敌人攻击", Match = (p) => p is ShacklingPotion, Scope = FightScope.HardFight, Condition = (c) => c.AnyEnemyIntendsAttack },
        new()
        {
            Name = "铁心覆甲", Match = (p) => p is HeartOfIron, Scope = FightScope.HardFight,
            Condition = (c) => c.AnyEnemyIntendsAttack || (c.Owner.Creature?.HasPower<PlatingPower>() ?? false),
        },
        new()
        {
            Name = "速度药水有技能牌", Match = (p) => p is SpeedPotion, Scope = FightScope.HardFight,
            Condition = (c) => c.Hand.Any((card) => card.Type == CardType.Skill),
        },

        // —— 卡牌授予类（精英/Boss 首回合）——
        new() { Name = "攻击药水首回合", Match = (p) => p is AttackPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "技能药水首回合", Match = (p) => p is SkillPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "能力药水首回合", Match = (p) => p is PowerPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },
        new() { Name = "无色药水首回合", Match = (p) => p is ColorlessPotion, Scope = FightScope.HardFight, FirstRoundOnly = true },

        // —— 给真人玩家优先 ——
        new() { Name = "复制药水优先真人", Match = (p) => p is Duplicator, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.HumanFirst },
        new() { Name = "超巨化优先真人", Match = (p) => p is GigantificationPotion, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.HumanFirst },

        // —— 角色专属给队友 ——
        new() { Name = "扩容给药水机器人", Match = (p) => p is PotionOfCapacity, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Defect) },
        new() { Name = "黑暗精华给故障机器人", Match = (p) => p is EssenceOfDarkness, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Defect) },
        new() { Name = "星星给储君", Match = (p) => p is StarPotion, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Regent) },
        new() { Name = "王之勇气给储君", Match = (p) => p is KingsCourage, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Regent) },
        new() { Name = "骨头酿给亡灵契约师", Match = (p) => p is BoneBrew, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Necrobinder) },
        new() { Name = "尸鬼瓮给亡灵契约师", Match = (p) => p is PotOfGhouls, Scope = FightScope.HardFight, FirstRoundOnly = true, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Necrobinder) },
        new() { Name = "士兵炖汤给铁甲战士", Match = (p) => p is SoldiersStew, Scope = FightScope.BossFight, Target = TargetKind.AllyCharacter, AllyCharacterClass = typeof(Ironclad) },
        new() { Name = "药水石头首回合对敌", Match = (p) => p is PotionShapedRock, FirstRoundOnly = true },

        // —— 手牌构成条件类 ——
        new()
        {
            Name = "灰水消耗状态诅咒", Match = (p) => p is Ashwater, Scope = FightScope.HardFight,
            Condition = (c) => c.Hand.Count((card) => card.Type is CardType.Status or CardType.Curse) >= 2,
            CardPicker = (ctx, options, _, maxSelect) =>
                options.Where((card) => card.Type is CardType.Status or CardType.Curse).Take(maxSelect).ToList(),
        },
        new()
        {
            Name = "赌徒特酿换掉坏牌", Match = (p) => p is GamblersBrew, Scope = FightScope.HardFight,
            Condition = (c) => BadCardCount(c) >= 5,
            CardPicker = (_, options, _, maxSelect) =>
                options.Where(IsBadCard).Take(maxSelect).ToList(),
        },
        new()
        {
            Name = "瓶装潜能洗坏牌", Match = (p) => p is BottledPotential, Scope = FightScope.HardFight,
            Condition = (c) => BadCardCount(c) >= 5,
        },
        new()
        {
            Name = "发光水Boss洗坏牌", Match = (p) => p is GlowwaterPotion, Scope = FightScope.BossFight,
            FirstRoundOnly = true, Condition = (c) => BadCardCount(c) >= 5,
        },
        new()
        {
            Name = "熔炉祝福升级关键牌", Match = (p) => p is BlessingOfTheForge, Scope = FightScope.HardFight,
            Condition = (c) => c.Hand.Count >= 5
                && (c.Hand.Any((card) => card.Type == CardType.Power && card.IsUpgradable)
                    || c.Hand.Any((card) => card.Rarity is CardRarity.Rare or CardRarity.Ancient)),
        },
        new()
        {
            Name = "狡诈药水低手牌", Match = (p) => p is CunningPotion, Scope = FightScope.HardFight,
            Condition = (c) => c.Hand.Count <= 6,
        },
        new()
        {
            Name = "痊愈药水无牌可出", Match = (p) => p is CureAll, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.StartOfTurn,
            Condition = (c) => !c.Hand.Any((card) => card.CanPlay()),
        },
        new()
        {
            Name = "癫狂之触免费高费牌", Match = (p) => p is TouchOfInsanity, Scope = FightScope.HardFight,
            Condition = (c) => c.Hand.Any((card) => card.EnergyCost.GetAmountToSpend() >= 3),
            CardPicker = (_, options, _, _) => options
                .Where((card) => card.EnergyCost.GetAmountToSpend() >= 3)
                .OrderByDescending((card) => card.EnergyCost.GetAmountToSpend())
                .Take(1)
                .ToList(),
        },
        new()
        {
            Name = "预知之滴取能力稀有牌", Match = (p) => p is DropletOfPrecognition, Scope = FightScope.HardFight,
            Condition = (c) => c.DrawPile.Any((card) => card.Type == CardType.Power || card.Rarity == CardRarity.Rare),
            CardPicker = (_, options, _, _) => options
                .Where((card) => card.Type == CardType.Power || card.Rarity == CardRarity.Rare)
                .OrderBy((card) => card.EnergyCost.GetAmountToSpend())
                .Take(1)
                .ToList(),
        },
        new()
        {
            Name = "液态记忆取弃牌能力牌", Match = (p) => p is LiquidMemories,
            Condition = (c) => c.DiscardPile.Any((card) => card.Type == CardType.Power),
            CardPicker = (_, options, _, _) => options.Where((card) => card.Type == CardType.Power).Take(1).ToList(),
        },

        // —— 回合结束前防御/资源类 ——
        new()
        {
            Name = "格挡药水硬仗防御", Match = (p) => p is BlockPotion, Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.TotalIncomingDamage >= c.Block + 10 || c.LethalThreat,
        },
        new()
        {
            Name = "固化药水格挡不足", Match = (p) => p is Fortifier, Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.TotalIncomingDamage > 0 && c.Block * 3m < c.TotalIncomingDamage,
        },
        new()
        {
            Name = "罐装幽灵免伤", Match = (p) => p is GhostInAJar, Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.TotalIncomingDamage >= c.Block + 30 || c.LethalThreat,
        },
        new()
        {
            Name = "幸运补剂缓冲", Match = (p) => p is LuckyTonic, Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.TotalIncomingDamage >= c.Block + 30 || c.LethalThreat,
        },
        new()
        {
            Name = "瓶中船双回合格挡", Match = (p) => p is ShipInABottle, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.TotalIncomingDamage >= c.Block + 15 || c.LethalThreat,
        },
        new()
        {
            Name = "能量药水救高费牌", Match = (p) => p is EnergyPotion, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.Energy == 0 && c.Hand.Any(IsEnergyBlocked),
        },
        new()
        {
            Name = "迅捷药水剩能量抽牌", Match = (p) => p is SwiftPotion, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.Energy > 0 && (c.Hand.Count == 0 || !c.Hand.Any((card) => card.CanPlay())),
        },
        new()
        {
            Name = "异蛇之油剩能量抽牌", Match = (p) => p is SneckoOil, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.Energy > 0,
        },
        new()
        {
            Name = "稳定血清保留能力牌", Match = (p) => p is StableSerum, Scope = FightScope.HardFight,
            Phases = WakuuPotionPhase.EndOfTurn,
            Condition = (c) => c.Hand.Any((card) => card.Type == CardType.Power),
        },
    };

    /// <summary>
    /// 战斗内入口。phase 由调用方指定：遗物钩子先跑 StartOfTurn，出牌循环结束后跑 EndOfTurn；
    /// 无牌可出的回合也会跑 EndOfTurn（防御类药水的时机）。
    /// </summary>
    public static async Task UseEligiblePotionsInCombatAsync(
        RelicModel relic, Player player, PlayerChoiceContext choiceContext, ICombatState combatState, WakuuPotionPhase phase)
    {
        if (!LocalWakuuAutopilotConfig.AutoUsePotions || !LocalWakuuRelicRuntime.IsVakuuFormMode(player))
        {
            return;
        }

        // 有弹层打开时不喝药（真人在交互），本次跳过；下次触发自然重试
        if ((NOverlayStack.Instance?.ScreenCount ?? 0) > 0)
        {
            return;
        }

        List<PotionModel> potions = player.Potions.ToList();
        if (potions.Count == 0)
        {
            return;
        }

        PotionRuleContext ctx = BuildContext(player, combatState, potions);
        foreach (PotionModel potion in potions)
        {
            // 污浊药水绝不自动使用：战斗中使用会伤害全场（含自己与队友），只允许在商人处投掷
            if (potion is FoulPotion)
            {
                continue;
            }

            PotionRule? rule = Rules.FirstOrDefault((r) => SafeMatch(r.Match, potion));
            string reason;
            if (rule != null)
            {
                if (!rule.Phases.HasFlag(phase))
                {
                    continue;
                }

                if (!IsScopeAllowed(rule.Scope, ctx))
                {
                    continue;
                }

                if (rule.FirstRoundOnly && ctx.Round > 1)
                {
                    continue;
                }

                if (rule.Condition != null && !SafeCondition(rule.Condition, ctx))
                {
                    continue;
                }

                reason = rule.Name;
            }
            else if (IsModPotion(potion))
            {
                // mod 药水兜底规则：普通战斗随机回合消耗（效果未知，与其过期不如随机用掉）
                if (!ShouldUseModPotion(potion, ctx))
                {
                    continue;
                }

                reason = $"mod药水预定回合{RollModPotionRound(potion, combatState)}";
            }
            else
            {
                continue; // 未收录原版药水保守跳过
            }

            Creature? target = ResolveTarget(rule, potion, ctx);
            if (!potion.IsValidTarget(target))
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库自动用药跳过（目标非法）: player={player.NetId}, potion={potion.Id.Entry}, targetType={potion.TargetType}");
                continue;
            }

            try
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自动用药: player={player.NetId}, round={ctx.Round}, phase={phase}, "
                    + $"potion={potion.Id.Entry}, reason={reason}, target={target?.LogName ?? "无"}");

                if (rule?.DiscardInsteadOfUse == true)
                {
                    await PotionCmd.Discard(potion);
                }
                else
                {
                    using (PushSelectorFor(rule, ctx))
                    {
                        await potion.OnUseWrapper(choiceContext, target);
                    }
                }
            }
            catch (Exception exception)
            {
                // 单瓶失败不影响后续药水与出牌流程；若药水已被移除则不会重复喝
                LocalMultiControlLogger.Warn(
                    $"瓦库自动用药异常（跳过该瓶）: potion={potion.Id.Entry}, error={exception.Message}");
            }
        }
    }

    // ------------------------------------------------------------------
    // 上下文构建与判定辅助
    // ------------------------------------------------------------------
    private static PotionRuleContext BuildContext(Player player, ICombatState combatState, List<PotionModel> potions)
    {
        IReadOnlyList<CardModel> hand = PileType.Hand.GetPile(player).Cards;
        IReadOnlyList<CardModel> draw = PileType.Draw.GetPile(player).Cards;
        IReadOnlyList<CardModel> discard = PileType.Discard.GetPile(player).Cards;

        bool anyAttack = false;
        int totalIncoming = 0;
        try
        {
            foreach (Creature? enemy in combatState.GetCreaturesOnSide(CombatSide.Enemy))
            {
                if (enemy == null || !enemy.IsAlive || !enemy.IsHittable)
                {
                    continue;
                }

                MonsterModel? monster = enemy.Monster;
                if (monster == null || !monster.IntendsToAttack)
                {
                    continue;
                }

                anyAttack = true;
                foreach (AbstractIntent intent in monster.NextMove.Intents)
                {
                    if (intent is AttackIntent attackIntent)
                    {
                        totalIncoming += attackIntent.GetTotalDamage(combatState.Allies, enemy);
                    }
                }
            }
        }
        catch (Exception exception)
        {
            // 意图解析异常时保守视为"没有可估的伤害"，不阻塞用药流程
            LocalMultiControlLogger.Warn($"估算敌人意图伤害失败: {exception.Message}");
        }

        return new PotionRuleContext
        {
            Owner = player,
            CombatState = combatState,
            FullBar = potions,
            Hand = hand,
            DrawPile = draw,
            DiscardPile = discard,
            HardFight = IsHardFight(),
            BossFight = IsBossFight(),
            AnyEnemyIntendsAttack = anyAttack,
            TotalIncomingDamage = totalIncoming,
        };
    }

    private static bool IsHardFight()
    {
        RoomType roomType = GetRoomType();
        return roomType is RoomType.Elite or RoomType.Boss;
    }

    private static bool IsBossFight()
    {
        return GetRoomType() == RoomType.Boss;
    }

    private static RoomType GetRoomType()
    {
        try
        {
            return RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.RoomType ?? RoomType.Unassigned;
        }
        catch
        {
            return RoomType.Unassigned;
        }
    }

    private static bool SafeMatch(Func<PotionModel, bool> match, PotionModel potion)
    {
        try
        {
            return match(potion);
        }
        catch
        {
            return false;
        }
    }

    private static bool SafeCondition(Func<PotionRuleContext, bool> condition, PotionRuleContext ctx)
    {
        try
        {
            return condition(ctx);
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"瓦库用药条件判定异常（保守跳过）: {exception.Message}");
            return false;
        }
    }

    private static bool IsScopeAllowed(FightScope scope, PotionRuleContext ctx)
    {
        return scope switch
        {
            FightScope.HardFight => ctx.HardFight,
            FightScope.BossFight => ctx.BossFight,
            _ => true,
        };
    }

    private static int EnemyCount(PotionRuleContext ctx)
    {
        try
        {
            return ctx.CombatState.HittableEnemies.Count();
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>打击/防御/状态/诅咒（灰水、赌徒、瓶装潜能、发光水的"坏牌"口径）。</summary>
    private static bool IsBadCard(CardModel card)
    {
        return LocalWakuuRestAutoChoice.IsBasicStrikeOrDefend(card)
            || card.Type is CardType.Status or CardType.Curse;
    }

    private static int BadCardCount(PotionRuleContext ctx)
    {
        return ctx.Hand.Count(IsBadCard);
    }

    /// <summary>能量不足导致打不出（排除能量充足也打不出的牌）。</summary>
    private static bool IsEnergyBlocked(CardModel card)
    {
        try
        {
            return !card.CanPlay(out UnplayableReason reason, out _)
                && reason.HasFlag(UnplayableReason.EnergyCostTooHigh);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsModPotion(PotionModel potion)
    {
        string? ns = potion.GetType().Namespace;
        return ns == null || !ns.StartsWith("MegaCrit.Sts2.Core.Models.Potions", StringComparison.Ordinal);
    }

    // ------------------------------------------------------------------
    // 目标解析
    // ------------------------------------------------------------------
    private static Creature? ResolveTarget(PotionRule? rule, PotionModel potion, PotionRuleContext ctx)
    {
        if (rule == null)
        {
            return ResolveDefaultTarget(potion, ctx);
        }

        switch (rule.Target)
        {
            case TargetKind.HumanFirst:
                return ResolveHumanFirstTarget(ctx);

            case TargetKind.AllyCharacter:
                return ResolveAllyCharacterTarget(ctx, rule.AllyCharacterClass);

            default:
                return ResolveDefaultTarget(potion, ctx);
        }
    }

    private static Creature? ResolveDefaultTarget(PotionModel potion, PotionRuleContext ctx)
    {
        if (potion.TargetType == TargetType.AnyEnemy)
        {
            return ctx.CombatState.HittableEnemies.FirstOrDefault();
        }

        if (potion.TargetType == TargetType.AllEnemies)
        {
            return null; // 全体 splash，无需目标
        }

        return ctx.Owner.Creature;
    }

    private static Creature? ResolveHumanFirstTarget(PotionRuleContext ctx)
    {
        Player? teammate = GetOtherPlayers(ctx.Owner).FirstOrDefault((p) => p.Creature is { IsDead: false });
        return teammate?.Creature ?? ctx.Owner.Creature;
    }

    private static Creature? ResolveAllyCharacterTarget(PotionRuleContext ctx, Type? characterClass)
    {
        if (characterClass == null)
        {
            return ctx.Owner.Creature;
        }

        // 自己就是该职业 → 自用；否则找该职业存活队友；都没有 → 自用
        if (!characterClass.IsInstanceOfType(ctx.Owner.Character))
        {
            Player? mate = GetOtherPlayers(ctx.Owner).FirstOrDefault((p) =>
                characterClass.IsInstanceOfType(p.Character) && p.Creature is { IsDead: false });
            if (mate != null)
            {
                return mate.Creature;
            }
        }

        return ctx.Owner.Creature;
    }

    private static IEnumerable<Player> GetOtherPlayers(Player owner)
    {
        try
        {
            return RunManager.Instance.DebugOnlyGetState()?.Players
                .Where((p) => p != null && p.NetId != owner.NetId) ?? Enumerable.Empty<Player>();
        }
        catch
        {
            return Enumerable.Empty<Player>();
        }
    }

    // ------------------------------------------------------------------
    // 选择器 / mod 药水兜底
    // ------------------------------------------------------------------
    private static IDisposable PushSelectorFor(PotionRule? rule, PotionRuleContext ctx)
    {
        if (rule?.CardPicker != null)
        {
            Func<PotionRuleContext, IReadOnlyList<CardModel>, int, int, List<CardModel>> picker = rule.CardPicker;
            return CardSelectCmd.PushSelector(new LocalWakuuTargetedCardSelector(
                (options, minSelect, maxSelect) => picker(ctx, options, minSelect, maxSelect)));
        }

        return CardSelectCmd.PushSelector(new LocalWakuuStrategySelector());
    }

    private static bool ShouldUseModPotion(PotionModel potion, PotionRuleContext ctx)
    {
        // 普通战斗随机回合消耗；硬仗不动 mod 药水（效果未知，别在关键战赌）
        if (ctx.HardFight)
        {
            return false;
        }

        return ctx.Round == RollModPotionRound(potion, ctx.CombatState);
    }

    /// <summary>mod 药水的随机回合计划：同一战斗内稳定复用预掷结果，换战斗重掷。</summary>
    private static int RollModPotionRound(PotionModel potion, ICombatState combatState)
    {
        if (_modPotionPlan.TryGetValue(potion, out (object Combat, int Round) plan)
            && ReferenceEquals(plan.Combat, combatState))
        {
            return plan.Round;
        }

        int rolled;
        lock (_randomLock)
        {
            rolled = _random.Next(1, ModPotionRoundMax + 1);
        }

        PruneModPotionPlan(combatState);
        _modPotionPlan[potion] = (combatState, rolled);
        return rolled;
    }

    private static void PruneModPotionPlan(ICombatState combatState)
    {
        if (_modPotionPlan.Count <= 16)
        {
            return;
        }

        List<object> stale = _modPotionPlan
            .Where((kv) => !ReferenceEquals(kv.Value.Combat, combatState))
            .Select((kv) => kv.Key)
            .ToList();
        foreach (object key in stale)
        {
            _modPotionPlan.Remove(key);
        }
    }
}
