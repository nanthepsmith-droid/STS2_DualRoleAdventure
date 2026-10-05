using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HextechRunesLocalCoopFix.Scripts;

/// <summary>
/// 与主 mod（DualRoleAdventure / LocalMultiControl）之间的**只读桥**。
///
/// 只按「类型全名 + 成员名」反射，**不做编译期引用**：补丁 mod 不依赖主 mod 的程序集，
/// 主 mod 不在（或成员被改名）时整体退化成「不干预」——此时海克斯保持原版行为，
/// 也就是本补丁要修的那个软锁会回来，但绝不会因为我们而多出新的坏行为。
///
/// 用的几个成员都是主 mod 里**被声明为唯一取数入口**的东西：
/// <list type="bullet">
/// <item><c>LocalSeatSource.IsLocalSeat(ulong)</c> —— 「这一席是不是我们的本地席位」；</item>
/// <item><c>LocalSelfCoopContext.GetSlotLabel(ulong)</c> —— 席位号（与游戏内「角色N」提示同一口径）；</item>
/// <item><c>LocalSelfCoopContext.IsEnabled / UseSingleAdventureMode</c> +
/// <c>LocalWakuuAutopilotConfig.BackgroundMode</c> + <c>LocalWakuuRelicRuntime.IsVakuuFormModeById(ulong)</c>
/// —— 「这一席是不是后台托管中的瓦库席位」（= 主 mod 的
/// <c>LocalWakuuRelicRuntime.IsBackgroundHostedWakuu</c> / 纯逻辑
/// <c>WakuuSelfDrawnChoicePolicy.IsManagedWakuuSeat</c> 同一口径）。</item>
/// </list>
/// </summary>
internal static class MainModBridge
{
    private const string LoopbackServiceTypeName = "LocalMultiControl.Scripts.Runtime.LocalLoopbackHostGameService";
    private const string SeatSourceTypeName = "LocalMultiControl.Scripts.Runtime.LocalSeatSource";
    private const string SelfCoopContextTypeName = "LocalMultiControl.Scripts.Runtime.LocalSelfCoopContext";
    private const string AutopilotConfigTypeName = "LocalMultiControl.Scripts.Runtime.LocalWakuuAutopilotConfig";
    private const string WakuuRelicRuntimeTypeName = "LocalMultiControl.Scripts.Runtime.LocalWakuuRelicRuntime";

    private static bool _resolved;
    private static MethodInfo? _isLocalSeat;
    private static MethodInfo? _getSlotLabel;
    private static bool _fallbackWarned;

    private static bool _wakuuResolved;
    private static PropertyInfo? _selfCoopEnabled;
    private static PropertyInfo? _singleAdventureMode;
    private static PropertyInfo? _backgroundMode;
    private static MethodInfo? _isVakuuFormModeById;
    private static bool _wakuuFallbackWarned;

    /// <summary>当前 Run 是不是主 mod 的本地多控会话（按回环网络服务的类型名判定）。</summary>
    internal static bool IsLocalCoopRun()
    {
        INetGameService? service = RunManager.Instance?.NetService;
        return service != null
            && string.Equals(service.GetType().FullName, LoopbackServiceTypeName, StringComparison.Ordinal);
    }

    /// <summary>这一席是不是本地席位（主 mod 缺席时退化为「本次 Run 的玩家都算本地」）。</summary>
    internal static bool IsLocalSeat(ulong netId)
    {
        if (netId == 0UL)
        {
            return false;
        }

        EnsureResolved();
        if (_isLocalSeat != null)
        {
            try
            {
                return _isLocalSeat.Invoke(null, new object[] { netId }) is true;
            }
            catch (Exception exception)
            {
                WarnFallback(exception);
            }
        }

        // 兜底：回环会话里不存在真正的远端玩家 ⇒ 本次 Run 的玩家都当作本地席位。
        return IsPlayerOfCurrentRun(netId);
    }

    /// <summary>席位标签（与主 mod 的「角色N」提示同口径）。</summary>
    internal static string SeatLabel(Player player)
    {
        EnsureResolved();
        if (_getSlotLabel != null)
        {
            try
            {
                if (_getSlotLabel.Invoke(null, new object[] { player.NetId }) is string label
                    && !string.IsNullOrEmpty(label)
                    && label != "?")
                {
                    return $"角色{label}";
                }
            }
            catch (Exception exception)
            {
                WarnFallback(exception);
            }
        }

        int index = PlayerIndexOfCurrentRun(player.NetId);
        return index >= 0 ? $"角色{index + 1}" : $"玩家{player.NetId}";
    }

    /// <summary>席位标签（只有 NetId 的场景用；主 mod 缺席时退化为「角色N」按 Run 内顺序）。</summary>
    internal static string SeatLabelById(ulong netId)
    {
        EnsureResolved();
        if (_getSlotLabel != null)
        {
            try
            {
                if (_getSlotLabel.Invoke(null, new object[] { netId }) is string label
                    && !string.IsNullOrEmpty(label)
                    && label != "?")
                {
                    return $"角色{label}";
                }
            }
            catch (Exception exception)
            {
                WarnFallback(exception);
            }
        }

        int index = PlayerIndexOfCurrentRun(netId);
        return index >= 0 ? $"角色{index + 1}" : $"玩家{netId}";
    }

    /// <summary>
    /// 这一席是不是「**后台托管中的瓦库席位**」——与主 mod 的
    /// <c>LocalWakuuRelicRuntime.IsBackgroundHostedWakuu(player)</c>（以及纯逻辑
    /// <c>WakuuSelfDrawnChoicePolicy.IsManagedWakuuSeat</c>）**同一口径**：
    /// 本地多控启用 + 单人冒险档 + 后台托管档 + 该席处于瓦库形态。
    ///
    /// <para>本补丁只在它为 true 时替这一席自动作答（真人席位一律不代点）。
    /// 主 mod 缺席 / 成员改名 ⇒ 返回 false，退化成既有行为（真人自己点一下符文界面），
    /// 不会因为我们而多出新的坏行为。</para>
    /// </summary>
    internal static bool IsAutomatedWakuuSeat(ulong netId)
    {
        if (netId == 0UL)
        {
            return false;
        }

        EnsureWakuuResolved();
        if (_selfCoopEnabled == null || _singleAdventureMode == null || _backgroundMode == null
            || _isVakuuFormModeById == null)
        {
            return false;
        }

        try
        {
            if (ReadBoolProperty(_selfCoopEnabled) is not true
                || ReadBoolProperty(_singleAdventureMode) is not true
                || ReadBoolProperty(_backgroundMode) is not true)
            {
                return false;
            }

            return _isVakuuFormModeById.Invoke(null, new object[] { netId }) is true;
        }
        catch (Exception exception)
        {
            WarnWakuuFallback(exception);
            return false;
        }
    }

    private static bool? ReadBoolProperty(PropertyInfo property)
    {
        return property.GetValue(null) switch
        {
            bool value => value,
            _ => null,
        };
    }

    /// <summary>
    /// 解析「后台托管瓦库」判定所需的成员。主 mod 的程序集可能晚于本补丁加载 ⇒ 找不到就下次再试，
    /// 只有连续失败到真正被用到时才打一条 WARN（且只打一次）。
    /// </summary>
    private static void EnsureWakuuResolved()
    {
        if (_wakuuResolved)
        {
            return;
        }

        Type? contextType = FindType(SelfCoopContextTypeName);
        Type? configType = FindType(AutopilotConfigTypeName);
        Type? relicRuntimeType = FindType(WakuuRelicRuntimeTypeName);

        _selfCoopEnabled = contextType?.GetProperty(
            "IsEnabled", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _singleAdventureMode = contextType?.GetProperty(
            "UseSingleAdventureMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _backgroundMode = configType?.GetProperty(
            "BackgroundMode", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        _isVakuuFormModeById = relicRuntimeType?.GetMethod(
            "IsVakuuFormModeById", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (_selfCoopEnabled != null && _singleAdventureMode != null && _backgroundMode != null
            && _isVakuuFormModeById != null)
        {
            _wakuuResolved = true;
        }
    }

    private static void WarnWakuuFallback(Exception exception)
    {
        if (_wakuuFallbackWarned)
        {
            return;
        }

        _wakuuFallbackWarned = true;
        MegaCrit.Sts2.Core.Logging.Log.Warn(
            $"{Entry.LogPrefix} 读取主 mod「后台托管瓦库」判定失败，本次不代答符文界面: "
            + $"{exception.GetType().Name}: {exception.Message}");
    }

    private static bool IsPlayerOfCurrentRun(ulong netId) => PlayerIndexOfCurrentRun(netId) >= 0;

    private static int PlayerIndexOfCurrentRun(ulong netId)
    {
        RunState? runState = RunManager.Instance?.DebugOnlyGetState();
        if (runState == null)
        {
            return -1;
        }

        IReadOnlyList<Player> players = runState.Players;
        for (int i = 0; i < players.Count; i++)
        {
            if (players[i] != null && players[i].NetId == netId)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// 解析主 mod 的成员。找不到就一直重试（主 mod 的程序集可能晚于本补丁加载），
    /// 只有两个成员都拿不到时才在第一次使用时打一条 WARN。
    /// </summary>
    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        Type? seatSourceType = FindType(SeatSourceTypeName);
        Type? contextType = FindType(SelfCoopContextTypeName);

        _isLocalSeat = seatSourceType?.GetMethod(
            "IsLocalSeat",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        _getSlotLabel = contextType?.GetMethod(
            "GetSlotLabel",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

        if (_isLocalSeat != null || _getSlotLabel != null)
        {
            _resolved = true;
        }
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
                // 个别程序集 GetType 会抛，跳过即可。
            }
        }

        return null;
    }

    private static void WarnFallback(Exception? exception)
    {
        if (_fallbackWarned)
        {
            return;
        }

        _fallbackWarned = true;
        MegaCrit.Sts2.Core.Logging.Log.Warn(
            $"{Entry.LogPrefix} 读取主 mod 席位信息失败，退化为「本 Run 玩家都算本地席位」: "
            + $"{exception?.GetType().Name}: {exception?.Message}");
    }
}
