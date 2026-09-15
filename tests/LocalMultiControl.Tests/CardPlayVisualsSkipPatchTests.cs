using System;
using System.Linq;
using System.Reflection;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 「队列路径出牌加速」补丁的锚点哨兵（改进-2 / 方案 D 第三步）。
///
/// 这个补丁的全部价值都建立在两件事上，且**两件都不会因为编译失败而暴露**：
///   1. 补丁类必须带**类级** `[HarmonyPatch]`（本项目坑 1：只写在方法上 = 整个类被 PatchAll 静默跳过）；
///   2. 前缀参数必须叫 `skipCardPileVisuals` 且为 `ref bool` —— Harmony 按**参数名**绑定原方法形参，
///      改名或改成取值传递都会静默失效（补丁挂上了，但参数永远改不掉）。
/// 另外它必须登记到 `PatchDomainMap`（分组隔离），目标需在启动自检清单里（缺失至少能 WARN）。
///
/// 所以用反射把这几条钉死：任何一条被改动，单测先红，不必等到实机发现"加速没生效"。
/// </summary>
[TestFixture]
public class CardPlayVisualsSkipPatchTests
{
    private const string PatchClassName = "CardPlayVisualsSkipPatch";

    private static Type PatchType =>
        typeof(Entry).Assembly.GetType($"LocalMultiControl.Scripts.Patch.{PatchClassName}", throwOnError: true)!;

    private static MethodInfo PrefixMethod =>
        PatchType.GetMethod("Prefix", BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("补丁类缺少私有静态 Prefix 方法");

    private static CustomAttributeData? FindAttribute(MemberInfo member, string attributeFullName)
    {
        return member.GetCustomAttributesData()
            .FirstOrDefault(data => data.AttributeType.FullName == attributeFullName);
    }

    [Test]
    public void 补丁类必须有类级HarmonyPatch()
    {
        Assert.That(FindAttribute(PatchType, "HarmonyLib.HarmonyPatch"), Is.Not.Null,
            "坑 1：类级 [HarmonyPatch] 缺失 → 整个补丁类被 PatchAll 静默跳过（补丁永不触发且无报错）");
    }

    [Test]
    public void 前缀必须是ref跳过卡牌堆演出()
    {
        MethodInfo prefix = PrefixMethod;
        Assert.That(prefix.ReturnType, Is.EqualTo(typeof(void)),
            "前缀必须返回 void；返回 bool 会变成'拦截原实现'（本项目禁用 ref __runOriginal 那套）");

        ParameterInfo? argument = prefix.GetParameters()
            .FirstOrDefault(parameter => parameter.Name == "skipCardPileVisuals");
        Assert.That(argument, Is.Not.Null,
            "前缀必须用参数名 skipCardPileVisuals 绑定原方法形参（Harmony 按名绑定，改名即静默失效）");
        Assert.That(argument!.ParameterType.IsByRef, Is.True,
            "必须是 ref 才能改写原方法形参（取值传递改不动）");
        Assert.That(argument.ParameterType.GetElementType()!.Name, Is.EqualTo("Boolean"));
    }

    [Test]
    public void 前缀必须能拿到出牌实例()
    {
        ParameterInfo? instance = PrefixMethod.GetParameters()
            .FirstOrDefault(parameter => parameter.Name == "__instance");
        Assert.That(instance, Is.Not.Null, "需要 __instance 才能按牌主人（瓦库形态）判定");
        Assert.That(instance!.ParameterType.Name, Is.EqualTo("CardModel"));
    }

    [Test]
    public void 目标方法必须是OnPlayWrapper而不是别的入口()
    {
        CustomAttributeData? attribute = FindAttribute(PatchType, "HarmonyLib.HarmonyPatch");
        Assert.That(attribute, Is.Not.Null);

        // 类级写法 [HarmonyPatch(typeof(CardModel), nameof(CardModel.OnPlayWrapper))]：
        // 构造参数 = [类型, 方法名]。只校验方法名，避免在测试里强行解析游戏类型。
        Assert.That(attribute!.ConstructorArguments.Any(argument => Equals(argument.Value, "OnPlayWrapper")),
            Is.True, "锚点必须钉在 CardModel.OnPlayWrapper（队列路径唯一的公共下游）");
    }

    [Test]
    public void 补丁类必须登记到Combat分组()
    {
        Assert.That(PatchDomainMap.ResolveFor(PatchType), Is.EqualTo(PatchDomain.Combat),
            "未登记分组会走'隔离组'兜底并打 WARN，且失去分组故障隔离");
    }

    [Test]
    public void 启动自检清单必须包含OnPlayWrapper()
    {
        Assert.That(Entry.OptionalPatchTargets.Contains("MegaCrit.Sts2.Core.Models.CardModel.OnPlayWrapper"),
            Is.True, "缺了只是少一份提速，所以放 Optional；但必须登记，否则游戏更新改名后无人告警");
    }

    [Test]
    public void 不允许出现runOriginal()
    {
        // 本项目坑 2：本游戏内置 Harmony 2.4.2.0 上 ref bool __runOriginal 会生成 InvalidProgramException，
        // 导致 PatchAll 整体抛异常、整个 mod 初始化崩溃。前缀只能靠"返回 void + 改参数"。
        Assert.That(PrefixMethod.GetParameters().Any(parameter => parameter.Name == "__runOriginal"), Is.False);
        Assert.That(PrefixMethod.GetParameters().Any(parameter => parameter.Name == "__result"), Is.False);
    }
}
