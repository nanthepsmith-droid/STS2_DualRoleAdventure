using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using LocalMultiControl.Scripts.Runtime;
using LocalMultiControl.Scripts.Scripts;
using MegaCrit.Sts2.Core.Commands;
using NUnit.Framework;

namespace LocalMultiControl.Tests;

/// <summary>
/// 选牌入口归属路由自检测试（改进-2 / Phase 1）。
///
/// 关键守卫：**用游戏程序集元数据枚举 CardSelectCmd 的全部 From* 入口**，
/// 断言没有「未分类的新入口」。游戏更新新增选牌入口时这里会先红
/// （与 ExpectedPatchTargetsTests 同一套思路：写错 / 游戏更新时单测先现形）。
/// </summary>
[TestFixture]
public class WakuuSelectorRouteAuditTests
{
    [Test]
    public void 分类_已接入归属者的入口()
    {
        foreach (string name in new[]
                 {
                     "FromHand", "FromHandForDiscard", "FromHandForUpgrade",
                     "FromSimpleGrid", "FromSimpleGridForRewards",
                     "FromChooseACardScreen", "FromCombatPile",
                 })
        {
            Assert.That(WakuuSelectorRouteAudit.Classify(name), Is.EqualTo(WakuuSelectorRouteAudit.OwnerAware), name);
        }
    }

    [Test]
    public void 分类_已知但退回栈顶语义的入口()
    {
        foreach (string name in new[]
                 {
                     "FromDeckForUpgrade", "FromDeckForTransformation",
                     "FromDeckForEnchantment", "FromDeckForRemoval", "FromDeckGeneric",
                 })
        {
            Assert.That(WakuuSelectorRouteAudit.Classify(name), Is.EqualTo(WakuuSelectorRouteAudit.LegacyFallback), name);
        }
    }

    /// <summary>
    /// **r154（BUG-19）哨兵**：宣称「已接入归属者」的入口，必须在
    /// <c>CardSelectForegroundSwitchPatch</c> 里**真的**有一个 `[HarmonyPatch(typeof(CardSelectCmd), "该入口名")]` 前缀。
    ///
    /// 起因：`FromSimpleGridForRewards` 曾被单方面登记进分类清单却**没有**任何前缀补丁 —— 分类说"已接入"、
    /// 实际归属者永远为空 ⇒ 守卫落回「栈顶语义」⇒ 真人打「类猪体」时被瓦库的选择器替他把牌选掉。
    /// 这条哨兵就是防这种"清单与补丁各说各话"（补丁漏挂不会编译失败，只会在实机静默出错）。
    /// </summary>
    [Test]
    public void 归属者清单必须与补丁前缀一一对应()
    {
        Type patchType = typeof(Entry).Assembly.GetType(
            "LocalMultiControl.Scripts.Patch.CardSelectForegroundSwitchPatch", throwOnError: true)!;

        List<(string Entry, string Patch)> patched = new();
        foreach (MethodInfo method in patchType.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
        {
            CustomAttributeData? attribute = method.GetCustomAttributesData()
                .FirstOrDefault(data => data.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            if (attribute == null)
            {
                continue;
            }

            bool targetsCardSelectCmd = attribute.ConstructorArguments
                .Any(argument => (argument.Value as Type) == typeof(CardSelectCmd));
            string? methodName = attribute.ConstructorArguments
                .Select(argument => argument.Value as string)
                .FirstOrDefault(value => !string.IsNullOrEmpty(value));
            if (targetsCardSelectCmd && methodName != null)
            {
                patched.Add((methodName, method.Name));
            }
        }

        Assert.That(patched, Is.Not.Empty, "没能从 CardSelectForegroundSwitchPatch 上读到任何 [HarmonyPatch] 前缀");

        // ① 每个自称 ownerAware 的入口，都必须真的有前缀补丁
        foreach (string entry in WakuuSelectorRouteAudit.EnumerateEntries())
        {
            if (WakuuSelectorRouteAudit.Classify(entry) != WakuuSelectorRouteAudit.OwnerAware)
            {
                continue;
            }

            Assert.That(
                patched.Any(item => item.Entry == entry),
                Is.True,
                $"入口 {entry} 被分类为 ownerAware，但 CardSelectForegroundSwitchPatch 里没有它的 [HarmonyPatch] 前缀"
                + "（归属者永远不会被写入 ⇒ 会退回栈顶语义被别人抢答）");
        }

        // ② 反过来：每个前缀补丁的目标入口，都必须被登记为 ownerAware（避免"补了但没登记"）
        foreach ((string entry, string patch) in patched)
        {
            Assert.That(
                WakuuSelectorRouteAudit.Classify(entry),
                Is.EqualTo(WakuuSelectorRouteAudit.OwnerAware),
                $"补丁 {patch} 已经为入口 {entry} 写归属者，但它没有被登记为 ownerAware");
        }
    }

    [Test]
    public void 分类_不读选择器的入口()
    {
        Assert.That(
            WakuuSelectorRouteAudit.Classify("FromChooseABundleScreen"),
            Is.EqualTo(WakuuSelectorRouteAudit.Ignored));
    }

    [Test]
    public void 分类_未知名字一律落入unknown()
    {
        Assert.That(WakuuSelectorRouteAudit.Classify("FromBrandNewThing"), Is.EqualTo(WakuuSelectorRouteAudit.Unknown));
        Assert.That(WakuuSelectorRouteAudit.Classify(""), Is.EqualTo(WakuuSelectorRouteAudit.Unknown));
    }

    [Test]
    public void 入口枚举_无未分类的新入口()
    {
        IReadOnlyList<string> entries;
        try
        {
            entries = WakuuSelectorRouteAudit.EnumerateEntries();
        }
        catch (Exception exception)
        {
            Assert.Ignore($"无法加载游戏程序集枚举选牌入口（本地无游戏安装时不判失败）: {exception.Message}");
            return;
        }

        Assert.That(entries, Is.Not.Empty, "应至少枚举到若干 CardSelectCmd.From* 入口");

        List<string> unknown = entries
            .Where(name => WakuuSelectorRouteAudit.Classify(name) == WakuuSelectorRouteAudit.Unknown)
            .ToList();

        Assert.That(
            unknown,
            Is.Empty,
            "发现未分类的选牌入口（多半来自游戏更新）：请核对它是否读取 CardSelectCmd.Selector；"
            + "若会读取，接入归属者（CardSelectForegroundSwitchPatch 前缀 / WakuuSelectorRegistry.Open），"
            + "并更新 WakuuSelectorRouteAudit 的分类清单。未分类入口：" + string.Join(", ", unknown));
    }
}
