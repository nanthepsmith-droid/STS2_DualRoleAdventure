using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.RelicPools;
using LocalMultiControl.Scripts.Models.Relics;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private const string BuildMarker = "Revival v1.38 (game v0.111.0, marker=2026-08-29-r29)";

    private static Harmony? _harmony;

    /// <summary>
    /// 启动自检期望清单：这些目标必须被 Harmony 打上，否则说明被 PatchAll 静默跳过
    /// （本 mod 坑 1：类上缺类级 [HarmonyPatch] 时整个类被跳过且无任何报错）。
    /// 用「简单类型名.方法名」匹配 GetPatchedMethods() 的 (DeclaringType.Name, Name)。
    /// 维护口径：与 Scripts/Tools/patch_coverage.py 生成的 docs/patch-coverage.md 一致，
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
    };

    public static void Init()
    {
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");
        LocalMultiControlLogger.Info(BuildMarker);
        LocalWakuuAutopilotConfig.Reload("entry-init");
        RegisterWakuuRelicsToPool();
        LocalWakuuRelicLocalization.Initialize();
        _harmony = new Harmony("sts2.dualroleadventure");
        _harmony.PatchAll();

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
