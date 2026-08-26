using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 事件自动选择的真实入口挂点：EventSynchronizer.BeginEvent 在进入事件房时为
/// 所有玩家创建各自的事件实例（内部 BeginEvent 是 fire-and-forget，选项稍后才就绪）。
/// 原先挂在 NEventRoom.RefreshEventState 上不可行——该方法仅在选项被选后
/// （StateChanged）才触发，初次进房的 SetupLayout→SetOptions 路径完全不经过它，
/// 导致后台瓦库的事件永远无人触发（表现为安全网超时切前台等人工）。
/// </summary>
[HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.BeginEvent))]
internal static class EventSynchronizerBeginEventPatch
{
    [HarmonyPostfix]
    private static void Postfix()
    {
        try
        {
            LocalWakuuEventAutoChoice.TryBeginPendingEvents();
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"扫描瓦库事件失败: {exception.Message}");
        }
    }
}
