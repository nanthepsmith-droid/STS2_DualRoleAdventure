using System.Collections.Generic;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 托管出牌「看门狗记账键」的纯函数（r325）。
///
/// **钉死的不变式**：换一场战斗之后，**同一席位、同一回合号**必须拿到**不同的键**。
///
/// 背景（2026-10-28 实机）：旧键是 <c>回合:席位</c>。战斗结束时还停在出牌 await 上的循环**永远不会
/// 返回**，它的记账键就永远留在"在飞"集合里；而每场战斗的回合号都从 1 重来 ⇒ 下一场战斗的键直接撞上
/// ⇒ 那一席被 <c>watchdog-in-flight</c> 永久挡回、整局不再出牌（实机：11 个瓦库里 8 个从第 3 场战斗
/// 起不出牌，而它们活着、有手牌有能量）。
///
/// 本类只做字符串/算术，不碰 Godot / 游戏类型，便于单测。
/// </summary>
internal static class HostedWatchdogKeyPolicy
{
    /// <summary>战斗 ID 拿不到时的占位（正常战斗中不会用到）。</summary>
    public const int UnknownCombatId = -1;

    /// <summary>
    /// 组键：<c>战斗ID:回合:席位</c>。**战斗 ID 是必须的那一段**（见类注释）。
    /// </summary>
    public static string BuildKey(int combatId, int roundNumber, ulong seatId)
    {
        return $"{combatId}:{roundNumber}:{seatId}";
    }

    /// <summary>
    /// 是否该清账：战斗 ID 与上一次记账的命名空间不同 ⇒ 换战斗（或换局），旧记账一律作废。
    /// </summary>
    public static bool ShouldResetNamespace(int lastNameSpace, int currentCombatId)
    {
        return lastNameSpace != currentCombatId;
    }

    /// <summary>
    /// 一条"在飞"记账是否已经超时（= 那条循环判定为死循环，应放行重试）。
    ///
    /// <paramref name="startedAtMs"/> 为 0（没有记录）时**不判死**：宁可多等一轮，也不要误杀刚起的循环。
    /// </summary>
    public static bool IsStuckInFlight(long startedAtMs, long nowMs, long thresholdMs)
    {
        if (startedAtMs <= 0L || thresholdMs <= 0L || nowMs < startedAtMs)
        {
            return false;
        }

        return nowMs - startedAtMs >= thresholdMs;
    }

    /// <summary>
    /// 指定席位此刻是否有"在飞"的出牌循环（r326：结束回合门用它跳过"正在打的席位"的整桌手牌扫描）。
    ///
    /// 键形如 <c>战斗ID:回合:席位</c> ⇒ 必须按**后缀 `":"+席位`** 精确匹配，不能 `Contains`
    /// （否则 `327` 会误命中 `1327` / `3270`）。任一条年轻（≤<paramref name="maxAgeMs"/>）即算在飞；
    /// 记账存在但缺开始时刻时保守地算"在飞"（宁可少扫一次，也不要误判成可以收口）。
    /// </summary>
    public static bool IsSeatInFlight(
        IEnumerable<string> inFlightKeys,
        IReadOnlyDictionary<string, long> startedAtMs,
        ulong seatId,
        long nowMs,
        long maxAgeMs)
    {
        string suffix = ":" + seatId;
        foreach (string key in inFlightKeys)
        {
            if (key == null || !key.EndsWith(suffix, System.StringComparison.Ordinal))
            {
                continue;
            }

            if (!startedAtMs.TryGetValue(key, out long started) || started <= 0L)
            {
                return true;
            }

            if (maxAgeMs <= 0L || nowMs - started <= maxAgeMs)
            {
                return true;
            }
        }

        return false;
    }
}
