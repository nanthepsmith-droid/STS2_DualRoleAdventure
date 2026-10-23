using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// r312 启动耗时：给游戏「逐个 mod 初始化」的那一步挂计时（<c>ModManager.CallModInitializer</c>）。
///
/// 目的：回答「进主菜单 ~120 秒里，别的 mod 的初始化占多少、哪一个最慢」。
/// 只记账、不干预：prefix/postfix 里只有 Stopwatch，任何异常都吞掉。
///
/// **刻意不给类加 <c>[HarmonyPatch]</c>**（与 PreloadStallGuard 同口径）：
/// 目标改名时手工挂载只打一条 WARN，不会被 <c>PatchAll</c> 分组拖成「Core 组失败即停」。
/// 代价：覆盖不到排在我们前面的那些 mod（它们的初始化已经跑完）—— 那一段由
/// <see cref="StartupProfileProbe"/> 的 since_process_start 整体兜住。
/// </summary>
internal static class StartupProfilerPatch
{
    private const string TargetTypeName = "MegaCrit.Sts2.Core.Modding.ModManager";
    private const string TargetMethodName = "CallModInitializer";

    private static long _stepStartTicks;
    private static int _count;

    internal static void Install(Harmony harmony)
    {
        try
        {
            MethodInfo? target = AccessTools.Method(typeof(ModManager), TargetMethodName);
            if (target == null)
            {
                LogWarn($"目标不存在，逐 mod 计时未启用: {TargetTypeName}.{TargetMethodName}");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfilerPatch), nameof(Prefix))),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(StartupProfilerPatch), nameof(Postfix))));
            LocalMultiControlLogger.Info("[StartupProf] 逐 mod 初始化计时已挂上（只统计排在我们之后的 mod）");
        }
        catch (Exception exception)
        {
            // 诊断补丁失败绝不能连累初始化
            LogWarn($"挂载失败（不影响加载）: {exception.Message}");
        }
    }

    private static void Prefix()
    {
        _stepStartTicks = Stopwatch.GetTimestamp();
    }

    private static void Postfix(Type initializerType)
    {
        try
        {
            double elapsedMs = (Stopwatch.GetTimestamp() - _stepStartTicks) * 1000.0 / Stopwatch.Frequency;
            int index = Interlocked.Increment(ref _count);
            LocalMultiControlLogger.Info(
                $"[StartupProf] other_mod_init index={index} ms={elapsedMs:F1} " +
                $"since_process_start_ms={StartupProfileProbe.SinceProcessStartMs():F0} mod={Describe(initializerType)}");
        }
        catch
        {
            // 记账失败一律吞掉：绝不能影响别的 mod 的初始化
        }
    }

    private static string Describe(Type initializerType)
    {
        try
        {
            string? assemblyName = initializerType.Assembly.GetName().Name;
            return string.IsNullOrEmpty(assemblyName)
                ? initializerType.Name
                : $"{assemblyName}/{initializerType.Name}";
        }
        catch
        {
            return "<unknown>";
        }
    }

    private static void LogWarn(string message)
    {
        LocalMultiControlLogger.Warn($"[StartupProf] {message}");
    }
}
