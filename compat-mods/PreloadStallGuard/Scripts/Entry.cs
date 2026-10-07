using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Nodes;
using PreloadStallGuard.Scripts.Diagnostics;
using PreloadStallGuard.Scripts.Patch;

namespace PreloadStallGuard.Scripts;

/// <summary>
/// PreloadStallGuard —— 资源预加载「停滞检测 + 超时放行」兜底补丁 mod（附带战斗推进链诊断打点）。
///
/// 病灶：<c>PreloadManager</c> 的会话交给 <c>NAssetLoader</c> 后台排队处理；一旦某个会话
/// 永不完成（`_Process` 没被驱动，或某个资源在 Godot 线程加载器里永久停在 InProgress），
/// 所有 <c>await session.WaitForCompletion()</c> 的调用方就永久挂起。落在战斗开始阶段时表现为
/// 「不抽牌 / 不能换人 / 点结束回合无效」（战斗停在 NotPlayPhase）——
/// 2026-10-02 实机的触发源是第三方 GensokyoSpire（MomoLib）的 'MomoVfx' 预加载。
///
/// 补丁**逐个挂、失败只 WARN**（不走 PatchAll）：诊断/兜底这类"救火"补丁必须健壮，
/// 任何单个目标随游戏更新改名都不能把其余探测一起拖垮。
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    internal const string LogPrefix = "[PreloadStallGuard]";

    private static Harmony? _harmony;
    private static int _patched;
    private static int _failed;

    public static void Init()
    {
        try
        {
            _harmony = new Harmony("sts2.preloadstallguard");

            // ── 兜底主链路：入队即登记 ──
            PatchOne(
                AccessTools.Method(typeof(NAssetLoader), nameof(NAssetLoader.LoadInTheBackground)),
                "NAssetLoader.LoadInTheBackground",
                postfix: AccessTools.Method(typeof(PreloadStallGuardPatch), nameof(PreloadStallGuardPatch.Postfix)));

            // ── 诊断：会话生命周期（谁创建 / 谁在等 / 有没有被驱动） ──
            PatchOne(
                AccessTools.Method(typeof(AssetCache), nameof(AssetCache.CreateSession)),
                "AssetCache.CreateSession",
                postfix: AccessTools.Method(typeof(PreloadWaitProbe), "CreateSessionPostfix"));

            PatchOne(
                AccessTools.Method(typeof(AssetLoadingSession), nameof(AssetLoadingSession.WaitForCompletion)),
                "AssetLoadingSession.WaitForCompletion",
                prefix: AccessTools.Method(typeof(PreloadWaitProbe), "WaitForCompletionPrefix"));

            PatchOne(
                AccessTools.PropertyGetter(typeof(AssetLoadingSession), nameof(AssetLoadingSession.Task)),
                "AssetLoadingSession.get_Task",
                postfix: AccessTools.Method(typeof(PreloadWaitProbe), "TaskGetterPostfix"));

            PatchOne(
                AccessTools.Method(typeof(AssetLoadingSession), nameof(AssetLoadingSession.Process)),
                "AssetLoadingSession.Process",
                prefix: AccessTools.Method(typeof(PreloadWaitProbe), "ProcessPrefix"));

            // ── 诊断：战斗开始推进链打点 ──
            PatchOne(
                AccessTools.Method(typeof(Hook), nameof(Hook.BeforeCombatStart)),
                "Hook.BeforeCombatStart",
                prefix: AccessTools.Method(typeof(CombatFlowProbe), nameof(CombatFlowProbe.BeforeCombatStartPrefix)));

            PatchOne(
                AccessTools.Method(typeof(Hook), nameof(Hook.BeforeSideTurnStart)),
                "Hook.BeforeSideTurnStart",
                prefix: AccessTools.Method(typeof(CombatFlowProbe), nameof(CombatFlowProbe.BeforeSideTurnStartPrefix)));

            PatchOne(
                AccessTools.Method(typeof(Hook), nameof(Hook.AfterBlockCleared)),
                "Hook.AfterBlockCleared",
                prefix: AccessTools.Method(typeof(CombatFlowProbe), nameof(CombatFlowProbe.AfterBlockClearedPrefix)));

            PatchOne(
                AccessTools.Method(typeof(CombatManager), "StartTurn"),
                "CombatManager.StartTurn",
                prefix: AccessTools.Method(typeof(CombatFlowProbe), nameof(CombatFlowProbe.StartTurnPrefix)));

            PatchOne(
                AccessTools.Method(typeof(CombatManager), "RunAutoPrePlayPhase"),
                "CombatManager.RunAutoPrePlayPhase",
                prefix: AccessTools.Method(typeof(CombatFlowProbe), nameof(CombatFlowProbe.RunAutoPrePlayPhasePrefix)));

            Log.Info($"{LogPrefix} INIT_OK: patched={_patched}, failed={_failed}, "
                + $"probe={(PreloadStallInspector.ProbeAvailable ? "ok" : "degraded")}, "
                + $"timeoutMs={PreloadStallPolicy.DefaultStallTimeoutMs}, "
                + $"mode={(PreloadStallPolicy.ForceReleaseEnabled ? "force" : "report")}, "
                + $"diagnostics={(PreloadStallLog.DiagnosticsEnabled ? "on" : "off")}");
        }
        catch (Exception ex)
        {
            // 兜底补丁失败绝不能连累游戏启动：只记日志，不抛。
            Log.Error($"{LogPrefix} INIT_ERROR: {ex}");
        }
    }

    private static void PatchOne(
        MethodInfo? target,
        string label,
        MethodInfo? prefix = null,
        MethodInfo? postfix = null)
    {
        if (_harmony == null || target == null || (prefix == null && postfix == null))
        {
            _failed++;
            Log.Warn($"{LogPrefix} 目标或补丁方法缺失，跳过: {label}");
            return;
        }

        try
        {
            _harmony.Patch(
                target,
                prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                postfix: postfix != null ? new HarmonyMethod(postfix) : null);
            _patched++;
        }
        catch (Exception ex)
        {
            _failed++;
            Log.Warn($"{LogPrefix} 挂补丁失败: {label}: {ex.Message}");
        }
    }
}
