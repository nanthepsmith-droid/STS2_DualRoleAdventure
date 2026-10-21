using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// ⑨ 海克斯「燃烧（Burn）」视觉的 **Spine 骨骼发射点**兜底 —— 止损掉帧（2026-10-21 实机）。
///
/// <para><b>症状</b>：打到一半突然从 60fps 掉到 **1fps**，引擎 process ≈ 1500ms/帧；日志里每帧刷
/// <c>ERROR: Native Spine object not set. at SpineBone::get_world_x/y</c> +
/// <c>HextechBurnVisual.UpdateBoneEmissionPoints ← ApplyIntensity ← Tick</c>
/// （实测一份 7MB 日志里 2944 次）。</para>
///
/// <para><b>根因（反编译 `HextechRunes.HextechBurnVisual` 确认）</b>：它在 <c>Start()</c> 里抓一次
/// 该生物 Spine 骨骼（`_emissionBones` + `_spineSpriteNode`），之后每帧 <c>Tick() → ApplyIntensity →
/// UpdateBoneEmissionPoints</c> 逐根读 <c>get_world_x/y</c> 来定位火焰粒子。生物节点被摘掉 / Spine 被
/// 换掉之后这些骨骼的原生对象已释放，但它**没有重新校验**，于是每帧打印 native 错误
/// （Godot 的 ERROR 走同步 I/O ⇒ 直接把帧时间打爆）。</para>
///
/// <para><b>修法</b>：两层守卫，发现"Spine 或骨骼已失效"就调它自己的
/// <c>DisableBoneEmission()</c>（它自带矩形发射兜底）——**火焰照常显示、只是不再跟着骨骼走**：
/// ① <c>Tick</c> 前缀：每帧便宜地检查 `_spineSpriteNode` 是否还在树里；
/// ② <c>UpdateBoneEmissionPoints</c> 前缀：真到要读骨骼那一刻再逐个 <c>IsInstanceValid</c>，
/// 不合法就直接跳过原方法（这是"最后一米"，保证不会有任何一次 native 读）。</para>
///
/// <para>全程反射 + 失败只 WARN（成员缺失就整套不挂，绝不留半挂状态）。</para>
/// </summary>
internal static class HextechBurnVisualGuardPatch
{
    internal const string BurnVisualTypeName = "HextechRunes.HextechBurnVisual";

    private static bool _resolved;
    private static FieldInfo? _spineField;
    private static FieldInfo? _bonesField;
    private static MethodInfo? _disableBoneEmission;
    private static bool _warned;
    private static int _disabledCount;

    internal static void Install(
        Harmony harmony,
        Type burnVisualType,
        List<string> patched,
        List<string> missing)
    {
        _spineField = FindField(burnVisualType, "_spineSpriteNode");
        _bonesField = FindField(burnVisualType, "_emissionBones");
        _disableBoneEmission = burnVisualType.GetMethod(
            "DisableBoneEmission",
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        if (_spineField == null || _bonesField == null || _disableBoneEmission == null)
        {
            // **不做半挂**：守卫需要的三件缺一件就整套不挂（否则可能把本来正常的火焰关掉）。
            missing.Add($"{burnVisualType.Name}(Spine 骨骼发射点兜底)");
            Log.Warn($"{Entry.LogPrefix} 燃烧视觉的守卫成员缺失（海克斯可能已更新）⇒ 不挂该守卫: "
                + $"_spineSpriteNode={_spineField != null}, _emissionBones={_bonesField != null}, "
                + $"DisableBoneEmission={_disableBoneEmission != null}。"
                + "（原生 Spine 报错若复发，仍会把帧率打到很低 —— 请把日志带给维护者。）");
            return;
        }

        _resolved = true;
        Patch(harmony, burnVisualType, "Tick", 0, nameof(TickPrefix), null, patched, missing);
        Patch(harmony, burnVisualType, "UpdateBoneEmissionPoints", 0, nameof(UpdateBoneEmissionPointsPrefix), null, patched, missing);
    }

    /// <summary>① 每帧便宜检查：Spine 节点被摘掉就立刻切矩形发射（之后 Tick 里 `_emissionBones == null` 直接早退）。</summary>
    private static void TickPrefix(object __instance)
    {
        if (!_resolved)
        {
            return;
        }

        try
        {
            if (!HasBoneEmission(__instance) || IsSpineUsable(__instance))
            {
                return;
            }

            Disable(__instance, "Tick");
        }
        catch (Exception exception)
        {
            WarnOnce($"燃烧视觉 Tick 守卫失败（放任原逻辑）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>② 真要读骨骼之前再逐个校验；任何一个不合法就跳过原方法（保证一次 native 读都不发生）。</summary>
    private static bool UpdateBoneEmissionPointsPrefix(object __instance)
    {
        if (!_resolved)
        {
            return true;
        }

        try
        {
            if (!HasBoneEmission(__instance))
            {
                return true; // 本来就是矩形发射，原方法会自己早退
            }

            if (IsSpineUsable(__instance) && AreBonesUsable(__instance))
            {
                return true;
            }

            Disable(__instance, "UpdateBoneEmissionPoints");
            return false;
        }
        catch (Exception exception)
        {
            // 守卫自己出错也绝不能放行到 native 读：直接跳过原方法（视觉退化为矩形发射）。
            WarnOnce($"燃烧视觉骨骼守卫失败（本次跳过骨骼发射点）: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    private static bool HasBoneEmission(object instance)
    {
        return _bonesField!.GetValue(instance) is Array bones && bones.Length > 0;
    }

    private static bool IsSpineUsable(object instance)
    {
        object? spine = _spineField!.GetValue(instance);
        if (spine == null)
        {
            return false; // 有骨骼却没有 spine 节点 = 不一致，禁用更安全
        }

        if (spine is not GodotObject godotObject)
        {
            return true; // 不是 Godot 对象（理论上不会），不做判断
        }

        // 只判"原生对象还在不在"：**不要**额外要求 IsInsideTree（生物入场/换位的瞬间可能暂时不在树里，
        // 那种情况骨骼照样能读，误判会把本来正常的火焰永久降级成矩形发射）。
        return GodotObject.IsInstanceValid(godotObject) && !godotObject.IsQueuedForDeletion();
    }

    private static bool AreBonesUsable(object instance)
    {
        if (_bonesField!.GetValue(instance) is not Array bones)
        {
            return false;
        }

        foreach (object? bone in bones)
        {
            if (bone is not GodotObject godotObject
                || !GodotObject.IsInstanceValid(godotObject)
                || godotObject.IsQueuedForDeletion())
            {
                return false;
            }
        }

        return true;
    }

    private static void Disable(object instance, string source)
    {
        _disableBoneEmission!.Invoke(instance, null);
        int count = Interlocked.Increment(ref _disabledCount);
        if (count <= 3)
        {
            Log.Info($"{Entry.LogPrefix} 海克斯燃烧视觉的 Spine 骨骼发射点已停用（源={source}，第 {count} 次）："
                + "该生物的 Spine 已失效，改走它自带的矩形发射 —— 火焰照常显示，但不再每帧刷 native 错误把帧率打到 1。");
        }
    }

    private static void Patch(
        Harmony harmony,
        Type type,
        string methodName,
        int parameterCount,
        string prefix,
        string? postfix,
        List<string> patched,
        List<string> missing)
    {
        try
        {
            MethodInfo? target = null;
            foreach (MethodInfo candidate in type.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
            {
                if (candidate.Name == methodName && candidate.GetParameters().Length == parameterCount)
                {
                    target = candidate;
                    break;
                }
            }

            if (target == null)
            {
                missing.Add($"{type.Name}.{methodName}/{parameterCount}");
                Log.Warn($"{Entry.LogPrefix} 燃烧视觉守卫目标未找到（海克斯可能已更新），跳过: {type.Name}.{methodName}/{parameterCount}");
                return;
            }

            harmony.Patch(
                target,
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HextechBurnVisualGuardPatch), prefix)),
                postfix: postfix == null ? null : new HarmonyMethod(AccessTools.Method(typeof(HextechBurnVisualGuardPatch), postfix)));
            patched.Add($"{type.Name}.{methodName}");
        }
        catch (Exception exception)
        {
            missing.Add($"{type.Name}.{methodName}/{parameterCount}");
            Log.Warn($"{Entry.LogPrefix} 挂燃烧视觉守卫失败: {type.Name}.{methodName}/{parameterCount}: "
                + $"{exception.GetType().Name}: {exception.Message}");
        }
    }

    private static FieldInfo? FindField(Type type, string name)
    {
        try
        {
            return type.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch
        {
            return null;
        }
    }

    private static void WarnOnce(string message)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} {message}");
    }
}
