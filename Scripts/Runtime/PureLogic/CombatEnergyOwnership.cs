namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 战斗能量球（NEnergyCounter）归属判定的纯函数（BUG-1：战斗第一回合能量不同步）。
///
/// 背景：进战斗前把前台停在瓦库托管角色上时，观感是「手牌/UI 是真人玩家（战斗开始的默认第一个玩家），
/// 能量球却是瓦库的」——能量球与手牌不属于同一个玩家（瓦库带【瓦库形态】+1 能量，数值也会串）。
/// 根因可以是多条路径（入战刷新取到的"当前玩家"与手牌归属不一致、切换前台时 UI 刷新部分失败等），
/// 但**不变量只有一条**：能量球显示的玩家必须等于当前展示的手牌所属玩家。
///
/// 这里只做「谁该拥有能量球」的判定，不碰任何 Godot / 游戏类型，便于单测。
/// 优先级：手牌归属 &gt; 受控玩家（拿不到手牌归属时才退化到受控玩家兜底）。
/// </summary>
internal static class CombatEnergyOwnership
{
    /// <summary>
    /// 判断能量球是否需要按目标玩家重建。
    /// </summary>
    /// <param name="energyPlayerId">当前能量球绑定的玩家（取不到传 null）。</param>
    /// <param name="handPlayerId">当前展示的手牌所属玩家（取不到传 null）。</param>
    /// <param name="controlledPlayerId">当前受控/前台玩家（兜底用，取不到传 null）。</param>
    /// <param name="targetPlayerId">需要重建时输出的目标玩家。</param>
    /// <returns>true = 需要按 targetPlayerId 重建能量球；false = 归属已一致或无法判定（不做无依据的改动）。</returns>
    public static bool TryResolveMismatch(
        ulong? energyPlayerId,
        ulong? handPlayerId,
        ulong? controlledPlayerId,
        out ulong targetPlayerId)
    {
        targetPlayerId = 0;

        // 手牌归属优先；没有手牌信息才退化到受控玩家兜底。
        ulong? target = handPlayerId ?? controlledPlayerId;

        // 拿不到任何归属信息 → 不做无依据的重建（避免把正常状态改坏）。
        if (!target.HasValue || target.Value == 0)
        {
            return false;
        }

        // 归属已一致 → 什么都不做（这是绝大多数回合的常态，绝不重复重建能量球）。
        if (energyPlayerId.HasValue && energyPlayerId.Value == target.Value)
        {
            return false;
        }

        targetPlayerId = target.Value;
        return true;
    }
}
