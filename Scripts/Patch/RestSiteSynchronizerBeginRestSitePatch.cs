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
    private static void Postfix(RestSiteSynchronizer __instance)
    {
        try
        {
            LocalWakuuRestAutoChoice.TryBeginPending();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"扫描瓦库休息区失败: {exception.Message}");
        }

        // 第三方席位（Co-op Bots 合成 Bot）在同一时刻就被索取并作答，而房间节点还没建出来
        // ⇒ 订阅它的选择事件，等房间就绪后补画气泡（纯表现，见 LocalRestSiteSeatBubble）。
        LocalRestSiteSeatBubble.OnRestSiteBegun(__instance);
    }
}
