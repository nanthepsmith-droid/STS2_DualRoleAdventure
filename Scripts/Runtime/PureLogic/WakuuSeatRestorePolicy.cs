using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 从存档标记恢复「瓦库席位」时的过滤规则（r166 修：读档后瓦库席位丢失）。
///
/// 判据与大厅 UI 勾选一致：只认**本地席位表里存在的 id**（第三方往局里塞的席位不算我们的），
/// 并丢掉 0 这类占位值；顺序沿用存档里的顺序（便于日志比对）。
///
/// 背景：读档（「继续游戏」/ ESC 快速重启）此前只恢复玩家 id、**完全不恢复 `wakuu=` 段**，
/// 而瓦库席位又会在进我们自己的大厅入口时被显式清空 ⇒ 读档后 `IsWakuuEnabled` 为空
/// ⇒ 托管遗物不补发、`IsVakuuFormMode=false` ⇒ 瓦库整局不出牌、不自动选事件（2026-09-27 实机）。
/// 证据与修法见 `maintenance-docs/decision-records/runtime架构分层重构评估.md` 同期的 BUG 记录。
/// </summary>
internal static class WakuuSeatRestorePolicy
{
    /// <summary>
    /// 过滤存档里的瓦库席位：丢掉 0 与不在 <paramref name="localPlayerIds"/> 里的 id，保持入参顺序。
    /// </summary>
    public static List<ulong> FilterToLocalSeats(
        IReadOnlyList<ulong> localPlayerIds,
        IReadOnlyList<ulong> savedWakuuPlayerIds)
    {
        HashSet<ulong> localSeats = new();
        for (int i = 0; i < localPlayerIds.Count; i++)
        {
            localSeats.Add(localPlayerIds[i]);
        }

        List<ulong> restored = new();
        for (int i = 0; i < savedWakuuPlayerIds.Count; i++)
        {
            ulong playerId = savedWakuuPlayerIds[i];
            if (playerId == 0 || !localSeats.Contains(playerId) || restored.Contains(playerId))
            {
                continue;
            }

            restored.Add(playerId);
        }

        return restored;
    }
}
