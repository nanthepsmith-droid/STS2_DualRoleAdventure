using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;

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

    /// <summary>基类 `HextechCreatureAttachedVisual` 的 <c>protected NCreature Creature</c>（点名用，缺了不影响守卫）。</summary>
    private static PropertyInfo? _creatureProperty;

    private static bool _warned;
    private static int _disabledCount;

    /// <summary>「点名」日志去重（每个视觉只打一条，最多 3 条）。</summary>
    private static readonly HashSet<ulong> LoggedVisuals = new();

    /// <summary>
    /// **默认强制矩形发射**（不给骨骼发射机会）。
    ///
    /// <para>为什么不再"检测失效再降级"（v1.4.0/1.4.1 的教训，2026-10-21 第二次实机仍 1fps）：
    /// 海克斯自己的有效性检查（比较 `SpineBody.BoundObject` 的 InstanceId）**恰好过不了这一关** ——
    /// 骨骼原生对象被就地释放（同一颗 SpineSprite 节点、id 不变）时，我们的 `IsInstanceValid`
    /// 与它的 id 比较**都为真**，而 `get_world_x` 已经读不到原生对象 ⇒ 只能每帧报错。
    /// 既然"读骨骼"这条路在可观测层面无法判定安全，就干脆不走它（它自带矩形发射，火焰照常显示）。</para>
    ///
    /// <para>想恢复海克斯原样（骨骼跟随）设环境变量 <c>HRLX_BURN_BONE_EMISSION=1</c>。</para>
    /// </summary>
    private static readonly bool ForceRectEmission =
        !string.Equals(System.Environment.GetEnvironmentVariable("HRLX_BURN_BONE_EMISSION"), "1", StringComparison.Ordinal);

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
        _creatureProperty = FindPropertyInHierarchy(burnVisualType, "Creature");

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

        if (ForceRectEmission)
        {
            // 强制矩形发射：连"重新抓骨骼"这一步也挡掉（否则它可能在 UpdateBoneEmissionPoints 尾巴里
            // 重新 init 一次、下一帧又被我们关掉，来回抖）。
            Patch(harmony, burnVisualType, "TryInitBoneEmission", 0, nameof(TryInitBoneEmissionPrefix), null, patched, missing);
            Log.Info($"{Entry.LogPrefix} 燃烧视觉：默认**强制矩形发射**（不读骨骼 ⇒ 不会再刷 native 错误把帧率打到 1）。"
                + "想恢复海克斯原样（火焰跟骨骼走）设环境变量 HRLX_BURN_BONE_EMISSION=1。");
        }
    }

    /// <summary>
    /// ① 每帧检查。
    /// <list type="bullet">
    /// <item><b>默认（强制矩形发射）</b>：只要发现骨骼缓存非空就关掉它，并给每个视觉只打一条"点名"日志；</item>
    /// <item><b>检测档</b>（`HRLX_BURN_BONE_EMISSION=1`）：只在 Spine 失效时才关（v1.4.0 行为，已知抓不到"原生对象被就地释放"）。</item>
    /// </list>
    /// </summary>
    private static void TickPrefix(object __instance)
    {
        if (!_resolved)
        {
            return;
        }

        try
        {
            if (ForceRectEmission)
            {
                if (HasBoneEmission(__instance))
                {
                    Disable(__instance);
                }

                LogOncePerVisual(__instance);
                return;
            }

            if (!HasBoneEmission(__instance) || IsSpineUsable(__instance))
            {
                return;
            }

            Disable(__instance);
            LogDisableOnce(__instance, "Tick");
        }
        catch (Exception exception)
        {
            WarnOnce($"燃烧视觉 Tick 守卫失败（放任原逻辑）: {exception.GetType().Name}: {exception.Message}");
        }
    }

    /// <summary>② 真到要读骨骼那一刻的最后一道闸：有任何可疑就不让原方法跑（`HRLX_BURN_BONE_EMISSION=1` 时也保留）。</summary>
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

            if (!ForceRectEmission && IsSpineUsable(__instance) && AreBonesUsable(__instance))
            {
                return true;
            }

            Disable(__instance);
            LogDisableOnce(__instance, "UpdateBoneEmissionPoints");
            return false;
        }
        catch (Exception exception)
        {
            // 守卫自己出错也绝不能放行到 native 读：直接跳过原方法（视觉退化为矩形发射）。
            WarnOnce($"燃烧视觉骨骼守卫失败（本次跳过骨骼发射点）: {exception.GetType().Name}: {exception.Message}");
            return false;
        }
    }

    /// <summary>③ 强制矩形发射时，连"重新抓骨骼"也不允许（防它自己在尾巴里 re-init 一次，下一帧再被关）。</summary>
    private static bool TryInitBoneEmissionPrefix(object __instance)
    {
        _ = __instance;
        return !(_resolved && ForceRectEmission);
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

    private static void Disable(object instance)
    {
        _disableBoneEmission!.Invoke(instance, null);
    }

    private static void LogDisableOnce(object instance, string source)
    {
        int count = Interlocked.Increment(ref _disabledCount);
        if (count > 3)
        {
            return;
        }

        Log.Info($"{Entry.LogPrefix} 海克斯燃烧视觉的 Spine 骨骼发射点已停用（源={source}，第 {count} 次）: "
            + $"对象={DescribeOwner(instance)} —— 该生物的 Spine 已失效，改走它自带的矩形发射"
            + "（火焰照常显示，但不再每帧刷 native 错误把帧率打到 1）。");
    }

    /// <summary>强制档下给**每个视觉**只打一条（最多 3 条），用来回答"这个燃烧挂在谁身上"。</summary>
    private static void LogOncePerVisual(object instance)
    {
        try
        {
            if (instance is not GodotObject godotObject)
            {
                return;
            }

            ulong id = godotObject.GetInstanceId();
            lock (LoggedVisuals)
            {
                if (!LoggedVisuals.Add(id) || LoggedVisuals.Count > 3)
                {
                    return;
                }
            }

            Log.Info($"{Entry.LogPrefix} 海克斯燃烧视觉改用矩形发射（不读骨骼，防 native 报错把帧率打到 1）: "
                + $"对象={DescribeOwner(instance)}");
        }
        catch
        {
            // 纯诊断，失败就算了
        }
    }

    /// <summary>
    /// 「点名」这个视觉挂在谁身上 —— 下次复发就能一句话定死是**我们的临时席位（战神）**、
    /// **Co-op Bots 合成席位**、真人、还是**怪物**（2026-10-21 用户要求补的探针）。
    /// 纯诊断：任何失败都只返回一段说明，不抛、不影响守卫。
    /// </summary>
    private static string DescribeOwner(object instance)
    {
        try
        {
            if (_creatureProperty == null)
            {
                return "未知（基类的 Creature 属性没解析到）";
            }

            if (_creatureProperty.GetValue(instance) is not NCreature creatureNode)
            {
                return "未知（Creature 属性为 null）";
            }

            if (!GodotObject.IsInstanceValid(creatureNode))
            {
                return "生物节点=已释放（IsInstanceValid=false）";
            }

            Creature? entity = creatureNode.Entity;
            if (entity == null)
            {
                return "生物实体=null（节点还在、数据层已摘）";
            }

            string modelId = SafeEntry(entity);
            if (entity.IsMonster)
            {
                return $"怪物={modelId}";
            }

            Player? player = entity.Player;
            if (player != null)
            {
                return $"席位={MainModBridge.DescribeSeat(player.NetId)}, 角色={modelId}";
            }

            return $"生物={modelId}（既非怪物也非玩家）";
        }
        catch (Exception exception)
        {
            return $"未知（读取失败: {exception.GetType().Name}: {exception.Message}）";
        }
    }

    private static string SafeEntry(Creature entity)
    {
        try
        {
            return entity.ModelId.Entry ?? entity.Name;
        }
        catch
        {
            try
            {
                return entity.Name;
            }
            catch
            {
                return "unknown";
            }
        }
    }

    private static PropertyInfo? FindPropertyInHierarchy(Type type, string name)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            PropertyInfo? property = current.GetProperty(
                name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (property != null)
            {
                return property;
            }
        }

        return null;
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
