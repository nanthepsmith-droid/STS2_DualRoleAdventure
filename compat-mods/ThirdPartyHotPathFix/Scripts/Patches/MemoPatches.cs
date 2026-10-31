using System;
using MegaCrit.Sts2.Core.Logging;
using ThirdPartyHotPathFix.Scripts.Memo;

namespace ThirdPartyHotPathFix.Scripts.Patches;

/// <summary>
/// 三个「热路径纯函数」的记忆化补丁（前缀查缓存 + 后缀写缓存）。
///
/// | 目标 | 归属 | 不缓存时的开销（实测主线程占比） |
/// |---|---|---|
/// | `MegaCrit.Sts2.Core.Helpers.StringHelper.Slugify(string)` | 原版 | 1.2%（3 个 `Regex.Replace` + 正则解析） |
/// | `STS2RitsuLib.Content.ModContentRegistry.NormalizePublicStem(string)` | RitsuLib | **5.2%**（4 个 `Regex.Replace`，被 YUI 的关键词判定逐卡调用） |
/// | `YuiExtra.Patches.YuiCoreRuntimeTypeLookup.FindType(string[])` | YUICore | **4.3%**（每次 `AppDomain.GetAssemblies()` + 逐程序集 `GetType`） |
///
/// 三者都是纯函数（同入参必同出参），所以缓存不会改变任何行为；
/// 只缓存**非 null 结果**：`FindType` 失败可能是「目标程序集还没加载」，不能钉死。
/// </summary>
internal static class MemoPatches
{
    internal static readonly Memo<string> SlugifyMemo = new();
    internal static readonly Memo<string> NormalizePublicStemMemo = new();
    internal static readonly Memo<Type> RuntimeTypeLookupMemo = new();

    // ── 原版 StringHelper.Slugify(string) ────────────────────────────────────

    public static bool PrefixSlugify(string __0, ref string __result)
        => MemoHelper.Enter(SlugifyMemo, "StringHelper.Slugify", __0, ref __result);

    public static void PostfixSlugify(string __0, string __result)
        => MemoHelper.Leave(SlugifyMemo, __0, __result);

    // ── RitsuLib ModContentRegistry.NormalizePublicStem(string) ──────────────

    public static bool PrefixNormalizePublicStem(string __0, ref string __result)
        => MemoHelper.Enter(NormalizePublicStemMemo, "RitsuLib NormalizePublicStem", __0, ref __result);

    public static void PostfixNormalizePublicStem(string __0, string __result)
        => MemoHelper.Leave(NormalizePublicStemMemo, __0, __result);

    // ── YUICore YuiCoreRuntimeTypeLookup.FindType(params string[]) ───────────

    public static bool PrefixRuntimeTypeLookup(string[] __0, ref Type? __result)
    {
        string? key = SingleName(__0);
        if (!RuntimeTypeLookupMemo.TryGet(key, out Type cached))
        {
            return true;
        }

        __result = cached;
        MemoHelper.LogFirstHit(RuntimeTypeLookupMemo, "YUICore FindType");
        return false;
    }

    public static void PostfixRuntimeTypeLookup(string[] __0, Type? __result)
        => RuntimeTypeLookupMemo.Store(SingleName(__0), __result);

    /// <summary>
    /// 只给**单名字**调用做缓存（常见形态，键就是调各处的字面量，零分配）；
    /// 多名字的 fallback 形态不缓存，避免键构造的开销反噬。
    /// </summary>
    private static string? SingleName(string[]? names)
        => names is { Length: 1 } ? names[0] : null;
}

/// <summary>前缀 / 后缀的公共动作：查缓存、写缓存、首次命中打一行锚点。</summary>
internal static class MemoHelper
{
    public static bool Enter<TValue>(Memo<TValue> memo, string label, string? key, ref TValue result)
        where TValue : class
    {
        if (!memo.TryGet(key, out TValue cached))
        {
            return true;
        }

        result = cached;
        LogFirstHit(memo, label);
        return false;
    }

    public static void Leave<TValue>(Memo<TValue> memo, string? key, TValue result)
        where TValue : class
        => memo.Store(key, result);

    /// <summary>首次命中打一行 —— 用来确认「补丁真的生效了」（这是唯一无法离线验证的环节）。</summary>
    public static void LogFirstHit<TValue>(Memo<TValue> memo, string label)
        where TValue : class
    {
        if (memo.Hits == 1)
        {
            Log.Info($"[HotPathFix] {label} 记忆化首次命中 —— 补丁确实生效（此后同类调用不再重算）。");
        }
    }
}
