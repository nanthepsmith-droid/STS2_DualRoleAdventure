using System;
using System.Collections.Generic;
using System.Linq;
using LocalMultiControl.Scripts.Runtime;
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
                     "FromSimpleGrid", "FromChooseACardScreen", "FromCombatPile",
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
                     "FromSimpleGridForRewards", "FromDeckForUpgrade", "FromDeckForTransformation",
                     "FromDeckForEnchantment", "FromDeckForRemoval", "FromDeckGeneric",
                 })
        {
            Assert.That(WakuuSelectorRouteAudit.Classify(name), Is.EqualTo(WakuuSelectorRouteAudit.LegacyFallback), name);
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
