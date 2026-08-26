using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 修复"火堆后出发黑屏"：本地多控下，前台角色休息完成后玩家可直接切人/点地图出发，
//  而另一位角色（如后台瓦库）的休息区选择还没完成——此时
//  RestSiteSynchronizer.AfterAllRestSitesCompleted 会永远等待未完成的
//  completionTaskSource，MoveToMapCoordAction 卡死在黑幕里（游戏源码注释明确
//  提示过该挂起风险，原版只靠断线处理兜底）。
///
/// 修复：出发前把所有仍未完成的休息区状态按"跳过"补完（options 清空 + TrySetResult），
/// 与原版断线处理语义一致、幂等安全。已正常选完的不受影响。
/// </summary>
[HarmonyPatch(typeof(MoveToMapCoordAction), "ExecuteAction")]
internal static class MoveToMapCoordRestSiteCompletionPatch
{
    private static readonly FieldInfo? _restSitesField = AccessTools.Field(
        AccessTools.TypeByName("MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer"), "_restSites");

    [HarmonyPrefix]
    private static void Prefix()
    {
        if (!LocalSelfCoopContext.IsEnabled
            || !LocalSelfCoopContext.UseSingleAdventureMode
            || !RunManager.Instance.IsInProgress)
        {
            return;
        }

        try
        {
            object? synchronizer = RunManager.Instance.RestSiteSynchronizer;
            if (synchronizer == null || _restSitesField?.GetValue(synchronizer) is not IEnumerable<object> restSites)
            {
                return;
            }

            int completedCount = 0;
            foreach (object restSite in restSites)
            {
                if (restSite == null)
                {
                    continue;
                }

                TaskCompletionSource? completion =
                    AccessTools.Field(restSite.GetType(), "completionTaskSource")?.GetValue(restSite) as TaskCompletionSource;
                if (completion == null || completion.Task.IsCompleted)
                {
                    continue;
                }

                if (AccessTools.Field(restSite.GetType(), "options")?.GetValue(restSite) is System.Collections.IEnumerable options)
                {
                    options.GetType().GetMethod("Clear")?.Invoke(options, null);
                }

                completion.TrySetResult();
                completedCount++;
            }

            if (completedCount > 0)
            {
                LocalMultiControlLogger.Info(
                    $"出发前检测到 {completedCount} 个未完成的休息区选择，已按跳过补完（防出发黑屏死等）");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"补完休息区状态失败（不影响本次出发）: {exception.Message}");
        }
    }
}
