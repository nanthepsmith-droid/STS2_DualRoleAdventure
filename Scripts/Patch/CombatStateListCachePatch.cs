using System;
using System.Collections.Generic;
using HarmonyLib;
using LocalMultiControl.Scripts.Runtime;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;

namespace LocalMultiControl.Scripts.Patch;

/// <summary>
/// 给 <see cref="CombatState"/> 的三个派生列表属性做**一帧一缓存**（r330，2026-10-30）。
///
/// <para><b>为什么</b>：原版实现是"每次访问都重新拼一遍" ——</para>
/// <code>
/// public IReadOnlyList&lt;Creature&gt; Creatures =&gt; _allies.Concat(_enemies).ToList();            // 2 次分配
/// public IReadOnlyList&lt;Creature&gt; PlayerCreatures =&gt; Creatures.Where(IsPlayer).ToList();      // 再 2 次
/// public IReadOnlyList&lt;Player&gt; Players =&gt; PlayerCreatures.Select(c =&gt; c.Player).ToList();    // 再 2 次
/// </code>
/// <para>而游戏的 hook 分发（<c>CombatState.IterateHookListeners</c> / <c>ModHelper.IterateAllCombatStateSubscribers</c>）
/// **对每个订阅者都要取一次** <c>Players</c> / <c>Creatures</c>。单人时这条链很短、没人注意；
/// 12 席本地多控时双方的生物数 ×12、订阅者又多（每个第三方 mod 都挂 hook） ⇒ 它变成主线程第一开销。
/// 2026-10-30 的 <c>dotnet-trace</c> 采样（用户实机 floor 11、12 席）实测：**主线程 58.6% 的自耗时**
/// 在 <c>List.CopyTo ← List.AddRange ← ConcatIterator.ToList ← get_Creatures</c>。</para>
///
/// <para><b>做法</b>：前缀里命中缓存就直接给结果并跳过原实现；没命中就让**整条原链（含第三方前缀/后缀）**跑完，
/// 再由后缀把结果按帧缓存。失效有两个来源：</para>
/// <list type="number">
/// <item><b>生物变动事件</b>（<c>CreaturesChanged</c>，原版 <c>AddCreature</c> / <c>RemoveCreature</c> 都会发）
///   —— 这条保证**同帧内**加了/死了生物也不会读到旧列表（正确性）；</item>
/// <item><b>时间分桶</b>（`Environment.TickCount64` 变了就整表作废，≈每 15ms）—— 这条是为了**兜住第三方**：
///   别的 mod 可能给这些属性挂了按血量/状态过滤的后缀，周期性重建才不会把它们的动态结果冻住。
///   ⚠ 分桶**必须用纯托管的时钟**：曾用 `Engine.GetProcessFrames()`（原生 icall），
///   而这两个属性每秒被调用十万次以上 ⇒ 采样里光这一次 icall 就吃掉主线程 35.7%。</item>
/// </list>
///
/// <para>⚠ 只在本地多控会话（<see cref="LocalSelfCoopContext.IsEnabled"/>）里启用；其它入口原样放行。
/// 出问题可置环境变量 <c>LMC_NO_STATE_CACHE=1</c> 一键关掉（只记录不崩）。</para>
/// </summary>
[HarmonyPatch(typeof(CombatState))]
internal static class CombatStateListCachePatch
{
    private static readonly bool Enabled =
        !string.Equals(System.Environment.GetEnvironmentVariable("LMC_NO_STATE_CACHE"), "1", StringComparison.Ordinal);

    private static CombatState? _state;

    /// <summary>
    /// r331：失效分桶的**上一次时间桶**（`Environment.TickCount64`，纯托管读，≈1~2ns）。
    ///
    /// ⚠ 这里曾经用 `Engine.GetProcessFrames()` 做"每帧失效"，2026-10-31 的采样证明那是错的：
    /// 这两个属性每秒被第三方调用**十万次以上**，而每次 icall 要过原生边界（~1~2µs）
    /// ⇒ 采样里 `ShouldCache` 一个人占了主线程 **35.7%**，比它想优化的东西还贵。
    /// 现在改成毫秒分桶（Windows 上 TickCount64 分辨率 ~15ms，等价"接近每帧失效"），
    /// 语义不变、成本变成一次字段读。
    /// </summary>
    private static long _bucket = long.MinValue;

    private static List<Creature>? _creatures;
    private static List<Creature>? _playerCreatures;
    private static List<Player>? _players;

    private static long _hits;
    private static long _builds;

    [HarmonyPatch(nameof(CombatState.Creatures), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool CreaturesPrefix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        return !TryServeCached(__instance, _creatures, ref __result);
    }

    [HarmonyPatch(nameof(CombatState.Creatures), MethodType.Getter)]
    [HarmonyPostfix]
    private static void CreaturesPostfix(CombatState __instance, IReadOnlyList<Creature>? __result)
    {
        if (!ShouldCache(__instance) || __result == null || _creatures != null)
        {
            return;
        }

        _creatures = CopyOf(__result);
        CountBuild();
    }

    [HarmonyPatch(nameof(CombatState.PlayerCreatures), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool PlayerCreaturesPrefix(CombatState __instance, ref IReadOnlyList<Creature> __result)
    {
        return !TryServeCached(__instance, _playerCreatures, ref __result);
    }

    [HarmonyPatch(nameof(CombatState.PlayerCreatures), MethodType.Getter)]
    [HarmonyPostfix]
    private static void PlayerCreaturesPostfix(CombatState __instance, IReadOnlyList<Creature>? __result)
    {
        if (!ShouldCache(__instance) || __result == null || _playerCreatures != null)
        {
            return;
        }

        _playerCreatures = CopyOf(__result);
        CountBuild();
    }

    [HarmonyPatch(nameof(CombatState.Players), MethodType.Getter)]
    [HarmonyPrefix]
    private static bool PlayersPrefix(CombatState __instance, ref IReadOnlyList<Player> __result)
    {
        return !TryServeCached(__instance, _players, ref __result);
    }

    [HarmonyPatch(nameof(CombatState.Players), MethodType.Getter)]
    [HarmonyPostfix]
    private static void PlayersPostfix(CombatState __instance, IReadOnlyList<Player>? __result)
    {
        if (!ShouldCache(__instance) || __result == null || _players != null)
        {
            return;
        }

        _players = CopyOf(__result);
        CountBuild();
    }

    /// <summary>命中缓存 ⇒ 直接给结果并跳过原实现；否则返回 false 让原链（含第三方补丁）照常跑。</summary>
    private static bool TryServeCached<T>(CombatState instance, List<T>? cached, ref IReadOnlyList<T> result)
    {
        if (!ShouldCache(instance) || cached == null)
        {
            return false;
        }

        result = cached;
        _hits++;
        if (_hits % 500_000 == 0)
        {
            LocalMultiControlLogger.Info(
                $"[状态列表缓存] 已复用 {_hits} 次（省掉同次数的 Concat/ToList 分配），重建 {_builds} 次");
        }

        return true;
    }

    /// <summary>
    /// 该不该缓存：本地多控会话 + 开关开着。跳过缓存时**必须**把缓存清掉 ——
    /// 否则后续一旦又变成"该缓存"，就会把上一次会话的旧列表端出去。
    /// </summary>
    private static bool ShouldCache(CombatState instance)
    {
        if (!Enabled || !LocalSelfCoopContext.IsEnabled || instance == null)
        {
            return false;
        }

        if (!ReferenceEquals(_state, instance))
        {
            if (_state != null)
            {
                _state.CreaturesChanged -= OnCreaturesChanged;
            }

            _state = instance;
            instance.CreaturesChanged += OnCreaturesChanged;
            Clear();
            return true;
        }

        long bucket = Environment.TickCount64;
        if (bucket != _bucket)
        {
            _bucket = bucket;
            Clear();
        }

        return true;
    }

    private static void OnCreaturesChanged(ICombatState _)
    {
        Clear();
    }

    private static void Clear()
    {
        _creatures = null;
        _playerCreatures = null;
        _players = null;
    }

    /// <summary>
    /// 复制一份再存：原版返回的是 `List&lt;T&gt;`（`IReadOnlyList` 只是声明），
    /// 直接缓存同一个实例的话，任何把它转回 `List` 就地改动的代码都会把我们的缓存改脏。
    /// 每帧/每次生物变动才复制一次，代价可以忽略。
    /// </summary>
    private static List<T> CopyOf<T>(IReadOnlyList<T> source)
    {
        List<T> copy = new(source.Count);
        for (int i = 0; i < source.Count; i++)
        {
            copy.Add(source[i]);
        }

        return copy;
    }

    private static void CountBuild()
    {
        _builds++;
    }
}
