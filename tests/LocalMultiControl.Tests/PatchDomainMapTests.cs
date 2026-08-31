using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Scripts;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 补丁分组完整性测试（维护性改进 2.3：PatchAll 分组隔离）。
///
/// 强制约束：所有 [HarmonyPatch] 补丁类都必须能在 <see cref="PatchDomainMap"/> 中解析到域
/// （顶层类直接登记，嵌套类继承容器）。防止新增/改名补丁漏登记导致分组逻辑失效。
/// </summary>
[TestFixture]
public class PatchDomainMapTests
{
    private static Assembly ModAssembly => typeof(Entry).Assembly;

    /// <summary>
    /// 元数据级判断是否带 [HarmonyPatch]：按 attribute 类型全名匹配，
    /// 不依赖程序集身份（测试上下文与主 DLL 的 0Harmony 加载身份可能不一致，
    /// 强类型 GetCustomAttribute&lt;HarmonyPatch&gt;() 会静默返回 null）。
    /// </summary>
    private static bool HasHarmonyPatchAttribute(Type type)
    {
        return type.GetCustomAttributesData()
            .Any(data => data.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
    }

    private static List<Type> CollectPatchTypes()
    {
        // 不排除 abstract：补丁类几乎都是静态类（编译为 abstract+sealed），与 Harmony PatchAll 口径一致。
        return ModAssembly.GetTypes()
            .Where(type => type.IsClass && HasHarmonyPatchAttribute(type))
            .ToList();
    }

    [Test]
    public void 所有补丁类都能解析到分组()
    {
        List<Type> patchTypes = CollectPatchTypes();
        Assert.That(patchTypes, Is.Not.Empty, "程序集中应存在带 [HarmonyPatch] 的补丁类");

        List<string> unresolved = patchTypes
            .Where(type => PatchDomainMap.ResolveFor(type) == null)
            .Select(type => type.FullName ?? type.Name)
            .ToList();

        Assert.That(unresolved, Is.Empty,
            $"以下补丁类未登记分组（应登记到 PatchDomainMap.ByTypeName，或确认容器类已登记）：{string.Join(", ", unresolved)}");
    }

    [Test]
    public void 顶层补丁类必须直接登记()
    {
        List<string> unregisteredTopLevel = CollectPatchTypes()
            .Where(type => type.DeclaringType == null && PatchDomainMap.ResolveFor(type) == null)
            .Select(type => type.Name)
            .ToList();

        Assert.That(unregisteredTopLevel, Is.Empty,
            $"以下顶层补丁类未直接登记到 PatchDomainMap.ByTypeName：{string.Join(", ", unregisteredTopLevel)}");
    }

    [Test]
    public void 映射表中所有条目都指向真实存在的补丁类()
    {
        HashSet<string> realPatchTypeNames = new(CollectPatchTypes().Select(type => type.Name));
        List<string> staleKeys = PatchDomainMap.ByTypeName.Keys
            .Where(name => !realPatchTypeNames.Contains(name))
            .ToList();

        Assert.That(staleKeys, Is.Empty,
            $"PatchDomainMap 中以下条目已无对应补丁类（类被删除/改名，应清理映射）：{string.Join(", ", staleKeys)}");
    }

    [Test]
    public void 应用顺序覆盖全部组且无重复()
    {
        PatchDomain[] order = PatchDomainMap.ApplyOrder;
        PatchDomain[] allDomains = Enum.GetValues<PatchDomain>();

        Assert.That(order.Length, Is.EqualTo(allDomains.Length),
            "ApplyOrder 必须恰好覆盖全部 7 个补丁域一次");
        Assert.That(order, Is.EquivalentTo(allDomains));
        Assert.That(order.GroupBy(domain => domain).Any(group => group.Count() > 1), Is.False, "ApplyOrder 不允许重复域");
    }

    [Test]
    public void 每组都有补丁类()
    {
        List<Type> patchTypes = CollectPatchTypes();
        List<string> emptyDomains = Enum.GetValues<PatchDomain>()
            .Where(domain => patchTypes.Count(type => PatchDomainMap.ResolveFor(type) == domain) == 0)
            .Select(domain => domain.ToString())
            .ToList();

        Assert.That(emptyDomains, Is.Empty,
            $"以下分组没有任何补丁类（映射可能错位）：{string.Join(", ", emptyDomains)}");
    }

    [Test]
    public void Core组应包含网络回环与选牌串行化等基座补丁()
    {
        // 关键基座类名抽查：防止 Core 组被误改导致"失败即停"失去意义。
        string[] mustBeCore =
        {
            "LocalPlayerLimitNetworkPatch",     // 网络回环
            "LocalMultiControlPatch",           // 运行主控
            "NPlayerHandSelectCardsSerializationPatch", // 选牌串行化
            "CardPileAddForegroundContextPinPatch",     // NetId 钉住
            "EventSynchronizerBeginEventPatch",         // 事件同步器
            "RewardsSetSynchronizerSelectLocalRewardPatch", // 奖励同步器
        };

        Dictionary<string, Type> byName = CollectPatchTypes()
            .GroupBy(type => type.Name)
            .ToDictionary(group => group.Key, group => group.First());

        List<string> missing = mustBeCore
            .Where(name => !byName.TryGetValue(name, out Type? patchType)
                           || PatchDomainMap.ResolveFor(patchType) != PatchDomain.Core)
            .ToList();

        Assert.That(missing, Is.Empty,
            $"以下基座补丁不在 Core 组（失败即停语义失效）：{string.Join(", ", missing)}");
    }
}
