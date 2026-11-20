using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 回放（run history）面板的**席位序判定**（2026-11-20 收工 ④ 的纯逻辑部分）。
///
/// <para><b>为什么要单独一套</b>：本机会话已经在读档/回放时不存在了，
/// <see cref="LocalSeatLabels.TryResolve"/> 走的 <c>LocalSelfCoopContext.TryGetSlotIndex</c> 必空
/// ⇒ 原版 <c>PlatformUtil.GetPlayerName</c> 只能拿到我们伪造的 NetId 原串（界面上一串数字）。</para>
///
/// <para><b>判据</b>：本 mod 的本地席位 ID 是 <c>本地平台 ID + 0..N-1</c> 的**连续号**
/// （见 <c>LocalSelfCoopContext.BuildSequentialPlayerIds</c>）⇒ 只要存档里的 <c>Players</c> 在
/// 「本地平台 ID 偏移」上对得上，就能把 id 反推成席位序。官方联机的队友 id 与本机 id 不会
/// 恰好差 1~N 且等于该位置，因此不会被误判成席位。</para>
///
/// <para>纯函数（无 Godot / 游戏类型依赖），可直接单测。</para>
/// </summary>
internal static class RunHistorySeatLabelPolicy
{
    /// <summary>
    /// 把存档里的某个玩家 id 反推成 0 起的席位序；不是本机连续席位 ⇒ null。
    /// </summary>
    /// <param name="playerIds">回放里该局的玩家 id 列表（顺序 = 席位序）。</param>
    /// <param name="localPlayerId">本机平台 id（<c>PlatformUtil.GetLocalPlayerId</c>）。</param>
    /// <param name="targetPlayerId">要显示的玩家 id。</param>
    internal static int? ResolveSlotIndex(
        IReadOnlyList<ulong>? playerIds,
        ulong localPlayerId,
        ulong targetPlayerId)
    {
        if (playerIds == null || playerIds.Count < 2 || localPlayerId == 0 || targetPlayerId == 0)
        {
            return null;
        }

        if (targetPlayerId < localPlayerId)
        {
            return null;
        }

        ulong offset = targetPlayerId - localPlayerId;
        if (offset >= (ulong)playerIds.Count)
        {
            return null;
        }

        int index = (int)offset;
        // 双保险：该位置上的 id 必须**就是**它 —— 官方联机的队友 id 不会这样对上。
        return playerIds[index] == targetPlayerId ? index : (int?)null;
    }
}
