using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 本地多控大厅「席位集合对齐」的共用编排（R2 第四项）。
///
/// 每日页与自定义页原先各有一份**逐字相同**的实现（加缺失席位 / 删多余本地席位 / 标记 ready），
/// 差异只有两处文案串 —— 都抽成参数注入，实现只留这一份：
/// <list type="bullet">
/// <item>日志前缀（<c>每日挑战</c> / <c>自定义模式</c>）—— 面板与按钮名、日志文案是实机契约与
/// <c>log_scan.py</c> 锚点依赖，**逐字保持不变**；</item>
/// <item>会话 sender 上下文来源串（<c>daily-run-opened</c> / <c>custom-run-opened</c>）。</item>
/// </list>
///
/// ⚠ **这里只对齐「席位集合」**，不碰角色来源：Daily 的角色由日期种子 RNG 决定（调用方随后逐席位
/// 驱动游戏自己的 <c>SetupLobbyParams</c>），Custom 的角色是真人点选后同步 UI —— 语义不同，不合并。
///
/// 调用方仍需自己持有 <c>_isReconciling</c> 重入守卫（各页各自一份，语义不变）。
/// </summary>
internal static class LocalLobbySeatReconciler
{
    /// <summary>本地伪席位的默认进阶等级（`AddLocalHostPlayerInternal` 的第 2 个参数）。</summary>
    internal const int MaxLocalAscensionLevel = 10;

    /// <summary>
    /// 把大厅的本地席位对齐到 <paramref name="targetPlayerIds"/>：
    /// ① 缺失的席位逐个「切 sender 再 AddLocalHostPlayerInternal」（sender 决定新席位用哪个 id）；
    /// ② 超出目标的本地席位从大厅移除并通知输入同步器与界面；
    /// ③ 非主席位统一标记 ready（否则会卡在「等待其他玩家」）；
    /// ④ 收起 sender 上下文并打一条同步日志（文案与重构前逐字一致）。
    /// </summary>
    /// <param name="lobby">目标大厅。</param>
    /// <param name="loopbackService">本页的回环服务（切 sender 用）。</param>
    /// <param name="listener">大厅回调接收者 —— Daily / Custom 两个页面本身就是 <see cref="IStartRunLobbyListener"/>。</param>
    /// <param name="targetPlayerIds">目标本地席位（顺序即加入顺序）。</param>
    /// <param name="logPrefix">日志前缀（<c>每日挑战</c> / <c>自定义模式</c>）。</param>
    /// <param name="senderContextSource"><c>EnsureLobbySenderContext</c> 的来源串。</param>
    internal static void Reconcile(
        StartRunLobby lobby,
        LocalLoopbackHostGameService loopbackService,
        IStartRunLobbyListener listener,
        IReadOnlyList<ulong> targetPlayerIds,
        string logPrefix,
        string senderContextSource)
    {
        UnlockState unlockState = SaveManager.Instance.GenerateUnlockStateFromProgress();
        SerializableUnlockState serializableUnlockState = unlockState.ToSerializable();

        int added = 0;
        foreach (ulong playerId in targetPlayerIds)
        {
            if (lobby.Players.Any(player => player.id == playerId))
            {
                continue;
            }

            loopbackService.SetCurrentSenderId(playerId);
            _ = lobby.AddLocalHostPlayerInternal(serializableUnlockState, MaxLocalAscensionLevel);
            added++;
        }

        // 每日页固定 4 席（Custom 页走全局上限）：超出的本地伪席位必须移除，否则会一直卡在「等待其他玩家」
        List<ulong> removablePlayerIds = LocalSelfCoopContext.LocalPlayerIds
            .Skip(targetPlayerIds.Count)
            .ToList();
        int removed = 0;
        foreach (ulong removableId in removablePlayerIds)
        {
            int playerIndex = lobby.Players.FindIndex(player => player.id == removableId);
            if (playerIndex < 0)
            {
                continue;
            }

            StartRunLobbyPlayer removedPlayer = lobby.Players[playerIndex];
            lobby.Players.RemoveAt(playerIndex);
            lobby.InputSynchronizer.OnPlayerDisconnected(removedPlayer.id);
            listener.RemotePlayerDisconnected(removedPlayer);
            removed++;
        }

        bool readyChanged = false;
        for (int i = 0; i < lobby.Players.Count; i++)
        {
            StartRunLobbyPlayer player = lobby.Players[i];
            if (player.id == LocalSelfCoopContext.PrimaryPlayerId || player.isReady)
            {
                continue;
            }

            player.isReady = true;
            lobby.Players[i] = player;
            listener.PlayerChanged(player, false);
            readyChanged = true;
        }

        LocalSelfCoopContext.EnsureLobbySenderContext(senderContextSource);
        LocalMultiControlLogger.Info(
            $"{logPrefix}大厅本地人数已同步: target={targetPlayerIds.Count}, actual={lobby.Players.Count}, "
            + $"added={added}, removed={removed}, readyChanged={readyChanged}");
    }
}
