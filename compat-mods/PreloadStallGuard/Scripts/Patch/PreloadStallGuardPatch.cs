using System;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace PreloadStallGuard.Scripts.Patch;

/// <summary>
/// 挂点：<c>NAssetLoader.LoadInTheBackground(AssetLoadingSession)</c> ——
/// 所有走 <c>PreloadManager</c> 的资源预加载（含第三方自定义的，如 GensokyoSpire 的 'MomoVfx'）
/// 都从这一个入口入队，且入队后立刻返回 <c>session.Task</c> 给等待者。在这里登记，
/// 「首次观察时间」才等于真实入队时间。
/// </summary>
[HarmonyPatch(typeof(NAssetLoader), nameof(NAssetLoader.LoadInTheBackground))]
internal static class PreloadStallGuardPatch
{
    [HarmonyPostfix]
    internal static void Postfix(AssetLoadingSession session)
    {
        try
        {
            if (session == null || session.IsCompleted)
            {
                return;
            }

            PreloadStallRegistry.Track(session, (long)Time.GetTicksMsec());
        }
        catch (Exception ex)
        {
            Log.Warn($"[PreloadStallGuard] 登记预加载会话失败: {ex.Message}");
        }
    }
}
