using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 选牌入口「归属者路由」启动自检（改进-2 / Phase 1 / 方案 §4.4）。
///
/// 目的：<c>CardSelectCmd</c> 上每个「让玩家选牌」的 <c>From*</c> 入口，在瓦库托管下都必须知道
/// 「这次是给谁选」，否则选择器只能退回「栈顶语义」——单瓦库尚可，多瓦库就会抢答。
/// 游戏更新新增入口时，本自检会把它列进 <c>unknown</c> 并告警，避免静默退化。
///
/// 分类（维护口径）：
/// <list type="bullet">
/// <item><c>ownerAware</c>：入口前缀会写入归属者（<c>CardSelectForegroundSwitchPatch</c> 的 6 个 From* 前缀）；</item>
/// <item><c>legacyFallback</c>：会读 <c>CardSelectCmd.Selector</c> 但入口本身不写归属者，
///   依赖异步链上继承的值 / 注册表命中；已知且在册，不算告警；</item>
/// <item><c>ignored</c>：方法体**不读** <c>Selector</c>（栈上压了也不会被自动作答），无需归属者；</item>
/// <item><c>unknown</c>：不在上述任何一类的**新入口**（多半来自游戏更新）→ WARN。</item>
/// </list>
///
/// 本自检只输日志，**永不致命**（内部全量 try/catch）。
/// </summary>
internal static class WakuuSelectorRouteAudit
{
    internal const string OwnerAware = "ownerAware";
    internal const string LegacyFallback = "legacyFallback";
    internal const string Ignored = "ignored";
    internal const string Unknown = "unknown";

    /// <summary>入口前缀写归属者的入口（与 CardSelectForegroundSwitchPatch 的补丁一一对应）。</summary>
    private static readonly HashSet<string> OwnerAwareEntries = new(StringComparer.Ordinal)
    {
        "FromHand",
        "FromHandForDiscard",
        "FromHandForUpgrade",
        "FromSimpleGrid",
        "FromChooseACardScreen",
        "FromCombatPile",
    };

    /// <summary>会读 Selector、但入口自身不写归属者的既有入口（依赖栈顶语义 / 注册表命中）。</summary>
    private static readonly HashSet<string> LegacyFallbackEntries = new(StringComparer.Ordinal)
    {
        "FromSimpleGridForRewards",
        "FromDeckForUpgrade",
        "FromDeckForTransformation",
        "FromDeckForEnchantment",
        "FromDeckForRemoval",
        "FromDeckGeneric",
    };

    /// <summary>方法体不读 Selector 的入口（压栈也无效，属于「无需归属者」）。</summary>
    private static readonly HashSet<string> IgnoredEntries = new(StringComparer.Ordinal)
    {
        "FromChooseABundleScreen",
    };

    /// <summary>启动阶段调用（经 Entry.RunStage，任何异常都不外抛）。</summary>
    internal static void Run()
    {
        try
        {
            IReadOnlyList<string> entries = EnumerateEntries();
            List<string> unknown = new();
            List<string> ownerAware = new();
            List<string> legacy = new();
            List<string> ignored = new();

            foreach (string name in entries)
            {
                switch (Classify(name))
                {
                    case OwnerAware:
                        ownerAware.Add(name);
                        break;
                    case LegacyFallback:
                        legacy.Add(name);
                        break;
                    case Ignored:
                        ignored.Add(name);
                        break;
                    default:
                        unknown.Add(name);
                        break;
                }
            }

            LocalMultiControlLogger.Info(
                $"SELECTOR_ROUTE from={entries.Count} ownerAware=[{string.Join(",", ownerAware)}] "
                + $"legacyFallback=[{string.Join(",", legacy)}] ignored=[{string.Join(",", ignored)}] "
                + $"unknown=[{string.Join(",", unknown)}]");

            if (unknown.Count > 0)
            {
                LocalMultiControlLogger.Warn(
                    $"选牌入口归属路由自检: 发现未分类的新入口 [{string.Join(", ", unknown)}]（可能来自游戏更新）；"
                    + "请核对其是否读取 CardSelectCmd.Selector —— 若会读取，需要接入归属者"
                    + "（CardSelectForegroundSwitchPatch 的 From* 前缀，或改用 WakuuSelectorRegistry.Open 压栈）。");
            }
        }
        catch (Exception exception)
        {
            // 自检自身失败不影响 mod 可用性（本阶段不做任何实际补丁工作）
            LocalMultiControlLogger.Warn($"选牌入口归属路由自检执行失败（忽略）: {exception.Message}");
        }
    }

    /// <summary>反射枚举 <c>CardSelectCmd</c> 上所有公开静态、返回 Task、名为 From* 的入口（去重按方法名）。</summary>
    internal static IReadOnlyList<string> EnumerateEntries()
    {
        return typeof(CardSelectCmd)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name.StartsWith("From", StringComparison.Ordinal))
            .Where(method => !method.IsGenericMethodDefinition)
            .Where(method => typeof(Task).IsAssignableFrom(method.ReturnType))
            .Select(method => method.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>按入口名分类（见类型注释；未知入口返回 <see cref="Unknown"/>）。</summary>
    internal static string Classify(string entryName)
    {
        if (OwnerAwareEntries.Contains(entryName))
        {
            return OwnerAware;
        }

        if (LegacyFallbackEntries.Contains(entryName))
        {
            return LegacyFallback;
        }

        if (IgnoredEntries.Contains(entryName))
        {
            return Ignored;
        }

        return Unknown;
    }
}
