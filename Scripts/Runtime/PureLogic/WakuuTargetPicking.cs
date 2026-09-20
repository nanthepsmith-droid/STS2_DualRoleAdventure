using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 目标选择纯函数（可行性分析 §18.2.6「目标选择升级」，用户 2026-09-20 拍板实现）。
///
/// 规则（只用于 <c>vakuuBrain=scored</c> 档，不影响默认档的既有行为）：
/// ① 能击杀的敌人优先（消除威胁源，且击杀后本回合剩余伤害不再溢出到"血量第一"的敌人身上）；
/// ② 否则打**有效血量最低**的敌人（（当前血 - 格挡）最小）—— 集火，尽快减少敌人数量；
/// ③ 并列时取**战斗顺序更靠前**的那个（稳定、可回归，与"最左兜底"同向）。
///
/// 为什么不用"第一个可打敌人"（旧行为）：旧行为会让伤害平摊到每个敌人身上，
/// 谁也打不死，是本项目 §21.4.2 #6 记录的明确改进点。
/// </summary>
internal static class WakuuTargetPicking
{
    /// <summary>
    /// 从敌人有效血量表里挑一个目标下标；空表返回 -1。
    /// <paramref name="estimatedDamage"/> 传"这张牌对单个目标的粗估伤害"，用于判断能否击杀。
    /// </summary>
    public static int PickEnemyIndex(IReadOnlyList<int> enemyEffectiveHp, int estimatedDamage)
    {
        if (enemyEffectiveHp == null || enemyEffectiveHp.Count == 0)
        {
            return -1;
        }

        // ① 可击杀优先（按战斗顺序取第一个能打死的）。
        for (int i = 0; i < enemyEffectiveHp.Count; i++)
        {
            if (enemyEffectiveHp[i] <= estimatedDamage)
            {
                return i;
            }
        }

        // ② 集火：有效血量最低（并列取最左）。
        int best = 0;
        for (int i = 1; i < enemyEffectiveHp.Count; i++)
        {
            if (enemyEffectiveHp[i] < enemyEffectiveHp[best])
            {
                best = i;
            }
        }

        return best;
    }
}
