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
    private const string BuildMarker = "Revival v1.37 released (game v0.111.0, marker=2026-08-26-r22)";

    private static Harmony? _harmony;

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
