using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 进入火堆时为瓦库角色启动自动选择（autoRestChoice 开关，规则见 LocalWakuuRestAutoChoice）。
/// </summary>
[HarmonyPatch(typeof(RestSiteSynchronizer), nameof(RestSiteSynchronizer.BeginRestSite))]
internal static class RestSiteSynchronizerBeginRestSitePatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            LocalWakuuRestAutoChoice.TryBeginPending();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"扫描瓦库休息区失败: {exception.Message}");
        }
    }
}
