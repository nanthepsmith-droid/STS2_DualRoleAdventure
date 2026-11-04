using System;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;

namespace HoldYourTurnLocalCoopFix;

/// <summary>
/// <c>TurnHolder.CanPressTurnEnd</c> 的 getter 前缀。
///
/// <para>只有三个条件同时成立才"放行"（<c>__result = true</c>）：
/// ① 本次 Run 是本地多控回环会话；② HoldYourTurn 绑定的那一席不是当前前台；
/// ③ 当前前台那位确实处在自己的玩家回合里。<b>其余情况一律交回原版</b>
/// （包括"它就是绑定席位"→ 保留 1.5s 防误点）。</para>
///
/// <para>任何异常都吞掉并返回 true（= 不干预）：第三方兼容补丁的底线是不能把游戏弄崩。</para>
/// </summary>
internal static class EndTurnSeatCompat
{
    /// <summary>`TurnHolder._player`（private static Player?）。</summary>
    private static FieldInfo? _boundPlayerField;

    private static bool _warned;
    private static ulong _lastLoggedForeground;
    private static ulong _lastLoggedBound;

    internal static void Bind(Type turnHolderType)
    {
        try
        {
            _boundPlayerField = turnHolderType.GetField(
                "_player",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            if (_boundPlayerField == null)
            {
                Log.Warn($"{Entry.LogPrefix} 找不到 {turnHolderType.FullName}._player，"
                    + "本补丁将无法判断它绑定了哪一席（退化为不干预）。");
            }
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
        }
    }

    /// <summary>Harmony 前缀：返回 false = 用我们的 <paramref name="__result"/> 顶掉原 getter。</summary>
    public static bool Prefix(ref bool __result)
    {
        try
        {
            if (_boundPlayerField == null)
            {
                return true;
            }

            if (!MainModBridge.IsLocalCoopRun())
            {
                return true;
            }

            if (_boundPlayerField.GetValue(null) is not Player bound)
            {
                return true;
            }

            ulong foreground = MainModBridge.ForegroundSeatId();
            if (foreground == 0UL || foreground == bound.NetId)
            {
                // 前台就是它绑定的那一席（含"还没切过"）⇒ 原逻辑，保留它的 1.5s 保护。
                return true;
            }

            if (!CombatManager.Instance.IsInProgress)
            {
                return true;
            }

            Player? foregroundPlayer = MainModBridge.FindPlayer(foreground);
            if (foregroundPlayer == null || !CombatManager.Instance.IsPartOfPlayerTurn(foregroundPlayer))
            {
                return true;
            }

            // 前台这一席确实在自己的玩家回合里 ⇒ 它的结束回合按钮不能被"别人的绑定"判成灰。
            __result = true;
            LogOnce(foreground, bound.NetId);
            return false;
        }
        catch (Exception exception)
        {
            WarnOnce(exception);
            return true;
        }
    }

    private static void LogOnce(ulong foreground, ulong bound)
    {
        if (_lastLoggedForeground == foreground && _lastLoggedBound == bound)
        {
            return;
        }

        _lastLoggedForeground = foreground;
        _lastLoggedBound = bound;
        Log.Info($"{Entry.LogPrefix} HoldYourTurn 兼容：非绑定席位放行结束回合"
            + $"(foreground={foreground}, bound={bound})。");
    }

    private static void WarnOnce(Exception exception)
    {
        if (_warned)
        {
            return;
        }

        _warned = true;
        Log.Warn($"{Entry.LogPrefix} 放行判定失败，交回 HoldYourTurn 原逻辑："
            + $"{exception.GetType().Name}: {exception.Message}");
    }
}
