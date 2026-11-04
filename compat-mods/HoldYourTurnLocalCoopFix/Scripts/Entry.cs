using System;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace HoldYourTurnLocalCoopFix;

/// <summary>
/// HoldYourTurnLocalCoopFix —— 让「别急着结束回合！」（HoldYourTurn / 工坊 3810229386）
/// 与**本地多控**（DualRoleAdventure / LocalMultiControl）并用时，第二个及以后的真人也能结束回合。
///
/// <para><b>病灶（反编译 v0.2.3 实证）</b>：<c>TurnHolder.OnCombatSetUp</c> 在战斗开始时遍历
/// <c>CombatState.Players</c>，只挑**第一个** <c>LocalContext.IsMe(player)</c> 为真的席位就
/// <c>_player = player; break;</c>。之后：
/// <list type="bullet">
/// <item><c>CanPressTurnEnd</c> 要求 <c>CombatManager.IsPartOfPlayerTurn(_player)</c> ⇒ 轮到别人时恒 false；</item>
/// <item><c>NEndTurnButton.RefreshEnabled</c> 的 Postfix：<c>if (!CanPressTurnEnd) Disable()</c>；</item>
/// <item><c>NEndTurnButton.CallReleaseLogic</c> 的 Prefix：<c>!IsEnabled || !CanPressTurnEnd</c> 直接吞点击。</item>
/// </list>
/// 而主 mod 的本地多控里，每个席位都是「本机玩家」⇒ 第二人之后永远命中这三条。</para>
///
/// <para><b>修法</b>：只补 <c>CanPressTurnEnd</c> 的 getter —— 当「当前前台席位（主 mod 的受控位）
/// ≠ 它绑定的那一席」时直接给 <c>true</c>，把判定交回原版 / 主 mod；绑定席位自己仍保留
/// 「加成后 1.5s 内防误点」的保护。<b>刻意不改 <c>_player</c> 的绑定</b>：它的
/// <c>Subscriptions</c> / <c>_gainSequence</c> / <c>_undoRequested</c> 是整套按单席设计的台账，
/// 改绑会连带出「撤销结束回合」的错乱。</para>
///
/// <para>全程反射 + 失败只 WARN：HoldYourTurn 缺席 / 改名 / 升级重构都只是让本补丁退化成「不干预」。</para>
/// </summary>
[ModInitializer(nameof(Init))]
public static class Entry
{
    internal const string LogPrefix = "[HoldYourTurnLocalCoopFix]";

    /// <summary>目标类型（HoldYourTurn 的内部静态类；命名空间固定）。</summary>
    private const string TurnHolderTypeName = "HoldYourTurn.HoldYourTurnCode.TurnHolder";

    /// <summary>目标属性（`public static bool CanPressTurnEnd { get; }`）。</summary>
    private const string TargetPropertyName = "CanPressTurnEnd";

    private static Harmony? _harmony;
    private static bool _installed;
    private static bool _waitingLogged;

    public static void Init()
    {
        try
        {
            _harmony = new Harmony("sts2.holdyourturn.localcoopfix");

            // HoldYourTurn 的程序集可能晚于本补丁加载（槽位顺序不保证）⇒ 先试一次，失败挂监听等它。
            AppDomain.CurrentDomain.AssemblyLoad += OnAssemblyLoad;
            TryInstall("init");

            if (!_installed)
            {
                Log.Info($"{LogPrefix} 等待 HoldYourTurn 程序集加载后再挂补丁。");
            }
        }
        catch (Exception exception)
        {
            // 第三方兼容补丁绝不允许连累游戏启动：只记日志，不抛。
            Log.Error($"{LogPrefix} INIT_ERROR: {exception}");
        }
    }

    private static void OnAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (_installed)
        {
            return;
        }

        try
        {
            string? name = args.LoadedAssembly.GetName().Name;
            if (string.IsNullOrEmpty(name) || name.IndexOf("HoldYourTurn", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return;
            }

            Log.Info($"{LogPrefix} 检测到 HoldYourTurn 程序集加载: {name}，尝试挂补丁。");
            TryInstall($"assembly-load:{name}");
        }
        catch
        {
            // 监听回调里的异常一律吞掉（不能影响游戏自身的程序集加载流程）。
        }
    }

    private static void TryInstall(string source)
    {
        if (_installed || _harmony == null)
        {
            return;
        }

        Type? turnHolderType = FindType(TurnHolderTypeName);
        if (turnHolderType == null)
        {
            if (!_waitingLogged)
            {
                _waitingLogged = true;
                Log.Info($"{LogPrefix} 尚未找到 {TurnHolderTypeName}（未装 HoldYourTurn 或版本过旧），暂不挂补丁。");
            }

            return;
        }

        PropertyInfo? property = turnHolderType.GetProperty(
            TargetPropertyName,
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        MethodInfo? getter = property?.GetGetMethod(nonPublic: true);
        if (getter == null || getter.ReturnType != typeof(bool) || getter.GetParameters().Length != 0)
        {
            Log.Warn($"{LogPrefix} INIT_DEGRADED: {TurnHolderTypeName}.{TargetPropertyName} 结构不符"
                + $"（property={(property != null)}, getter={(getter != null)}），本补丁不干预。");
            return;
        }

        MethodInfo? prefix = AccessTools.Method(typeof(EndTurnSeatCompat), nameof(EndTurnSeatCompat.Prefix));
        if (prefix == null)
        {
            Log.Error($"{LogPrefix} INIT_ERROR: 找不到自身的 Prefix 方法，本补丁不干预。");
            return;
        }

        _harmony.Patch(getter, prefix: new HarmonyMethod(prefix));
        EndTurnSeatCompat.Bind(turnHolderType);

        _installed = true;
        AppDomain.CurrentDomain.AssemblyLoad -= OnAssemblyLoad;

        Log.Info($"{LogPrefix} INIT_OK: 已接管 {TurnHolderTypeName}.{TargetPropertyName} 的 getter"
            + $"（绑定席位保留原保护，其他本地席位放行），source={source}");
    }

    private static Type? FindType(string fullName)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                Type? type = assembly.GetType(fullName, throwOnError: false);
                if (type != null)
                {
                    return type;
                }
            }
            catch
            {
                // 个别程序集 GetType 会抛，跳过继续找。
            }
        }

        return null;
    }
}
