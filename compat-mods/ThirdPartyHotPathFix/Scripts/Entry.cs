using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using ThirdPartyHotPathFix.Scripts.Gates;
using ThirdPartyHotPathFix.Scripts.Patches;

namespace ThirdPartyHotPathFix.Scripts;

/// <summary>
/// 独立性能兼容补丁 mod（12 席 / 本地多控规模下的第三方热路径）。
///
/// 两件事，都**只碰性能、不碰玩法**：
///
/// 1. **空转过滤链跳过**（四家）：`Koishi` / `figure_Saya` / `newsanguo` 都挂在
///    `CombatState.IterateHookListeners` 的后缀上逐 listener 过滤，`YUICore` 的
///    `YuiRelicTriggerReplay.Wrap` 还要给每个遗物建 scope 写 `AsyncLocal`（实测 10.7% 主线程）。
///    这四条链在「机制不在场」时是纯空转 ⇒ 整条跳过（见 <see cref="ArmedGates"/>）。
/// 2. **热路径纯函数记忆化**（三方）：`StringHelper.Slugify` / RitsuLib `NormalizePublicStem` /
///    YUICore `FindType` 每次调用都重跑正则或逐程序集反射（合计 ≈10.7% 主线程）⇒ 只算一次
///    （见 <see cref="MemoPatches"/>）。
///
/// 红线：判不出来就不跳过；第三方类型/方法找不到就只记一行日志、保持对方原行为；
/// 打补丁失败绝不影响游戏启动。
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    private const string HarmonyId = "thirdparty.hotpath";
    private const string LogPrefix = "[HotPathFix]";
    private const string DisableEnvVar = "HOT_PATH_FIX";

    // 四家的过滤链目标（2026-10-31 反编译确认；改名时全部优雅退化）
    private const string KoishiPatchType = "Koishi.KoishiCode.Patch.SealAllRelicPatch";
    private const string KoishiPowerType = "Koishi.KoishiCode.Powers.SealAllPower";
    private const string SayaPatchType = "figure_Saya.ModSupport.Patches.Combat.GeneralizedResistanceSuppressionPatch";
    private const string SayaPowerType = "figure_Saya.Scripts.Powers.GeneralizedResistancePower";
    private const string NewsanguoPatchType = "newsanguo.Scripts.Patches.AbilitySuppressionPatch";
    private const string NewsanguoCardType = "newsanguo.Scripts.Cards.AntiMagicField";
    private const string NewsanguoPowerType = "newsanguo.Scripts.Powers.IronCavalryPower";
    private const string YuiReplayType = "YuiExtra.Hooks.YuiRelicTriggerReplay";
    private const string YuiNotifyHookType = "YuiExtra.Hooks.IAfterRelicTriggeredHook";
    private const string YuiRepeaterType = "YuiExtra.Hooks.IRelicTriggerRepeater";

    // 记忆化目标
    private const string SlugifyType = "MegaCrit.Sts2.Core.Helpers.StringHelper";
    private const string RitsuNormalizeType = "STS2RitsuLib.Content.ModContentRegistry";
    private const string YuiTypeLookupType = "YuiExtra.Patches.YuiCoreRuntimeTypeLookup";

    private static Harmony? _harmony;
    private static bool _powerWatchInstalled;
    private static bool _koishiInstalled;
    private static bool _sayaInstalled;
    private static bool _newsanguoInstalled;
    private static bool _yuiInstalled;
    private static bool _slugifyMemoInstalled;
    private static bool _ritsuStemMemoInstalled;
    private static bool _yuiFindTypeMemoInstalled;
    private static bool _listenerCapacityInstalled;
    private static bool _allReadyLogged;

    public static void Init()
    {
        try
        {
            if (IsDisabledByEnv())
            {
                Log.Info($"{LogPrefix} 已按环境变量 {DisableEnvVar}=0 关闭（第三方与热路径保持原样）。");
                return;
            }

            _harmony = new Harmony(HarmonyId);
            InstallPowerWatch();
            TryInstallAll();

            // 四家的实现 dll / RitsuLib / YUICore 都可能晚于本 mod 加载
            // （Koishi、Saya 都是壳 + 内嵌实现）⇒ 常驻监听，装齐后自动不再重试。
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;

            Log.Info($"{LogPrefix} initialized（{DescribeState()}）。");
        }
        catch (Exception ex)
        {
            // 性能补丁绝不能反过来让游戏起不来
            Log.Warn($"{LogPrefix} 初始化失败（游戏继续，等同于未安装）: {ex}");
        }
    }

    private static bool IsDisabledByEnv()
    {
        string? value = Environment.GetEnvironmentVariable(DisableEnvVar);
        return value == "0" || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }

    private static string DescribeState()
    {
        return $"过滤链闸门 Koishi={State(_koishiInstalled)} Saya={State(_sayaInstalled)} "
            + $"newsanguo={State(_newsanguoInstalled)} YUI={State(_yuiInstalled)}；"
            + $"记忆化 Slugify={State(_slugifyMemoInstalled)} RitsuStem={State(_ritsuStemMemoInstalled)} "
            + $"YUIFindType={State(_yuiFindTypeMemoInstalled)}；"
            + $"列表容量={State(_listenerCapacityInstalled)}；"
            + $"能力版本监听={State(_powerWatchInstalled)}";

        static string State(bool installed) => installed ? "已装" : "未装";
    }

    private static bool AllReady =>
        _powerWatchInstalled
        && _koishiInstalled
        && _sayaInstalled
        && _newsanguoInstalled
        && _yuiInstalled
        && _slugifyMemoInstalled
        && _ritsuStemMemoInstalled
        && _yuiFindTypeMemoInstalled
        && _listenerCapacityInstalled;

    /// <summary>能力增删是原版唯一入口（`Creature.ApplyPowerInternal` / `RemovePowerInternal`）。</summary>
    private static void InstallPowerWatch()
    {
        if (_harmony == null || _powerWatchInstalled)
        {
            return;
        }

        MethodInfo? apply = typeof(Creature).GetMethod(
            "ApplyPowerInternal",
            BindingFlags.Instance | BindingFlags.Public);
        MethodInfo? remove = typeof(Creature).GetMethod(
            "RemovePowerInternal",
            BindingFlags.Instance | BindingFlags.Public);
        if (apply == null || remove == null)
        {
            Log.Warn($"{LogPrefix} 找不到 Creature.ApplyPowerInternal / RemovePowerInternal（游戏版本变了）⇒"
                + " Koishi / Saya / newsanguo 三家闸门**停用**（保持第三方原行为，只损失性能）。");
            return;
        }

        MethodInfo? postfix = typeof(VendorFilterPatches).GetMethod(
            nameof(VendorFilterPatches.OnPowerMutated),
            BindingFlags.Static | BindingFlags.Public);
        if (postfix == null)
        {
            return;
        }

        try
        {
            var patch = new HarmonyMethod(postfix);
            _harmony.Patch(apply, postfix: patch);
            _harmony.Patch(remove, postfix: patch);
            _powerWatchInstalled = true;
            ArmedGates.PowerVersionWatchInstalled = true;
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} 能力版本监听打补丁失败 ⇒ Koishi / Saya / newsanguo 三家闸门停用"
                + $"（保持第三方原行为，只损失性能）: {ex.Message}");
        }
    }

    private static void TryInstallAll()
    {
        if (_harmony == null)
        {
            return;
        }

        if (!_koishiInstalled)
        {
            _koishiInstalled = TryPatchKoishi();
        }

        if (!_sayaInstalled)
        {
            _sayaInstalled = TryPatchSaya();
        }

        if (!_newsanguoInstalled)
        {
            _newsanguoInstalled = TryPatchNewsanguo();
        }

        if (!_yuiInstalled)
        {
            _yuiInstalled = TryPatchYui();
        }

        if (!_slugifyMemoInstalled)
        {
            _slugifyMemoInstalled = TryPatchSlugifyMemo();
        }

        if (!_ritsuStemMemoInstalled)
        {
            _ritsuStemMemoInstalled = TryPatchNormalizePublicStemMemo();
        }

        if (!_yuiFindTypeMemoInstalled)
        {
            _yuiFindTypeMemoInstalled = TryPatchFindTypeMemo();
        }

        if (!_listenerCapacityInstalled)
        {
            _listenerCapacityInstalled = TryPatchListenerCapacity();
        }

        if (AllReady && !_allReadyLogged)
        {
            _allReadyLogged = true;
            Log.Info($"{LogPrefix} 全部闸门与记忆化已就绪（{DescribeState()}）。");
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        try
        {
            TryInstallAll();
        }
        catch
        {
            // 加载回调里的异常不能影响游戏加载流程
        }
    }

    // ── 四家过滤链闸门 ──────────────────────────────────────────────────────

    private static bool TryPatchKoishi()
    {
        Type? patchType = FindType(KoishiPatchType);
        Type? powerType = FindType(KoishiPowerType);
        if (patchType == null || powerType == null)
        {
            return false;
        }

        VendorTypes.SealAllPower = powerType;
        return TryPatchVendorMethod(
            patchType,
            "Postfix",
            nameof(VendorFilterPatches.SkipKoishiSealAll),
            "Koishi SealAllRelicPatch.Postfix");
    }

    private static bool TryPatchSaya()
    {
        Type? patchType = FindType(SayaPatchType);
        Type? powerType = FindType(SayaPowerType);
        if (patchType == null || powerType == null)
        {
            return false;
        }

        VendorTypes.GeneralizedResistancePower = powerType;
        return TryPatchVendorMethod(
            patchType,
            "FilterSuppressedDebuffs",
            nameof(VendorFilterPatches.SkipSayaGeneralizedResistance),
            "figure_Saya GeneralizedResistanceSuppressionPatch.FilterSuppressedDebuffs");
    }

    private static bool TryPatchNewsanguo()
    {
        Type? patchType = FindType(NewsanguoPatchType);
        Type? cardType = FindType(NewsanguoCardType);
        Type? powerType = FindType(NewsanguoPowerType);
        if (patchType == null || cardType == null || powerType == null)
        {
            return false;
        }

        VendorTypes.AntiMagicFieldCard = cardType;
        VendorTypes.IronCavalryPower = powerType;
        return TryPatchVendorMethod(
            patchType,
            "Postfix",
            nameof(VendorFilterPatches.SkipNewsanguoAbilitySuppression),
            "newsanguo AbilitySuppressionPatch.Postfix");
    }

    private static bool TryPatchYui()
    {
        Type? replayType = FindType(YuiReplayType);
        Type? notifyHook = FindType(YuiNotifyHookType);
        Type? repeater = FindType(YuiRepeaterType);
        if (replayType == null || notifyHook == null || repeater == null)
        {
            return false;
        }

        VendorTypes.AfterRelicTriggeredHook = notifyHook;
        VendorTypes.RelicTriggerRepeater = repeater;
        return TryPatchVendorMethod(
            replayType,
            "Wrap",
            nameof(VendorFilterPatches.SkipYuiRelicTriggerReplay),
            "YUICore YuiRelicTriggerReplay.Wrap",
            requiredFirstParamName: "listeners");
    }

    /// <param name="requiredFirstParamName">
    /// 需要按名字注入的前缀参数（Harmony 按**参数名**匹配）。
    /// 名字对不上就别硬打 —— 打上了也拿不到原 sequence，跳过时会把 `__result` 写成 null。
    /// </param>
    private static bool TryPatchVendorMethod(
        Type targetType,
        string methodName,
        string prefixName,
        string label,
        string? requiredFirstParamName = null)
    {
        if (_harmony == null)
        {
            return false;
        }

        MethodInfo? target = targetType.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (target == null)
        {
            Log.Warn($"{LogPrefix} {targetType.FullName} 里找不到 {methodName}（第三方版本可能已变），跳过该家。");
            return false;
        }

        if (requiredFirstParamName != null)
        {
            ParameterInfo[] parameters = target.GetParameters();
            if (parameters.Length != 1 || parameters[0].Name != requiredFirstParamName)
            {
                Log.Warn($"{LogPrefix} {label} 的参数已变（期望 1 个名为 {requiredFirstParamName} 的参数），"
                    + "跳过该家（保持第三方原行为）。");
                return false;
            }
        }

        MethodInfo? prefix = typeof(VendorFilterPatches).GetMethod(
            prefixName,
            BindingFlags.Static | BindingFlags.Public);
        if (prefix == null)
        {
            Log.Warn($"{LogPrefix} 找不到本 mod 的补丁方法 {prefixName}。");
            return false;
        }

        try
        {
            _harmony.Patch(target, prefix: new HarmonyMethod(prefix));
        }
        catch (Exception ex)
        {
            // 例：参数名/签名对不上。保持第三方原行为即可，不能把它搞坏。
            Log.Warn($"{LogPrefix} 补丁 {label} 失败（保持第三方原行为）: {ex.Message}");
            return false;
        }

        Log.Info($"{LogPrefix} 已补丁 {label}（机制不在场时跳过整条过滤链）。");
        return true;
    }

    // ── 记忆化 ──────────────────────────────────────────────────────────────

    private static bool TryPatchSlugifyMemo()
    {
        Type? helperType = FindType(SlugifyType);
        if (helperType == null)
        {
            return false;
        }

        return TryPatchMemo(
            helperType,
            "Slugify",
            nameof(MemoPatches.PrefixSlugify),
            nameof(MemoPatches.PostfixSlugify),
            "StringHelper.Slugify");
    }

    private static bool TryPatchNormalizePublicStemMemo()
    {
        Type? registryType = FindType(RitsuNormalizeType);
        if (registryType == null)
        {
            return false;
        }

        return TryPatchMemo(
            registryType,
            "NormalizePublicStem",
            nameof(MemoPatches.PrefixNormalizePublicStem),
            nameof(MemoPatches.PostfixNormalizePublicStem),
            "RitsuLib ModContentRegistry.NormalizePublicStem");
    }

    private static bool TryPatchFindTypeMemo()
    {
        Type? lookupType = FindType(YuiTypeLookupType);
        if (lookupType == null)
        {
            return false;
        }

        return TryPatchMemo(
            lookupType,
            "FindType",
            nameof(MemoPatches.PrefixRuntimeTypeLookup),
            nameof(MemoPatches.PostfixRuntimeTypeLookup),
            "YUICore YuiCoreRuntimeTypeLookup.FindType");
    }

    /// <summary>
    /// 放宽 `IterateHookListeners` 里 listener 列表的初始容量（原版 `Players.Count * 50`）。
    /// 12 席 + 符文类 mod 一过 600 槽，**每次分发**都要扩容一次，实测能吃掉主线程 28~30%。
    /// 只改分配大小、不改语义；找不到 IL 形态就只告警、保持原样。
    /// </summary>
    private static bool TryPatchListenerCapacity()
    {
        if (_harmony == null)
        {
            return false;
        }

        int patched = 0;
        patched += PatchListenerCapacityFor(FindType("MegaCrit.Sts2.Core.Combat.CombatState"), "CombatState");
        patched += PatchListenerCapacityFor(FindType("MegaCrit.Sts2.Core.Runs.RunState"), "RunState");
        if (patched == 0)
        {
            Log.Warn($"{LogPrefix} 没找到 IterateHookListeners 状态机（或 IL 形态不符），"
                + "listener 列表初始容量保持原样（不修也不坏）。");
            return false;
        }

        Log.Info($"{LogPrefix} 已放宽 listener 列表初始容量 50 → {ListenerListCapacityPatch.Multiplier} × 席位数"
            + $"（{patched} 个迭代器；可用 {ListenerListCapacityPatch.EnvVarName} 覆盖乘数）。");
        return true;
    }

    private static int PatchListenerCapacityFor(Type? owner, string label)
    {
        if (_harmony == null || owner == null)
        {
            return 0;
        }

        MethodInfo? transpiler = typeof(ListenerListCapacityPatch).GetMethod(
            nameof(ListenerListCapacityPatch.Transpiler),
            BindingFlags.Static | BindingFlags.NonPublic);
        if (transpiler == null)
        {
            return 0;
        }

        int patched = 0;
        foreach (Type nested in owner.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic))
        {
            // 编译器生成的迭代器：<IterateHookListeners>d__69（序号随版本变，别写死）
            if (!nested.Name.StartsWith("<IterateHookListeners>", StringComparison.Ordinal))
            {
                continue;
            }

            MethodInfo? moveNext = nested.GetMethod(
                "MoveNext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                Type.EmptyTypes,
                null);
            if (moveNext == null)
            {
                continue;
            }

            TranspilerReport.LastReplaced = -1;
            TranspilerReport.LastOriginalValue = 0;
            try
            {
                _harmony.Patch(moveNext, transpiler: new HarmonyMethod(transpiler));
            }
            catch (Exception ex)
            {
                Log.Warn($"{LogPrefix} {label}.{nested.Name} 容量转译失败（保持原样）: {ex.Message}");
                continue;
            }

            if (TranspilerReport.LastReplaced > 0)
            {
                patched++;
                Log.Info($"{LogPrefix} {label}.{nested.Name}: listener 列表容量乘数 "
                    + $"{TranspilerReport.LastOriginalValue} → {ListenerListCapacityPatch.Multiplier}。");
            }
            else
            {
                Log.Warn($"{LogPrefix} {label}.{nested.Name}: 没找到 `Players.Count * N` + `new List<AbstractModel>(int)` "
                    + "的预期形态（游戏版本可能已变），该处保持原样。");
            }
        }

        return patched;
    }

    private static bool TryPatchMemo(Type targetType, string methodName, string prefixName, string postfixName, string label)

    {
        if (_harmony == null)
        {
            return false;
        }

        MethodInfo? target = targetType.GetMethod(
            methodName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (target == null)
        {
            Log.Warn($"{LogPrefix} {targetType.FullName} 里找不到 {methodName}（版本可能已变），跳过该记忆化。");
            return false;
        }

        MethodInfo? prefix = typeof(MemoPatches).GetMethod(prefixName, BindingFlags.Static | BindingFlags.Public);
        MethodInfo? postfix = typeof(MemoPatches).GetMethod(postfixName, BindingFlags.Static | BindingFlags.Public);
        if (prefix == null || postfix == null)
        {
            Log.Warn($"{LogPrefix} 找不到本 mod 的记忆化补丁方法 {prefixName}/{postfixName}。");
            return false;
        }

        try
        {
            _harmony.Patch(
                target,
                prefix: new HarmonyMethod(prefix),
                postfix: new HarmonyMethod(postfix));
        }
        catch (Exception ex)
        {
            Log.Warn($"{LogPrefix} 记忆化 {label} 打补丁失败（保持原行为）: {ex.Message}");
            return false;
        }

        Log.Info($"{LogPrefix} 已补丁 {label}（同参数只算一次）。");
        return true;
    }

    /// <summary>按类型全名在已加载程序集里找（第三方可能晚加载，所以每次尝试都重新扫）。</summary>
    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? found = assembly.GetType(fullName, throwOnError: false);
                if (found != null)
                {
                    return found;
                }
            }
            catch
            {
                // 动态程序集 / 反射生成程序集可能不支持 GetType
            }
        }

        return null;
    }
}
