namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 「**结构外成员**」（典型 = 战神/临时席位：只存在于 `CombatState`、**不进** `runState.Players`）
/// 撞上"每个玩家一份的原版账本"时，我们该怎么办的**统一判据**（r264/r265；根因与实机铁证见 references AE-24）。
///
/// <para><b>背景</b>：原版有大量"按玩家取一份"的结构 ——
/// <c>xxx[runState.GetPlayerSlotIndex(player)]</c>（索引，`GetPlayerSlotIndex` 对不在 `Players` 里的玩家返回
/// <b>-1</b>）或 <c>_actionQueues.First(q =&gt; q.ownerId == playerId)</c>（按键查，查不到直接抛）。
/// 结构外成员一到就会 `ArgumentOutOfRangeException` / `InvalidOperationException`
/// （2026-10-11 实机把 <c>turn loop</c> 打死，整场战斗废掉）。
/// 同族消费者清单由 <c>Scripts/Tools/scan_slot_index_consumers.py</c> 维护，已挂 `preflight` G2b。</para>
///
/// <para><b>处置口径：改道 &gt; 跳过 &gt; 不管</b>（AE-24）——
/// 能"给它一份自己的账"就改道（如 choiceId 的独立序列、动作队列补一条）；
/// 只有确认"跳过不会让某个聚合判据永远不成立"时才跳过（如悬停模型）；
/// 否则明确登记为不可达并**不加**（如 `EventCombatSynchronizer` 的"全员就位"计数）。</para>
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

    /// <summary>
    /// 要不要给这个"结构外成员"补一条**本地动作队列**（`ActionQueueSet._actionQueues`）。
    ///
    /// <para>原版按 `runState.Players` 在开局建队列，而战神是**战斗内**才出现的 ⇒ 它入队任何 action
    /// （例如牌效果触发的 `GenericHookGameAction`）都会在 <c>GetQueue</c> 处抛
    /// <c>Tried to get local action queue for nonexistent player with ID …</c>
    /// （2026-10-11 r265 实机：「不死之烟」触发过一次，被 r262 的黑名单兜住 = 该牌本局托管不打）。</para>
    ///
    /// <para>这里只认**登记的临时席位**（不用"不在 runState 里"的通用判据）：给任意结构外成员造队列
    /// 风险更高，而临时席位是我们自己创建、生命周期可控的。
    /// 补队列时必须**照抄现有队列的状态位**（`isPaused` / `isCancellingCombatActions` / …），
    /// 否则新队列会带着"取消一切战斗动作"的初始值把该席位刚入队的 hook 直接取消掉。</para>
    /// </summary>
    internal static bool ShouldProvideActionQueue(bool localMultiEnabled, bool isTemporarySeat, ulong playerId)
    {
        return localMultiEnabled && isTemporarySeat && playerId != 0UL;
    }
}
