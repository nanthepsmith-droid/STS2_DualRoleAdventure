using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.RunHistoryScreen;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 回放（run history）面板的玩家名（2026-11-20 收工 ④，纯显示）。
///
/// <para><b>现象</b>：读档/回放里玩家名是一串数字（我们伪造的 NetId 原串）。</para>
///
/// <para><b>根因</b>：那条链走 <c>PlatformUtil.GetPlayerName(history.PlatformType, player.Id)</c>，
/// 而回放是从主菜单打开的，**本机会话早就没了** ⇒ <see cref="LocalSeatLabels.TryResolve"/> 的
/// 「问会话」必空 ⇒ 放行原版 ⇒ 平台不认识这个 id，直接回原串。</para>
///
/// <para><b>做法</b>：在 <c>NRunHistory.DisplayRun</c> 前缀里把这局的「玩家 id 列表 + 本机平台 id」
/// 登记进 <see cref="RunHistorySeatLabels"/>（必须在建玩家图标之前，原版在本方法内建图标）；
/// 名字解析时由 <see cref="LocalSeatLabels"/> 兜底问这张表 ⇒ 显示 <c>Player N</c>。
/// 号位口径 = 存档里的席位序 + 1，与局内 Tab / 小键盘切人一致。</para>
/// </summary>
[HarmonyPatch(typeof(NRunHistory), "DisplayRun")]
internal static class NRunHistoryDisplayRunPatch
{
    [HarmonyPrefix]
    private static void Prefix(NRunHistory __instance, RunHistory history)
    {
        try
        {
            List<ulong> playerIds = history.Players?
                .Where((player) => player != null)
                .Select((player) => player.Id)
                .ToList() ?? new List<ulong>();
            ulong localPlayerId = PlatformUtil.GetLocalPlayerId(history.PlatformType);
            RunHistorySeatLabels.Register(__instance, playerIds, localPlayerId, "run-history.DisplayRun");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn(
                $"回放席位表登记异常（回放里的名字保持原版）: {exception.GetType().Name}: {exception.Message}");
        }
    }
}
