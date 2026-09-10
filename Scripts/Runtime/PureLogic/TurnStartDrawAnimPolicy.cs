namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「跳过他人回合开始抽牌演出」的判定纯函数（改进-1）。
///
/// 背景：本地多控下，回合开始会依次把前台切到每个真人玩家、逐个播完其自动抽牌动画再切下一个
/// （<c>CombatManager.SetupPlayerTurn</c> 前缀里 <c>TryEnsureForegroundForPlayer</c>）。
/// 2 人还能忍，满员 10+ 人时要等很久。
///
/// 依据（反编译 sts2src `CardPileCmd.GetTweenForCardsChangingPiles`）：
/// <code>
/// bool flag = LocalContext.IsMe(result.cardAdded.Owner);
/// if (!flag &amp;&amp; pileType != PileType.Play &amp;&amp; pileType2 != PileType.Play) continue;  // 不建节点、不做补间
/// </code>
/// 也就是说**非本地玩家**（非前台）的 Draw→Hand 本来就不播动画、直接数据生效。
/// 所以「不切前台」即等价于「跳过演出」，且不需要拦截任何卡牌逻辑 —— 数据完全照常。
///
/// 这里只判定「要不要跳过这次前台切换」，不碰任何 Godot / 游戏类型，便于单测。
/// 判定为跳过的前提：开关开 + 本地多控生效 + 有明确的前台玩家 + 该玩家**不是**前台那位。
/// </summary>
internal static class TurnStartDrawAnimPolicy
{
    /// <summary>
    /// 是否跳过该玩家在回合开始时的前台切换（= 跳过其抽牌演出）。
    /// </summary>
    /// <param name="toggleEnabled">配置开关（<c>skipTurnStartDrawAnim</c>）是否开启。</param>
    /// <param name="localMultiControlEnabled">本地多控是否生效（单人局一律不干预）。</param>
    /// <param name="foregroundPlayerId">当前前台玩家 id（取不到传 0）。</param>
    /// <param name="playerId">本次回合开始 hook 对应的玩家 id（取不到传 0）。</param>
    /// <returns>true = 跳过切换，让该玩家的抽牌瞬时生效；false = 按原逻辑切前台播演出。</returns>
    public static bool ShouldSkipSwitch(
        bool toggleEnabled,
        bool localMultiControlEnabled,
        ulong foregroundPlayerId,
        ulong playerId)
    {
        if (!toggleEnabled || !localMultiControlEnabled)
        {
            return false;
        }

        // 没有前台信息 / 无效 id：不做无依据的干预
        if (foregroundPlayerId == 0UL || playerId == 0UL)
        {
            return false;
        }

        // 本来就盯着这一位 → 切换是空操作，不算「跳过他人」，保持原样（让演出正常播）
        return playerId != foregroundPlayerId;
    }
}
