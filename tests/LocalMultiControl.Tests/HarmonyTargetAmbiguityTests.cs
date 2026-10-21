#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 补丁目标解析的**构建期**哨兵（r292，2026-10-21 线上事故）。
///
/// 事故原形：r291 的 BGM 探针写了 `[HarmonyPatch(nameof(NRunMusicController.UpdateTrack))]`，
/// 而该方法有**两个重载**（`UpdateTrack()` 与 `UpdateTrack(string,float)`）⇒ Harmony 运行时
/// `AccessTools.DeclaredMethod` 抛 `AmbiguousMatchException` ⇒ **整个 Ui 补丁组被跳过**
/// （切人按钮 / 手牌 UI / 结束回合按钮全没挂），`INIT_OK` 却仍然是 OK。
///
/// 这条测试把同一件事提前到 `dotnet test`：凡是"只写方法名、没给 `Type[]`"的补丁目标，
/// 只要目标类型上存在同名重载就直接 FAIL。
/// </summary>
[TestFixture]
public class HarmonyTargetAmbiguityTests
{
    /// <summary>与 `AccessTools.allDeclared` 同口径（含 private / 静态 / 实例）。</summary>
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static
        | BindingFlags.DeclaredOnly;

    [Test]
    public void 所有补丁目标都能按名唯一解析()
    {
        Assembly assembly = typeof(LocalMultiControl.Scripts.Scripts.Entry).Assembly;
        List<string> problems = new();

        foreach (Type patchType in assembly.GetTypes().Where(type => type.IsClass))
        {
            HarmonyPatch? classAttr = patchType.GetCustomAttribute<HarmonyPatch>(inherit: false);
            if (classAttr == null)
            {
                continue;
            }

            Type? classTarget = classAttr.info?.declaringType;

            // ① 类级属性自带方法名（`[HarmonyPatch(typeof(X), nameof(X.M))]`）也要查。
            Check(classAttr.info, classTarget, $"{patchType.Name}（类级）", problems);

            // ② 方法级属性（`[HarmonyPatch(nameof(X.M))]` 写在补丁方法上）。
            foreach (MethodInfo patchMethod in patchType.GetMethods(AllDeclared))
            {
                foreach (HarmonyPatch attr in patchMethod.GetCustomAttributes<HarmonyPatch>(inherit: false))
                {
                    Check(attr.info, classTarget, $"{patchType.Name}.{patchMethod.Name}", problems);
                }
            }
        }

        Assert.That(
            problems,
            Is.Empty,
            "以下补丁目标按方法名解析会歧义（Harmony 抛 AmbiguousMatchException ⇒ 整个补丁组被跳过）。"
            + "修法 = 显式给参数类型，例如 `[HarmonyPatch(nameof(X.M), new Type[0])]`：\n  "
            + string.Join("\n  ", problems));
    }

    private static void Check(
        HarmonyMethod? info, Type? classTarget, string owner, List<string> problems)
    {
        if (info == null || string.IsNullOrEmpty(info.methodName))
        {
            return;
        }

        // 属性访问器 / 构造器 / 迭代器：按 MethodType 解析，不看重载。
        if (info.methodType is { } methodType && methodType != MethodType.Normal)
        {
            return;
        }

        // 已显式给参数类型（含无参的 `new Type[0]`）⇒ 不会歧义。
        if (info.argumentTypes != null)
        {
            return;
        }

        Type? target = info.declaringType ?? classTarget;
        if (target == null)
        {
            return;
        }

        List<MethodInfo> candidates = target.GetMethods(AllDeclared)
            .Where(method => string.Equals(method.Name, info.methodName, StringComparison.Ordinal))
            .ToList();

        if (candidates.Count > 1)
        {
            problems.Add(
                $"{owner} → {target.Name}.{info.methodName} 有 {candidates.Count} 个同名重载"
                + $"（{string.Join(" / ", candidates.Select(Format))}）");
        }
    }

    private static string Format(MethodInfo method)
        => method.Name + "(" + string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name)) + ")";
}
