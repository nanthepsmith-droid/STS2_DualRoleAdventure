using System;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 弹层入栈后的幽灵自愈（r147，配 <see cref="LocalOverlayPhantomGuard"/>）。
///
/// 为什么必须挂在 `Push` 上：游戏 `NOverlayStack.Push` 里
/// `AddChildSafely` 失败（"Parent node is busy setting up children"）**不会抛异常**，
/// 只会打印一条 ERROR 然后继续执行 —— 于是名单里多出一个没有节点的条目，
/// 共享背板随之变暗并吞掉全部输入（整局软锁）。这里在 Push 之后（以及 Push 抛异常时）
/// 排一次下一帧的判定，把这种条目清掉。
///
/// 之所以"排到下一帧"而不是立刻判定，见 `LocalOverlayPhantomGuard.SchedulePhantomCheck`：
/// `AddChildSafely` 可能改走 `CallDeferred(AddChild)`（下一帧才入栈），立刻判定会误伤正常弹层。
/// </summary>
[HarmonyPatch(typeof(NOverlayStack), nameof(NOverlayStack.Push))]
internal static class NOverlayStackPhantomGuardPatch
{
    [HarmonyPostfix]
    private static void Postfix(NOverlayStack __instance, IOverlayScreen screen)
    {
        LocalOverlayPhantomGuard.SchedulePhantomCheck(__instance, screen, "push-postfix");
    }

    /// <summary>
    /// `Push` 自己抛异常时（例如 `screen.AfterOverlayOpened()` 的 NRE），
    /// `_overlays.Add` 往往已经跑过 —— 同样需要兜一次。异常原样抛出（第三方调用方自己会接）。
    /// </summary>
    [HarmonyFinalizer]
    private static Exception? Finalizer(NOverlayStack __instance, IOverlayScreen screen, Exception? __exception)
    {
        if (__exception != null)
        {
            LocalOverlayPhantomGuard.SchedulePhantomCheck(__instance, screen, "push-threw");
        }

        return __exception;
    }
}
