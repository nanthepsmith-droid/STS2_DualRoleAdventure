using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace LocalMultiControl.Scripts.Runtime;

/// <summary>
/// 第三方**同步器私有字段 `_localPlayerId`** 的唯一反射读写入口（R3 第三批 B3）。
///
/// **为什么必须反射**：游戏把「本地玩家」直接塞在这些 private 字段里，且到处拿它做判定 ——
/// `RewardsSetSynchronizer.SelectLocalReward` 用 `_localPlayerId` 定位本地玩家（校验
/// `reward.Player == LocalPlayer`）、`HookPlayerChoiceContext` 用它决定选择动作是否本地入队
/// （`_gameAction.OwnerId != _localPlayerId` 就不执行）、`EventSynchronizer` / `RestSiteSynchronizer` /
/// `RewardSynchronizer` 用它定位"这次流程属于谁"。多控下我们必须按当前归属角色临时改绑它，
/// 只能反射写（不能改游戏实现）。
///
/// **本类是唯一入口**（R3 收编口径）：新增读写点请走这里，
/// **不要**再写 `AccessTools.Field(..., "_localPlayerId")` —— 那会退回到"多路各自反射猜身份"。
///
/// 职责边界（刻意保持"薄"）：
/// <list type="bullet">
/// <item>只做「找字段（按类型缓存）+ 取/写值 + ulong 安全转换」；</item>
/// <item>**不打日志、不吞异常** —— 异常与日志口径留给调用方，保证各站点行为与日志文案逐字不变
/// （各站点原有 try/catch + WARN/去重逻辑照旧）；</item>
/// <item>找不到字段时 `TryRead` 返回 null / `TryWrite` 返回 false（与旧写法 `?.GetValue` / `?.SetValue` 等价）。</item>
/// </list>
///
/// 口径依据见 `maintenance-docs/decision-records/runtime架构分层重构评估.md` §八 B3。
/// </summary>
internal static class SynchronizerLocalPlayerId
{
    private const string FieldName = "_localPlayerId";

    /// <summary>
    /// 字段查找缓存（Type → FieldInfo?，含"没有这个字段"的 null 缓存）。
    /// `AccessTools.Field` 会向上搜基类，所以按**运行期类型**缓存与旧写法 `target.GetType()` 等价；
    /// 这些读点分布在奖励 / 事件 / 休息区 / 手牌变换路径上（异步等待中会被反复调用），
    /// 每次调用都重新做一遍反射查找纯属浪费。
    /// </summary>
    private static readonly Dictionary<Type, FieldInfo?> FieldCache = new();

    private static readonly object FieldCacheLock = new();

    /// <summary>读「本地玩家 id」（按 `target` 的运行期类型找字段；null / 无字段 / 字段值不是 ulong ⇒ null）。</summary>
    internal static ulong? TryRead(object? target)
        => target == null ? null : TryRead(target, target.GetType());

    /// <summary>读「本地玩家 id」（显式指定同步器类型，与旧写法 `AccessTools.Field(typeof(X), …)` 口径一致）。</summary>
    internal static ulong? TryRead(object? target, Type synchronizerType)
    {
        if (target == null)
        {
            return null;
        }

        return ResolveField(synchronizerType)?.GetValue(target) is ulong playerId ? playerId : null;
    }

    /// <summary>读「本地玩家 id」，缺失按 0（等价于旧写法 `… as ulong? ?? 0UL`）。</summary>
    internal static ulong ReadOrZero(object? target, Type synchronizerType)
        => TryRead(target, synchronizerType) ?? 0UL;

    /// <summary>写「本地玩家 id」（按 `target` 的运行期类型找字段）。字段不存在 ⇒ false。</summary>
    internal static bool TryWrite(object? target, ulong playerId)
        => target != null && TryWrite(target, target.GetType(), playerId);

    /// <summary>写「本地玩家 id」（显式指定同步器类型）。字段不存在 ⇒ false。</summary>
    internal static bool TryWrite(object? target, Type synchronizerType, ulong playerId)
    {
        FieldInfo? field = ResolveField(synchronizerType);
        if (target == null || field == null)
        {
            return false;
        }

        field.SetValue(target, playerId);
        return true;
    }

    private static FieldInfo? ResolveField(Type synchronizerType)
    {
        lock (FieldCacheLock)
        {
            if (FieldCache.TryGetValue(synchronizerType, out FieldInfo? cached))
            {
                return cached;
            }
        }

        FieldInfo? field = AccessTools.Field(synchronizerType, FieldName);

        lock (FieldCacheLock)
        {
            FieldCache[synchronizerType] = field;
        }

        return field;
    }
}
