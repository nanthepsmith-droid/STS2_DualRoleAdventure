#nullable enable
using System;
using System.Diagnostics;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Runs;

namespace LocalMultiControl.Scripts.Patch;

// ============================================================
// 【BGM 探针】只读诊断（r291，2026-10-21 用户报「局内经常没有背景音乐」）。
//
// 纪律：**这一版只写日志、不改任何行为**（不拦截、不改参数、不补播）。
// 目的 = 把"到底谁要求播/停、选了哪首、bank 有没有加载成功、房间切到哪个 Progress 段"
// 变成可数事实，下一轮再谈修法（`todo.md` §待查-9）。
//
// 覆盖的原版链路（`sts2src` 反编译）：
// - `NRunMusicController.UpdateMusic`：进幕/换幕时解析本幕曲目（`ResolveMusic` 用 run 种子 + 本幕
//   `BgMusicOptions`）；**bank 加载失败 ⇒ 记 `_failedTrack` 且本幕不再重试 ⇒ 就是"没有 BGM"**，
//   这条是本次最想抓的静默失败。
// - `UpdateTrack`：房间 → FMOD `Progress` 段（战斗/商店/火堆/宝箱/精英…）。
// - `StopMusic` / `PlayCustomMusic` / `StopCustomMusic` / `UpdateCustomTrack`：
//   **第三方 mod 走的就是这几条**（本机 `IntegratedStrategyEvents` 自带战斗音乐、`Oddmelt` 自带 BGM）。
// - `NAudioManager.PlayMusic/StopMusic`：主菜单与 game_over 走的入口；`_audioNode == null` 时调用会被**静默丢弃**。
//
// 关闭方式：环境变量 `LMC_BGM_PROBE=0`（默认开）。日志前缀统一 `[BGM探针]`。
//
// ⚠ **结论（2026-10-21，r292 首跑）**：局内没 BGM = **第三方幕的 `BgMusicOptions` 为空**
//   （实测 `ARKNIGHTS_BOSS_ACT_GARDEN_OF_GROTESQUERIES_ACT1/2`）⇒ 原版 `ResolveMusic` 返回 null ⇒ 整幕静音；
//   主菜单走 `NAudioManager.PlayMusic` 所以有声音。**用户拍板：纯第三方缺内容，主 mod 不修、也不做兼容补丁**
//   （2026-10-21）⇒ r293 加的"原版第一幕兜底"已按此**移除**（r294）。本文件保留**只读**探针，
//   只用于下次遇到别的幕/别的 mod 时能立刻看出是不是同一个原因。
// ============================================================

/// <summary>探针的开关与输出（两处补丁共用；本身不持有任何游戏状态）。</summary>
internal static class BgmProbe
{
    private static bool _installedLogged;

    /// <summary>默认开；`LMC_BGM_PROBE=0` 可关（关掉后连"是否就绪"都不打）。</summary>
    internal static bool Enabled { get; } =
        !string.Equals(Environment.GetEnvironmentVariable("LMC_BGM_PROBE"), "0", StringComparison.Ordinal);

    internal static void Info(string message)
    {
        if (!Enabled)
        {
            return;
        }

        if (!_installedLogged)
        {
            _installedLogged = true;
            LocalMultiControlLogger.Info(
                "BGM 探针已就绪（只读，不改行为；关掉：环境变量 LMC_BGM_PROBE=0）—— 记 UpdateMusic/UpdateTrack/"
                + "StopMusic/自定义曲目/Audio.PlayMusic 的调用与 bank 加载成败");
        }

        LocalMultiControlLogger.Info($"[BGM探针] {message}");
    }

    internal static void Warn(string message)
    {
        if (!Enabled)
        {
            return;
        }

        LocalMultiControlLogger.Warn($"[BGM探针] {message}");
    }

    /// <summary>调用方提示（跳过 Harmony/本仓帧）：定位"谁把音乐停了"。</summary>
    internal static string CallerHint()
    {
        try
        {
            StackTrace trace = new(2, false);
            int shown = 0;
            string result = string.Empty;
            foreach (StackFrame frame in trace.GetFrames() ?? Array.Empty<StackFrame>())
            {
                MethodBase? method = frame.GetMethod();
                string? type = method?.DeclaringType?.Name;
                if (type == null || type.Contains("Harmony", StringComparison.Ordinal)
                    || type.Contains("BgmProbe", StringComparison.Ordinal))
                {
                    continue;
                }

                result += (result.Length == 0 ? string.Empty : " <- ") + type + "." + method!.Name;
                if (++shown >= 3)
                {
                    break;
                }
            }

            return result.Length == 0 ? "?" : result;
        }
        catch (Exception)
        {
            return "?";
        }
    }
}

/// <summary>`NRunMusicController`（局内音乐）的只读探针。</summary>
[HarmonyPatch(typeof(NRunMusicController))]
internal static class BgmProbeRunMusicPatch
{
    private static readonly FieldInfo? CurrentTrackField =
        AccessTools.Field(typeof(NRunMusicController), "_currentTrack");

    private static readonly FieldInfo? FailedTrackField =
        AccessTools.Field(typeof(NRunMusicController), "_failedTrack");

    private static readonly FieldInfo? RunStateField =
        AccessTools.Field(typeof(NRunMusicController), "_runState");

    // ⚠ r292：`NRunMusicController.UpdateTrack` 有**两个重载**（无参 public + `(string,float)` private），
    //   只写方法名会让 Harmony 抛 `AmbiguousMatchException` ⇒ **整个 Ui 补丁组被跳过**（r291 线上事故）。
    //   本项目口径：**同名重载一律显式给 `Type[]`**（无参也写成 `new Type[0]`）。
    [HarmonyPostfix]
    [HarmonyPatch(nameof(NRunMusicController.UpdateMusic), new Type[0])]
    private static void UpdateMusicPostfix(NRunMusicController __instance)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            string current = ReadString(CurrentTrackField, __instance);
            string failed = ReadString(FailedTrackField, __instance);
            string actInfo = "act=?, 候选=?";
            string selection = "?";

            try
            {
                if (RunStateField?.GetValue(__instance) is IRunState state)
                {
                    var act = state.Act;
                    actInfo = $"act=index{act?.Index}, 候选={act?.BgMusicOptions?.Length ?? 0}";
                    NRunMusicController.MusicSelection? resolved = NRunMusicController.ResolveMusic(
                        current == "none" ? null : current,
                        act!.BgMusicOptions,
                        act.MusicBankPaths,
                        state.Rng.Seed);
                    selection = resolved?.Track ?? "none（与当前相同/本幕无曲目）";
                }
            }
            catch (Exception)
            {
                // 拿不到 run 状态（菜单/退局）⇒ 只打能拿到的部分
            }

            BgmProbe.Info(
                $"UpdateMusic: {actInfo}, 选中={selection}, 当前={current}, 失败={failed}, "
                + $"非交互={NonInteractiveMode.IsActive}");

            if (failed != "none")
            {
                BgmProbe.Warn(
                    $"⚠ 本幕 music bank 加载失败 ⇒ 原版记 `_failedTrack` 后**不再重试**，本幕就是没有 BGM: "
                    + $"track={failed}, {actInfo}（若是第三方幕：查它的 .bank 是否随 mod 分发）");
            }
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"UpdateMusic 探针异常（已忽略）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NRunMusicController.UpdateTrack), new Type[0])]
    private static void UpdateTrackPostfix(NRunMusicController __instance)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            string room = "?";
            if (RunStateField?.GetValue(__instance) is IRunState state)
            {
                room = state.CurrentRoom?.RoomType.ToString() ?? "null";
            }

            BgmProbe.Info(
                $"UpdateTrack(房间→Progress 段): room={room}, 当前={ReadString(CurrentTrackField, __instance)}, "
                + $"失败={ReadString(FailedTrackField, __instance)}");
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"UpdateTrack 探针异常（已忽略）: {exception.Message}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NRunMusicController.StopMusic), new Type[0])]
    private static void StopMusicPostfix(NRunMusicController __instance)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            BgmProbe.Info(
                $"StopMusic: 停前当前={ReadString(CurrentTrackField, __instance)}, 调用方={BgmProbe.CallerHint()}");
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"StopMusic 探针异常（已忽略）: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NRunMusicController.PlayCustomMusic), new[] { typeof(string) })]
    private static void PlayCustomMusicPrefix(string customMusic)
        => BgmProbe.Info($"PlayCustomMusic（第三方自定义曲目）: track={customMusic}, 调用方={BgmProbe.CallerHint()}");

    [HarmonyPostfix]
    [HarmonyPatch(nameof(NRunMusicController.StopCustomMusic), new Type[0])]
    private static void StopCustomMusicPostfix(NRunMusicController __instance)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            BgmProbe.Info(
                $"StopCustomMusic: 恢复当前={ReadString(CurrentTrackField, __instance)}, 调用方={BgmProbe.CallerHint()}");
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"StopCustomMusic 探针异常（已忽略）: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NRunMusicController.UpdateCustomTrack), new[] { typeof(string), typeof(float) })]
    private static void UpdateCustomTrackPrefix(string customTrack, float label)
        => BgmProbe.Info($"UpdateCustomTrack: track={customTrack}, label={label}, 调用方={BgmProbe.CallerHint()}");

    private static string ReadString(FieldInfo? field, object instance)
    {
        try
        {
            return field?.GetValue(instance) as string ?? "none";
        }
        catch (Exception)
        {
            return "?";
        }
    }
}

/// <summary>`NAudioManager`（菜单 / game_over 音乐）的只读探针：重点是 `_audioNode` 是否为空。</summary>
[HarmonyPatch(typeof(NAudioManager))]
internal static class BgmProbeAudioManagerPatch
{
    private static readonly FieldInfo? AudioNodeField =
        AccessTools.Field(typeof(NAudioManager), "_audioNode");

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NAudioManager.PlayMusic), new[] { typeof(string) })]
    private static void PlayMusicPrefix(NAudioManager __instance, string music)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            BgmProbe.Info(
                $"Audio.PlayMusic: event={music}, audioNode={NodeState(__instance)}, 调用方={BgmProbe.CallerHint()}");
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"Audio.PlayMusic 探针异常（已忽略）: {exception.Message}");
        }
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(NAudioManager.StopMusic), new Type[0])]
    private static void StopMusicPrefix(NAudioManager __instance)
    {
        if (!BgmProbe.Enabled)
        {
            return;
        }

        try
        {
            BgmProbe.Info($"Audio.StopMusic: audioNode={NodeState(__instance)}, 调用方={BgmProbe.CallerHint()}");
        }
        catch (Exception exception)
        {
            BgmProbe.Warn($"Audio.StopMusic 探针异常（已忽略）: {exception.Message}");
        }
    }

    /// <summary>null = 还没 `_Ready`（此时 PlayMusic 会被**静默丢弃**，是"点了没声"的一类根因）。</summary>
    private static string NodeState(NAudioManager instance)
    {
        try
        {
            return AudioNodeField?.GetValue(instance) != null ? "ok" : "null（本次播放会被静默丢弃）";
        }
        catch (Exception)
        {
            return "?";
        }
    }
}
