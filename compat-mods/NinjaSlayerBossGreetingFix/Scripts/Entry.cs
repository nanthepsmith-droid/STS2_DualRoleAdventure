using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using NinjaSlayerBossGreetingFix.Scripts.Patch;

namespace NinjaSlayerBossGreetingFix.Scripts;

/// <summary>
/// 独立补丁 mod：修复 NinjaSlayer 的「Boss 问候」在本地回环（本地双角色/多控）下抛 NullReferenceException
/// 导致战斗卡死的问题。主 mod（DualRoleAdventure）保持不动，所有改动只在本补丁内。
///
/// 目标类型 <c>NinjaSlayer.Code.ExternalAnimations.BossGreetingSync</c> 是 internal，
/// 且 NinjaSlayer 可能晚于本补丁加载，所以这里不引用它、也不走 PatchAll：
/// 先立即尝试一次，找不到就挂 AppDomain.AssemblyLoad 监听，等它加载后再补。
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    private const string SyncTypeName = "NinjaSlayer.Code.ExternalAnimations.BossGreetingSync";

    private static Harmony? _harmony;
    private static bool _patched;
    private static bool _warnedMissing;

    public static void Init()
    {
        _harmony = new Harmony("sts2.ninjaslayer.bossgreeting.fix");

        if (!TryPatch())
        {
            // NinjaSlayer 尚未加载（或未安装）：挂监听，等它的程序集加载后再补。
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
        }

        Log.Info("[NinjaSlayerBossGreetingFix] initialized");
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (_patched || _harmony == null)
        {
            return;
        }

        try
        {
            TryPatch();
        }
        catch
        {
            // 监听回调里的异常不能影响游戏加载流程
        }
    }

    private static bool TryPatch()
    {
        if (_patched || _harmony == null)
        {
            return _patched;
        }

        Type? syncType = FindType(SyncTypeName);
        if (syncType == null)
        {
            // NinjaSlayer 未安装时属正常情况：只记一次，不刷屏。
            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Log.Info("[NinjaSlayerBossGreetingFix] 未找到 NinjaSlayer.BossGreetingSync（未安装或尚未加载），跳过补丁。");
            }
            return false;
        }

        FieldInfo? networkField = syncType.GetField("_network", BindingFlags.Instance | BindingFlags.NonPublic);
        PropertyInfo? participantsProp = syncType.GetProperty("Participants", BindingFlags.Instance | BindingFlags.NonPublic);
        MethodInfo? getter = participantsProp?.GetGetMethod(nonPublic: true);
        if (networkField == null || getter == null)
        {
            Log.Warn("[NinjaSlayerBossGreetingFix] NinjaSlayer 版本可能已变（找不到 BossGreetingSync.Participants/_network），跳过补丁。");
            return false;
        }

        BossGreetingParticipantsPatch.NetworkField = networkField;

        MethodInfo? postfix = typeof(BossGreetingParticipantsPatch).GetMethod(
            nameof(BossGreetingParticipantsPatch.Postfix), BindingFlags.Static | BindingFlags.Public);
        if (postfix == null)
        {
            Log.Warn("[NinjaSlayerBossGreetingFix] 找不到补丁方法本身，跳过补丁。");
            return false;
        }

        try
        {
            _harmony.Patch(getter, postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            // 打补丁失败（例如 NinjaSlayer 换了实现）绝不能反过来让本 mod / 游戏启动失败。
            Log.Warn($"[NinjaSlayerBossGreetingFix] 打补丁失败，跳过（游戏继续）: {ex.Message}");
            return false;
        }

        _patched = true;
        Log.Info("[NinjaSlayerBossGreetingFix] 已补丁 BossGreetingSync.get_Participants（本地回环 NetHost=null 时收缩参与者）。");
        return true;
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? t = asm.GetType(fullName, throwOnError: false);
                if (t != null)
                {
                    return t;
                }
            }
            catch
            {
                // 某些程序集（动态/反射生成）不支持 GetType，跳过
            }
        }

        return null;
    }
}
