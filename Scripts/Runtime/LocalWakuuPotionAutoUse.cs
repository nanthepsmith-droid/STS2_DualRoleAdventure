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
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库战斗中自动用药水（Phase 2.5 保守版，独立开关 autoUsePotions，默认关）。
/// 规则（2026-08-25 拍板，同日追加混沌/污浊两条）：
/// - 果汁 FruitJuice（+5 最大生命）：到手立刻喝（见 PotionProcuredAutoDrinkPatch）；战斗中若仍在手也随时补喝；
/// - 治疗类（血液/再生）：自身血量 &lt;50% 时对自用；
/// - 增益类（力量/敏捷/专注/能量/格挡等）：精英或 Boss 战第 1 回合对自用；
/// - 攻击/减益类（火焰/毒素/虚弱/缠绕/毁灭等）：精英或 Boss 战第 1 回合对第一个敌人；
/// - 卡牌授予类（攻击/技能/能力/无色药水，用户点名追加）：精英或 Boss 战第 1 回合使用，
///   内部的 FromChooseACardScreen 选牌由托管选择器自动作答；
/// - 混沌药水 DistilledChaos：仅当栏内只剩它时自动喝一瓶（打抽牌堆顶 3 张，纯价值不压栏）；
/// - 污浊药水 FoulPotion 绝不在战斗中使用（全场伤害含自己），只在商人处自动投掷
///   （见 LocalWakuuMerchantFoulThrow）；
/// - mod 药水（非游戏命名空间）：普通战斗随机回合消耗（效果未知，与其过期不如随机用掉）；
/// - 其余原版药水保守跳过（宁可不用也不误用），后续按需扩充分类表。
///
/// 战斗中获得的药水（炼药 Alchemize、混沌结算产物等）：出牌循环结束后会补做一次评估，
/// 下一回合开始也会重新评估（每次调用都实时读药水栏），不会遗漏。
///
/// 实现要点：
/// - 直接 await potion.OnUseWrapper(choiceContext, target)——与瓦库自动出牌同一条顺序链，
///   不经 ActionQueueSynchronizer 入队，避免与出牌循环并发交错；
/// - 药水内部若弹选牌，压入 LocalWakuuStrategySelector 自动作答（坑 4 守卫对瓦库归属放行）；
/// - 目标解析后必须 IsValidTarget 校验；每瓶药独立 try/catch，失败不影响后续药水与出牌；
/// - 无需回合级去重：OnUseWrapper 开头即从药水栏移除（RemoveBeforeUse），消费本身就是去重。
/// </summary>
internal static class LocalWakuuPotionAutoUse
{
    /// <summary>mod 药水随机消耗的候选回合范围 [1, ModPotionRoundMax]。</summary>
    private const int ModPotionRoundMax = 3;

    private enum WakuuPotionCategory
    {
        /// <summary>保守策略：不自动使用。</summary>
        None,

        /// <summary>+最大生命类：任何时机都喝。</summary>
        ImmediateMaxHp,

        /// <summary>混沌药水：仅当栏内只剩它时自动喝（用户 2026-08-25 追加）。</summary>
        DistilledChaosOnly,

        /// <summary>治疗类：低血时对自用。</summary>
        HealLowHp,

        /// <summary>增益类：精英/Boss 战首回合对自用。</summary>
        BuffHardFight,

        /// <summary>攻击/减益类：精英/Boss 战首回合对第一个敌人。</summary>
        DamageDebuffHardFight,

        /// <summary>卡牌授予类：精英/Boss 战首回合使用（选牌由选择器作答）。</summary>
        CardGrantHardFight,

        /// <summary>mod 药水：普通战斗随机回合消耗。</summary>
        ModRandom,
    }

    private static readonly Random _random = new();
    private static readonly object _randomLock = new();

    /// <summary>
    /// mod 药水的随机回合计划：药水实例 → (战斗状态引用, 预定回合)。
    /// 按引用比较；换战斗后首次评估会重掷。容量有界（每局持有的 mod 药水数）。
    /// </summary>
    private static readonly Dictionary<object, (object Combat, int Round)> _modPotionPlan = new();

    /// <summary>
    /// 战斗内入口：由 ExecuteBeforePlayPhaseStartAsync 在出牌循环前调用
    /// （遗物钩子每回合触发一次 + 看门狗可能补触发，消费即去重，重复进入为无害空转）。
    /// </summary>
    public static async Task UseEligiblePotionsInCombatAsync(
        RelicModel relic, Player player, PlayerChoiceContext choiceContext, ICombatState combatState)
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

        bool hardFight = IsHardFight();
        int round = combatState.RoundNumber;
        List<PotionModel> potions = player.Potions.ToList();
        if (potions.Count == 0)
        {
            return;
        }

        // "只剩混沌药水"判定：栏内所有药水都是 DistilledChaos 时自动喝一瓶
        // （混沌=自动打抽牌堆顶 3 张，纯价值药水，压在栏里不如早用；用完生成的局面由后续回合规则接管）
        bool onlyDistilledChaos = potions.All((p) => p is DistilledChaos);

        foreach (PotionModel potion in potions)
        {
            // 污浊药水绝不自动使用：战斗中使用会伤害全场（含自己与队友），只允许在商人处投掷
            // （见 LocalWakuuMerchantFoulThrow）
            if (potion is FoulPotion)
            {
                continue;
            }

            WakuuPotionCategory category = Classify(potion);
            if (!ShouldUseNow(potion, category, combatState, player, hardFight, round, onlyDistilledChaos, out string reason))
            {
                continue;
            }

            Creature? target = ResolveTarget(potion, combatState, player);
            if (!potion.IsValidTarget(target))
            {
                LocalMultiControlLogger.Warn(
                    $"瓦库自动用药跳过（目标非法）: player={player.NetId}, potion={potion.Id.Entry}, targetType={potion.TargetType}");
                continue;
            }

            try
            {
                LocalMultiControlLogger.Info(
                    $"瓦库自动用药: player={player.NetId}, round={round}, potion={potion.Id.Entry}, "
                    + $"category={category}, target={target?.LogName ?? "无"}, reason={reason}");
                using (CardSelectCmd.PushSelector(new LocalWakuuStrategySelector()))
                {
                    await potion.OnUseWrapper(choiceContext, target);
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

    /// <summary>当前是否精英/Boss 战。</summary>
    private static bool IsHardFight()
    {
        try
        {
            RoomType roomType = RunManager.Instance.DebugOnlyGetState()?.CurrentRoom?.RoomType ?? RoomType.Unassigned;
            return roomType == RoomType.Elite || roomType == RoomType.Boss;
        }
        catch
        {
            return false; // 房间不可判时按普通战斗处理（保守：少用攻击性资源）
        }
    }

    /// <summary>
    /// 分类表：按 C# 类型匹配（编译期可查，游戏更新改名会在构建期暴露，优于字符串 id）。
    /// 命中即返回对应类别；未列出的原版药水返回 None（不自动用）。
    /// </summary>
    private static WakuuPotionCategory Classify(PotionModel potion)
    {
        string? ns = potion.GetType().Namespace;
        if (ns == null || !ns.StartsWith("MegaCrit.Sts2.Core.Models.Potions", StringComparison.Ordinal))
        {
            return WakuuPotionCategory.ModRandom;
        }

        if (potion is FruitJuice)
        {
            return WakuuPotionCategory.ImmediateMaxHp;
        }

        if (potion is DistilledChaos)
        {
            return WakuuPotionCategory.DistilledChaosOnly;
        }

        if (potion is BloodPotion or RegenPotion)
        {
            return WakuuPotionCategory.HealLowHp;
        }

        if (potion is AttackPotion or SkillPotion or PowerPotion or ColorlessPotion)
        {
            return WakuuPotionCategory.CardGrantHardFight;
        }

        if (potion is FirePotion or PoisonPotion or VulnerablePotion or WeakPotion or ShacklingPotion
            or PotionOfBinding or PotionOfDoom or PowderedDemise or BeetleJuice or ExplosiveAmpoule)
        {
            return WakuuPotionCategory.DamageDebuffHardFight;
        }

        if (potion is StrengthPotion or DexterityPotion or FocusPotion or EnergyPotion or SpeedPotion
            or FlexPotion or GigantificationPotion or CunningPotion or SwiftPotion or BlockPotion
            or PotionOfCapacity or BlessingOfTheForge or HeartOfIron or Fortifier or Clarity
            or StableSerum or StarPotion)
        {
            return WakuuPotionCategory.BuffHardFight;
        }

        return WakuuPotionCategory.None;
    }

    private static bool ShouldUseNow(
        PotionModel potion,
        WakuuPotionCategory category,
        ICombatState combatState,
        Player player,
        bool hardFight,
        int round,
        bool onlyDistilledChaos,
        out string reason)
    {
        switch (category)
        {
            case WakuuPotionCategory.ImmediateMaxHp:
                reason = "果汁随时喝";
                return true;

            case WakuuPotionCategory.DistilledChaosOnly:
                reason = onlyDistilledChaos ? "栏内只剩混沌药水" : "栏内有其他药水";
                return onlyDistilledChaos;

            case WakuuPotionCategory.HealLowHp:
                decimal maxHp = player.Creature?.MaxHp ?? 1m;
                decimal currentHp = player.Creature?.CurrentHp ?? 0m;
                bool lowHp = currentHp * 2m < maxHp;
                reason = lowHp ? "血量<50%" : "血量健康";
                return lowHp;

            case WakuuPotionCategory.BuffHardFight:
            case WakuuPotionCategory.DamageDebuffHardFight:
            case WakuuPotionCategory.CardGrantHardFight:
                reason = hardFight ? (round <= 1 ? "硬仗首回合" : "非首回合") : "非硬仗";
                return hardFight && round <= 1;

            case WakuuPotionCategory.ModRandom:
                int plannedRound = RollModPotionRound(potion, combatState);
                reason = $"mod药水预定回合{plannedRound}";
                return !hardFight && round == plannedRound;

            default:
                reason = "未分类保守跳过";
                return false;
        }
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

    /// <summary>
    /// 目标解析：AnyEnemy→第一个可打敌人；AllEnemies→null（全体 splash）；
    /// 其余（AnyPlayer/Self 等）→自身。AnyAlly 类药水解析为自身后会被 IsValidTarget 拦下（保守不自选队友）。
    /// </summary>
    private static Creature? ResolveTarget(PotionModel potion, ICombatState combatState, Player owner)
    {
        if (potion.TargetType == TargetType.AnyEnemy)
        {
            return combatState.HittableEnemies.FirstOrDefault();
        }

        if (potion.TargetType == TargetType.AllEnemies)
        {
            return null;
        }

        return owner.Creature;
    }
}
