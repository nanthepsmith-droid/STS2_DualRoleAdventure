#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 补丁目标**能否真的解析到**的构建期哨兵（r402，2026-11-25 游戏 0.114 适配事故）。
///
/// <para><b>事故原形</b>：0.114 把三处签名改了，编译期完全看不出来（`nameof` 只保证方法名），
/// 但 Harmony 运行时会整类跳过，日志只有一行 `补丁类应用失败 ⇒ HarmonyException`：</para>
/// <list type="bullet">
/// <item>`NCharacterSelectScreen.PlayerChanged(player, bool)` 少了第二个参数 ⇒ 我们的后缀声明了
///   `bool isRandomCharacterResolution` ⇒ `<c>Parameter "isRandomCharacterResolution" not found</c>`。</item>
/// <item>`VfxCmd.PlayOnCreature/Center(Creature, string, bool forceIfDead = false)` 多了可选参数 ⇒
///   `Type[]` 是**精确签名匹配**（可选参数不生成重载）⇒ 解析到 null。</item>
/// <item>`CardSelectCmd.FromChooseABundleScreen(player, bundles, bool canSkip = false)` 同理。</item>
/// </list>
///
/// <para><b>本测试做两件事</b>：① 每条 `[HarmonyPatch]` 的"类型 + 方法名 + Type[]"必须能在**真实游戏程序集**
/// 里解析出唯一方法（属性 getter / 构造器分别走各自的 AccessTools）；② 补丁方法的每个参数名必须能对上
/// 目标方法的参数名（或以 `__` / `___` 开头的 Harmony 特殊参数），否则运行时就是"参数找不到"。</para>
///
/// <para>⚠ 用 `TargetMethod()` 自己算目标的补丁（第三方反射目标，如 Koishi / NinjaSlayer）没有声明式信息，
/// 这里跳过 —— 那类只能靠实机日志。</para>
/// </summary>
[TestFixture]
public class HarmonyTargetResolvableTests
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    [Test]
    public void 所有补丁目标都能在真实游戏程序集里解析到()
    {
        Assembly assembly = typeof(LocalMultiControl.Scripts.Scripts.Entry).Assembly;
        List<string> problems = new();

        foreach (Type patchType in assembly.GetTypes().Where(type => type.IsClass))
        {
            HarmonyPatch? classAttr = patchType.GetCustomAttribute<HarmonyPatch>(inherit: false);
            HarmonyMethod? classInfo = classAttr?.info;
            if (classAttr != null)
            {
                Check(patchType, patchType.Name + "（类级）", classInfo, classInfo, null, problems);
            }

            foreach (MethodInfo patchMethod in patchType.GetMethods(AllDeclared))
            {
                foreach (HarmonyPatch attr in patchMethod.GetCustomAttributes<HarmonyPatch>(inherit: false))
                {
                    Check(patchType, patchType.Name + "." + patchMethod.Name, attr.info, classInfo, patchMethod, problems);
                }
            }
        }

        Assert.That(
            problems,
            Is.Empty,
            "以下补丁目标在游戏程序集里解析不到 / 参数对不上（Harmony 会**整类跳过**，日志只有一行「补丁类应用失败」）。"
            + "修法 = 按新源码改 `Type[]` 或补丁方法参数名：\n  " + string.Join("\n  ", problems));
    }

    private static void Check(
        Type patchType,
        string owner,
        HarmonyMethod? info,
        HarmonyMethod? classInfo,
        MethodInfo? patchMethod,
        List<string> problems)
    {
        if (info == null)
        {
            // TargetMethod() 自算目标（第三方反射目标）——没有声明式信息，跳过。
            return;
        }

        // 类级 `[HarmonyPatch(typeof(X))]` 只声明目标类型（方法名/构造器由方法级属性给）⇒ 这里没有可解析目标。
        if (info.methodName == null
            && info.methodType is not (MethodType.Constructor or MethodType.StaticConstructor))
        {
            return;
        }

        Type? target = info.declaringType ?? classInfo?.declaringType;
        if (target == null)
        {
            return;
        }

        MethodBase? resolved = Resolve(info, target);
        if (resolved == null)
        {
            problems.Add(
                owner + " → " + target.Name + "." + (info.methodName ?? "<ctor>")
                + "(" + DescribeTypes(info.argumentTypes) + ") 解析不到"
                + DescribeCandidates(info, target));
            return;
        }

        if (patchMethod == null)
        {
            return;
        }

        HashSet<string> targetParamNames = resolved.GetParameters()
            .Select(parameter => parameter.Name ?? string.Empty)
            .Where(name => name.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        foreach (ParameterInfo patchParam in patchMethod.GetParameters())
        {
            string? name = patchParam.Name;
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Harmony 特殊参数（__instance / __result / __state / __exception / ___field …）。
            if (name.StartsWith("__", StringComparison.Ordinal))
            {
                continue;
            }

            if (!targetParamNames.Contains(name))
            {
                problems.Add(
                    owner + " → " + target.Name + "." + resolved.Name
                    + "：补丁参数 `" + name + "` 在目标方法里不存在（可用参数："
                    + string.Join(", ", targetParamNames) + "）");
            }
        }
    }

    private static MethodBase? Resolve(HarmonyMethod info, Type target)
    {
        string? name = info.methodName;
        Type[]? args = info.argumentTypes;

        try
        {
            switch (info.methodType)
            {
                case MethodType.Getter:
                case MethodType.Setter:
                    return AccessTools.DeclaredProperty(target, name) != null
                        ? AccessTools.DeclaredProperty(target, name)!.GetMethod ?? AccessTools.DeclaredMethod(target, "get_" + name)
                        : null;

                case MethodType.Constructor:
                    return AccessTools.DeclaredConstructor(target, args ?? Type.EmptyTypes);

                case MethodType.StaticConstructor:
                    return AccessTools.DeclaredConstructor(target, Type.EmptyTypes, searchForStatic: true);

                default:
                    if (name == null)
                    {
                        return null;
                    }

                    if (args != null)
                    {
                        return AccessTools.Method(target, name, args);
                    }

                    List<MethodInfo> candidates = target.GetMethods(AllDeclared)
                        .Where(method => string.Equals(method.Name, name, StringComparison.Ordinal))
                        .ToList();
                    return candidates.Count == 1 ? candidates[0] : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string DescribeTypes(Type[]? types)
        => types == null ? "未给 Type[]" : string.Join(", ", types.Select(type => type.Name));

    private static string DescribeCandidates(HarmonyMethod info, Type target)
    {
        if (info.methodName == null)
        {
            return string.Empty;
        }

        List<string> candidates = target.GetMethods(AllDeclared)
            .Where(method => string.Equals(method.Name, info.methodName, StringComparison.Ordinal))
            .Select(method => method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name)) + ")")
            .ToList();

        return candidates.Count == 0
            ? "（目标类型上已无此方法名）"
            : "（现有同名重载：" + string.Join(" / ", candidates) + "）";
    }
}
