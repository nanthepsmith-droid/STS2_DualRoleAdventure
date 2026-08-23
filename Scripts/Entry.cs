using System;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using LocalMultiControl.Scripts.Runtime;

namespace LocalMultiControl.Scripts;

[ModInitializer(nameof(Init))]
public partial class Entry
{
    private const string BuildMarker = "Revival v1.34 loaded (game v0.111.0, marker=2026-08-23-r2)";

    private static Harmony? _harmony;

    public static void Init()
    {
        LocalMultiControlLogger.Info("开始初始化 Harmony 补丁。");
        LocalMultiControlLogger.Info(BuildMarker);
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
}
