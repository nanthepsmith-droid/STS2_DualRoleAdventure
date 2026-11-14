using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「回合开始那一瞬上下文席位是空/无效时临时钉哪个席位」纯逻辑（r356，2026-11-14 实机）。
///
/// <para><b>根因链（marker r355 实机日志）</b>：
/// ① 战神（临时席位）的遗物钩子自动打出 `WATCHER-CUT_THROUGH_FATE`，卡牌选择走 BaseLib 包装层
/// `BaseLibCardSelectCmd.FromCombatPileInOrder`；我们的「远端选择防软锁兜底」把结果猜成 `Index`
/// ⇒ 调用方 `.AsCombatCards()` 抛异常 ⇒ 包住它的 `GenericHookGameAction` 永久停在
/// `waiting for player choice`（牌面停在屏幕中间）。
/// ② 期间又有一次**托管席位的手牌变换**：`CardTransformNetIdPinPatch` 为了防止原版视觉去前台手牌里
/// 找托管席位的牌节点（必抛）而把 **全局** `LocalContext.NetId` 让到了 `null`，正常路径靠
/// `RestoreNetIdAfterAsync`（await 变换 Task）还原 —— 但那条 Task 被 ① 堵住的队列拖住 ⇒
/// 还原没来得及跑。
/// ③ 原版 `NEndTurnButton.OnTurnStarted` 里 `Player me = LocalContext.GetMe(state)` 直接**返回 null**
/// （`NetId` 为空时原版就是返回 null，不抛）⇒ 下一句 `me.PlayerCombatState` NRE ⇒
/// `Combat #2 turn loop died … the combat is stuck until the room is restarted` ⇒ 无法结束回合。</para>
///
/// <para><b>本策略只管一件事</b>：在那次 `OnTurnStarted` 调用**期间**把上下文临时钉到一个"确实在
/// 这场战斗里"的本地席位（调用返回后立刻还原，见
/// <c>NEndTurnButtonOnTurnStartedContextGuardPatch</c>），从而不去和变换视觉抢全局状态。
/// 判据刻意保守：<b>现状可用就绝不动手</b>（返回 null = 不需要兜底）。</para>
/// </summary>
internal static class ContextSeatRecoveryPolicy
{
    /// <summary>
    /// 需要临时钉住时返回该席位 id；返回 null = 现状可用（或没有任何候选），不要动手。
    /// </summary>
    /// <param name="currentContextSeatId">当前 <c>LocalContext.NetId</c>（null = 未设置）。</param>
    /// <param name="candidatesPresentInCombat">
    /// 按优先级排好、且**已确认在这份战斗状态里存在**的本地席位（前台 → 受控 → 主席位 → 其它本地席位）。
    /// 顺序即优先级，本方法不做二次排序。
    /// </param>
    public static ulong? Decide(ulong? currentContextSeatId, IReadOnlyList<ulong>? candidatesPresentInCombat)
    {
        if (candidatesPresentInCombat == null || candidatesPresentInCombat.Count == 0)
        {
            // 一个可用的本地席位都没有（例如自己已撤离战斗）：保持现状，别乱钉。
            return null;
        }

        if (currentContextSeatId.HasValue && Contains(candidatesPresentInCombat, currentContextSeatId.Value))
        {
            return null;
        }

        return candidatesPresentInCombat[0];
    }

    private static bool Contains(IReadOnlyList<ulong> seatIds, ulong seatId)
    {
        for (int i = 0; i < seatIds.Count; i++)
        {
            if (seatIds[i] == seatId)
            {
                return true;
            }
        }

        return false;
    }
}
