using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 整局进度写入的「本地玩家识别」校正（2026-09-27，r155，BUG-20）。
///
/// 现象：本地多控下整局结束时游戏打一条
/// `Local player with net id 1 not found in run! Progress will not be updated`，
/// 随后直接从 `ProgressSaveManager.UpdateWithRunData` 返回 ——
/// **本局的胜场/败场/时长/卡牌与遗物统计/epoch 解锁一条都不写入**。
/// 连带后果（用户报的 bug）：新角色的 `CharacterStats` 条目永远不会被创建，
/// 结算页第二页 `NGameOverScreen.SaveBadgesToProgress` 用索引器取该角色统计抛
/// `KeyNotFoundException` → `AnimateRunSummary` 中断 → 「返回主菜单」按钮没被
/// Visible/Enable ⇒ **游戏结束后没有结束按钮，卡死在结算页**。
///
/// 根因：本 mod 的回环 host 服务把网络平台报成 `PlatformType.None`，而 run 存档里的
/// `platform_type` 取自 `RunManager.ToSave()` 的 `NetService.Platform` ⇒ 也是 None；
/// None 平台策略的本地玩家 id 是占位值 `1`，run 里的玩家 NetId 却是本机 Steam ID
/// （76561198…）⇒ `Players.FirstOrDefault(p =&gt; p.NetId == 1)` 落空 ⇒ 提前 return。
///
/// 修法：进 `UpdateWithRunData` 前，若「按 run 自己记的平台认不到玩家」而
/// 「按 `PlatformUtil.PrimaryPlatform`（Steam）能认到」，就临时把 run 的 PlatformType
/// 换成 PrimaryPlatform，让游戏原本那套逻辑照常跑完整；finalizer 里立刻还原，
/// 不影响后续 RunHistory / 每日榜 / 读档等对同一个 run 对象的读取。
/// 判据严格（只在原逻辑确实认不到人时才动手），单人局与正常平台局保持原样。
/// </summary>
[HarmonyPatch(typeof(ProgressSaveManager), nameof(ProgressSaveManager.UpdateWithRunData))]
internal static class ProgressSaveManagerUpdateWithRunDataPatch
{
    /// <summary>Prefix 与 Finalizer 之间传递的还原信息（Harmony 的 __state）。</summary>
    private sealed class PatchState
    {
        internal SerializableRun? Run;
        internal PlatformType OriginalPlatform;
    }

    [HarmonyPrefix]
    private static void Prefix(SerializableRun serializableRun, ref PatchState? __state)
    {
        try
        {
            List<SerializablePlayer>? players = serializableRun?.Players;
            if (players == null)
            {
                return;
            }

            // R3 B5 复核：这里问的是**平台身份**（平台 API 认定的本机玩家），不是席位表身份 ——
            // 口径已统一在纯逻辑 `RunProgressLocalPlayerPolicy.Decide(...)`（r155），刻意不接 `LocalSeatSource`。
            PlatformType original = serializableRun!.PlatformType;
            ulong localId = PlatformUtil.GetLocalPlayerId(original);
            PlatformType primary = PlatformUtil.PrimaryPlatform;
            bool hasAlternative = primary != original;
            ulong primaryId = hasAlternative ? PlatformUtil.GetLocalPlayerId(primary) : 0UL;

            RunProgressLocalPlayerPolicy.Action action = RunProgressLocalPlayerPolicy.Decide(
                players.Select(player => player.NetId).ToList(),
                localId,
                hasAlternative,
                primaryId,
                out ulong resolvedNetId);

            switch (action)
            {
                case RunProgressLocalPlayerPolicy.Action.Keep:
                    // 单人局，或 run 自己记的平台就能认到本地玩家 → 原样交给游戏
                    return;

                case RunProgressLocalPlayerPolicy.Action.CannotResolve:
                    LocalMultiControlLogger.Warn(
                        "整局进度写入：本地玩家识别失败（原平台与主平台都认不到），本局进度不会写入。"
                        + $"platform={original}, netId={localId}, primary={primary}, primaryNetId={primaryId}, "
                        + $"players={DescribePlayers(players)}");
                    return;

                case RunProgressLocalPlayerPolicy.Action.Rewrite:
                    serializableRun.PlatformType = primary;
                    __state = new PatchState { Run = serializableRun, OriginalPlatform = original };
                    LocalMultiControlLogger.Info(
                        $"整局进度写入已校正本地玩家识别：platform={original} -> {primary}, netId={resolvedNetId}, "
                        + $"players={DescribePlayers(players)}");
                    return;

                default:
                    return;
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"整局进度写入本地玩家校正异常(忽略，保持原平台): {exception.Message}");
        }
    }

    [HarmonyFinalizer]
    private static void Finalizer(PatchState? __state)
    {
        try
        {
            if (__state?.Run == null)
            {
                return;
            }

            __state.Run.PlatformType = __state.OriginalPlatform;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"整局进度写入平台还原异常(忽略): {exception.Message}");
        }
    }

    private static string DescribePlayers(IReadOnlyList<SerializablePlayer> players)
    {
        return string.Join(",", players.Select(player => $"{player.NetId}:{player.CharacterId?.Entry ?? "none"}"));
    }
}
