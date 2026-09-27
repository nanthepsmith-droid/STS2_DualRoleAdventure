using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 每日挑战（Daily）本地多控的大厅判定纯函数（r156，《本地多角色扩展到 Daily 模式可行性分析》§五）。
///
/// 为什么单独一份而不是复用 Custom 的判定：Daily 页与 Custom 页有三处硬差异 ——
/// ① Daily 大厅是**异步**建的（先 await 时间服务器），`_lobby` 可能长时间为 null；
/// ② 人数上限写死在 `new StartRunLobby(..., 4)` 且 `_maxPlayers` 是 readonly ⇒ 只能 clamp 到 4；
/// ③ 角色由「日期种子 + 人数」的 RNG 序列决定，**不能随机、不能手选**，
///    因此除了「席位集合要不要 reconcile」还需要一个「要不要重新跑一遍角色分配」的判据
///    （人数/成员一变，种子与每席位角色都会变，必须重分配）。
///
/// 这里只做纯判定，不碰 Godot / 游戏类型，便于单测。
/// </summary>
internal static class DailyLobbyPolicy
{
    /// <summary>Daily 页固定 4 人上限（游戏 `NDailyRunScreen` 建厅时写死 4，`_maxPlayers` readonly 不可扩容）。</summary>
    internal const int MaxDailyLocalPlayerCount = 4;

    /// <summary>
    /// 是否需要调整大厅里的本地席位。
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
