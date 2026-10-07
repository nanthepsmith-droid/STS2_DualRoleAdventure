using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;

namespace PreloadStallGuard.Scripts.Diagnostics;

/// <summary>
/// 预加载会话生命周期探针：抓「谁创建 / 谁在等 / 有没有被驱动」三类事实。
///
/// 为什么这么抓（2026-10-02 第二轮排查的教训）：只挂 <c>NAssetLoader.LoadInTheBackground</c>
/// 会漏掉「第三方自己 <c>CreateSession</c> + 自己 <c>Process()</c>、压根不走原版排队链路」的写法；
/// 而真正会卡住流程的永远是「有人在等它」。所以在 <c>WaitForCompletion</c> / <c>.Task</c> 上也登记。
/// 注意这两个方法本身必须是**零分配快路径**（每个会话只抓一次栈），因为 <c>Process</c> 每帧都会调。
/// </summary>
internal static class PreloadWaitProbe
{
    [HarmonyPostfix]
    private static void CreateSessionPostfix(AssetLoadingSession __result)
    {
        PreloadStallRegistry.NoteCreated(__result);
    }

    [HarmonyPrefix]
    private static void WaitForCompletionPrefix(AssetLoadingSession __instance)
    {
        PreloadStallRegistry.NoteWaiter(__instance, "WaitForCompletion");
    }

    [HarmonyPostfix]
    private static void TaskGetterPostfix(AssetLoadingSession __instance)
    {
        PreloadStallRegistry.NoteWaiter(__instance, "Task");
    }

    [HarmonyPrefix]
    private static void ProcessPrefix(AssetLoadingSession __instance)
    {
        PreloadStallRegistry.NoteDriven(__instance);
    }
}
