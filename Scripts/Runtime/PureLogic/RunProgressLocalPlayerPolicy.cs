using System.Collections.Generic;
using System.Linq;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 整局进度写入时「该不该改写 run 的平台字段来认本地玩家」的判定纯函数
/// （r155，BUG-20：游戏结束后结算页没有结束按钮）。
///
/// 背景：游戏 <c>ProgressSaveManager.UpdateWithRunData</c> 在多玩家局里用
/// `PlatformUtil.GetLocalPlayerId(run.PlatformType)` 找「本地玩家」；本 mod 的回环 host 服务
/// 把网络平台报成 `PlatformType.None`（占位本地玩家 id = 1），而 run 里的玩家 NetId 是本机
/// Steam ID ⇒ 找不到人 ⇒ 直接 return ⇒ 本局进度（胜场/时长/卡牌统计/epoch 解锁）一条都不写入，
/// 进而让结算页徽章保存崩在缺角色统计上。
///
/// 这里只判定「要不要临时改写成备用平台（PrimaryPlatform，通常是 Steam）」以及「改完用哪个 id 认人」，
/// 不碰任何 Godot / 游戏类型，便于单测。口径保守：单人局、原平台已能认到人都一律不动。
/// </summary>
internal static class RunProgressLocalPlayerPolicy
{
    /// <summary>进度写入时的本地玩家识别决策。</summary>
    internal enum Action
    {
        /// <summary>不动，按原平台继续（单人局，或原平台已能认到本地玩家）。</summary>
        Keep,

        /// <summary>原平台认不到、备用平台能认到 → 临时改写成备用平台。</summary>
        Rewrite,

        /// <summary>原平台与备用平台都认不到本地玩家 → 进度必然丢失（由调用方打 WARN）。</summary>
        CannotResolve,
    }

    /// <summary>
    /// 判定是否需要把 run 的平台临时改写为备用平台。
    /// </summary>
    /// <param name="playerNetIds">run 里所有玩家的 NetId。</param>
    /// <param name="runPlatformLocalId">按 run 自己记的平台（PlatformType）解析出的本地玩家 id。</param>
    /// <param name="hasAlternativePlatform">是否存在与 run 平台不同的备用平台。</param>
    /// <param name="alternativePlatformLocalId">备用平台解析出的本地玩家 id（<paramref name="hasAlternativePlatform"/> 为 false 时忽略）。</param>
    /// <param name="resolvedNetId">决策后用于识别本地玩家的 NetId（Keep / Rewrite 时有效）。</param>
    internal static Action Decide(
        IReadOnlyList<ulong> playerNetIds,
        ulong runPlatformLocalId,
        bool hasAlternativePlatform,
        ulong alternativePlatformLocalId,
        out ulong resolvedNetId)
    {
        resolvedNetId = runPlatformLocalId;

        // 单人局：原版直接 Players.First()，与平台识别无关，不插手
        if (playerNetIds == null || playerNetIds.Count <= 1)
        {
            return Action.Keep;
        }

        // 原平台就能认到本地玩家 → 保持原样（真实 LAN / 正常平台的局走这里）
        if (playerNetIds.Contains(runPlatformLocalId))
        {
            return Action.Keep;
        }

        if (!hasAlternativePlatform || !playerNetIds.Contains(alternativePlatformLocalId))
        {
            return Action.CannotResolve;
        }

        resolvedNetId = alternativePlatformLocalId;
        return Action.Rewrite;
    }
}
