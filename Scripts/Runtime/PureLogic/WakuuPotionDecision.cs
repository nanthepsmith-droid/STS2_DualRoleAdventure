namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 瓦库自动用药的规则判定纯函数。
/// 从 LocalWakuuPotionAutoUse 的判定序列原样搬移（行为零变化），输入全部为标量，
/// 便于单元测试覆盖"相位/范围/首回合/条件/昏眩"的判定组合。
/// </summary>
internal static class WakuuPotionDecision
{
    /// <summary>
    /// 判定一条药水规则在当前评估相位/战斗场景下是否应使用。
    /// 判定顺序与原运行时代码一致：
    /// 1) 相位不含当前相位 → 不用；2) 战斗范围不符 → 不用；3) 仅首回合但已过首回合 → 不用；
    /// 4) 带附加条件且条件不满足 → 不用；5) 昏眩时跳过且当前昏眩 → 不用。
    /// </summary>
    public static bool ShouldUseRule(
        WakuuPotionPhase phases,
        WakuuPotionFightScope scope,
        bool firstRoundOnly,
        bool hasCondition,
        bool conditionMet,
        bool skipWhenStunned,
        bool stunned,
        WakuuPotionPhase phase,
        int round,
        bool hardFight,
        bool bossFight)
    {
        if (!phases.HasFlag(phase))
        {
            return false;
        }

        if (!IsScopeAllowed(scope, hardFight, bossFight))
        {
            return false;
        }

        if (firstRoundOnly && round > 1)
        {
            return false;
        }

        if (hasCondition && !conditionMet)
        {
            return false;
        }

        if (skipWhenStunned && stunned)
        {
            return false;
        }

        return true;
    }

    /// <summary>战斗范围判定：AnyCombat 恒真；HardFight 需精英/Boss；BossFight 仅 Boss。</summary>
    public static bool IsScopeAllowed(WakuuPotionFightScope scope, bool hardFight, bool bossFight)
    {
        return scope switch
        {
            WakuuPotionFightScope.HardFight => hardFight,
            WakuuPotionFightScope.BossFight => bossFight,
            _ => true,
        };
    }
}
