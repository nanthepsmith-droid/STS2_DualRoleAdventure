using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Runs;

namespace HoldYourTurnLocalCoopFix;

/// <summary>
/// 与主 mod（DualRoleAdventure / LocalMultiControl）之间的**只读桥**。
///
/// <para>只按「类型全名 + 成员名」反射，**不做编译期引用**：本补丁不依赖主 mod 的程序集，
/// 主 mod 不在（或成员改名）时整体退化成「不干预」—— HoldYourTurn 保持原行为（第二人点不动），
/// 但绝不会因为我们而多出新的坏行为。</para>
///
/// <para>用到的两个口径都是主 mod 里被声明为**唯一入口**的：
/// <list type="bullet">
/// <item>回环网络服务类型 = 本地多控会话（真实联机 / 单人一律 false）；</item>
/// <item><c>LocalSeatSource.ForegroundSeatId()</c> = 当前前台（受控位优先）席位。</item>
/// </list></para>
/// </summary>
internal static class MainModBridge
{
    private const string LoopbackServiceTypeName = "LocalMultiControl.Scripts.Runtime.LocalLoopbackHostGameService";
    private const string SeatSourceTypeName = "LocalMultiControl.Scripts.Runtime.LocalSeatSource";

    private static bool _resolved;
    private static bool _warned;
    private static MethodInfo? _foregroundSeatId;

    /// <summary>当前 Run 是不是主 mod 的本地多控会话（按回环网络服务类型名判定）。</summary>
    internal static bool IsLocalCoopRun()
    {
        try
        {
            INetGameService? service = RunManager.Instance?.NetService;
            return service != null
                && string.Equals(service.GetType().FullName, LoopbackServiceTypeName, StringComparison.Ordinal);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>当前前台（受控位优先）席位；主 mod 缺席时退化为 <c>LocalContext.NetId</c>。</summary>
    internal static ulong ForegroundSeatId()
    {
        EnsureResolved();
        if (_foregroundSeatId != null)
        {
            try
            {
                return _foregroundSeatId.Invoke(null, null) is ulong id ? id : 0UL;
            }
            catch (Exception exception)
            {
                WarnOnce(exception);
            }
        }

        return LocalContext.NetId ?? 0UL;
    }

    /// <summary>按 NetId 取当前 Run 的 Player（取不到 = null）。</summary>
    internal static Player? FindPlayer(ulong netId)
    {
        if (netId == 0UL)
        {
            return null;
        }

        try
        {
            RunState? runState = RunManager.Instance?.DebugOnlyGetState();
            if (runState == null)
            {
                return null;
            }

            foreach (Player player in runState.Players)
            {
                if (player != null && player.NetId == netId)
                {
                    return player;
                }
            }
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }

        return null;
    }

    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        Type? seatSourceType = FindType(SeatSourceTypeName);
        _foregroundSeatId = seatSourceType?.GetMethod(
            "ForegroundSeatId",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            types: Type.EmptyTypes,
            modifiers: null);

        if (_foregroundSeatId != null)
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
                // 个别程序集 GetType 会抛，跳过继续找。
            }
        }

        return null;
    }

    private static void WarnOnce(Exception exception)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        MegaCrit.Sts2.Core.Logging.Log.Warn(
            $"{Entry.LogPrefix} 读取主 mod 前台席位失败，退化为 LocalContext："
            + $"{exception.GetType().Name}: {exception.Message}");
    }
}
