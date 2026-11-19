using System;
using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 本地席位显示名的**唯一解析入口**（2026-11-18 §排期 6）：瓦库 = <c>Bot N</c>，真人 = <c>Player N</c>。
///
/// <para><b>为什么要这一层</b>：战斗内左侧玩家列表的名牌由原版 <c>NMultiplayerPlayerState._Ready</c> 画，
/// 取的是 <c>PlatformUtil.GetPlayerNameRaw</c>；而本地多控的所有席位都是同一台机器 / 同一个 Steam 账号
/// 伪造出来的 ⇒ 原样显示时**一桌人全是同一个昵称**，完全分不清谁是谁。同族只用 Raw 的还有
/// <c>Creature.cs</c>（creature 的玩家名，tooltip 用）。</para>
///
/// <para><b>覆盖范围</b>：只接管**本机会话席位**（<see cref="LocalSelfCoopContext.TryGetSlotIndex"/> 命中），
/// 其余 id（真联机队友 / 第三方 Bot / 战神这类临时席位）一律返回 null ⇒ 调用方放行原版昵称，
/// 绝不改写别人的名字。</para>
///
/// <para><b>纯逻辑</b>：下标 → 称呼的映射在 <see cref="SeatDisplayNamePolicy"/>（可单测）；
/// 本类只负责"问会话 + 问是不是瓦库 + 去重日志 + 兜异常"。</para>
/// </summary>
internal static class LocalSeatLabels
{
    /// <summary>
    /// 日志去重（每个席位每次进程只打一条 `玩家显示名解析: …`）。
    /// **纯日志用途、不属于复位契约**：漏清只会少打一条锚点，不会影响功能；进局 / 退局各有一次复位。
    /// </summary>
    private static readonly HashSet<ulong> _loggedPlayerIds = new();

    /// <summary>
    /// 解析本地席位显示名（<c>Bot N</c> / <c>Player N</c>）。
    /// 非本地席位 / 没开本地多角色 ⇒ **null**（调用方必须放行原版昵称）。
    /// </summary>
    internal static string? TryResolve(ulong playerId)
    {
        try
        {
            if (playerId == 0UL || !LocalSelfCoopContext.IsEnabled)
            {
                return null;
            }

            if (!LocalSelfCoopContext.TryGetSlotIndex(playerId, out int slotIndex))
            {
                return null;
            }

            SeatDisplayKind kind = LocalSelfCoopContext.IsWakuuEnabled(playerId)
                ? SeatDisplayKind.Wakuu
                : SeatDisplayKind.Human;

            string? label = SeatDisplayNamePolicy.ResolveFromSlotIndex(slotIndex, kind);
            if (label != null && _loggedPlayerIds.Add(playerId))
            {
                LocalMultiControlLogger.Info(
                    $"玩家显示名解析: seat={slotIndex + 1}, player={playerId}, label={label}, kind={kind}");
            }

            return label;
        }
        catch (Exception exception)
        {
            // 显示名解析绝不能抛：调用方是原版的 _Ready / tooltip 链，抛了会打断整条玩家列表构建。
            LocalMultiControlLogger.Warn(
                $"玩家显示名解析失败（放行原版名字）: {exception.GetType().Name}: {exception.Message}");
            return null;
        }
    }

    /// <summary>
    /// 给自建 UI（奖励标签 / 选人文案 / 幽灵手 / 切人提示）用的称呼：解析不到就退回「未知角色」。
    /// </summary>
    internal static string Describe(ulong playerId)
    {
        return TryResolve(playerId) ?? LocalModText.UnknownSeat;
    }

    /// <summary>进局 / 退局复位日志去重表（只影响锚点数量，不影响功能）。</summary>
    internal static void ResetLogDedupe(string reason)
    {
        if (_loggedPlayerIds.Count == 0)
        {
            return;
        }

        LocalMultiControlLogger.Info(
            $"玩家显示名解析日志去重已复位: cleared={_loggedPlayerIds.Count}, reason={reason}");
        _loggedPlayerIds.Clear();
    }
}
