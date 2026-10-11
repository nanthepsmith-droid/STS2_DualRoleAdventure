namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「**结构外成员**」（典型 = 战神/临时席位：只存在于 `CombatState`、**不进** `runState.Players`）
/// 撞上"按槽位索引记账"的原版代码时，要不要由我们的护栏直接放行/跳过（r265）。
///
/// <para><b>为什么要一条统一的判据</b>：原版多处代码是
/// <c>xxx[runState.GetPlayerSlotIndex(player)]</c> 形式的记账，而
/// <c>IPlayerCollection.GetPlayerSlotIndex</c> 的接口注释就写着对不在 `Players` 里的玩家返回 <b>-1</b>
/// ⇒ 结构外成员一到就 `ArgumentOutOfRangeException`（2026-10-11 实机把 <c>turn loop</c> 打死，
/// 见 references AE-24；同族消费者清单由 <c>Scripts/Tools/scan_slot_index_consumers.py</c> 维护，
/// 已挂 `preflight` G2b）。</para>
///
/// <para><b>判据</b>（两个都成立才拦）：① 本地多控开启（原版 / 真联机一律不介入）；
/// ② 该玩家是临时席位，**或**它根本不在 `runState.Players` 里（通用：第三方将来塞的结构外成员也兜住）。
/// `runState` 拿不到时调用方按"在场"传 ⇒ **不拦**（宁可少拦也不误拦真人）。</para>
///
/// 纯逻辑（无 Godot / 游戏类型依赖）⇒ 可单测。
/// </summary>
internal static class ForeignSeatGuardPolicy
{
    /// <summary>悬停模型追踪（`HoveredModelTracker`）要不要跳过这个玩家。</summary>
    /// <param name="localMultiEnabled">本地多控是否开启。</param>
    /// <param name="isTemporarySeat">是不是登记的临时席位（战神 / POC 哨兵号段）。</param>
    /// <param name="playerInRunState">该玩家是否在 `runState.Players` 里（拿不到 runState 时传 <c>true</c>）。</param>
    /// <param name="playerId">玩家 NetId（0 一律不拦）。</param>
    internal static bool ShouldSkipHoverTracking(
        bool localMultiEnabled,
        bool isTemporarySeat,
        bool playerInRunState,
        ulong playerId)
    {
        if (!localMultiEnabled || playerId == 0UL)
        {
            return false;
        }

        return isTemporarySeat || !playerInRunState;
    }
}
