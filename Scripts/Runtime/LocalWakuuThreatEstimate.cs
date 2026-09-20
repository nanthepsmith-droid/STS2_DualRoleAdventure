using System;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.Intents;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 敌方意图伤害粗估（可行性分析 §21.4.2 #10 记录的那套简化预测，全项目唯一一份实现）。
///
/// 原本内联在 <c>LocalWakuuPotionAutoUse.BuildContext</c> 里（供药水规则表的
/// `TotalIncomingDamage` / `AnyEnemyIntendsAttack` 使用），2026-09-20 抽成本文件给
/// `ScoredWakuuBrain` 的致死线判定复用 —— **逻辑逐字搬移、行为零变化**（同一套 try/catch 与告警）。
///
/// 精度边界（刻意）：只累加**攻击意图**对全体友方的伤害，不考虑多段随机目标、敌方增益/减益、
/// 后续回合的行动推进（那需要推进怪物 AI 状态机与 RNG，代价高）。对"是否需要防御"这种
/// 阈值型判断足够（§21.4.2 #10 的结论）。
/// </summary>
internal static class LocalWakuuThreatEstimate
{
    /// <summary>
    /// 返回（是否有敌人正在攻击意图, 本回合粗估来袭总伤害）。
    /// 解析失败时保守返回（false, 0）：宁可"不认为危险"，也不要因为估算异常去干扰出牌决策。
    /// </summary>
    public static (bool AnyAttack, int TotalIncoming) EstimateIncomingThreat(ICombatState combatState)
    {
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
            // 意图解析异常时保守视为"没有可估的伤害"，不阻塞任何调用方（用药 / 评分）
            LocalMultiControlLogger.Warn($"估算敌人意图伤害失败: {exception.Message}");
        }

        return (anyAttack, totalIncoming);
    }

    /// <summary>只关心总伤害时的便捷入口（评分大脑的致死线判定用）。</summary>
    public static int EstimateIncomingDamage(ICombatState combatState)
    {
        return EstimateIncomingThreat(combatState).TotalIncoming;
    }
}
