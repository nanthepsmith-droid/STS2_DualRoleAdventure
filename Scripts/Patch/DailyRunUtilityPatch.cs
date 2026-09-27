using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Daily;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 本地多控每日局「禁止上传排行榜分数」（r156，已拍板方向 ——
/// 见 `maintenance-docs/decision-records/本地多角色扩展到Daily模式可行性分析.md` §三差异 3）。
///
/// 为什么必须拦：结束局时游戏对 Host 调 `DailyRunUtility.UploadScore(DailyTime, score, Players)`
/// （`RunManager.cs:1682-1689`，本地回环 `Type == NetGameType.Host` 正好命中），
/// 而上传用的是**原始 NetId**；本地多控的伪玩家 id（`1,2,3…` / 自生成序列）不是真实 Steam 用户
/// ⇒ 会去查询不存在的用户、把 `{真实id, 假id…}` 一起塞进每日榜（榜名还带 `{人数}p`），
/// 污染他人视野、可能触发风控。游戏自己只对「恰好 1 人且 id==1」做了平台 id 特判，
/// 多本地席位时完全不设防。
///
/// 处置：仅在「本地多控开启 + 当前 NetService 是本地回环」时跳过上传，并打一条可 grep 的
/// `DAILY_SCORE_SKIP` 锚点；真实联机 / 单人每日局行为完全不变（前缀直接放行）。
/// </summary>
[HarmonyPatch(typeof(DailyRunUtility), nameof(DailyRunUtility.UploadScore))]
internal static class DailyRunUtilityUploadScorePatch
{
    [HarmonyPrefix]
    private static bool Prefix(DateTimeOffset time, int score, List<SerializablePlayer> players, ref Task __result)
    {
        if (!LocalSelfCoopContext.IsEnabled)
        {
            return true;
        }

        if (RunManager.Instance?.NetService is not LocalLoopbackHostGameService)
        {
            return true;
        }

        __result = Task.CompletedTask;
        LocalMultiControlLogger.Info(
            "DAILY_SCORE_SKIP 本地多控每日局跳过排行榜分数上传: "
            + $"score={score}, dailyTime={time:yyyy-MM-dd}, players={DescribePlayers(players)}");
        return false;
    }

    private static string DescribePlayers(List<SerializablePlayer>? players)
    {
        if (players == null || players.Count == 0)
        {
            return "none";
        }

        return string.Join(",", players.Select(player => player.NetId.ToString()));
    }
}
