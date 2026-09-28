using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 本地多控「大厅席位」的纯判定（R2 第四项：把 Daily / Custom 两份同构判定收成一份）。
///
/// 原本这份判定叫 <c>DailyLobbyPolicy</c>，只给每日页用；Custom 页则把同一套「要不要 reconcile」
/// 内联在补丁里（<c>NCustomRunLocalSelfCoopPatch.TryReconcileLocalPlayers</c>）——
/// 两处语义逐字相同，改动时容易只改一边。现统一到这里。
///
/// ⚠ **这里只回答「席位集合/顺序」的问题**，不碰角色来源：
/// Daily 的角色由「日期种子 + 人数」的 RNG 决定（逐席位驱动游戏自己的 <c>SetupLobbyParams</c>），
/// Custom 的角色是真人点选后同步 UI —— 两者差异是**语义差异**，不做合并。
///
/// Daily 页接入时的三处硬差异仍然成立（见 `本地多角色扩展到 Daily 模式可行性分析` §五）：
/// ① 大厅**异步**建（先 await 时间服务器），`_lobby` 可能长时间为 null；
/// ② 人数上限写死在 `new StartRunLobby(..., 4)` 且 `_maxPlayers` 是 readonly ⇒ 只能 clamp 到 4；
/// ③ 人数/成员一变，每日种子与每席位角色都会变 ⇒ 需要一个「要不要重跑角色分配」的判据（席位指纹）。
///
/// 这里只做纯判定，不碰 Godot / 游戏类型，便于单测。
/// </summary>
internal static class LocalLobbySeatPolicy
{
    /// <summary>Daily 页固定 4 人上限（游戏 `NDailyRunScreen` 建厅时写死 4，`_maxPlayers` readonly 不可扩容）。</summary>
    internal const int MaxDailyLocalPlayerCount = 4;

    /// <summary>
    /// 解析「本次要进大厅的本地席位」= 已知本地席位表的前 N 个（N = 目标人数，又被页面上限截断）。
    ///
    /// Daily 传 <c>ClampSeatCount(LobbyLocalPlayerLimit)</c>（页面上限 4）；
    /// Custom 传全局上限 <c>MaxLocalPlayerCount</c>（目标人数本身已被 context clamp 到该上限内）。
    /// </summary>
    /// <param name="knownLocalIds">本 mod 已知的全部本地伪玩家 id（顺序即槽位顺序）。</param>
    /// <param name="desiredCount">目标本地人数。</param>
    /// <param name="seatLimit">本页席位上限（Daily = 4）。</param>
    internal static List<ulong> ResolveTargetSeats(
        IReadOnlyList<ulong> knownLocalIds,
        int desiredCount,
        int seatLimit)
    {
        if (knownLocalIds == null || desiredCount <= 0 || seatLimit <= 0)
        {
            return new List<ulong>();
        }

        return knownLocalIds.Take(System.Math.Min(desiredCount, seatLimit)).ToList();
    }

    /// <summary>
    /// 需要调整大厅里的本地席位吗。
    /// </summary>
    /// <param name="lobbySeatIds">大厅当前所有玩家的 id（顺序即席位顺序）。</param>
    /// <param name="targetSeatIds">目标本地席位 id。</param>
    /// <param name="knownLocalIds">本 mod 已知的全部本地伪玩家 id（用于区分「我们的席位」与「真联机玩家」）。</param>
    internal static bool NeedsReconcile(
        IReadOnlyList<ulong> lobbySeatIds,
        IReadOnlyList<ulong> targetSeatIds,
        IReadOnlyList<ulong> knownLocalIds)
    {
        if (targetSeatIds == null || targetSeatIds.Count <= 1)
        {
            return false;
        }

        bool missing = targetSeatIds.Any(id => !lobbySeatIds.Contains(id));
        bool extra = lobbySeatIds.Any(id => knownLocalIds.Contains(id) && !targetSeatIds.Contains(id));
        return missing || extra;
    }

    /// <summary>
    /// 大厅里属于本地席位的 id，**保持大厅顺序** —— Daily 的角色分配就是按这个顺序 roll 的，
    /// 所以顺序不能重排（否则每席位拿到的角色会与官方多人大厅不一致）。
    /// </summary>
    internal static List<ulong> OrderedLocalSeats(
        IReadOnlyList<ulong> lobbySeatIds,
        IReadOnlyList<ulong> knownLocalIds)
    {
        if (lobbySeatIds == null || knownLocalIds == null)
        {
            return new List<ulong>();
        }

        return lobbySeatIds.Where(id => knownLocalIds.Contains(id)).ToList();
    }

    /// <summary>
    /// 席位序列指纹（顺序相关）：人数或成员一变，指纹就变 —— 用来避免每帧重跑角色分配。
    /// </summary>
    internal static int ComputeSeatSignature(IReadOnlyList<ulong> orderedSeatIds)
    {
        if (orderedSeatIds == null || orderedSeatIds.Count == 0)
        {
            return 0;
        }

        unchecked
        {
            int hash = 17;
            foreach (ulong id in orderedSeatIds)
            {
                hash = hash * 31 + (int)(id ^ (id >> 32));
            }

            return hash;
        }
    }

    /// <summary>
    /// 是否需要（重新）按每日种子给各席位分配角色。
    /// </summary>
    /// <param name="orderedSeatIds">大厅里属于本地席位的 id（保持大厅顺序 —— 游戏就是按这个顺序 roll 角色）。</param>
    /// <param name="assignedSignature">上次分配时的席位指纹（0 = 还没分配过）。</param>
    internal static bool NeedsCharacterAssignment(IReadOnlyList<ulong> orderedSeatIds, int assignedSignature)
    {
        if (orderedSeatIds == null || orderedSeatIds.Count == 0)
        {
            return false;
        }

        return ComputeSeatSignature(orderedSeatIds) != assignedSignature;
    }

    /// <summary>
    /// 把目标人数收进 Daily 的 4 人上限（`_maxPlayers` 不可扩容，超了会加不进大厅）。
    /// </summary>
    internal static int ClampSeatCount(int desiredCount)
    {
        if (desiredCount < 0)
        {
            return 0;
        }

        return desiredCount > MaxDailyLocalPlayerCount ? MaxDailyLocalPlayerCount : desiredCount;
    }
}
