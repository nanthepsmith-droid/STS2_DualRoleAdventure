using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Runtime;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 个人偏好记录器「什么算真人决策」的三条不变式（r129/r130）：
/// ① **删牌语义**只认 <c>TO_REMOVE</c> —— 复制 / 变化 / 升级与删牌共用 <c>FromDeckGeneric</c>，不能误记；
/// ② **自动化作用域按归属者比较** —— 只看"作用域非空"会把真人自己的操作一起吞掉
///    （2026-09-13 实机 marker r129：真人连删 2 张同名牌全被 `跳过: 瓦库商店自动采购作用域内` 吞掉；
///    r130 更早一层的原因是 `PurchaseOwnerId` 曾以 **AsyncLocal 字段**参与比较，恒 false）；
/// ③ **锚点**留在 <c>CardSelectCmd.FromDeckGeneric</c> 而不是入口 <c>FromDeckForRemoval</c>：
///    后者只有 4 行、是纯包装方法，有被 JIT 内联的风险（实机曾一整局 0 条记录）。
///    这条测试防止将来又被"顺手挪回小包装方法"。
/// </summary>
[TestFixture]
public class WakuuRecordScopePolicyTests
{
    [Test]
    public void TO_REMOVE_判为删牌()
    {
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("TO_REMOVE"), Is.True);
    }

    [Test]
    public void 复制变化升级语义均判否()
    {
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("TO_TRANSFORM"), Is.False);
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("TO_UPGRADE"), Is.False);
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt(string.Empty), Is.False);
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt(null), Is.False);
    }

    [Test]
    public void 手牌消耗与弃牌语义不算删牌()
    {
        // TO_EXHAUST / TO_DISCARD 是**手牌**语义；即便将来有人把它们接到牌组通用入口，也不该记成删牌。
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("TO_EXHAUST"), Is.False);
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("TO_DISCARD"), Is.False);
    }

    [Test]
    public void 大小写不同不判为删牌()
    {
        Assert.That(WakuuRecordScopePolicy.IsDeckRemovalPrompt("to_remove"), Is.False);
    }

    [Test]
    public void 作用域归属者就是本人_判为自动化()
    {
        Assert.That(WakuuRecordScopePolicy.IsAutoScopeOwnedBy(100UL, 100UL), Is.True);
    }

    [Test]
    public void 作用域归属者是别人_不算本人自动化()
    {
        // 这条就是 r129 实机 bug 的回归用例：别的角色在自动采购，真人自己的操作仍应记录。
        Assert.That(WakuuRecordScopePolicy.IsAutoScopeOwnedBy(100UL, 200UL), Is.False);
    }

    [Test]
    public void 没有作用域在跑_不算自动化()
    {
        Assert.That(WakuuRecordScopePolicy.IsAutoScopeOwnedBy(null, 0UL), Is.False);
        Assert.That(WakuuRecordScopePolicy.IsAutoScopeOwnedBy(null, 12345UL), Is.False);
    }

    [Test]
    public void 删牌记录锚点必须挂在_FromDeckGeneric()
    {
        // 与 PatchDomainMapTests 同口径：按 attribute 类型全名取，避免测试上下文里 0Harmony 的身份不一致。
        List<CustomAttributeData> patches = typeof(PersonalDeckRemovalPatch)
            .GetCustomAttributesData()
            .Where(data => data.AttributeType.FullName == "HarmonyLib.HarmonyPatch")
            .ToList();
        Assert.That(patches, Is.Not.Empty,
            "PersonalDeckRemovalPatch 缺少类级 [HarmonyPatch]（PatchAll 会静默跳过整个类）。");

        // [HarmonyPatch(Type, string methodName, Type[] argumentTypes)] → 第 2 个构造参数是方法名。
        List<string> methodNames = patches
            .Select(data => data.ConstructorArguments.Count > 1 ? data.ConstructorArguments[1].Value as string : null)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .ToList();
        Assert.That(methodNames, Does.Contain("FromDeckGeneric"),
            "删牌记录必须挂在 FromDeckGeneric 上：挂 FromDeckForRemoval 会被 JIT 内联，实机永远记不到。");
    }
}
