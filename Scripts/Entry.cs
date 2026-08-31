using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Patch;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private const string BuildMarker = "Revival v1.38 (game v0.111.0, marker=2026-08-31-r46)";

    private static Harmony? _harmony;

    /// <summary>
    /// 启动自检期望清单：这些目标必须被 Harmony 打上，否则说明被 PatchAll 静默跳过
    /// （本 mod 坑 1：类上缺类级 [HarmonyPatch] 时整个类被跳过且无任何报错）。
    /// 用「简单类型名.方法名」匹配 GetPatchedMethods() 的 (DeclaringType.Name, Name)。
    /// 维护口径：与 Scripts/Tools/patch_coverage.py 生成的 patch-coverage.md（pain/maintenance-docs/，无 git）一致，
    /// 覆盖最关键、最易被游戏更新波及的目标；缺失只报错、不阻止加载。
    /// </summary>
    private static readonly string[] ExpectedPatchTargets =
    {
        "NPlayerHand.SelectCards",                        // 战斗内手牌选牌串行化
        "CardSelectCmd.FromHand",
        "CardSelectCmd.FromHandForDiscard",
        "CardSelectCmd.FromHandForUpgrade",
        "CardSelectCmd.FromSimpleGrid",
        "CardSelectCmd.FromChooseACardScreen",
        "CardSelectCmd.FromCombatPile",
        "CardSelectCmd.ShouldSelectLocalCard",
        "CombatManager.SetupPlayerTurn",                  // 回合开始切前台 / 结束按钮重评
        "CombatManager.DoTurnEnd",
        "CombatManager.FlushPlayerHand",
        "CombatManager.SetReadyToEndTurn",
        "CombatManager.SetReadyToBeginEnemyTurn",
        "CreatureCmd.Kill",                               // 击杀后战斗胜利结算
        "EventSynchronizer.BeginEvent",
        "EventSynchronizer.ChooseLocalOption",
        "RewardsSet.Offer",
        "RewardsCmd.OfferCustom",
        "RewardsCmd.OfferForRoomEnd",
        "CombatRoom.OfferRoomEndRewards",
        "RewardsSetSynchronizer.SelectLocalReward",
        "PotionCmd.TryToProcure",
        "WhisperingEarring.AfterAutoPrePlayPhaseEnteredLate",
        "ActionQueueSet.CombatEnded",                     // 战斗结束残留动作清理
        "NEndTurnButton.CallReleaseLogic",
        "CardSelectCmd.FromDeckForEnchantment",           // 瓦库事件附魔选牌自动作答
        "CardCmd.Transform",                              // 手牌变换期间 NetId 钉住（UI 同步）
    };

    public static void Init()
    {
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");
        LocalMultiControlLogger.Info(BuildMarker);
        LocalWakuuAutopilotConfig.Reload("entry-init");
        RegisterWakuuRelicsToPool();
        LocalWakuuRelicLocalization.Initialize();
        _harmony = new Harmony("sts2.dualroleadventure");
        try
        {
            if (PatchDomainMap.UseGroupedPatchAll)
            {
                ApplyAllPatchGroups();
            }
            else
            {
                // 回滚预案（实施方案 2.3）：整体关闭分组容错，回到旧 PatchAll 直跑。
                _harmony.PatchAll();
            }
        }
        catch (Exception patchException)
        {
            // r38 防御 + 2.3 分组：Core 组失败即停时会走到这里（后续补丁组未应用）。
            // 已应用的补丁保留；缺失会在下方启动自检中报出。
            LocalMultiControlLogger.Error($"Harmony 补丁初始化中断（请结合启动自检缺失清单定位具体补丁）: {patchException}");
        }

        try
        {
            var patchedMethods = _harmony.GetPatchedMethods();
            var patchedList = patchedMethods.ToList();
            LocalMultiControlLogger.Info($"Harmony 补丁统计: 已打补丁方法数={patchedList.Count}");
            foreach (var method in patchedList)
            {
                if (method.Name.Contains("SelectCards") || method.Name.Contains("FromHand")
                    || method.Name.Contains("FromSimpleGrid") || method.Name.Contains("FromChooseACard")
                    || method.Name.Contains("FromCombatPile") || method.Name.Contains("ShouldSelectLocalCard"))
                {
                    LocalMultiControlLogger.Info($"  已打补丁: {method.DeclaringType?.FullName}.{method.Name}");
                }
            }

            // 启动自检：期望补丁清单 vs 实际已打补丁，缺失即 ERROR（多为方法级-only 被静默跳过）。
            var patchedKeys = patchedList
                .Where(method => method.DeclaringType != null)
                .Select(method => $"{method.DeclaringType!.Name}.{method.Name}")
                .ToHashSet(StringComparer.Ordinal);
            List<string> missingTargets = ExpectedPatchTargets.Where(target => !patchedKeys.Contains(target)).ToList();
            if (missingTargets.Count > 0)
            {
                foreach (string target in missingTargets)
                {
                    LocalMultiControlLogger.Error($"启动自检: 期望补丁缺失(可能被 PatchAll 静默跳过): {target}");
                }

                LocalMultiControlLogger.Error(
                    $"启动自检: {missingTargets.Count}/{ExpectedPatchTargets.Length} 个期望补丁缺失，请用 Scripts/Tools/patch_coverage.py 重新生成覆盖清单核对。");
            }
            else
            {
                LocalMultiControlLogger.Info($"启动自检: {ExpectedPatchTargets.Length} 个期望补丁全部生效。");
            }
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"Harmony 补丁统计失败: {exception.Message}");
        }

        LocalMultiControlLogger.Info("Mod 初始化完成。");
    }

    /// <summary>
    /// 分组应用全部 Harmony 补丁（维护性改进 2.3：PatchAll 分组隔离）。
    ///
    /// 按 <see cref="PatchDomainMap"/> 将补丁类归入 7 个域并逐组 try-catch：
    ///   - Core（本地多控运行基座）失败即停：异常上抛由 Init 兜底记录，不再应用后续补丁组；
    ///   - 其余组失败打 Error 并跳过，继续下一组；
    ///   - 未登记分组的补丁类打 Warn 并按「隔离组」兜底应用（应补登记到 PatchDomainMap）。
    /// </summary>
    private static void ApplyAllPatchGroups()
    {
        Assembly assembly = typeof(Entry).Assembly;
        // 与 Harmony PatchAll 的收集口径一致：不排除 abstract（静态补丁类编译为 abstract+sealed）。
        List<Type> allPatchTypes = assembly.GetTypes()
            .Where(type => type.IsClass && type.GetCustomAttribute<HarmonyPatch>() != null)
            .ToList();

        var grouped = new Dictionary<PatchDomain, List<Type>>();
        var unregistered = new List<Type>();
        foreach (Type patchType in allPatchTypes)
        {
            PatchDomain? domain = PatchDomainMap.ResolveFor(patchType);
            if (domain == null)
            {
                unregistered.Add(patchType);
                continue;
            }

            if (!grouped.TryGetValue(domain.Value, out List<Type>? domainTypes))
            {
                domainTypes = new List<Type>();
                grouped[domain.Value] = domainTypes;
            }

            domainTypes.Add(patchType);
        }

        foreach (PatchDomain domain in PatchDomainMap.ApplyOrder)
        {
            if (grouped.TryGetValue(domain, out List<Type>? domainTypes))
            {
                ApplyPatchGroup(domain, domainTypes, failFast: domain == PatchDomain.Core);
            }
        }

        if (unregistered.Count > 0)
        {
            foreach (Type unregisteredType in unregistered)
            {
                LocalMultiControlLogger.Warn(
                    $"补丁类未登记分组（已按隔离组兜底应用，请登记到 PatchDomainMap）: {unregisteredType.FullName}");
            }

            ApplyPatchGroup(domain: null, unregistered, failFast: false);
        }
    }

    /// <summary>
    /// 应用单个补丁组。组内任一补丁失败时：
    ///   failFast=true（Core）→ 打 Error 并上抛，后续补丁组不再应用（错误基线上继续更危险）；
    ///   failFast=false → 打 Error 并跳过本组，继续其余组。
    /// </summary>
    private static void ApplyPatchGroup(PatchDomain? domain, IReadOnlyList<Type> patchTypes, bool failFast)
    {
        if (patchTypes.Count == 0)
        {
            return;
        }

        string groupName = domain?.ToString() ?? "Unregistered";
        try
        {
            foreach (Type patchType in patchTypes)
            {
                _harmony!.CreateClassProcessor(patchType).Patch();
            }

            LocalMultiControlLogger.Info($"补丁组[{groupName}] 应用完成：{patchTypes.Count} 类");
        }
        catch (Exception exception) when (failFast)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败（本组失败即停，后续补丁组不再应用）: {exception}");
            throw;
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Error(
                $"补丁组[{groupName}] 应用失败，已跳过该组（其余组继续）: {exception}");
        }
    }

    /// <summary>
    /// 把瓦库托管遗物注册进事件遗物池（与原版低语耳环同池）。
    /// 不入池的遗物在 RelicModel.Pool 里会因 First() 找不到匹配而抛异常，
    /// 导致悬停/点开遗物描述时 UI 中断（表现为"未解锁"且无法退出描述页）。
    /// 事件遗物池没有任何随机奖励入口引用，注册后不会被随机抽到。
    /// </summary>
    private static void RegisterWakuuRelicsToPool()
    {
        try
        {
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuStarterRelic>();
            ModHelper.AddModelToPool<EventRelicPool, LocalWakuuFormRelic>();
            LocalMultiControlLogger.Info("已注册瓦库托管遗物到事件遗物池。");
        }
        catch (Exception exception)
        {
            LocalMultiControlLogger.Warn($"注册瓦库遗物到遗物池失败（遗物描述页可能异常）: {exception.Message}");
        }
    }
}
